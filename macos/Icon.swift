import AppKit

// Palette from Jai Singh Dhaliwal's portfolio design tokens.
let navy = NSColor(srgbRed: 19 / 255, green: 30 / 255, blue: 41 / 255, alpha: 1)
let coral = NSColor(srgbRed: 249 / 255, green: 66 / 255, blue: 58 / 255, alpha: 1)
let paper = NSColor(srgbRed: 247 / 255, green: 246 / 255, blue: 243 / 255, alpha: 1)
let coralHighlight = NSColor(srgbRed: 255 / 255, green: 109 / 255, blue: 100 / 255, alpha: 1)

func tag(left: CGFloat, bottom: CGFloat) -> NSBezierPath {
    let path = NSBezierPath()
    func point(_ x: CGFloat, _ y: CGFloat) -> NSPoint { NSPoint(x: left + x, y: bottom + y) }
    path.move(to: point(48, 0))
    path.line(to: point(394, 0))
    path.curve(to: point(427, 13), controlPoint1: point(409, 0), controlPoint2: point(417, 4))
    path.line(to: point(530, 105))
    path.curve(to: point(530, 175), controlPoint1: point(552, 124), controlPoint2: point(552, 156))
    path.line(to: point(427, 267))
    path.curve(to: point(394, 280), controlPoint1: point(417, 276), controlPoint2: point(409, 280))
    path.line(to: point(48, 280))
    path.curve(to: point(0, 232), controlPoint1: point(21, 280), controlPoint2: point(0, 259))
    path.line(to: point(0, 48))
    path.curve(to: point(48, 0), controlPoint1: point(0, 21), controlPoint2: point(21, 0))
    path.close()
    return path
}

func drawIcon(compact: Bool) {
    let tile = NSBezierPath(roundedRect: NSRect(x: 62, y: 62, width: 900, height: 900),
                            xRadius: 208, yRadius: 208)
    let rim = NSColor(srgbRed: 0.22, green: 0.28, blue: 0.34, alpha: 1)
    if compact {
        navy.setFill()
        tile.fill()
    } else {
        let upper = NSColor(srgbRed: 0.14, green: 0.20, blue: 0.26, alpha: 1)
        NSGradient(starting: upper, ending: navy)!.draw(in: tile, angle: -69)
        rim.setStroke()
        let inset = NSBezierPath(roundedRect: NSRect(x: 65, y: 65, width: 894, height: 894),
                                 xRadius: 206, yRadius: 206)
        inset.lineWidth = 4
        inset.stroke()
    }

    // The offset paper tag represents the original. The coral tag is the edited copy.
    paper.setFill()
    tag(left: 222, bottom: compact ? 441 : 425).fill()
    if !compact {
        NSColor.black.withAlphaComponent(0.24).setFill()
        tag(left: 258, bottom: 299).fill()
    }
    let front = tag(left: 258, bottom: 315)
    if compact {
        coral.setFill()
        front.fill()
    } else {
        NSGradient(starting: coralHighlight, ending: coral)!.draw(in: front, angle: -73)
    }

    navy.setFill()
    let hole = compact ? 70.0 : 62.0
    NSBezierPath(ovalIn: NSRect(x: 687 - hole / 2, y: 455 - hole / 2,
                              width: hole, height: hole)).fill()
}

let output = URL(fileURLWithPath: CommandLine.arguments[1], isDirectory: true)
try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)
for size in [16, 24, 32, 48, 64, 128, 256, 512, 1024] {
    let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size,
                                  bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                                  colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    let context = NSGraphicsContext(bitmapImageRep: bitmap)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = context
    let scale = CGFloat(size) / 1024
    context.cgContext.scaleBy(x: scale, y: scale)
    drawIcon(compact: size <= 32)
    NSGraphicsContext.restoreGraphicsState()
    let png = bitmap.representation(using: .png, properties: [:])!
    try png.write(to: output.appendingPathComponent("\(size).png"))
}
