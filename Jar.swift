import AppKit

// One coordinate system for the vessel, contents, hit regions and optical layers.
enum JarGeometry {
    static let size = NSSize(width: 260, height: 260)
    static let sceneRect = NSRect(x: 25, y: 42, width: 210, height: 210)
    static let claudePlacement = NSRect(x: 0.55, y: 0.74, width: 0.40, height: 0.24)
    static var claudeRect: NSRect {
        NSRect(x: sceneRect.minX + claudePlacement.minX * sceneRect.width,
               y: sceneRect.maxY - claudePlacement.maxY * sceneRect.height,
               width: claudePlacement.width * sceneRect.width, height: claudePlacement.height * sceneRect.height)
    }
    static let frames = Dictionary(uniqueKeysWithValues: Client.allCases.map { ($0, PackedSceneRegion(client: $0).frame) })
    static let mouth = NSRect(x: 17, y: 234, width: 226, height: 24)
    // Fit the glass to the existing pile: narrower walls and a raised floor.
    // Art, facial patches and hit regions keep their original size and position.
    static var glassTransform: AffineTransform {
        var transform = AffineTransform()
        transform.translate(x: 130, y: 258)
        transform.scale(x: 0.90, y: 0.967)
        transform.translate(x: -130, y: -258)
        return transform
    }
    static func beginGlassDrawing() {
        NSGraphicsContext.saveGraphicsState()
        (glassTransform as NSAffineTransform).concat()
    }
    static func body() -> NSBezierPath {
        let p = NSBezierPath()
        p.move(to: NSPoint(x: 24, y: 246)); p.line(to: NSPoint(x: 236, y: 246))
        p.curve(to: NSPoint(x: 250, y: 219), controlPoint1: NSPoint(x: 235, y: 236), controlPoint2: NSPoint(x: 249, y: 235))
        p.curve(to: NSPoint(x: 251, y: 61), controlPoint1: NSPoint(x: 254, y: 170), controlPoint2: NSPoint(x: 254, y: 100))
        p.curve(to: NSPoint(x: 222, y: 24), controlPoint1: NSPoint(x: 250, y: 38), controlPoint2: NSPoint(x: 243, y: 27))
        p.curve(to: NSPoint(x: 38, y: 24), controlPoint1: NSPoint(x: 174, y: 16), controlPoint2: NSPoint(x: 86, y: 16))
        p.curve(to: NSPoint(x: 9, y: 61), controlPoint1: NSPoint(x: 17, y: 27), controlPoint2: NSPoint(x: 10, y: 38))
        p.curve(to: NSPoint(x: 10, y: 219), controlPoint1: NSPoint(x: 6, y: 100), controlPoint2: NSPoint(x: 6, y: 170))
        p.curve(to: NSPoint(x: 24, y: 246), controlPoint1: NSPoint(x: 11, y: 235), controlPoint2: NSPoint(x: 25, y: 236))
        p.close(); return p
    }
    static func interior() -> NSBezierPath {
        // The rear arc of the mouth is the top of the visible cavity.
        let p = NSBezierPath()
        p.move(to: NSPoint(x: 24, y: 246))
        p.curve(to: NSPoint(x: 236, y: 246), controlPoint1: NSPoint(x: 26, y: 259), controlPoint2: NSPoint(x: 234, y: 259))
        p.curve(to: NSPoint(x: 242, y: 218), controlPoint1: NSPoint(x: 234, y: 236), controlPoint2: NSPoint(x: 241, y: 231))
        p.curve(to: NSPoint(x: 243, y: 65), controlPoint1: NSPoint(x: 247, y: 165), controlPoint2: NSPoint(x: 247, y: 100))
        p.curve(to: NSPoint(x: 228, y: 38), controlPoint1: NSPoint(x: 243, y: 46), controlPoint2: NSPoint(x: 243, y: 40))
        p.curve(to: NSPoint(x: 32, y: 38), controlPoint1: NSPoint(x: 170, y: 30), controlPoint2: NSPoint(x: 90, y: 30))
        p.curve(to: NSPoint(x: 17, y: 65), controlPoint1: NSPoint(x: 17, y: 40), controlPoint2: NSPoint(x: 17, y: 46))
        p.curve(to: NSPoint(x: 18, y: 218), controlPoint1: NSPoint(x: 13, y: 100), controlPoint2: NSPoint(x: 13, y: 165))
        p.curve(to: NSPoint(x: 24, y: 246), controlPoint1: NSPoint(x: 19, y: 231), controlPoint2: NSPoint(x: 26, y: 236))
        p.close(); p.transform(using: glassTransform); return p
    }
}

