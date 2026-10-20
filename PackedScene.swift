import AppKit

enum PackedSceneArt {
    static let image = NSImage(contentsOf: Bundle.main.resourceURL!.appendingPathComponent("pile-scene.png"))!
}

// The same prepared opaque face plates and isolated eye sprites are consumed
// by both native renderers. Regenerate with --export-claude-eyes after changing
// the Claude source image or its scene coordinates; no conversation paths needed.
enum ClaudeArt {
    private struct EyeSpec: Decodable {
        let name: String
        let eye: [CGFloat]
        let openRect: [CGFloat]
        let closedRect: [CGFloat]
    }
    private struct Manifest: Decodable {
        let sourceCrop: [Int]
        let eyes: [EyeSpec]
    }
    private struct EyeDrawing {
        let rect: NSRect, openRect: NSRect, closedRect: NSRect
        let face: NSImage, opened: NSImage, closed: NSImage
    }
    private static let manifest = try! JSONDecoder().decode(Manifest.self, from: Data(contentsOf: Bundle.main.resourceURL!.appendingPathComponent("claude-eyes.json")))
    static let image: NSImage = {
        let raw = NSImage(contentsOf: Bundle.main.resourceURL!.appendingPathComponent("pet-claude.png"))!
        let crop = manifest.sourceCrop
        let cg = raw.cgImage(forProposedRect: nil, context: nil, hints: nil)!
        let trimmed = cg.cropping(to: CGRect(x: CGFloat(crop[0]), y: CGFloat(crop[1]), width: CGFloat(crop[2]), height: CGFloat(crop[3])))!
        return NSImage(cgImage: trimmed, size: NSSize(width: CGFloat(crop[2]), height: CGFloat(crop[3])))
    }()
    private static let hitBitmap = NSBitmapImageRep(data: image.tiffRepresentation!)!
    static func contains(_ point: NSPoint) -> Bool {
        let frame = JarGeometry.claudeRect
        guard frame.contains(point) else { return false }
        let x = min(hitBitmap.pixelsWide - 1, max(0, Int((point.x - frame.minX) / frame.width * CGFloat(hitBitmap.pixelsWide))))
        let y = min(hitBitmap.pixelsHigh - 1, max(0, Int((frame.maxY - point.y) / frame.height * CGFloat(hitBitmap.pixelsHigh))))
        return (hitBitmap.colorAt(x: x, y: y)?.alphaComponent ?? 0) > 0.25
    }
    private static func rect(_ a: [CGFloat]) -> NSRect {
        let scene = JarGeometry.sceneRect
        return NSRect(x: scene.minX + a[0] * scene.width, y: scene.maxY - (a[1] + a[3]) * scene.height,
                      width: a[2] * scene.width, height: a[3] * scene.height)
    }
    private static let eyes: [EyeDrawing] = manifest.eyes.map { spec in
        func asset(_ suffix: String) -> NSImage { NSImage(contentsOf: Bundle.main.resourceURL!.appendingPathComponent(spec.name + suffix + ".png"))! }
        return EyeDrawing(rect: rect(spec.eye), openRect: rect(spec.openRect), closedRect: rect(spec.closedRect),
                          face: asset("-face"), opened: asset("-open"), closed: asset("-closed"))
    }
    static func drawEyes(closure: CGFloat, sceneOrigin: NSPoint) {
        for eye in eyes {
            eye.face.draw(in: eye.rect.offsetBy(dx: -sceneOrigin.x, dy: -sceneOrigin.y), from: .zero, operation: .sourceOver, fraction: 1)
            let isOpen = closure < 0.72
            let amount = max(0, min(1, isOpen ? 1 - closure / 0.72 : (closure - 0.72) / 0.28))
            let sourceRect = isOpen ? eye.openRect : eye.closedRect
            let height = sourceRect.height * amount
            let target = NSRect(x: sourceRect.minX - sceneOrigin.x, y: sourceRect.midY - height / 2 - sceneOrigin.y, width: sourceRect.width, height: height)
            if height > 0 { (isOpen ? eye.opened : eye.closed).draw(in: target, from: .zero, operation: .sourceOver, fraction: 1) }
        }
    }
}

