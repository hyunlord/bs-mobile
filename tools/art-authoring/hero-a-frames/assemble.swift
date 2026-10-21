import AppKit
import Foundation
// Atlas assembly only: exact RGBA copying and whole-frame integer translation.
let root = URL(fileURLWithPath: CommandLine.arguments[1])
let out = URL(fileURLWithPath: CommandLine.arguments[2])
try FileManager.default.createDirectory(at: out, withIntermediateDirectories: true)
struct Job { let file: String; let columns: Int; let rows: Int; let body: Int; let head: Int; let crowns: [Int]; let chins: [Int] }
let jobs = [
 Job(file:"walk-e",columns:4,rows:2,body:436,head:63,crowns:[47,55,49,44,32,41,41,34],chins:[110,118,112,107,95,104,104,97]),
 Job(file:"walk-s",columns:4,rows:2,body:443,head:62,crowns:[47,47,48,49,27,27,27,29],chins:[109,109,110,111,89,89,89,91]),
 Job(file:"walk-se",columns:4,rows:2,body:455,head:62,crowns:[15,22,16,14,3,11,5,4],chins:[77,84,78,76,65,73,67,66]),
 Job(file:"walk-ne",columns:4,rows:2,body:441,head:62,crowns:[37,38,36,37,32,32,26,28],chins:[99,100,98,99,94,94,88,90]),
 Job(file:"walk-n-v3",columns:4,rows:2,body:475,head:64,crowns:[15,16,16,16,12,12,14,14],chins:[79,80,80,80,76,76,78,78]),
 Job(file:"slash-light-e-v2",columns:2,rows:4,body:214,head:32,crowns:[41,68,54,53,40,30,11,12],chins:[73,100,86,85,72,62,43,44])
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
let manifest:[String:Any] = ["schemaVersion":1,"method":"frame-drawn","headHeightRatio":63.0/436.0,"status":"candidate; user video review pending","provenance":"AI-generated independently drawn frame artwork, not human hand-drawn and not rig-baked","clips":clips]
try JSONSerialization.data(withJSONObject:manifest,options:[.prettyPrinted,.sortedKeys]).write(to:out.appendingPathComponent("manifest.json"))
try JSONSerialization.data(withJSONObject:geometry,options:[.prettyPrinted,.sortedKeys]).write(to:out.appendingPathComponent("frame-landmarks.json"))
print("Wrote \(clips.count) direction clips, independently authored poses; alpha clipping 0")
