import Foundation
import AppKit
import CoreGraphics
import ImageIO
import UniformTypeIdentifiers
let dir=URL(fileURLWithPath:CommandLine.arguments[0]).deletingLastPathComponent(),d=try JSONSerialization.jsonObject(with:Data(contentsOf:dir.appendingPathComponent("geometry-proposal.json"))) as! [String:Any],zones=d["zones"] as! [[String:Any]],buildings=d["buildings"] as! [[String:Any]],river=d["river"] as! [String:Any],water=river["cells"] as! [[Int]],bridges=river["bridges"] as! [[String:Any]]
let colors=["b8ba73","a6b596","a6b596","a6b596","a6b596","c4a47a","708c70","96958b","b8aa6c","b8bc82","b5ab7e","998d9d","84a6a0"]
func color(_ hex:String,_ alpha:CGFloat=1)->CGColor{let n=Int(hex,radix:16)!;return CGColor(red:CGFloat(n>>16&255)/255,green:CGFloat(n>>8&255)/255,blue:CGFloat(n&255)/255,alpha:alpha)}
for iso in [true,false]{let w=iso ? 3520:1320,h=iso ? 1800:980,c=CGContext(data:nil,width:w,height:h,bitsPerComponent:8,bytesPerRow:w*4,space:CGColorSpace(name:CGColorSpace.sRGB)!,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue)!
 func p(_ x:Double,_ y:Double)->CGPoint{let u=iso ? 1400+32*(x-y):50+(x-8)*18,v=iso ? -200+16*(x+y):100+(y-8)*18;return CGPoint(x:u,y:Double(h)-v)}
 func text(_ s:String,_ x:Double,_ y:Double,_ size:CGFloat){NSGraphicsContext.saveGraphicsState();NSGraphicsContext.current=NSGraphicsContext(cgContext:c,flipped:false);(s as NSString).draw(at:CGPoint(x:x,y:Double(h)-y-Double(size)),withAttributes:[.font:NSFont.boldSystemFont(ofSize:size),.foregroundColor:NSColor(cgColor:color("292b30"))!]);NSGraphicsContext.restoreGraphicsState()}
 func polygon(_ x:Int,_ y:Int,_ width:Int,_ height:Int,_ fill:String?,_ stroke:String,_ alpha:CGFloat,_ line:CGFloat){let pts=[p(Double(x),Double(y)),p(Double(x+width),Double(y)),p(Double(x+width),Double(y+height)),p(Double(x),Double(y+height))];c.beginPath();c.addLines(between:pts);c.closePath();c.setStrokeColor(color(stroke,alpha));c.setLineWidth(line);if let f=fill{c.setFillColor(color(f));c.drawPath(using:.fillStroke)}else{c.strokePath()}}
 c.setFillColor(color("ece8dd"));c.fill(CGRect(x:0,y:0,width:w,height:h));text("r8 DESIGN ONLY — 13 single rectangles / 1664 cells / \(iso ? "native64×32 isometric":"top-down logical cells")",36,18,28)
 for(i,z)in zones.enumerated(){polygon(z["x"] as! Int,z["y"] as! Int,z["width"] as! Int,z["height"] as! Int,colors[i],"303832",1,3)}
 for z in zones{let x=z["x"] as! Int,y=z["y"] as! Int;for a in x..<x+(z["width"] as! Int){for b in y..<y+(z["height"] as! Int){polygon(a,b,1,1,nil,"353b32",0.2,1)}}}
 for q in water{polygon(q[0],q[1],1,1,"5289a8","83b3c4",1,0.5)}
 for(i,b)in bridges.enumerated(){let x=b["x"] as! Int,y=b["y"] as! Int,bw=b["width"] as! Int;polygon(x,y,bw,1,"936f47","4a3524",1,3);let q=p(Double(x)+Double(bw)/2,Double(y)+0.5);text("B\(i+1)",q.x-16,Double(h)-q.y-30,iso ? 24:16)}
 for(i,b)in buildings.enumerated(){let x=b["x"] as! Int,y=b["y"] as! Int;polygon(x,y,2,2,"eed584","604c29",1,2);let q=p(Double(x+1),Double(y+1));text(String(UnicodeScalar(65+i)!),q.x-7,Double(h)-q.y-12,iso ? 20:13)}
 for(i,z)in zones.enumerated(){let q=p(Double(z["x"] as! Int)+Double(z["width"] as! Int)/2,Double(z["y"] as! Int)+Double(z["height"] as! Int)/2);text(String(i+1),q.x-(i==12 ? 45:8),Double(h)-q.y-35,iso ? 26:17)}
 let lx:Double=iso ? 2760:880;text("ZONE / RECTANGLE / AREA",lx,75,25)
 for(i,z)in zones.enumerated(){text("\(i+1) \((z["id"] as! String).replacingOccurrences(of:"zone:",with:"")) (\(z["x"]!),\(z["y"]!);\(z["width"]!)×\(z["height"]!)) \(z["area"]!)",lx,115+Double(i*37),iso ? 22:15)}
 text("START BUILDINGS (unchanged)",lx,635,23);for(i,b)in buildings.enumerated(){text("\(String(UnicodeScalar(65+i)!)) \(b["kind"]!) (\(b["x"]!),\(b["y"]!)) 2×2",lx,675+Double(i*(iso ? 31:22)),iso ? 21:14)}
 text("Blue river / brown B1–B3: visual only; no movement, building or unlock blocking.",36,Double(h)-76,iso ? 24:17);text("ANY open edge-neighbor + production/cost/reserve checks. Proposed outer threshold4→3; save format6 unchanged.",36,Double(h)-42,iso ? 24:17)
 let url=dir.appendingPathComponent("geometry-\(iso ? "isometric":"topdown").png"),dest=CGImageDestinationCreateWithURL(url as CFURL,UTType.png.identifier as CFString,1,nil)!;CGImageDestinationAddImage(dest,c.makeImage()!,nil);precondition(CGImageDestinationFinalize(dest))
}