// Visible parts of the three characters, traced in the cohesive render's normalized coordinates.
// Coordinates are measured from its upper-left corner; decorative objects are not interactive.
struct PackedSceneRegion {
    let client: Client
    var points: [NSPoint] {
        let raw: [(CGFloat, CGFloat)]
        switch client {
        case .claude:
            raw = [(0.555,0.800),(0.558,0.770),(0.575,0.760),(0.590,0.780),(0.600,0.745),(0.630,0.750),(0.650,0.780),(0.680,0.755),(0.730,0.746),(0.780,0.750),(0.840,0.780),(0.870,0.745),(0.900,0.750),(0.910,0.780),(0.930,0.760),(0.945,0.780),(0.943,0.820),(0.920,0.850),(0.900,0.870),(0.920,0.910),(0.900,0.940),(0.890,0.970),(0.840,0.980),(0.820,0.950),(0.680,0.950),(0.650,0.980),(0.600,0.970),(0.590,0.940),(0.570,0.920),(0.600,0.870),(0.580,0.850),(0.560,0.830)]
        case .chatgpt:
            raw = [(0.142,0.315),(0.162,0.273),(0.191,0.227),(0.211,0.190),(0.234,0.134),(0.271,0.082),(0.305,0.066),(0.342,0.064),(0.385,0.070),(0.423,0.091),(0.456,0.125),(0.485,0.173),(0.512,0.211),(0.545,0.263),(0.562,0.287),(0.547,0.327),(0.540,0.357),(0.511,0.365),(0.504,0.376),(0.511,0.396),(0.498,0.412),(0.469,0.414),(0.452,0.428),(0.435,0.444),(0.395,0.444),(0.354,0.449),(0.328,0.458),(0.300,0.451),(0.272,0.452),(0.240,0.436),(0.215,0.431),(0.184,0.413),(0.154,0.397),(0.142,0.364)]
        case .grok:
            raw = [(0.544,0.444),(0.568,0.403),(0.603,0.368),(0.650,0.340),(0.704,0.323),(0.758,0.320),(0.814,0.335),(0.860,0.367),(0.893,0.412),(0.913,0.465),(0.917,0.519),(0.902,0.567),(0.880,0.610),(0.854,0.635),(0.820,0.667),(0.783,0.674),(0.747,0.668),(0.713,0.669),(0.684,0.657),(0.658,0.654),(0.635,0.636),(0.619,0.615),(0.588,0.602),(0.570,0.576),(0.557,0.551),(0.548,0.518),(0.536,0.495),(0.550,0.469)]
        case .muse:
            raw = [(0.141,0.631),(0.166,0.595),(0.198,0.566),(0.244,0.540),(0.291,0.524),(0.342,0.519),(0.391,0.525),(0.440,0.542),(0.481,0.569),(0.516,0.604),(0.542,0.648),(0.561,0.691),(0.565,0.735),(0.555,0.773),(0.565,0.797),(0.553,0.826),(0.531,0.849),(0.503,0.858),(0.479,0.858),(0.456,0.869),(0.431,0.895),(0.410,0.911),(0.378,0.903),(0.352,0.889),(0.331,0.886),(0.312,0.874),(0.276,0.877),(0.255,0.854),(0.239,0.835),(0.213,0.820),(0.184,0.795),(0.165,0.766),(0.149,0.729),(0.141,0.685)]
        }
        let r = JarGeometry.sceneRect
        return raw.map { NSPoint(x: r.minX + $0.0 * r.width, y: r.minY + (1 - $0.1) * r.height) }
    }
    var path: NSBezierPath {
        let p = NSBezierPath(); let points = points
        p.move(to: points[0])
        for i in points.indices {
            let previous = points[(i + points.count - 1) % points.count], current = points[i]
            let next = points[(i + 1) % points.count], following = points[(i + 2) % points.count]
            let c1 = NSPoint(x: current.x + (next.x - previous.x) / 6, y: current.y + (next.y - previous.y) / 6)
            let c2 = NSPoint(x: next.x - (following.x - current.x) / 6, y: next.y - (following.y - current.y) / 6)
            p.curve(to: next, controlPoint1: c1, controlPoint2: c2)
        }
        p.close(); return p
    }
    var frame: NSRect { path.bounds.insetBy(dx: -1, dy: -1) }
    func localPath() -> NSBezierPath {
        let p = path, transform = AffineTransform(translationByX: -frame.minX, byY: -frame.minY)
        p.transform(using: transform); return p
    }
}

