import AppKit

struct AccountUsageWindow {
    let name: String
    let used: Double
    let resetsAt: Date?
}
struct AccountUsageSnapshot {
    let source: String
    let fetchedAt: Date
    let windows: [AccountUsageWindow]
    let detail: String
    var stale = false
    var text: String {
        let date = DateFormatter(); date.dateFormat = "MM-dd HH:mm"
        let lines = windows.prefix(3).map { window -> String in
            if let reset = window.resetsAt, reset <= Date() { return window.name + "\n周期已到期，等待刷新" }
            let used = String(format: "%.1f", window.used), left = String(format: "%.1f", 100 - window.used)
            return window.name + "\n已用 " + used + "% · 剩余 " + left + "%\n重置：" + (window.resetsAt.map { date.string(from: $0) } ?? "暂不可获取")
        }
        date.dateFormat = "HH:mm:ss"
        let updated = windows.isEmpty ? "" : "\n" + (stale ? "上次读取 " : "更新于 ") + date.string(from: fetchedAt)
        let extra = windows.count > 3 ? ["另有 \(windows.count - 3) 项额度，请在客户端查看。"] : []
        return ([source] + lines + extra + [detail]).joined(separator: "\n\n") + updated
    }
    static func missing(_ source: String, _ detail: String) -> AccountUsageSnapshot {
        AccountUsageSnapshot(source: source, fetchedAt: Date(), windows: [], detail: detail)
    }
}

