import AppKit
import SwiftUI
import Combine
import Carbon

let orange = Color(red: 0.86, green: 0.32, blue: 0.13)
let ink = Color(red: 0.19, green: 0.18, blue: 0.16)
func sprite(_ client: Client) -> NSImage { NSImage(contentsOf: Bundle.main.resourceURL!.appendingPathComponent(client.spriteName + ".png")) ?? NSImage() }

final class PetModel: ObservableObject {
    @Published private(set) var client: Client = .chatgpt
    @Published private var drafts: [Client: String] = [:]
    @Published private var modes: [Client: DeliveryMode] = [:]
    @Published var busy = false
    @Published var trusted = AXIsProcessTrusted()
    @Published var status = "先在 ChatGPT 中打开要通信的 dot 或会话。"
    @Published var statusOK = true
    private var results: [Client: DeliveryResult] = [:]
    let delivery = DesktopDelivery()
    var close: (() -> Void)?
    var onActivity: ((Client) -> Void)?
    var draft: String { get { drafts[client] ?? "" } set { drafts[client] = newValue } }
    var mode: DeliveryMode { get { modes[client] ?? .send } set { modes[client] = newValue } }
    @discardableResult func select(_ client: Client) -> Bool {
        guard !busy else { return false }
        self.client = client
        if let result = results[client] { status = result.message; statusOK = result.success }
        else { status = "先在 \(client.name) 中打开要通信的智能体或会话。"; statusOK = true }
        return true
    }
    func refresh() { trusted = delivery.trusted }
    var buttonTitle: String {
        if busy { return "正在交接…" }
        if mode == .newChat { return "打开 ChatGPT 草稿" }
        if !trusted { return "复制并打开 \(client.name)" }
        return mode == .send ? "发送给 \(client.name)" : "填入 \(client.name)"
    }
    func send() {
        guard !busy, !DeliveryRules.isEmptyDraft(draft) else { return }
        let target = client, text = draft, selectedMode = mode
        busy = true; status = "正在交给 \(target.name)…"; statusOK = true
        delivery.deliver(text, client: target, mode: selectedMode) { [weak self] result in
            guard let self else { return }
            self.results[target] = result; self.busy = false
            if self.client == target { self.status = result.message; self.statusOK = result.success }
            self.onActivity?(target)
        }
    }
}

