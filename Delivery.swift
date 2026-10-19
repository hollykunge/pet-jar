import AppKit
import ApplicationServices

enum Client: String, CaseIterable, Identifiable {
    case chatgpt, grok, muse, claude
    var id: String { rawValue }
    var name: String { switch self { case .chatgpt: return "ChatGPT"; case .grok: return "Grok Bot"; case .muse: return "Muse"; case .claude: return "Claude" } }
    var bundleID: String { switch self { case .chatgpt: return "com.openai.codex"; case .grok: return "com.anysphere.sand"; case .muse: return "com.meta.endo"; case .claude: return "com.anthropic.claudefordesktop" } }
    var mark: String { switch self { case .chatgpt: return "✳"; case .grok: return "／"; case .muse: return "m"; case .claude: return "✷" } }
    var spriteName: String { self == .grok ? "pet-grok-glossy" : "pet-\(rawValue)" }
    var characterName: String { switch self { case .chatgpt: return "dot"; case .grok: return "Bot"; case .muse: return "Muse"; case .claude: return "Claude" } }
    var applicationURL: URL? { NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundleID) }
}

enum DeliveryMode: String, CaseIterable {
    case fill = "填入当前会话"
    case send = "填入并发送"
    case newChat = "新建 ChatGPT 草稿"
}

struct DeliveryResult {
    let message: String
    let success: Bool
    // A prepared clipboard or opened client does not mean the draft was delivered.
    let inserted: Bool
}

enum DeliveryRules {
    static func isEmptyDraft(_ value: String) -> Bool { value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
    static func isSendButton(_ value: String) -> Bool {
        ["send", "send message", "send prompt", "submit", "发送", "发送消息", "发送提示", "提交"].contains(value.trimmingCharacters(in: .whitespacesAndNewlines).lowercased())
    }
    static func isExcludedInput(_ descriptor: String) -> Bool {
        let d = descriptor.lowercased()
        return ["search", "搜索", "password", "密码", "address bar", "地址栏", "filter", "筛选", "rename", "重命名"].contains { d.contains($0) }
    }
    static func isComposer(_ descriptor: String) -> Bool {
        let d = descriptor.lowercased()
        return ["message", "prompt", "ask anything", "ask grok", "ask chatgpt", "ask muse", "ask claude", "chat input", "composer", "发消息", "发送消息", "输入消息", "消息输入", "询问", "提问", "输入指令", "任务描述"].contains { d.contains($0) }
    }
    static func newChatURL(_ text: String) -> URL? {
        var parts = URLComponents()
        parts.scheme = "codex"; parts.host = "new"
        parts.queryItems = [URLQueryItem(name: "prompt", value: text)]
        return parts.url
    }
}

final class DesktopDelivery {
    var trusted: Bool { AXIsProcessTrusted() }

    func permissionSettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") { NSWorkspace.shared.open(url) }
    }

    func copy(_ text: String) {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(text, forType: .string)
    }

