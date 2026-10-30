// Offline reference renderer. No Unity/Core loading or placement generation.
// swiftc tools/art/render-terrain-layout.swift -o artifacts/render-terrain-layout
// render-terrain-layout layout.json SHA256-cache output-directory approved-directory
import Foundation
import CoreGraphics
import ImageIO
import UniformTypeIdentifiers
import CryptoKit
import AppKit
import CoreText

struct Source: Decodable { let sha256: String }
struct Asset: Decodable { let id: String; let size: [Double]; let anchor: [Double]; let source: Source }
struct Zone: Decodable { let id: String; let rect: [Double]; let groundAssetId: String }
struct Camera: Decodable { let id: String; let centerGrid: [Double]; let scale75: Double; let scale50: Double }
struct Background: Decodable { let assetId: String; let canvasSize: [Int]; let canvasOffset: [Double]; let tilePixels: Int }
struct Projection: Decodable { let tileWidth: Double; let tileHeight: Double }
struct Instance: Decodable {
    let id: String; let assetId: String; let zoneId: String; let category: String
    let grid: [Double]; let scale: [Double]; let flipX: Bool; let rotationRadians: Double
    let opacity: Double; let drawOrder: Int; let phase: String
}
struct ReviewOnly: Decodable { let buildings: [Instance]; let excludedInstances: [Instance] }
struct Layout: Decodable {
    let assets: [Asset]; let zones: [Zone]; let cameras: [Camera]; let background: Background
    let projection: Projection; let instances: [Instance]; let reviewOnly: ReviewOnly
}
let args = CommandLine.arguments
precondition(args.count == 4 || args.count == 5, "usage: render-terrain-layout layout.json SHA256-cache output-directory [approved-directory]")
let layout = try JSONDecoder().decode(Layout.self, from: Data(contentsOf: URL(fileURLWithPath: args[1])))
let cache = URL(fileURLWithPath: args[2]), output = URL(fileURLWithPath: args[3]), approved = URL(fileURLWithPath: args.count == 5 ? args[4] : "/__no_reference_requested__")
try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)
let assets = Dictionary(uniqueKeysWithValues: layout.assets.map { ($0.id, $0) })
let width = layout.background.canvasSize[0], height = layout.background.canvasSize[1]
let ox = layout.background.canvasOffset[0], oy = layout.background.canvasOffset[1]
let halfW = layout.projection.tileWidth / 2, halfH = layout.projection.tileHeight / 2
var imageCache = [String: CGImage]()
func context(_ w: Int, _ h: Int) -> CGContext {
    CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w * 4,
              space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
}
func loadImage(_ url: URL) -> CGImage {
    CGImageSourceCreateImageAtIndex(CGImageSourceCreateWithURL(url as CFURL, nil)!, 0, nil)!
}
func image(_ assetId: String) -> CGImage {
    if let found = imageCache[assetId] { return found }
    let asset = assets[assetId]!
    let url = cache.appendingPathComponent(asset.source.sha256 + ".png")
    let bytes = try! Data(contentsOf: url)
    let hash = SHA256.hash(data: bytes).map { String(format: "%02x", $0) }.joined()
    precondition(hash == asset.source.sha256, "asset SHA mismatch: \(assetId)")
    let result = loadImage(url)
    precondition(result.width == Int(asset.size[0]) && result.height == Int(asset.size[1]), "asset dimensions: \(assetId)")
    imageCache[assetId] = result
    return result
}
func save(_ im: CGImage, _ name: String) {
    let url = output.appendingPathComponent(name)
    precondition(!FileManager.default.fileExists(atPath: url.path), "refuse overwrite: \(url.path)")
    let destination = CGImageDestinationCreateWithURL(url as CFURL, UTType.png.identifier as CFString, 1, nil)!
    CGImageDestinationAddImage(destination, im, nil)
    precondition(CGImageDestinationFinalize(destination))
}
func drawTopDown(_ c: CGContext, _ im: CGImage, _ rect: CGRect) {
    c.saveGState(); c.translateBy(x: rect.minX, y: rect.maxY); c.scaleBy(x: 1, y: -1)
    c.draw(im, in: CGRect(x: 0, y: 0, width: rect.width, height: rect.height)); c.restoreGState()
}
func project(_ grid: [Double]) -> CGPoint { CGPoint(x: halfW * (grid[0] - grid[1]), y: halfH * (grid[0] + grid[1])) }
func placement(_ item: Instance) -> CGRect {
    let a = assets[item.assetId]!, p = project(item.grid)
    return CGRect(x: p.x - a.anchor[0] * item.scale[0], y: p.y - a.anchor[1] * item.scale[1],
                  width: a.size[0] * item.scale[0], height: a.size[1] * item.scale[1])
}
let ground = context(width, height)
ground.setFillColor(CGColor(red: 0.045, green: 0.065, blue: 0.11, alpha: 1)); ground.fill(CGRect(x: 0, y: 0, width: width, height: height))
ground.translateBy(x: 0, y: Double(height)); ground.scaleBy(x: 1, y: -1)
let tilePixels = layout.background.tilePixels
for x in stride(from: 0, to: width, by: tilePixels) {
    for y in stride(from: 0, to: height, by: tilePixels) {
        drawTopDown(ground, image(layout.background.assetId), CGRect(x: x, y: y, width: tilePixels, height: tilePixels))
    }
}
var groundCache = [String: CGImage]()
func plane(_ id: String) -> CGImage {
    if let found = groundCache[id] { return found }
    let c = context(4096, 4096), tile = image(id)
    c.interpolationQuality = .none; c.setShouldAntialias(false)
    for x in stride(from: 0, to: 4096, by: tilePixels) {
        for y in stride(from: 0, to: 4096, by: tilePixels) { c.draw(tile, in: CGRect(x: x, y: y, width: tilePixels, height: tilePixels)) }
    }
    let im = c.makeImage()!; groundCache[id] = im; return im
}
for zone in layout.zones {
    let r = zone.rect
    let corners = [[r[0],r[1]], [r[0]+r[2],r[1]], [r[0]+r[2],r[1]+r[3]], [r[0],r[1]+r[3]]].map { grid -> CGPoint in
        let p = project(grid); return CGPoint(x: ox+p.x, y: oy+p.y)
    }
    ground.saveGState(); ground.addLines(between: corners); ground.closePath(); ground.clip()
    ground.concatenate(CGAffineTransform(a: 0.5, b: 0.25, c: -0.5, d: 0.25, tx: ox, ty: oy))
    drawTopDown(ground, plane(zone.groundAssetId), CGRect(x: 0, y: 0, width: 4096, height: 4096)); ground.restoreGState()
}
let groundImage = ground.makeImage()!
func ordered(_ list: [Instance]) -> [Instance] { list.sorted { $0.drawOrder < $1.drawOrder } }
func render(_ camera: Camera, _ list: [Instance], scale: Double) -> CGImage {
    let c = context(390, 844), center = project(camera.centerGrid)
    c.setFillColor(CGColor(red: 0.045, green: 0.065, blue: 0.11, alpha: 1)); c.fill(CGRect(x: 0, y: 0, width: 390, height: 844))
    c.interpolationQuality = .high
    let x = 195-(center.x+ox)*scale, y = 422-(center.y+oy)*scale
    c.draw(groundImage, in: CGRect(x: x, y: 844-y-Double(height)*scale, width: Double(width)*scale, height: Double(height)*scale))
    for item in list {
        let r = placement(item), xx = 195+(r.minX-center.x)*scale, yy = 422+(r.minY-center.y)*scale
        c.saveGState(); c.setAlpha(item.opacity)
        if item.rotationRadians != 0 || item.flipX {
            c.translateBy(x: xx+r.width*scale/2, y: 844-yy-r.height*scale/2); c.rotate(by: -item.rotationRadians)
            if item.flipX { c.scaleBy(x: -1, y: 1) }
            c.draw(image(item.assetId), in: CGRect(x: -r.width*scale/2, y: -r.height*scale/2, width: r.width*scale, height: r.height*scale))
        } else {
            c.draw(image(item.assetId), in: CGRect(x: xx, y: 844-yy-r.height*scale, width: r.width*scale, height: r.height*scale))
        }
        c.restoreGState()
    }
    return c.makeImage()!
}
// Independent footprint-density check; not the historical visual-water metric.
// Denominator: every projected logical-zone pixel in this camera.
// Coverage: composited alpha >= 32 of props/buildings/paths/river/exterior crowns;
// ground decals and fog are not coverage. Water and banks both count as coverage.
func density(_ camera: Camera, _ list: [Instance]) -> [String: Any] {
    let c = context(390, 844), center = project(camera.centerGrid), scale = camera.scale75
    c.interpolationQuality = .high
    var onScreen = 0
    for item in list where item.category != "ground-decal" && !item.assetId.contains("mist") && !item.assetId.contains("fog") {
        let r = placement(item), xx = 195+(r.minX-center.x)*scale, yy = 422+(r.minY-center.y)*scale
        if xx+r.width*scale+r.height*scale < 0 || xx-r.height*scale > 390 || yy+r.height*scale+r.width*scale < 0 || yy-r.width*scale > 844 { continue }
        onScreen += 1
        c.saveGState(); c.setAlpha(item.opacity)
        if item.rotationRadians != 0 || item.flipX {
            c.translateBy(x:xx+r.width*scale/2,y:844-yy-r.height*scale/2); c.rotate(by:-item.rotationRadians)
            if item.flipX { c.scaleBy(x:-1,y:1) }
            c.draw(image(item.assetId),in:CGRect(x:-r.width*scale/2,y:-r.height*scale/2,width:r.width*scale,height:r.height*scale))
        } else { c.draw(image(item.assetId),in:CGRect(x:xx,y:844-yy-r.height*scale,width:r.width*scale,height:r.height*scale)) }
        c.restoreGState()
    }
    let bytes = c.data!.assumingMemoryBound(to:UInt8.self)
    var land = 0, bare = 0
    for y in 0..<844 { for x in 0..<390 {
        let u = (Double(x)+0.5-195)/scale+center.x, v = (Double(y)+0.5-422)/scale+center.y
        let gx = u/(2*halfW)+v/(2*halfH), gy = v/(2*halfH)-u/(2*halfW)
        if layout.zones.contains(where:{z in gx>=z.rect[0] && gx<z.rect[0]+z.rect[2] && gy>=z.rect[1] && gy<z.rect[1]+z.rect[3]}) {
            land += 1
            // Decoded RGBA rows are top-to-bottom, matching the historical masks.
            if bytes[(y*390+x)*4+3] < 32 { bare += 1 }
        }
    }}
    return ["method":"projected logical land, alpha>=32 art coverage including river/banks/crowns, excluding fog/ground decals; not historical visual-water metric", "logicalLandPixels":land, "emptyPixels":bare, "emptyPercent":Double(bare)*100/Double(max(1,land)), "intersectingSpriteBounds":onScreen]
}