struct ComposerView: View {
    @ObservedObject var model: PetModel
    @State private var showHelp = false
    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 10) {
                Image(nsImage: sprite(model.client)).resizable().scaledToFit().frame(width: 43, height: 43)
                VStack(alignment: .leading, spacing: 4) {
                    Text(model.client.name).font(.system(size: 17, weight: .semibold))
                    Text("正在和 \(model.client.characterName) 说话").font(.system(size: 10)).foregroundStyle(.secondary)
                }
                Spacer()
                Button { showHelp.toggle() } label: { Image(systemName: "questionmark.circle").font(.system(size: 14)) }.buttonStyle(.plain).foregroundStyle(.secondary).help("连接说明")
                    .popover(isPresented: $showHelp) {
                        VStack(alignment: .leading, spacing: 10) {
                            Text("点谁，就和谁说话").font(.headline)
                            Text("每只宠物固定对应一个 App，草稿分别保留。先在目标 App 里打开智能体会话，再在这里输入。").font(.system(size: 12)).lineSpacing(4)
                            Text("直接填入与发送需 macOS 辅助功能授权，并需逐个客户端实测。无法确认输入框或发送按钮时会停止自动操作。回复目前仍在原 App 中显示。").font(.caption).foregroundStyle(.secondary)
                        }.padding(18).frame(width: 280)
                    }
                Button { model.close?() } label: { Image(systemName: "xmark").font(.system(size: 11, weight: .semibold)).frame(width: 22, height: 24) }.buttonStyle(.plain).foregroundStyle(.secondary).help("收起 · Esc")
            }.padding(.bottom, 17)
            ZStack(alignment: .topLeading) {
                if model.draft.isEmpty { Text("想对 \(model.client.characterName) 说些什么？").font(.system(size: 14)).foregroundStyle(ink.opacity(0.35)).padding(.horizontal, 12).padding(.top, 13).allowsHitTesting(false) }
                TextEditor(text: $model.draft).font(.system(size: 14)).lineSpacing(4).scrollContentBackground(.hidden).padding(8).disabled(model.busy).accessibilityLabel("发送给 \(model.client.name) 的消息")
            }.frame(height: 108).background(Color.white.opacity(0.85)).clipShape(RoundedRectangle(cornerRadius: 14)).overlay(RoundedRectangle(cornerRadius: 14).stroke(ink.opacity(0.09), lineWidth: 1)).padding(.bottom, 10)
            HStack {
                Menu {
                    ForEach(DeliveryMode.allCases.filter { model.client == .chatgpt || $0 != .newChat }, id: \.rawValue) { mode in
                        Button { model.mode = mode } label: { if model.mode == mode { Label(mode.rawValue, systemImage: "checkmark") } else { Text(mode.rawValue) } }
                    }
                } label: { HStack(spacing: 5) { Text(model.mode.rawValue); Image(systemName: "chevron.down").font(.system(size: 8, weight: .semibold)) }.font(.system(size: 10)).foregroundStyle(.secondary) }.menuStyle(.borderlessButton).menuIndicator(.hidden).fixedSize().disabled(model.busy)
                Spacer(); Text("⌘ ↵").font(.system(size: 10, design: .monospaced)).foregroundStyle(.tertiary)
            }.padding(.bottom, 12)
            Button { model.send() } label: {
                HStack(spacing: 8) {
                    if model.busy { ProgressView().controlSize(.small) } else { Image(systemName: model.trusted ? "paperplane.fill" : "arrow.up.right").font(.system(size: 11, weight: .semibold)) }
                    Text(model.buttonTitle).font(.system(size: 12, weight: .semibold))
                }.frame(maxWidth: .infinity).frame(height: 40).foregroundStyle(Color.white).background(DeliveryRules.isEmptyDraft(model.draft) ? orange.opacity(0.4) : orange).clipShape(RoundedRectangle(cornerRadius: 12))
            }.buttonStyle(.plain).disabled(model.busy || DeliveryRules.isEmptyDraft(model.draft) || model.client.applicationURL == nil).keyboardShortcut(.return, modifiers: .command)
            HStack(alignment: .top, spacing: 6) {
                Image(systemName: model.statusOK ? "circle.dotted" : "info.circle").font(.system(size: 10)).padding(.top, 1)
                Text(model.status).font(.system(size: 10)).lineSpacing(3).fixedSize(horizontal: false, vertical: true)
            }.foregroundStyle(model.statusOK ? ink.opacity(0.5) : orange).frame(minHeight: 34, alignment: .topLeading).padding(.top, 11)
            if !model.trusted && model.mode != .newChat {
                HStack {
                    Text("开启辅助功能后可直接投递").font(.system(size: 10)).foregroundStyle(.secondary); Spacer()
                    Button("去开启 ↗") { model.delivery.permissionSettings() }.buttonStyle(.plain).font(.system(size: 10, weight: .medium)).foregroundStyle(orange)
                }.padding(.top, 3)
            } else { Text("草稿独立保留 · 点击罐中另一只宠物切换").font(.system(size: 10)).foregroundStyle(.tertiary).padding(.top, 3) }
        }.padding(20).frame(width: 360, height: 365, alignment: .topLeading).foregroundStyle(ink)
            .background(Color(red: 0.98, green: 0.97, blue: 0.94).opacity(0.99)).clipShape(RoundedRectangle(cornerRadius: 23))
            .overlay(RoundedRectangle(cornerRadius: 23).stroke(Color.white.opacity(0.9), lineWidth: 1)).padding(12).preferredColorScheme(.light)
            .onExitCommand { model.close?() }.onAppear { model.refresh() }
    }
}