    func deliver(_ text: String, client: Client, mode: DeliveryMode, completion: @escaping (DeliveryResult) -> Void) {
        guard !DeliveryRules.isEmptyDraft(text) else { completion(.init(message: "先写下你想说的话。", success: false, inserted: false)); return }
        guard text.utf8.count <= 100_000 else { completion(.init(message: "内容过长，请缩短后重试。", success: false, inserted: false)); return }
        guard let appURL = client.applicationURL else { completion(.init(message: "没有找到 \(client.name)，请先安装客户端。", success: false, inserted: false)); return }
        if mode == .newChat {
            guard client == .chatgpt, let url = DeliveryRules.newChatURL(text) else { completion(.init(message: "这个入口只支持 ChatGPT。", success: false, inserted: false)); return }
            NSWorkspace.shared.open([url], withApplicationAt: appURL, configuration: NSWorkspace.OpenConfiguration()) { _, error in
                DispatchQueue.main.async {
                    completion(.init(message: error == nil ? "已请求打开 ChatGPT 新草稿，请在客户端确认文字并发送。" : "ChatGPT 打开失败，草稿仍保留。", success: error == nil, inserted: false))
                }
            }
            return
        }
        if !trusted {
            copy(text)
            open(appURL) { opened in
                completion(.init(message: opened ? "文字已复制，\(client.name) 已打开。在目标会话中粘贴即可。" : "文字已复制，但客户端未能打开。", success: opened, inserted: false))
            }
            return
        }
        open(appURL) { opened in
            guard opened else { completion(.init(message: "客户端未能打开，草稿仍保留。", success: false, inserted: false)); return }
            // A bounded delay lets the target finish activation; no keystroke is emitted until focus is checked.
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.7) { self.insert(text, client: client, mode: mode, completion: completion) }
        }
    }

    private func open(_ url: URL, completion: @escaping (Bool) -> Void) {
        let config = NSWorkspace.OpenConfiguration(); config.activates = true
        NSWorkspace.shared.openApplication(at: url, configuration: config) { app, error in
            DispatchQueue.main.async { completion(app != nil && error == nil) }
        }
    }

    private func attr(_ element: AXUIElement, _ name: String) -> CFTypeRef? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, name as CFString, &value) == .success else { return nil }
        return value
    }
    private func string(_ element: AXUIElement, _ name: String) -> String { attr(element, name) as? String ?? "" }
    private func element(_ object: CFTypeRef?) -> AXUIElement? {
        guard let object, CFGetTypeID(object) == AXUIElementGetTypeID() else { return nil }
        return (object as! AXUIElement)
    }
    private func descriptor(_ el: AXUIElement) -> String {
        [kAXTitleAttribute, kAXDescriptionAttribute, kAXHelpAttribute, "AXPlaceholderValue", "AXIdentifier"].map { string(el, $0) }.joined(separator: " ")
    }
    private func isEditable(_ el: AXUIElement) -> Bool {
        let role = string(el, kAXRoleAttribute)
        let editable = (attr(el, "AXEditable") as? Bool) == true
        return (role == kAXTextAreaRole || role == kAXTextFieldRole || editable) && !DeliveryRules.isExcludedInput(descriptor(el)) && (attr(el, kAXEnabledAttribute) as? Bool != false)
    }
    private func descendants(_ root: AXUIElement) -> [AXUIElement] {
        var result: [AXUIElement] = [], stack = [root]
        let deadline = Date().addingTimeInterval(0.5)
        while let next = stack.popLast(), result.count < 1_500, Date() < deadline {
            result.append(next)
            if let children = attr(next, kAXChildrenAttribute) as? [AXUIElement] { stack.append(contentsOf: children.reversed()) }
        }
        return result
    }
    private func findComposer(_ app: AXUIElement) -> AXUIElement? {
        // Require recognizable composer semantics, even when the focused element is editable.
        if let focused = element(attr(app, kAXFocusedUIElementAttribute)), isEditable(focused), DeliveryRules.isComposer(descriptor(focused)) { return focused }
        guard let window = element(attr(app, kAXFocusedWindowAttribute)) else { return nil }
        let candidates = descendants(window).filter { isEditable($0) && DeliveryRules.isComposer(descriptor($0)) }
        return candidates.count == 1 ? candidates[0] : nil
    }
    private func targetActive(_ client: Client) -> Bool { NSWorkspace.shared.frontmostApplication?.bundleIdentifier == client.bundleID }
    private func fallback(_ text: String, _ message: String, _ completion: (DeliveryResult) -> Void) {
        copy(text)
        completion(.init(message: message + " 文字已复制，可手动粘贴。", success: false, inserted: false))
    }
    private func insert(_ text: String, client: Client, mode: DeliveryMode, completion: @escaping (DeliveryResult) -> Void) {
        guard targetActive(client), let running = NSRunningApplication.runningApplications(withBundleIdentifier: client.bundleID).first else {
            fallback(text, "目标客户端没有处于前台。", completion); return
        }
        let app = AXUIElementCreateApplication(running.processIdentifier)
        AXUIElementSetMessagingTimeout(app, 0.3)
        guard let composer = findComposer(app) else { fallback(text, "没有唯一识别出消息输入框，请先打开目标会话。", completion); return }
        guard let current = attr(composer, kAXValueAttribute) as? String else { fallback(text, "无法读取目标输入框，未尝试输入。", completion); return }
        guard DeliveryRules.isEmptyDraft(current) else { fallback(text, "目标输入框已有草稿，已保留原文。", completion); return }
        guard AXUIElementSetAttributeValue(composer, kAXFocusedAttribute as CFString, kCFBooleanTrue) == .success,
              let focused = element(attr(app, kAXFocusedUIElementAttribute)), CFEqual(focused, composer), targetActive(client) else {
            fallback(text, "无法确认目标输入框焦点。", completion); return
        }
        // Always paste through the editor, so React/contenteditable state receives real input events.
        copy(text)
        guard let source = CGEventSource(stateID: .combinedSessionState),
              let down = CGEvent(keyboardEventSource: source, virtualKey: 9, keyDown: true),
              let up = CGEvent(keyboardEventSource: source, virtualKey: 9, keyDown: false) else {
            completion(.init(message: "键盘事件创建失败，文字已复制。", success: false, inserted: false)); return
        }
        down.flags = .maskCommand; up.flags = .maskCommand
        down.postToPid(running.processIdentifier); up.postToPid(running.processIdentifier)
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.45) {
            guard self.targetActive(client), self.string(composer, kAXValueAttribute) == text else {
                completion(.init(message: "已尝试粘贴，但未确认输入结果。请查看客户端，勿重复发送。", success: false, inserted: false)); return
            }
            guard mode == .send else {
                completion(.init(message: "已确认文字填入 \(client.name)，等待你发送。", success: true, inserted: true)); return
            }
            self.pressSend(composer, app: app, client: client, completion: completion)
        }
    }
    private func pressSend(_ composer: AXUIElement, app: AXUIElement, client: Client, completion: @escaping (DeliveryResult) -> Void) {
        // Search only nearby composer ancestors. Never guess Enter, and never search another window.
        var current = composer
        var found: [AXUIElement] = []
        for _ in 0..<4 {
            guard let parent = element(attr(current, kAXParentAttribute)) else { break }
            current = parent
            if string(parent, kAXRoleAttribute) == kAXWindowRole { break }
            found = descendants(parent).filter {
                string($0, kAXRoleAttribute) == kAXButtonRole &&
                (DeliveryRules.isSendButton(string($0, kAXTitleAttribute)) || DeliveryRules.isSendButton(string($0, kAXDescriptionAttribute))) &&
                (attr($0, kAXEnabledAttribute) as? Bool == true)
            }
            if !found.isEmpty { break }
        }
        guard found.count == 1, targetActive(client),
              let focused = element(attr(app, kAXFocusedUIElementAttribute)), CFEqual(focused, composer) else {
            completion(.init(message: "文字已填入，未唯一识别出发送按钮。请在客户端发送。", success: false, inserted: true)); return
        }
        let result = AXUIElementPerformAction(found[0], kAXPressAction as CFString)
        completion(.init(message: result == .success ? "已在 \(client.name) 点击发送。接收与回复请查看客户端。" : "文字已填入，但发送按钮未响应。请在客户端发送。", success: result == .success, inserted: true))
    }
}
