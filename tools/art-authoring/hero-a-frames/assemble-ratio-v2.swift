import AppKit
import Foundation
// Atlas assembly only: exact RGBA copying and whole-frame integer translation.
let root = URL(fileURLWithPath: CommandLine.arguments[1])
let out = URL(fileURLWithPath: CommandLine.arguments[2])
try FileManager.default.createDirectory(at: out, withIntermediateDirectories: true)
struct Job { let file: String; let columns: Int; let rows: Int; let body: Int; let head: Int; let crowns: [Int]; let chins: [Int] }
let jobs = [
 Job(file:"walk-e-ratio-v2",columns:4,rows:2,body:431,head:55,crowns:[46,55,50,46,34,38,40,35],chins:[101,110,105,101,89,93,95,90]),
 Job(file:"walk-s-ratio-v4",columns:4,rows:2,body:438,head:54,crowns:[50,51,51,51,26,27,27,27],chins:[104,105,105,105,80,81,81,81]),
 Job(file:"walk-se-ratio-v2",columns:4,rows:2,body:451,head:55,crowns:[23,32,28,24,5,13,9,5],chins:[78,87,83,79,60,68,64,60]),
 Job(file:"walk-ne-ratio-v2",columns:4,rows:2,body:439,head:53,crowns:[44,45,44,44,33,33,33,33],chins:[97,98,97,97,86,86,86,86]),
 Job(file:"walk-n-ratio-v2",columns:4,rows:2,body:463,head:55,crowns:[21,22,22,22,16,16,16,16],chins:[76,77,77,77,71,71,71,71]),
 Job(file:"slash-light-e-ratio-v5",columns:2,rows:4,body:193,head:25,crowns:[56,78,67,67,50,46,21,32],chins:[81,103,92,92,75,71,46,57])
]
var clips = [[String:Any]]()
var geometry = [[String:Any]]()
for job in jobs {
 let source = NSBitmapImageRep(data: try Data(contentsOf: root.appendingPathComponent("source/\(job.file).png")))!
 precondition(source.bitsPerSample == 8 && source.samplesPerPixel == 4)
 let cw=source.pixelsWide/job.columns, ch=source.pixelsHigh/job.rows
 let ow=cw+128, oh=ch+128, ground=ch+24
 let dest=NSBitmapImageRep(bitmapDataPlanes:nil,pixelsWide:ow*job.columns,pixelsHigh:oh*job.rows,bitsPerSample:8,samplesPerPixel:4,hasAlpha:true,isPlanar:false,colorSpaceName:.deviceRGB,bitmapFormat:.alphaNonpremultiplied,bytesPerRow:0,bitsPerPixel:32)!
 memset(dest.bitmapData!,0,dest.bytesPerRow*dest.pixelsHigh)
 for frame in 0..<8 {
  let sx=(frame%job.columns)*cw, sy=(frame/job.columns)*ch
  var minX=cw, minY=ch, maxX=0, maxY=0
  for y in 0..<ch { for x in 0..<cw { if source.bitmapData![(sy+y)*source.bytesPerRow+(sx+x)*4+3]>127 { minX=min(minX,x);maxX=max(maxX,x);minY=min(minY,y);maxY=max(maxY,y) } } }
  let dx=64,dy=ground-maxY
  var lost=0
  for y in 0..<ch { for x in 0..<cw {
   let sourceIndex=(sy+y)*source.bytesPerRow+(sx+x)*4
   let xx=x+dx, yy=y+dy
   if xx<0 || xx>=ow || yy<0 || yy>=oh {if source.bitmapData![sourceIndex+3]>0 {lost+=1};continue}
   let destIndex=((frame/job.columns)*oh+yy)*dest.bytesPerRow+((frame%job.columns)*ow+xx)*4
   for channel in 0..<4 {dest.bitmapData![destIndex+channel]=source.bitmapData![sourceIndex+channel]}
  } }
  precondition(lost==0,"clipped alpha pixels")
  geometry.append(["clip":job.file,"frame":frame,"sourceBodyBounds":[minX,minY,maxX,maxY],"translationPixels":[dx,dy],"groundY":ground,"crownY":job.crowns[frame]+dy,"chinY":job.chins[frame]+dy,"landmarkMethod":"manual approximate source pixel annotation, crown includes hair; chin excludes collar","bodyScaleReferencePixels":job.body,"headReferencePixels":job.head,"alphaPixelsLost":lost])
 }
 let filename=job.file+"-atlas.png"
 try dest.representation(using:.png,properties:[:])!.write(to:out.appendingPathComponent(filename))
 let id=job.file.hasPrefix("walk") ? "walk":"slash-light"
 let direction = job.file.hasPrefix("walk-") ? String(job.file.dropFirst(5).split(separator:"-")[0]).uppercased() : "E"
 let mirrors = direction == "S" || direction == "N" ? [false] : [false,true]
 for mirror in mirrors {
  clips.append(["id":id,"direction":mirror ? direction.replacingOccurrences(of:"E",with:"W") : direction,"texture":filename,"columns":job.columns,"rows":job.rows,"frames":8,"fps":8,"loopDurationSeconds":1.0,"pivotX":0.5,"pivotY":Double(oh-ground)/Double(oh),"bodyHeightPixels":job.body,"headHeightPixels":job.head,"mirror":mirror,"frameWidth":ow,"frameHeight":oh,"authoredDirection":direction])
 }
}
let manifest:[String:Any] = ["schemaVersion":1,"method":"frame-drawn","headHeightRatio":55.0/431.0,"status":"candidate; user video review pending","provenance":"AI-generated independently drawn frame artwork, not human hand-drawn and not rig-baked","clips":clips]
try JSONSerialization.data(withJSONObject:manifest,options:[.prettyPrinted,.sortedKeys]).write(to:out.appendingPathComponent("manifest.json"))
try JSONSerialization.data(withJSONObject:geometry,options:[.prettyPrinted,.sortedKeys]).write(to:out.appendingPathComponent("frame-landmarks.json"))
print("Wrote \(clips.count) direction clips, independently authored poses; alpha clipping 0")
