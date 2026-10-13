import AppKit
import Foundation
// Reversible asset assembly: integer RGBA copy, no pose deformation or per-frame scaling.
precondition(CommandLine.arguments.count == 2, "Expected authoring directory")
let root=URL(fileURLWithPath:CommandLine.arguments[1])
let config=try JSONSerialization.jsonObject(with:Data(contentsOf:root.appendingPathComponent("authoring.json"))) as! [String:Any]
let jobs=config["clips"] as! [[String:Any]]
var clips=[[String:Any]](), checks=[[String:Any]]()
for job in jobs {
 let file=job["source"] as! String, direction=job["direction"] as! String, id=job["id"] as! String
 let cols=job["columns"] as! Int, rows=job["rows"] as! Int, count=job["frames"] as! Int
 let anchors=job["groundAnchors"] as! [[Int]]
 precondition(anchors.count == count, "Every frame needs a measured ground anchor; alpha bounds are not feet")
 let b=NSBitmapImageRep(data:try Data(contentsOf:root.appendingPathComponent(file)))!
 precondition(b.bitsPerSample==8 && b.samplesPerPixel==4 && !b.isPlanar && b.bytesPerRow >= b.pixelsWide*4)
 precondition(cols>0 && rows>0 && cols<=b.pixelsWide && rows<=b.pixelsHigh, "Invalid source grid")
 precondition(count>0 && count<=cols*rows, "Frames exceed source grid")
 let defaultWidth=b.pixelsWide/cols,defaultHeight=b.pixelsHigh/rows
 let rects=job["sourceRects"] as? [[Int]] ?? (0..<count).map { [$0%cols*defaultWidth,$0/cols*defaultHeight,defaultWidth,defaultHeight] }
 precondition(rects.count == count)
 for (rect,anchor) in zip(rects,anchors) {
  precondition(rect.count==4 && anchor.count==2, "Rectangle and ground anchor dimensions differ")
  precondition(rect[0]>=0 && rect[1]>=0 && rect[2]>0 && rect[3]>0, "Invalid source rectangle")
  precondition(rect[0]<=b.pixelsWide && rect[1]<=b.pixelsHigh && rect[2]<=b.pixelsWide-rect[0] && rect[3]<=b.pixelsHigh-rect[1], "Source rectangle leaves bitmap")
  precondition(anchor[0]>=0 && anchor[0]<=rect[2] && anchor[1]>=0 && anchor[1]<=rect[3], "Ground anchor leaves source cell")
 }
 let left=anchors.map{$0[0]}.max()!,right=zip(rects,anchors).map{$0.0[2]-$0.1[0]}.max()!
 let above=anchors.map{$0[1]}.max()!,below=zip(rects,anchors).map{$0.0[3]-$0.1[1]}.max()!
 let ow=max(left,right)*2+32,oh=above+below+32,ground=above+16
 let d=NSBitmapImageRep(bitmapDataPlanes:nil,pixelsWide:ow*cols,pixelsHigh:oh*rows,bitsPerSample:8,samplesPerPixel:4,hasAlpha:true,isPlanar:false,colorSpaceName:.deviceRGB,bitmapFormat:.alphaNonpremultiplied,bytesPerRow:0,bitsPerPixel:32)!
 memset(d.bitmapData!,0,d.bytesPerRow*d.pixelsHigh)
 for frame in 0..<count {
  let rect=rects[frame],sx=rect[0],sy=rect[1],cw=rect[2],ch=rect[3]
  var bounds=[cw,ch,0,0]
  for y in 0..<ch {for x in 0..<cw {if b.bitmapData![(y+sy)*b.bytesPerRow+(x+sx)*4+3]>127 {bounds[0]=min(bounds[0],x);bounds[1]=min(bounds[1],y);bounds[2]=max(bounds[2],x);bounds[3]=max(bounds[3],y)}}}
  let anchor=anchors[frame]
  let dy=ground-anchor[1],dx=ow/2-anchor[0]
  var lost=0
  for y in 0..<ch {for x in 0..<cw {
   let si=(y+sy)*b.bytesPerRow+(x+sx)*4,xx=x+dx,yy=y+dy
   if xx<0 || xx>=ow || yy<0 || yy>=oh {if b.bitmapData![si+3]>0 {lost+=1};continue}
   let di=(frame/cols*oh+yy)*d.bytesPerRow+(frame%cols*ow+xx)*4
   for c in 0..<4 {d.bitmapData![di+c]=b.bitmapData![si+c]}
  }}
  precondition(lost==0,"assembly clips source alpha: \(id) \(direction) frame \(frame)")
  checks.append(["id":id,"direction":direction,"frame":frame,"sourceBounds":bounds,"measuredGroundAnchor":anchor,"translation":[dx,dy],"groundY":ground,"lostPixels":lost])
 }
 let texture="\(id)-\(direction.lowercased())-atlas.png"
 try d.representation(using:.png,properties:[:])!.write(to:root.appendingPathComponent(texture))
 var clip=job;clip.removeValue(forKey:"source")
 clip["texture"]=texture;clip["mirror"]=false;clip["authoredDirection"]=direction
 clip["pivotX"]=0.5;clip["pivotY"]=Double(oh-ground)/Double(oh)
 clip["frameWidth"]=ow;clip["frameHeight"]=oh
 clips.append(clip)
}
let manifest:[String:Any] = ["schemaVersion":1,"method":"frame-drawn","status":"authored candidate; native video acceptance pending","clips":clips]
try JSONSerialization.data(withJSONObject:manifest,options:[.prettyPrinted,.sortedKeys]).write(to:root.appendingPathComponent("manifest.json"))
try JSONSerialization.data(withJSONObject:checks,options:[.prettyPrinted,.sortedKeys]).write(to:root.appendingPathComponent("assembly-checks.json"))
print("clips=\(clips.count) frames=\(checks.count) alpha loss=0")