private func glassColor(_ r: CGFloat, _ g: CGFloat, _ b: CGFloat, _ a: CGFloat) -> NSColor {
    NSColor(calibratedRed: r, green: g, blue: b, alpha: a)
}
private func glassGradient(_ path: NSBezierPath, _ colors: [NSColor], _ angle: CGFloat = 0) {
    NSGradient(colors: colors)?.draw(in: path, angle: angle)
}
private func glassStroke(_ path: NSBezierPath, _ color: NSColor, _ width: CGFloat) {
    color.setStroke(); path.lineWidth = width; path.lineCapStyle = .round; path.lineJoinStyle = .round; path.stroke()
}
private func lipRing() -> NSBezierPath {
    let p = NSBezierPath(ovalIn: JarGeometry.mouth); p.windingRule = .evenOdd
    p.append(NSBezierPath(ovalIn: JarGeometry.mouth.insetBy(dx: 5, dy: 4))); return p
}
private func drawLip() {
    glassGradient(lipRing(), [.white.withAlphaComponent(0.87), glassColor(0.31,0.46,0.48,0.54), .white.withAlphaComponent(0.94), glassColor(0.56,0.72,0.72,0.48), .white.withAlphaComponent(0.9)], 90)
    glassStroke(NSBezierPath(ovalIn: JarGeometry.mouth), glassColor(0.24,0.36,0.38,0.5), 0.6)
    glassStroke(NSBezierPath(ovalIn: JarGeometry.mouth.insetBy(dx: 0.9, dy: 0.8)), .white.withAlphaComponent(0.9), 0.9)
    glassStroke(NSBezierPath(ovalIn: JarGeometry.mouth.insetBy(dx: 5, dy: 4)), glassColor(0.26,0.44,0.46,0.47), 0.75)
    glassStroke(NSBezierPath(ovalIn: JarGeometry.mouth.insetBy(dx: 6, dy: 4.8)), .white.withAlphaComponent(0.63), 0.65)
}

final class JarBackView: NSView {
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
    override func draw(_ dirtyRect: NSRect) {
        JarGeometry.beginGlassDrawing()
        NSGraphicsContext.saveGraphicsState()
        let shadow = NSShadow(); shadow.shadowColor = NSColor.black.withAlphaComponent(0.24); shadow.shadowBlurRadius = 5; shadow.shadowOffset = NSSize(width: 0, height: -2); shadow.set()
        NSColor.black.withAlphaComponent(0.12).setFill(); NSBezierPath(ovalIn: NSRect(x: 23, y: 21, width: 214, height: 13)).fill()
        NSGraphicsContext.restoreGraphicsState()
        glassGradient(JarGeometry.body(), [glassColor(0.53,0.72,0.74,0.21), .white.withAlphaComponent(0.07), .white.withAlphaComponent(0.02), glassColor(0.48,0.65,0.69,0.23)])
        glassGradient(NSBezierPath(ovalIn: JarGeometry.mouth.insetBy(dx: 5, dy: 4)), [glassColor(0.35,0.47,0.47,0.17), .white.withAlphaComponent(0.08)], 90)
        NSGraphicsContext.saveGraphicsState(); NSBezierPath(rect: NSRect(x: 0,y: 246,width: 260,height: 14)).addClip(); drawLip(); NSGraphicsContext.restoreGraphicsState()
        NSGraphicsContext.restoreGraphicsState()
    }
}

// The cavity mask keeps the stationary pile and its facial patches inside the vessel.
final class JarContentsView: NSView {
    override init(frame: NSRect) {
        super.init(frame: frame); wantsLayer = true
        let mask = CAShapeLayer(); mask.path = JarGeometry.interior().cgPath; layer?.mask = mask
    }
    required init?(coder: NSCoder) { fatalError() }
}