// The pile is always a single stationary image. Only small eye regions can change.
final class ScenePlateView: NSView {
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
    override func draw(_ dirtyRect: NSRect) {
        PackedSceneArt.image.draw(in: JarGeometry.sceneRect, from: .zero, operation: .sourceOver, fraction: 1)
        ClaudeArt.image.draw(in: JarGeometry.claudeRect, from: .zero, operation: .sourceOver, fraction: 1)
    }
}

enum FacialExpressionArt {
    static let image = NSImage(contentsOf: Bundle.main.resourceURL!.appendingPathComponent("pile-expressions.png"))!
    static func eyeRects(for client: Client) -> [NSRect] {
        // Small feathered patches measured in the unchanged original image, from top-left.
        let raw: [NSRect]
        switch client {
        case .claude: raw = [NSRect(x: 0.672,y: 0.818,width: 0.054,height: 0.048), NSRect(x: 0.773,y: 0.818,width: 0.054,height: 0.048)]
        case .chatgpt: raw = [NSRect(x: 0.229,y: 0.194,width: 0.079,height: 0.075), NSRect(x: 0.351,y: 0.180,width: 0.082,height: 0.076)]
        case .grok: raw = [NSRect(x: 0.612,y: 0.406,width: 0.129,height: 0.199), NSRect(x: 0.740,y: 0.403,width: 0.132,height: 0.200)]
        case .muse: raw = [NSRect(x: 0.247,y: 0.683,width: 0.082,height: 0.073), NSRect(x: 0.404,y: 0.658,width: 0.082,height: 0.071)]
        }
        let scene = JarGeometry.sceneRect
        return raw.map { NSRect(x: scene.minX + $0.minX * scene.width, y: scene.maxY - $0.maxY * scene.height, width: $0.width * scene.width, height: $0.height * scene.height) }
    }
}

final class FacialExpressionView: NSView {
    let client: Client
    let sceneOrigin: NSPoint
    var strength: CGFloat = 0 { didSet { needsDisplay = true } }
    init(client: Client, frame: NSRect, sceneOrigin: NSPoint) {
        self.client = client; self.sceneOrigin = sceneOrigin
        super.init(frame: frame); wantsLayer = true
    }
    required init?(coder: NSCoder) { fatalError() }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
    override func draw(_ dirtyRect: NSRect) {
        if client == .claude { ClaudeArt.drawEyes(closure: strength, sceneOrigin: sceneOrigin); return }
        guard strength > 0, let context = NSGraphicsContext.current?.cgContext else { return }
        let colors = [CGColor(red: 1,green: 1,blue: 1,alpha: 1), CGColor(red: 1,green: 1,blue: 1,alpha: 1), CGColor(red: 1,green: 1,blue: 1,alpha: 0)]
        let feather = CGGradient(colorsSpace: CGColorSpaceCreateDeviceRGB(),colors: colors as CFArray,locations: [0,0.8,1])!
        for sceneEye in FacialExpressionArt.eyeRects(for: client) {
            let eye = sceneEye.offsetBy(dx: -sceneOrigin.x,dy: -sceneOrigin.y)
            context.saveGState(); context.clip(to: eye); context.beginTransparencyLayer(auxiliaryInfo: nil)
            FacialExpressionArt.image.draw(in: JarGeometry.sceneRect.offsetBy(dx: -sceneOrigin.x,dy: -sceneOrigin.y), from: .zero, operation: .sourceOver, fraction: strength)
            context.setBlendMode(.destinationIn)
            context.saveGState(); context.translateBy(x: eye.midX,y: eye.midY); context.scaleBy(x: eye.width / 2,y: eye.height / 2)
            context.drawRadialGradient(feather,startCenter: .zero,startRadius: 0,endCenter: .zero,endRadius: 1,options: [.drawsBeforeStartLocation,.drawsAfterEndLocation])
            context.restoreGState(); context.endTransparencyLayer(); context.restoreGState()
        }
    }
}