// Account reads only. No conversation transcripts, login changes, model calls,
// account-reset consumption or persistent credential copies are performed.
final class AccountUsageStore {
    private var cache: [Client: AccountUsageSnapshot] = [:]
    private var pending: [Client: [(AccountUsageSnapshot) -> Void]] = [:]
    func load(_ client: Client, completion: @escaping (AccountUsageSnapshot) -> Void) {
        if let snapshot = cache[client], Date().timeIntervalSince(snapshot.fetchedAt) < 45 { completion(snapshot); return }
        if pending[client] != nil { pending[client]!.append(completion); return }
        pending[client] = [completion]
        DispatchQueue.global(qos: .utility).async {
            let fetched = client == .chatgpt ? Self.codex() : client == .claude ? Self.claude() : .missing(client.name, "暂不可获取：该客户端尚未接入账号额度来源。")
            DispatchQueue.main.async {
                var result = fetched
                if fetched.windows.isEmpty, let old = self.cache[client], !old.windows.isEmpty {
                    result = AccountUsageSnapshot(source: old.source, fetchedAt: old.fetchedAt, windows: old.windows, detail: "刷新失败，显示上次结果。" + fetched.detail, stale: true)
                }
                self.cache[client] = result
                let callbacks = self.pending.removeValue(forKey: client) ?? []; callbacks.forEach { $0(result) }
            }
        }
    }
    private static func number(_ value: Any?) -> Double? {
        guard let n = value as? NSNumber, CFGetTypeID(n) != CFBooleanGetTypeID(), n.doubleValue.isFinite else { return nil }; return n.doubleValue
    }
    static func parseCodex(_ root: [String: Any]) -> AccountUsageSnapshot {
        var windows: [AccountUsageWindow] = []
        let buckets = root["rateLimitsByLimitId"] as? [String: [String: Any]]
        let selected = buckets?.isEmpty == false ? buckets! : ["codex": root["rateLimits"] as? [String: Any] ?? [:]]
        for (id, bucket) in selected.sorted(by: { $0.key < $1.key }) {
            let name = bucket["limitName"] as? String ?? (id == "codex" ? "Codex" : id)
            for key in ["primary", "secondary"] {
                guard let window = bucket[key] as? [String: Any], let used = number(window["usedPercent"]), used >= 0, used <= 100 else { continue }
                let minutes = number(window["windowDurationMins"])
                var duration = "周期额度"
                if let m = minutes, m > 0 { duration = m.truncatingRemainder(dividingBy: 1440) == 0 ? String(format: "%.0f天", m / 1440) : m >= 60 ? String(format: "%.1f小时", m / 60) : String(format: "%.0f分钟", m) }
                windows.append(AccountUsageWindow(name: name + " · " + duration, used: used, resetsAt: number(window["resetsAt"]).map { Date(timeIntervalSince1970: $0) }))
            }
        }
        return AccountUsageSnapshot(source: "本机 Codex 登录账号", fetchedAt: Date(), windows: windows, detail: windows.isEmpty ? "暂不可获取账号额度，缺失数据不会记为 0。" : "账号额度比例，非 Token 数量")
    }
    private static func codex() -> AccountUsageSnapshot {
        let env = ProcessInfo.processInfo.environment
        var paths = (env["PATH"] ?? "").split(separator: ":").map { String($0) + "/codex" }
        paths += ["/opt/homebrew/bin/codex", "/usr/local/bin/codex", NSHomeDirectory() + "/.local/bin/codex"]
        if let app = Client.chatgpt.applicationURL { paths.append(app.appendingPathComponent("Contents/Resources/codex").path) }
        guard let cli = paths.first(where: { FileManager.default.isExecutableFile(atPath: $0) }) else { return .missing("本机 Codex 登录账号", "未找到 Codex CLI，暂不可读取账号额度。") }
        let process = Process(), input = Pipe(), output = Pipe()
        process.executableURL = URL(fileURLWithPath: cli); process.arguments = ["app-server", "--listen", "stdio://"]
        process.standardInput = input; process.standardOutput = output; process.standardError = FileHandle.nullDevice
        process.currentDirectoryURL = FileManager.default.temporaryDirectory
        do { try process.run() } catch { return .missing("本机 Codex 登录账号", "额度读取失败，请检查本机登录状态。") }
        let timeout = DispatchWorkItem { if process.isRunning { process.terminate() } }
        DispatchQueue.global().asyncAfter(deadline: .now() + 15, execute: timeout)
        defer { timeout.cancel(); if process.isRunning { process.terminate() }; input.fileHandleForWriting.closeFile(); output.fileHandleForReading.closeFile() }
        var buffer = Data()
        func send(_ value: [String: Any]) throws { var data = try JSONSerialization.data(withJSONObject: value); data.append(10); try input.fileHandleForWriting.write(contentsOf: data) }
        func read(_ id: Int) throws -> [String: Any] {
            for _ in 0..<100 {
                while !buffer.contains(10) {
                    let chunk = output.fileHandleForReading.availableData
                    if chunk.isEmpty || buffer.count + chunk.count > 1048576 { throw NSError(domain: "Quota", code: 1) }; buffer.append(chunk)
                }
                let end = buffer.firstIndex(of: 10)!, line = buffer[..<end]; buffer.removeSubrange(...end)
                guard let row = try JSONSerialization.jsonObject(with: Data(line)) as? [String: Any], (row["id"] as? Int) == id else { continue }
                guard row["error"] == nil, let result = row["result"] as? [String: Any] else { throw NSError(domain: "Quota", code: 2) }; return result
            }
            throw NSError(domain: "Quota", code: 3)
        }
        do {
            try send(["id": 1, "method": "initialize", "params": ["clientInfo": ["name": "pet_jar_quota", "title": "PetJar Account Quota", "version": "0.5.3"]]])
            _ = try read(1); try send(["method": "initialized", "params": [:] as [String: Any]])
            try send(["id": 2, "method": "account/rateLimits/read"]); return parseCodex(try read(2))
        } catch { return .missing("本机 Codex 登录账号", "暂不可获取账号额度，请检查登录状态和网络。") }
    }
    private final class NoRedirect: NSObject, URLSessionTaskDelegate {
        func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) { completionHandler(nil) }
    }
    private final class ResponseBox {
        private let lock = NSLock()
        private var payload: [String: Any]?
        func set(_ value: [String: Any]?) { lock.lock(); defer { lock.unlock() }; payload = value }
        func get() -> [String: Any]? { lock.lock(); defer { lock.unlock() }; return payload }
    }
    private static func claude() -> AccountUsageSnapshot {
        let source = "本机 Claude Code 登录账号"
        let directory = ProcessInfo.processInfo.environment["CLAUDE_CONFIG_DIR"] ?? NSHomeDirectory() + "/.claude"
        guard let data = try? Data(contentsOf: URL(fileURLWithPath: directory + "/.credentials.json")),
              let root = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
              let oauth = root["claudeAiOauth"] as? [String: Any], let token = oauth["accessToken"] as? String else {
            return .missing(source, "未找到可读取的本机登录信息，请在 Claude Code 检查账号。")
        }
        if let expires = number(oauth["expiresAt"]), expires <= Date().timeIntervalSince1970 * 1000 { return .missing(source, "本机 Claude Code 登录已过期，请在客户端重新登录。") }
        var request = URLRequest(url: URL(string: "https://api.anthropic.com/api/oauth/usage")!); request.timeoutInterval = 10
        request.setValue("Bearer " + token, forHTTPHeaderField: "Authorization"); request.setValue("oauth-2025-04-20", forHTTPHeaderField: "anthropic-beta")
        let session = URLSession(configuration: .ephemeral, delegate: NoRedirect(), delegateQueue: nil)
        let semaphore = DispatchSemaphore(value: 0), box = ResponseBox()
        let task = session.dataTask(with: request) { bytes, response, _ in
            defer { semaphore.signal() }
            if let http = response as? HTTPURLResponse, http.statusCode == 200, let bytes { box.set((try? JSONSerialization.jsonObject(with: bytes)) as? [String: Any]) }
        }
        task.resume(); _ = semaphore.wait(timeout: .now() + 12); task.cancel(); session.invalidateAndCancel()
        guard let payload = box.get() else { return .missing(source, "暂不可获取账号额度，请检查登录状态和网络。") }
        let format = ISO8601DateFormatter(), fractional = ISO8601DateFormatter()
        fractional.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        var windows: [AccountUsageWindow] = []
        for (key, name) in [("five_hour", "5小时"), ("seven_day", "7天"), ("seven_day_sonnet", "Sonnet · 7天"), ("seven_day_opus", "Opus · 7天")] {
            guard let window = payload[key] as? [String: Any], let used = number(window["utilization"]), used >= 0, used <= 100 else { continue }
            windows.append(AccountUsageWindow(name: name, used: used, resetsAt: (window["resets_at"] as? String).flatMap { format.date(from: $0) ?? fractional.date(from: $0) }))
        }
        return AccountUsageSnapshot(source: source, fetchedAt: Date(), windows: windows, detail: windows.isEmpty ? "暂不可获取账号额度，缺失数据不会记为 0。" : "请确认与客户端使用同一账号")
    }
}