final class JarGlassView: NSView {
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
    override func draw(_ dirtyRect: NSRect) {
        JarGeometry.beginGlassDrawing()
        NSGraphicsContext.saveGraphicsState(); JarGeometry.body().addClip()
        // Broad, low-contrast transmission with narrow colored absorption at the edges.
        glassGradient(JarGeometry.body(), [glassColor(0.30,0.53,0.57,0.35), .white.withAlphaComponent(0.28), .clear, .clear, .clear, .white.withAlphaComponent(0.13), glassColor(0.30,0.50,0.54,0.36)])
        // Light bends the same artwork through the thick curved side walls and bottom.
        let edges = NSBezierPath(rect: NSRect(x: 13, y: 48, width: 11, height: 183))
        edges.append(NSBezierPath(rect: NSRect(x: 236,y: 48,width: 11,height: 183)))
        NSGraphicsContext.saveGraphicsState(); edges.addClip()
        PackedSceneArt.image.draw(in: JarGeometry.sceneRect.insetBy(dx: -12, dy: -3), from: .zero, operation: .sourceOver, fraction: 0.23)
        NSGraphicsContext.restoreGraphicsState()
        let baseBand = NSBezierPath(ovalIn: NSRect(x: 12,y: 22,width: 236,height: 30)); baseBand.windingRule = .evenOdd
        baseBand.append(NSBezierPath(ovalIn: NSRect(x: 22,y: 32,width: 216,height: 20)))
        // Only the near half belongs in front; the rear arc would cut across the pets.
        NSGraphicsContext.saveGraphicsState()
        NSBezierPath(rect: NSRect(x: 0,y: 0,width: 260,height: 37)).addClip()
        glassGradient(baseBand, [glassColor(0.32,0.53,0.55,0.4), .white.withAlphaComponent(0.72), glassColor(0.66,0.80,0.79,0.38), .white.withAlphaComponent(0.72)], 90)
        NSGraphicsContext.restoreGraphicsState()
        let caustic = NSBezierPath(); caustic.move(to: NSPoint(x: 30,y: 31)); caustic.curve(to: NSPoint(x: 230,y: 31),controlPoint1: NSPoint(x: 75,y: 19),controlPoint2: NSPoint(x: 185,y: 19))
        glassStroke(caustic,.white.withAlphaComponent(0.92),1.4)
        // Curved softbox reflections have width, falloff and a crisp outer edge.
        for (left, alpha) in [(true, CGFloat(0.58)), (false, CGFloat(0.30))] {
            let x: CGFloat = left ? 21 : 239
            let sign: CGFloat = left ? 1 : -1
            let ribbon = NSBezierPath(); ribbon.move(to: NSPoint(x: x,y: 220))
            ribbon.curve(to: NSPoint(x: x-2*sign,y: 77),controlPoint1: NSPoint(x: x-6*sign,y: 184),controlPoint2: NSPoint(x: x-6*sign,y: 111))
            ribbon.curve(to: NSPoint(x: x+5*sign,y: 72),controlPoint1: NSPoint(x: x,y: 69),controlPoint2: NSPoint(x: x+5*sign,y: 68))
            ribbon.curve(to: NSPoint(x: x+8*sign,y: 219),controlPoint1: NSPoint(x: x+2*sign,y: 118),controlPoint2: NSPoint(x: x+2*sign,y: 184)); ribbon.close()
            glassGradient(ribbon,[.white.withAlphaComponent(alpha),.white.withAlphaComponent(0.02)],left ? 0 : 180)
            let glint = NSBezierPath(); glint.move(to: NSPoint(x: x,y: 214)); glint.curve(to: NSPoint(x: x-2*sign,y: 113),controlPoint1: NSPoint(x: x-5*sign,y: 185),controlPoint2: NSPoint(x: x-4*sign,y: 144))
            glassStroke(glint,.white.withAlphaComponent(left ? 0.88 : 0.65),left ? 1.3 : 0.9)
        }
        NSGraphicsContext.restoreGraphicsState()
        // Separate outer and inner contours expose the wall thickness.
        let walls = NSBezierPath(); walls.move(to: NSPoint(x: 24,y: 245)); walls.curve(to: NSPoint(x: 10,y: 218),controlPoint1: NSPoint(x: 24,y: 234),controlPoint2: NSPoint(x: 11,y: 235)); walls.curve(to: NSPoint(x: 9,y: 61),controlPoint1: NSPoint(x: 6,y: 170),controlPoint2: NSPoint(x: 6,y: 100)); walls.curve(to: NSPoint(x: 38,y: 24),controlPoint1: NSPoint(x: 10,y: 38),controlPoint2: NSPoint(x: 17,y: 27)); walls.curve(to: NSPoint(x: 222,y: 24),controlPoint1: NSPoint(x: 86,y: 16),controlPoint2: NSPoint(x: 174,y: 16)); walls.curve(to: NSPoint(x: 251,y: 61),controlPoint1: NSPoint(x: 243,y: 27),controlPoint2: NSPoint(x: 250,y: 38)); walls.curve(to: NSPoint(x: 250,y: 218),controlPoint1: NSPoint(x: 254,y: 100),controlPoint2: NSPoint(x: 254,y: 170)); walls.curve(to: NSPoint(x: 236,y: 245),controlPoint1: NSPoint(x: 249,y: 235),controlPoint2: NSPoint(x: 236,y: 234))
        glassStroke(walls,glassColor(0.27,0.40,0.42,0.43),1.0)
        NSGraphicsContext.saveGraphicsState(); let shift=NSAffineTransform(); shift.translateX(by: 130,yBy: 130); shift.scaleX(by: 0.99,yBy: 0.99); shift.translateX(by: -130,yBy: -130); shift.concat(); glassStroke(walls,.white.withAlphaComponent(0.9),1.1); NSGraphicsContext.restoreGraphicsState()
        NSGraphicsContext.saveGraphicsState(); NSBezierPath(rect: NSRect(x: 0,y: 230,width: 260,height: 16)).addClip(); drawLip(); NSGraphicsContext.restoreGraphicsState()
        NSGraphicsContext.restoreGraphicsState()
    }
}