func renderWorld(_ list: [Instance]) -> CGImage {
    let c = context(width, height); c.draw(groundImage, in: CGRect(x: 0, y: 0, width: width, height: height))
    c.translateBy(x: 0, y: Double(height)); c.scaleBy(x: 1, y: -1)
    for item in list {
        let r = placement(item)
        c.saveGState(); c.setAlpha(item.opacity)
        if item.rotationRadians != 0 || item.flipX {
            c.translateBy(x: ox+r.midX, y: oy+r.midY); c.rotate(by: item.rotationRadians)
            if item.flipX { c.scaleBy(x: -1, y: 1) }
            drawTopDown(c, image(item.assetId), CGRect(x: -r.width/2, y: -r.height/2, width: r.width, height: r.height))
        } else { drawTopDown(c, image(item.assetId), CGRect(x: ox+r.minX, y: oy+r.minY, width: r.width, height: r.height)) }
        c.restoreGState()
    }
    return c.makeImage()!
}
func pixels(_ image: CGImage) -> [UInt8] {
    let c = context(image.width, image.height); c.draw(image, in: CGRect(x: 0, y: 0, width: image.width, height: image.height))
    return Array(UnsafeBufferPointer(start: c.data!.assumingMemoryBound(to: UInt8.self), count: image.width*image.height*4))
}
func difference(_ a: CGImage, _ b: CGImage) -> [String: Any] {
    precondition(a.width == b.width && a.height == b.height)
    let aa = pixels(a), bb = pixels(b); var count = 0, maxDelta = 0, total = 0
    var left = a.width, right = -1, top = a.height, bottom = -1
    for i in stride(from: 0, to: aa.count, by: 4) {
        var changed = false
        for j in 0..<4 { let d = abs(Int(aa[i+j])-Int(bb[i+j])); maxDelta = max(maxDelta,d); total += d; changed = changed || d != 0 }
        if changed { count += 1; let x = (i/4)%a.width, y = (i/4)/a.width; left = min(left,x); right = max(right,x); top = min(top,y); bottom = max(bottom,y) }
    }
    return ["changedPixels": count, "totalPixels": a.width*a.height, "changedPercent": Double(count)*100/Double(a.width*a.height),
            "maxChannelDelta": maxDelta, "meanChannelDelta": Double(total)/Double(aa.count),
            "changedBBoxTopLeft": count == 0 ? [] : [left, top, right-left+1, bottom-top+1]]
}
func historicalDensity(_ camera: Camera, _ list: [Instance]) -> [String: Any] {
    let count=390*844,center=project(camera.centerGrid),scale=camera.scale75
    let maskRoot=approved.deletingLastPathComponent().appendingPathComponent("preview")
    let prefix="density-\(camera.id)-house75-r9"
    let landURL=maskRoot.appendingPathComponent(prefix+"-land.png"),waterURL=maskRoot.appendingPathComponent(prefix+"-visual-water.png")
    let land=pixels(loadImage(landURL)),water=pixels(loadImage(waterURL))
    var occupied=[Bool](repeating:false,count:count),paths=occupied,trans=[Float](repeating:1,count:count)
    var categoryCounts=[String:Int]()
    for item in list.reversed() {
        let r=placement(item),x=195+(r.minX-center.x)*scale,y=422+(r.minY-center.y)*scale,ww=r.width*scale,hh=r.height*scale
        if x+ww+hh<=0 || x-hh>=390 || y+hh+ww<=0 || y-ww>=844 { continue }
        let c=context(390,844);c.interpolationQuality = .high;c.setAlpha(item.opacity)
        if item.rotationRadians != 0 || item.flipX {
            c.translateBy(x:x+ww/2,y:844-y-hh/2);c.rotate(by:-item.rotationRadians)
            if item.flipX { c.scaleBy(x:-1,y:1) }
            c.draw(image(item.assetId),in:CGRect(x:-ww/2,y:-hh/2,width:ww,height:hh))
        } else { c.draw(image(item.assetId),in:CGRect(x:x,y:844-y-hh,width:ww,height:hh)) }
        let p=c.data!.assumingMemoryBound(to:UInt8.self)
        var visible=0
        for i in 0..<count {
            let a=Float(p[i*4+3])/255,contribution=a*trans[i]
            if contribution>=32.0/255 {
                visible += 1
                if item.category == "path" { paths[i]=true }
                else if item.category != "exterior" && item.category != "river" && item.category != "ground-decal" { occupied[i]=true }
            }
            trans[i] *= (1-a)
        }
        if visible>=8 { categoryCounts[item.category,default:0] += 1 }
    }
    var dry=0,bare=0,noPath=0,bareNoPath=0
    for i in 0..<count where land[i*4]>=128 && water[i*4]<128 {
        dry += 1
        if !paths[i] { noPath += 1; if !occupied[i] { bareNoPath += 1 } }
        if !occupied[i] && !paths[i] { bare += 1 }
    }
    func digest(_ url:URL)->String { SHA256.hash(data:try! Data(contentsOf:url)).map{String(format:"%02x",$0)}.joined() }
    return ["method":"Historical r9 visual-water mask; front-to-back per-sprite alpha contribution >=32/255; exterior/fog/soil not occupied; paths count occupied", "landMaskSHA256":digest(landURL),"waterMaskSHA256":digest(waterURL), "dryLandPixels":dry,"barePixels":bare,"emptyPercent":Double(bare)*100/Double(max(1,dry)),"emptyExcludingPathsPercent":Double(bareNoPath)*100/Double(max(1,noPath)),"visibleInstancesByCategory":categoryCounts]
}

