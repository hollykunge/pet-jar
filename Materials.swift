import AppKit

struct MaterialPlacement {
    let name: String
    let frame: NSRect
    let angle: CGFloat
    init(_ name: String, _ x: CGFloat, _ y: CGFloat, _ w: CGFloat, _ h: CGFloat, _ angle: CGFloat = 0) {
        self.name = name; self.frame = NSRect(x: x, y: y, width: w, height: h); self.angle = angle
    }
    static let back: [MaterialPlacement] = [
        .init("knit", 58, 175, 77, 76, -12),
        .init("bubble", 132, 180, 74, 74, 18),
        .init("stone", 166, 135, 70, 80, 8),
        .init("moss", 32, 142, 72, 79, -10),
        .init("wood", 154, 83, 84, 80, 8),
        .init("bubble", 92, 135, 77, 77),
        .init("amber", 25, 87, 82, 85, -12),
        .init("blue", 84, 45, 83, 80, 14),
        .init("bubble", 176, 26, 63, 66),
        .init("stone", 32, 26, 74, 74, -9)
    ]
    static let front: [MaterialPlacement] = [
        .init("chrome", 18, 105, 92, 86, -12),
        .init("knit", 57, 18, 81, 82, 16),
        .init("bubble", 120, 17, 77, 76, -8),
        .init("moss", 166, 42, 72, 74),
        .init("blue", 26, 51, 58, 65, -12),
        .init("amber", 190, 96, 50, 58, -8)
    ]
    static var assetNames: Set<String> { Set((back + front).map { "prop-" + $0.name }) }
}

final class MaterialView: NSView {
    let imageView = NSImageView()
    let image: NSImage
    let bitmap: NSBitmapImageRep?
    init(_ placement: MaterialPlacement) {
        image = NSImage(contentsOf: Bundle.main.resourceURL!.appendingPathComponent("prop-\(placement.name).png")) ?? NSImage()
        if let cg = image.cgImage(forProposedRect: nil, context: nil, hints: nil) { bitmap = NSBitmapImageRep(cgImage: cg) } else { bitmap = nil }
        super.init(frame: placement.frame); wantsLayer = true
        imageView.frame = bounds; imageView.image = image; imageView.imageScaling = .scaleProportionallyUpOrDown
        imageView.wantsLayer = true; addSubview(imageView); imageView.frameCenterRotation = placement.angle
        imageView.setAccessibilityElement(false); imageView.setAccessibilityHidden(true)
        imageView.layer?.shadowColor = NSColor.black.cgColor; imageView.layer?.shadowOpacity = 0.18
        imageView.layer?.shadowRadius = 2; imageView.layer?.shadowOffset = CGSize(width: 0, height: -1)
        setAccessibilityElement(false); setAccessibilityHidden(true)
    }
    required init?(coder: NSCoder) { fatalError() }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
    func containsOpaquePoint(_ point: NSPoint) -> Bool {
        guard image.size.width > 0, image.size.height > 0, let bitmap else { return false }
        let p = imageView.convert(point, from: self), b = imageView.bounds
        let scale = min(b.width / image.size.width, b.height / image.size.height)
        let size = NSSize(width: image.size.width * scale, height: image.size.height * scale)
        let r = NSRect(x: b.midX - size.width / 2, y: b.midY - size.height / 2, width: size.width, height: size.height)
        guard r.contains(p) else { return false }
        let x = min(bitmap.pixelsWide - 1, max(0, Int((p.x - r.minX) / r.width * CGFloat(bitmap.pixelsWide))))
        let y = min(bitmap.pixelsHigh - 1, max(0, bitmap.pixelsHigh - 1 - Int((p.y - r.minY) / r.height * CGFloat(bitmap.pixelsHigh))))
        return (bitmap.colorAt(x: x, y: y)?.alphaComponent ?? 0) > 0.2
    }
}