final class PetJarView: NSView {
    var characters: [Client: CharacterView] = [:]
    var selected: Client?
    var chosen: ((Client) -> Void)?, opened: ((Client) -> Void)?, moved: (() -> Void)?
    private var lastClick: Client?
    private let quotaStore = AccountUsageStore()
    private let usagePopover = NSPopover()
    private let usageLabel = NSTextField(wrappingLabelWithString: "")
    private var usageRefreshedAt = Date.distantPast
    var startMouse = NSPoint.zero, startWindow = NSPoint.zero
    var dragged = false
    private var hoverTracking: NSTrackingArea?
    private var hoverTimer: Timer?
    // Used only by local visual QA; ordinary launches always follow the real pointer.
    var previewHover: Client?
    private(set) var hoveredClient: Client?
    let hint = NSTextField(labelWithString: "双击打开 · 悬停看额度")
    override init(frame: NSRect) {
        super.init(frame: frame); wantsLayer = true; addSubview(JarBackView(frame: bounds))
        let contents = JarContentsView(frame: bounds); addSubview(contents)
        contents.addSubview(ScenePlateView(frame: bounds))
        for client in Client.allCases { let character = CharacterView(client: client, frame: JarGeometry.frames[client]!); character.jar = self; contents.addSubview(character); characters[client] = character }
        addSubview(JarGlassView(frame: bounds))
        hint.frame = NSRect(x: 25, y: 3, width: 210, height: 16); hint.alignment = .center; hint.font = .systemFont(ofSize: 10, weight: .medium); hint.textColor = .secondaryLabelColor; addSubview(hint)
        let controller = NSViewController(); controller.view = NSView(frame: NSRect(x: 0, y: 0, width: 284, height: 180))
        usageLabel.font = .systemFont(ofSize: 11); usageLabel.isSelectable = false
        controller.view.addSubview(usageLabel); usagePopover.contentViewController = controller
        usagePopover.behavior = .applicationDefined; usagePopover.animates = false
    }
    required init?(coder: NSCoder) { fatalError() }
    deinit { hoverTimer?.invalidate() }
    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow(); hoverTimer?.invalidate(); hoverTimer = nil
        guard window != nil else { return }
        // Also reconcile a stationary/warped pointer, when no mouseMoved event was delivered.
        let timer = Timer(timeInterval: 0.05, repeats: true) { [weak self] _ in self?.refreshPointerHover() }
        RunLoop.main.add(timer, forMode: .common); hoverTimer = timer
    }
    private func refreshPointerHover() {
        guard let window, window.isVisible, !dragged else { updateHover(at: nil); return }
        if let previewHover, let frame = JarGeometry.frames[previewHover] {
            updateHover(at: NSPoint(x: frame.midX, y: frame.midY)); return
        }
        let screenPoint = NSEvent.mouseLocation
        let point = convert(window.convertPoint(fromScreen: screenPoint), from: nil)
        guard bounds.contains(point), NSWindow.windowNumber(at: screenPoint, belowWindowWithWindowNumber: 0) == window.windowNumber else { updateHover(at: nil); return }
        updateHover(at: point)
    }
    override func updateTrackingAreas() {
        if let hoverTracking { removeTrackingArea(hoverTracking) }
        let area = NSTrackingArea(rect: bounds, options: [.mouseEnteredAndExited, .mouseMoved, .activeAlways, .inVisibleRect], owner: self)
        addTrackingArea(area); hoverTracking = area; super.updateTrackingAreas()
    }
    func character(at point: NSPoint) -> CharacterView? {
        guard bounds.contains(point), JarGeometry.interior().contains(point) else { return nil }
        for client in Client.allCases.reversed() {
            if let character = characters[client], character.containsOpaquePoint(character.convert(point, from: self)) { return character }
        }
        return nil
    }
    override func hitTest(_ point: NSPoint) -> NSView? {
        let local = convert(point, from: superview)
        guard bounds.contains(local) else { return nil }
        return character(at: local) ?? self
    }
    func updateHover(at point: NSPoint?) {
        let next = point.flatMap { character(at: $0)?.client }
        guard hoveredClient != next else {
            if let next, Date().timeIntervalSince(usageRefreshedAt) > 45 { showQuota(next) }; return
        }
        if let previous = hoveredClient { characters[previous]?.setHovered(false) }
        hoveredClient = next
        if let next { characters[next]?.setHovered(true); NSCursor.pointingHand.set() } else { NSCursor.arrow.set() }
        hint.stringValue = next?.name ?? selected?.name ?? "双击打开 · 悬停看额度"
        usagePopover.close()
        if let next { showQuota(next) }
    }
    override func mouseEntered(with event: NSEvent) { mouseMoved(with: event) }
    override func mouseMoved(with event: NSEvent) { if !dragged { updateHover(at: convert(event.locationInWindow, from: nil)) } }
    override func mouseExited(with event: NSEvent) { updateHover(at: nil) }
    func choose(_ client: Client) { chosen?(client) }
    func open(_ client: Client) { usagePopover.close(); opened?(client) }
    func finishClick(_ client: Client, count: Int) {
        if count == 2, lastClick == client { lastClick = nil; open(client) } else { lastClick = client }
    }
    private func showQuota(_ client: Client) {
        guard window?.isVisible == true else { return }
        usageRefreshedAt = Date()
        func display(_ text: String) {
            usageLabel.stringValue = client.name + " · 账号额度\n\n" + text + "\n\n双击打开客户端"
            let height = min(430, usageLabel.cell!.cellSize(forBounds: NSRect(x: 0, y: 0, width: 256, height: 1000)).height + 28)
            usagePopover.contentSize = NSSize(width: 284, height: height)
            usageLabel.frame = NSRect(x: 14, y: 14, width: 256, height: height - 28)
            if !usagePopover.isShown { usagePopover.show(relativeTo: JarGeometry.frames[client]!, of: self, preferredEdge: .minX) }
        }
        display("正在读取账号额度…")
        quotaStore.load(client) { [weak self] snapshot in
            guard let self, self.hoveredClient == client, self.window?.isVisible == true else { return }
            display(snapshot.text)
        }
    }
    func setActive(_ client: Client?) {
        selected = client
        for (key, character) in characters { character.selected = key == client }
        if let client { characters[client]?.react(); hint.stringValue = client.name } else { hint.stringValue = "双击打开 · 悬停看额度" }
    }
    func beginDrag() { startMouse = NSEvent.mouseLocation; startWindow = window?.frame.origin ?? .zero; dragged = false }
    func continueDrag() {
        let p = NSEvent.mouseLocation, dx = p.x - startMouse.x, dy = p.y - startMouse.y
        if abs(dx) + abs(dy) > 4 { dragged = true; updateHover(at: nil) }; guard dragged else { return }
        window?.setFrameOrigin(NSPoint(x: startWindow.x + dx, y: startWindow.y + dy)); moved?()
    }
    func endDrag() { lastClick = nil; clamp(); moved?(); dragged = false }
    func clamp() {
        guard let window, let screen = NSScreen.screens.first(where: { $0.frame.intersects(window.frame) }) ?? NSScreen.main else { return }
        let r = screen.visibleFrame; window.setFrameOrigin(NSPoint(x: min(max(window.frame.minX, r.minX), r.maxX - window.frame.width), y: min(max(window.frame.minY, r.minY), r.maxY - window.frame.height)))
    }
    override func mouseDown(with event: NSEvent) { lastClick = nil; beginDrag() }
    override func mouseDragged(with event: NSEvent) { continueDrag() }
    override func mouseUp(with event: NSEvent) { if dragged { endDrag() } }
    override func rightMouseDown(with event: NSEvent) {
        let menu = NSMenu()
        for (index, client) in Client.allCases.enumerated() { let item = NSMenuItem(title: "和 \(client.name) 说话", action: #selector(pickFromMenu(_:)), keyEquivalent: ""); item.tag = index; item.target = self; menu.addItem(item) }
        menu.addItem(.separator()); menu.addItem(withTitle: "退出宠物罐", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"); NSMenu.popUpContextMenu(menu, with: event, for: self)
    }
    @objc func pickFromMenu(_ item: NSMenuItem) { choose(Client.allCases[item.tag]) }
}
