import AppKit
import QuartzCore

final class FloatingPanel: NSPanel { override var canBecomeKey: Bool { true }; override var canBecomeMain: Bool { false } }
final class AppDelegate: NSObject, NSApplicationDelegate {
    var pet: FloatingPanel!, petView: PetJarView!, statusItem: NSStatusItem!
    func applicationDidFinishLaunching(_ notification: Notification) {
        let menu = NSMenu(); menu.addItem(withTitle: "退出宠物罐", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        let main = NSMenu(); let item = NSMenuItem(); item.submenu = menu; main.addItem(item); NSApp.mainMenu = main
        pet = panel(size: JarGeometry.size); pet.title = "宠物罐 · 多材质智能体"
        petView = PetJarView(frame: NSRect(origin: .zero, size: JarGeometry.size)); pet.contentView = petView
        if CommandLine.arguments.contains("--freeze-expression") { petView.characters.values.forEach { $0.freezeExpression = true } }
        if let i = CommandLine.arguments.firstIndex(of: "--preview-hover"), CommandLine.arguments.indices.contains(i + 1) { petView.previewHover = Client(rawValue: CommandLine.arguments[i + 1]) }
        let screen = NSScreen.main!.visibleFrame, defaults = UserDefaults.standard
        pet.setFrameOrigin(NSPoint(x: defaults.object(forKey: "jarX") as? Double ?? screen.maxX - JarGeometry.size.width - 25, y: defaults.object(forKey: "jarY") as? Double ?? screen.minY + 35)); petView.clamp()
        petView.moved = { [weak self] in self?.petMoved() }
        petView.opened = { [weak self] client in self?.openClient(client) }
        pet.orderFrontRegardless(); setupStatusItem()
    }
    func panel(size: NSSize) -> FloatingPanel {
        let p = FloatingPanel(contentRect: NSRect(origin: .zero, size: size), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        p.isOpaque = false; p.backgroundColor = .clear; p.hasShadow = false; p.level = .floating
        p.acceptsMouseMovedEvents = true
        p.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]; p.isReleasedWhenClosed = false; p.hidesOnDeactivate = false; return p
    }
    func openClient(_ client: Client) {
        guard let url = client.applicationURL else {
            let alert = NSAlert(); alert.messageText = "没有找到 " + client.name
            alert.informativeText = "请先安装对应客户端，再双击宠物打开。"; alert.runModal(); return
        }
        let configuration = NSWorkspace.OpenConfiguration(); configuration.activates = true
        NSWorkspace.shared.openApplication(at: url, configuration: configuration) { app, error in
            if app == nil || error != nil {
                DispatchQueue.main.async {
                    let alert = NSAlert(); alert.messageText = client.name + " 打开失败"
                    alert.informativeText = "请检查客户端安装状态。"; alert.runModal()
                }
            }
        }
    }
    func petMoved() { UserDefaults.standard.set(pet.frame.minX, forKey: "jarX"); UserDefaults.standard.set(pet.frame.minY, forKey: "jarY") }
    @objc func resetPosition() { let frame = NSScreen.main!.visibleFrame; pet.setFrameOrigin(NSPoint(x: frame.maxX - JarGeometry.size.width - 25, y: frame.minY + 35)); petMoved(); pet.orderFrontRegardless() }
    func setupStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem.button?.image = NSImage(systemSymbolName: "pill.bottle", accessibilityDescription: "宠物罐")
        let menu = NSMenu()
        let reset = NSMenuItem(title: "找回宠物罐", action: #selector(resetPosition), keyEquivalent: ""); reset.target = self; menu.addItem(reset)
        menu.addItem(.separator()); menu.addItem(withTitle: "退出宠物罐", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"); statusItem.menu = menu
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
    for client in Client.allCases { precondition(FileManager.default.fileExists(atPath: Bundle.main.resourceURL!.appendingPathComponent(client.spriteName + ".png").path)) }
    let _ = NSApplication.shared
    let jar = PetJarView(frame: NSRect(origin: .zero, size: JarGeometry.size))
    let stationaryFrames = jar.characters.mapValues { $0.frame }
    jar.characters.values.forEach { $0.freezeExpression = true }
    for (point, target) in [(NSPoint(x: 99, y: 197), Client.chatgpt), (NSPoint(x: 180, y: 145), Client.grok), (NSPoint(x: 101, y: 99), Client.muse), (NSPoint(x: 182, y: 79), Client.claude)] {
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
        precondition(jar.character(at: point) == nil, "decorative shapes must not open clients")
    }
    print("PASS: per-pet client routing; exclusive facial hover; stationary bodies; blink cancellation; actual artwork and eye patches fit inside glass.")
} else { let app = NSApplication.shared, delegate = AppDelegate(); app.setActivationPolicy(.accessory); app.delegate = delegate; app.run() }