final class CharacterView: NSView {
    let client: Client
    let region: PackedSceneRegion
    let face: FacialExpressionView
    weak var jar: PetJarView?
    // Deterministic visual QA only; ordinary launches animate short blinks.
    var freezeExpression = false
    private(set) var hovering = false
    private var blinkTimer: Timer?
    private var expressionTimer: Timer?
    var expressionIsAnimating: Bool { expressionTimer != nil }
    init(client: Client, frame: NSRect) {
        self.client = client; region = PackedSceneRegion(client: client)
        face = FacialExpressionView(client: client,frame: NSRect(origin: .zero,size: frame.size),sceneOrigin: frame.origin)
        super.init(frame: frame); wantsLayer = true; addSubview(face)
        setAccessibilityElement(true); setAccessibilityRole(.button)
        let appearance: String
        switch client { case .chatgpt: appearance = "黄色眼镜领结宠物"; case .grok: appearance = "黑色亮球"; case .muse: appearance = "毛绒宠物"; case .claude: appearance = "暖橘色小螃蟹" }
        setAccessibilityLabel("\(client.name) \(appearance)，双击打开客户端，悬停查看账号额度")
        toolTip = nil
    }
    required init?(coder: NSCoder) { fatalError() }
    deinit { blinkTimer?.invalidate(); expressionTimer?.invalidate() }
    override func viewDidMoveToWindow() { super.viewDidMoveToWindow(); if window == nil { blinkTimer?.invalidate(); blinkTimer = nil; expressionTimer?.invalidate(); expressionTimer = nil } }
    override func accessibilityPerformPress() -> Bool { jar?.open(client); return true }
    func containsOpaquePoint(_ point: NSPoint) -> Bool {
        client == .claude ? ClaudeArt.contains(NSPoint(x: point.x + frame.minX, y: point.y + frame.minY)) : region.localPath().contains(point)
    }
    override func hitTest(_ point: NSPoint) -> NSView? { containsOpaquePoint(convert(point, from: superview)) ? self : nil }
    func setHovered(_ value: Bool) {
        guard hovering != value else { return }; hovering = value
        setAccessibilityValue(value ? "鼠标悬停中 · 面部表情" : "")
        blinkTimer?.invalidate(); blinkTimer = nil; expressionTimer?.invalidate(); expressionTimer = nil
        guard value else { face.strength = 0; return }
        if freezeExpression || NSWorkspace.shared.accessibilityDisplayShouldReduceMotion { face.strength = 1; return }
        // Yellow normally rests with closed eyes, so it opens them to greet the pointer.
        face.strength = client == .chatgpt ? 1 : 0
        if client != .chatgpt { blink() }
        let timer = Timer(timeInterval: 2.8,repeats: true) { [weak self] _ in self?.blink() }
        RunLoop.main.add(timer,forMode: .common); blinkTimer = timer
    }
    private func blink() {
        guard hovering, !freezeExpression else { return }
        animateEyes(resting: client == .chatgpt ? 1 : 0,duration: 0.34)
    }
    private func animateEyes(resting: CGFloat, duration: Double) {
        expressionTimer?.invalidate()
        let start = ProcessInfo.processInfo.systemUptime
        let timer = Timer(timeInterval: 1.0 / 60,repeats: true) { [weak self] timer in
            guard let self else { timer.invalidate(); return }
            let t = (ProcessInfo.processInfo.systemUptime - start) / duration
            guard t < 1 else { self.face.strength = resting; timer.invalidate(); self.expressionTimer = nil; return }
            let fraction: CGFloat
            if t < 0.28 { fraction = CGFloat(t / 0.28) }
            else if t < 0.60 { fraction = 1 }
            else { fraction = CGFloat((1 - t) / 0.40) }
            let eased = fraction * fraction * (3 - 2 * fraction)
            self.face.strength = resting == 1 ? 1 - eased : eased
        }
        expressionTimer = timer; RunLoop.main.add(timer,forMode: .common)
    }
    override func mouseMoved(with event: NSEvent) { jar?.mouseMoved(with: event) }
    override func mouseDown(with event: NSEvent) { jar?.beginDrag() }
    override func mouseDragged(with event: NSEvent) { jar?.continueDrag() }
    override func mouseUp(with event: NSEvent) { if jar?.dragged == true { jar?.endDrag() } else { jar?.finishClick(client, count: event.clickCount) } }
    override func rightMouseDown(with event: NSEvent) { jar?.rightMouseDown(with: event) }
}