final class FloatingPanel: NSPanel { override var canBecomeKey: Bool { true }; override var canBecomeMain: Bool { false } }
final class AppDelegate: NSObject, NSApplicationDelegate {
    let model = PetModel()
    var pet: FloatingPanel!, composer: FloatingPanel!, petView: PetJarView!, statusItem: NSStatusItem!
    var hotKey: EventHotKeyRef?
    var localMonitor: Any?, globalMonitor: Any?, refreshTimer: Timer?
    func applicationDidFinishLaunching(_ notification: Notification) {
        let menu = NSMenu(); menu.addItem(withTitle: "退出宠物罐", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        let main = NSMenu(); let item = NSMenuItem(); item.submenu = menu; main.addItem(item); NSApp.mainMenu = main
        pet = panel(size: JarGeometry.size); pet.title = "宠物罐 · 多材质智能体"
        petView = PetJarView(frame: NSRect(origin: .zero, size: JarGeometry.size)); pet.contentView = petView
        if CommandLine.arguments.contains("--freeze-expression") { petView.characters.values.forEach { $0.freezeExpression = true } }
        if let i = CommandLine.arguments.firstIndex(of: "--preview-hover"), CommandLine.arguments.indices.contains(i + 1) { petView.previewHover = Client(rawValue: CommandLine.arguments[i + 1]) }
        let screen = NSScreen.main!.visibleFrame, defaults = UserDefaults.standard
        pet.setFrameOrigin(NSPoint(x: defaults.object(forKey: "jarX") as? Double ?? screen.maxX - JarGeometry.size.width - 25, y: defaults.object(forKey: "jarY") as? Double ?? screen.minY + 35)); petView.clamp()
        composer = panel(size: NSSize(width: 384, height: 389)); composer.title = "ChatGPT · 对话"
        composer.contentView = NSHostingView(rootView: ComposerView(model: model))
        petView.chosen = { [weak self] client in self?.show(client) }; petView.moved = { [weak self] in self?.petMoved() }
        model.close = { [weak self] in self?.hideComposer() }; model.onActivity = { [weak self] client in self?.petView.characters[client]?.react() }
        pet.orderFrontRegardless(); setupStatusItem(); setupShortcut()
        refreshTimer = Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { [weak self] _ in self?.model.refresh() }
        if CommandLine.arguments.contains("--show-composer") { show(.chatgpt) }
    }
    func panel(size: NSSize) -> FloatingPanel {
        let p = FloatingPanel(contentRect: NSRect(origin: .zero, size: size), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        p.isOpaque = false; p.backgroundColor = .clear; p.hasShadow = false; p.level = .floating
        p.acceptsMouseMovedEvents = true
        p.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]; p.isReleasedWhenClosed = false; p.hidesOnDeactivate = false; return p
    }
    func positionComposer() {
        let visible = (pet.screen ?? NSScreen.main!).visibleFrame, anchor = JarGeometry.frames[model.client]!
        var x = pet.frame.minX - composer.frame.width + 24
        if x < visible.minX { x = pet.frame.maxX - 24 }
        x = max(visible.minX, min(x, visible.maxX - composer.frame.width))
        let y = max(visible.minY, min(pet.frame.minY + anchor.midY - composer.frame.height / 2 + 40, visible.maxY - composer.frame.height))
        composer.setFrameOrigin(NSPoint(x: x, y: y))
    }
    func show(_ client: Client) {
        guard model.select(client) else { return }
        petView.setActive(client); model.refresh(); composer.title = "\(client.name) · 对话"; positionComposer()
        if !composer.isVisible {
            composer.alphaValue = 0; composer.makeKeyAndOrderFront(nil)
            NSAnimationContext.runAnimationGroup { context in context.duration = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion ? 0 : 0.18; composer.animator().alphaValue = 1 }
        } else { composer.makeKeyAndOrderFront(nil) }
    }
    func hideComposer() { composer.orderOut(nil); petView.setActive(nil) }
    func petMoved() { positionComposer(); UserDefaults.standard.set(pet.frame.minX, forKey: "jarX"); UserDefaults.standard.set(pet.frame.minY, forKey: "jarY") }
    @objc func toggle() { if composer.isVisible { hideComposer() } else { show(model.client) } }
    @objc func resetPosition() { let frame = NSScreen.main!.visibleFrame; pet.setFrameOrigin(NSPoint(x: frame.maxX - JarGeometry.size.width - 25, y: frame.minY + 35)); petMoved(); pet.orderFrontRegardless() }
    func setupStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem.button?.image = NSImage(systemSymbolName: "pill.bottle", accessibilityDescription: "宠物罐")
        let menu = NSMenu()
        let open = NSMenuItem(title: "打开对话框", action: #selector(toggle), keyEquivalent: ""); open.target = self; menu.addItem(open)
        let reset = NSMenuItem(title: "找回宠物罐", action: #selector(resetPosition), keyEquivalent: ""); reset.target = self; menu.addItem(reset)
        menu.addItem(.separator()); menu.addItem(withTitle: "退出宠物罐", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"); statusItem.menu = menu
    }
    func setupShortcut() {
        var type = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, _, userData in
            guard let userData else { return OSStatus(eventNotHandledErr) }
            let delegate = Unmanaged<AppDelegate>.fromOpaque(userData).takeUnretainedValue(); DispatchQueue.main.async { delegate.toggle() }; return noErr
        }, 1, &type, Unmanaged.passUnretained(self).toOpaque(), nil)
        let result = RegisterEventHotKey(UInt32(kVK_Space), UInt32(optionKey), EventHotKeyID(signature: 0x5455414e, id: 1), GetApplicationEventTarget(), 0, &hotKey)
        if result != noErr { model.status = "⌥Space 已被占用，点击罐中的宠物也能呼出。" }
    }
    func applicationWillTerminate(_ notification: Notification) {
        refreshTimer?.invalidate(); if let localMonitor { NSEvent.removeMonitor(localMonitor) }; if let globalMonitor { NSEvent.removeMonitor(globalMonitor) }; if let hotKey { UnregisterEventHotKey(hotKey) }
    }
}

if let index = CommandLine.arguments.firstIndex(of: "--render-expression"), CommandLine.arguments.indices.contains(index + 2) {
    // Artifact rendering of the real view tree, with no onscreen window or input events.
    let _ = NSApplication.shared
    let jar = PetJarView(frame: NSRect(origin: .zero,size: JarGeometry.size))
    if let client = Client(rawValue: CommandLine.arguments[index + 1]), let character = jar.characters[client] {
        character.freezeExpression = true; character.setHovered(true)
    }
    let pixels = jar.bitmapImageRepForCachingDisplay(in: jar.bounds)!
    jar.cacheDisplay(in: jar.bounds,to: pixels)
    try! pixels.representation(using: .png,properties: [:])!.write(to: URL(fileURLWithPath: CommandLine.arguments[index + 2]))
    print("Rendered native facial expression preview")
} else if CommandLine.arguments.contains("--self-test") {
    let text = "请总结 A&B + C? #宠物罐\n你好 👋 https://example.com?q=a&b=2"
    precondition(URLComponents(url: DeliveryRules.newChatURL(text)!, resolvingAgainstBaseURL: false)!.queryItems!.first!.value == text)
    precondition(DeliveryRules.isEmptyDraft(" \n\t")); precondition(!DeliveryRules.isEmptyDraft("已有草稿"))
    precondition(DeliveryRules.isSendButton("发送消息")); precondition(!DeliveryRules.isSendButton("Send feedback"))
    precondition(DeliveryRules.isExcludedInput("Search chats")); precondition(DeliveryRules.isComposer("Message your dot")); precondition(!DeliveryRules.isComposer("New task name"))
    let model = PetModel()
    for client in Client.allCases { precondition(model.select(client)); precondition(model.draft.isEmpty); model.draft = "draft-for-\(client.rawValue)"; precondition(model.mode == .send) }
    for client in Client.allCases { precondition(model.select(client)); precondition(model.draft == "draft-for-\(client.rawValue)"); print("\(client.name): \(client.applicationURL != nil ? "installed" : "not installed"), independent draft OK") }
    model.busy = true; let previous = model.client; precondition(!model.select(.chatgpt)); precondition(model.client == previous)
    for client in Client.allCases { precondition(FileManager.default.fileExists(atPath: Bundle.main.resourceURL!.appendingPathComponent(client.spriteName + ".png").path)) }
    let _ = NSApplication.shared
    let jar = PetJarView(frame: NSRect(origin: .zero, size: JarGeometry.size))
    let stationaryFrames = jar.characters.mapValues { $0.frame }
    jar.characters.values.forEach { $0.freezeExpression = true }
    for (point, target) in [(NSPoint(x: 99, y: 197), Client.chatgpt), (NSPoint(x: 180, y: 145), Client.grok), (NSPoint(x: 101, y: 99), Client.muse)] {
        precondition((jar.hitTest(point) as? CharacterView)?.client == target, "mouse hit routing failed: \(target)")
        jar.updateHover(at: point)
        precondition(jar.hoveredClient == target)
        precondition(jar.characters.values.filter { $0.hovering }.count == 1, "overlapping pets must not hover together")
        for (client, character) in jar.characters {
            precondition(character.frame == stationaryFrames[client] && character.face.frame == character.bounds, "hover must never move or scale the pet")
            precondition(CATransform3DIsIdentity(character.layer!.transform) && CATransform3DIsIdentity(character.face.layer!.transform))
            precondition(character.face.strength == (client == target ? 1 : 0), "only the hovered pet may change facial expression")
        }
    }
    jar.updateHover(at: NSPoint(x: 8, y: 248))
    precondition(jar.hoveredClient == nil && jar.characters.values.allSatisfy { !$0.hovering })
    precondition(jar.characters.values.allSatisfy { $0.face.strength == 0 }, "leaving must restore original eyes")
    precondition(FileManager.default.fileExists(atPath: Bundle.main.resourceURL!.appendingPathComponent("pile-expressions.png").path))
    precondition(!(jar.hitTest(NSPoint(x: 8, y: 315)) is CharacterView))
    precondition(FileManager.default.fileExists(atPath: Bundle.main.resourceURL!.appendingPathComponent("pile-scene.png").path))
    // Test the actual art against the vessel rather than only comparing rectangles.
    let pixels = NSBitmapImageRep(data: try! Data(contentsOf: Bundle.main.resourceURL!.appendingPathComponent("pile-scene.png")))!
    let cavity = JarGeometry.interior(), scene = JarGeometry.sceneRect
    for y in stride(from: 0, to: pixels.pixelsHigh, by: 4) {
        for x in stride(from: 0, to: pixels.pixelsWide, by: 4) where (pixels.colorAt(x: x, y: y)?.alphaComponent ?? 0) > 0.25 {
            let point = NSPoint(x: scene.minX + (CGFloat(x) + 0.5) / CGFloat(pixels.pixelsWide) * scene.width, y: scene.maxY - (CGFloat(y) + 0.5) / CGFloat(pixels.pixelsHigh) * scene.height)
            precondition(cavity.contains(point), "visible art must fit inside the glass cavity")
        }
    }
    for client in Client.allCases {
        for rect in FacialExpressionArt.eyeRects(for: client) {
            precondition(cavity.contains(NSPoint(x: rect.minX,y: rect.minY)) && cavity.contains(NSPoint(x: rect.maxX,y: rect.maxY)), "facial expression must stay inside glass")
        }
    }
    let animatedPet = jar.characters[.grok]!
    animatedPet.freezeExpression = false; animatedPet.setHovered(true)
    precondition(animatedPet.expressionIsAnimating)
    precondition(animatedPet.layer?.animationKeys()?.isEmpty ?? true, "body must have no animations")
    RunLoop.current.run(until: Date().addingTimeInterval(0.13))
    precondition(animatedPet.face.strength > 0.75, "hover blink must actually reach the closed-eye frame")
    animatedPet.setHovered(false)
    precondition(!animatedPet.expressionIsAnimating, "leaving must cancel the blink")
    RunLoop.current.run(until: Date().addingTimeInterval(0.25))
    precondition(animatedPet.face.strength == 0 && animatedPet.frame == stationaryFrames[.grok], "cancelled blink must not restart or move the body")
    for point in [NSPoint(x: 158, y: 210), NSPoint(x: 155, y: 64), NSPoint(x: 224, y: 106)] {
        precondition(jar.character(at: point) == nil, "decorative shapes must not route messages")
    }
    print("PASS: per-pet routing/drafts; recipient lock; URL/input guards; exclusive facial hover; stationary bodies; blink cancellation; actual artwork and eye patches fit inside glass.")
} else { let app = NSApplication.shared, delegate = AppDelegate(); app.setActivationPolicy(.accessory); app.delegate = delegate; app.run() }