func caption(_ context: CGContext, text: String, rect: CGRect, size: CGFloat) {
    context.saveGState()
    let attr = NSAttributedString(string:text,attributes:[.font:NSFont.systemFont(ofSize:size),.foregroundColor:NSColor.white])
    let line = CTLineCreateWithAttributedString(attr)
    let width = CTLineGetTypographicBounds(line,nil,nil,nil)
    context.textMatrix = .identity
    context.textPosition = CGPoint(x:rect.midX-width/2,y:rect.minY+5)
    CTLineDraw(line,context)
    context.restoreGState()
}
func comparison(_ reference: CGImage, _ lossless: CGImage, _ final: CGImage, id: String, losslessDiff: [String: Any], cleanDiff: [String: Any]) -> CGImage {
    let c = context(1170, 932); c.setFillColor(CGColor(red: 0.06, green: 0.07, blue: 0.09, alpha: 1)); c.fill(CGRect(x: 0, y: 0, width: 1170, height: 932))
    for (index, pair) in [(reference,"승인 75px 원본"),(lossless,"JSON 무손실 재현"),(final,"예약 칸 보호본")].enumerated() {
        c.draw(pair.0, in: CGRect(x: index*390, y: 44, width: 390, height: 844))
        caption(c, text: pair.1, rect: CGRect(x: index*390, y: 896, width: 390, height: 28), size: 18)
    }
    caption(c, text: "\(id) · 390×844 원배율 / 재현 차이 \(losslessDiff["changedPixels"]!)px / 보호본 변경 \(cleanDiff["changedPixels"]!)px", rect: CGRect(x: 0, y: 8, width: 1170, height: 28), size: 16)
    return c.makeImage()!
}
let retained = ordered(layout.instances + layout.reviewOnly.buildings), lossless = ordered(layout.instances + layout.reviewOnly.buildings + layout.reviewOnly.excludedInstances)
var report = [[String: Any]]()
for camera in layout.cameras {
    let referenceURL = approved.appendingPathComponent("dense-r8-\(camera.id)-75px-r2-390x844.png")
    let a = render(camera, lossless, scale: camera.scale75), b = render(camera, retained, scale: camera.scale75)
    let changed = difference(a,b)
    save(a,"\(camera.id)-75px-lossless.png"); save(b,"\(camera.id)-75px-protected.png")
    let initial = retained.filter { $0.phase == "start-adjacent" || $0.phase == "exterior" }
    if camera.id == "start-village" { save(render(camera,initial,scale:camera.scale75),"start-adjacent-75px.png") }
    var row: [String:Any] = ["camera":camera.id,"size":[390,844],"losslessVsProtected":changed,"densityLossless":density(camera,lossless),"densityProtected":density(camera,retained)]
    if args.count == 5 {
        let reference=loadImage(referenceURL), originalDiff=difference(reference,a)
        save(comparison(reference,a,b,id:camera.id,losslessDiff:originalDiff,cleanDiff:changed),"\(camera.id)-comparison.png")
        row["approvedVsLossless"]=originalDiff
        let maskFile=approved.deletingLastPathComponent().appendingPathComponent("preview/density-\(camera.id)-house75-r9-land.png")
        if FileManager.default.fileExists(atPath:maskFile.path) {
            row["historicalDensityLossless"]=historicalDensity(camera,lossless)
            row["historicalDensityProtected"]=historicalDensity(camera,retained)
        }
    }
    report.append(row)
}
save(renderWorld(retained), "all13-protected-native.png")
let reportData = try JSONSerialization.data(withJSONObject: ["renderer":"CoreGraphics offline, canonical grid/asset anchor only", "runtime":"NOT_RUN", "instances":retained.count,"excluded":layout.reviewOnly.excludedInstances.count,"rows":report],options:[.prettyPrinted,.sortedKeys])
try reportData.write(to:output.appendingPathComponent("render-verification.json"),options:.withoutOverwriting)
print(String(data:reportData,encoding:.utf8)!)
