import AppKit
import Foundation
precondition(CommandLine.arguments.count == 2, "Expected assembled directory")
let root=URL(fileURLWithPath:CommandLine.arguments[1]), out=root.appendingPathComponent("runtime")
try FileManager.default.createDirectory(at:out,withIntermediateDirectories:true)
var manifest=try JSONSerialization.jsonObject(with:Data(contentsOf:root.appendingPathComponent("manifest.json"))) as! [String:Any]
var clips=manifest["clips"] as! [[String:Any]], bytes=0
for index in clips.indices {
 var c=clips[index]
 let texture=c["texture"] as! String,b=NSBitmapImageRep(data:try Data(contentsOf:root.appendingPathComponent(texture)))!
 let cols=c["columns"] as! Int,rows=c["rows"] as! Int,count=c["frames"] as! Int
 precondition(b.bitsPerSample==8 && b.samplesPerPixel==4 && !b.isPlanar && b.bytesPerRow>=b.pixelsWide*4)
 precondition(cols>0 && rows>0 && cols<=b.pixelsWide && rows<=b.pixelsHigh, "Invalid atlas grid")
 precondition(count>0 && count<=cols*rows && b.pixelsWide%cols==0 && b.pixelsHigh%rows==0, "Frames or dimensions disagree with atlas grid")
 let w=b.pixelsWide/cols,h=b.pixelsHigh/rows
 var box=[w,h,0,0]
 for f in 0..<count {for y in 0..<h {for x in 0..<w {
  if b.bitmapData![(f/cols*h+y)*b.bytesPerRow+(f%cols*w+x)*4+3]>0 {box[0]=min(box[0],x);box[1]=min(box[1],y);box[2]=max(box[2],x);box[3]=max(box[3],y)}
 }}}
 precondition(box[0]<=box[2] && box[1]<=box[3], "Atlas has no visible pixels")
 let pad=4,cw=box[2]-box[0]+1+pad*2,ch=box[3]-box[1]+1+pad*2
 let body=(c["bodyHeightPixels"] as! NSNumber).doubleValue
 precondition(body.isFinite && body>0, "Invalid authored body height")
 let scale=min(1,256/body)
 let ow=Int(ceil(Double(cw)*scale)),oh=Int(ceil(Double(ch)*scale))
 let d=NSBitmapImageRep(bitmapDataPlanes:nil,pixelsWide:ow*cols,pixelsHigh:oh*rows,bitsPerSample:8,samplesPerPixel:4,hasAlpha:true,isPlanar:false,colorSpaceName:.deviceRGB,bitmapFormat:[],bytesPerRow:0,bitsPerPixel:32)!
 memset(d.bitmapData!,0,d.bytesPerRow*d.pixelsHigh)
 let context=NSGraphicsContext(bitmapImageRep:d)!
 NSGraphicsContext.saveGraphicsState();NSGraphicsContext.current=context;context.imageInterpolation = .high
 for f in 0..<count {
  let cg=b.cgImage!.cropping(to:CGRect(x:f%cols*w+box[0],y:f/cols*h+box[1],width:box[2]-box[0]+1,height:box[3]-box[1]+1))!
  let image=NSImage(cgImage:cg,size:CGSize(width:cg.width,height:cg.height))
  let target=CGRect(x:Double(f%cols*ow)+Double(pad)*scale,y:Double((rows-1-f/cols)*oh)+Double(oh)-Double(ch-pad)*scale,width:Double(cg.width)*scale,height:Double(cg.height)*scale)
  image.draw(in:target,from:.zero,operation:.copy,fraction:1)
 }
 NSGraphicsContext.restoreGraphicsState()
 try d.representation(using:.png,properties:[:])!.write(to:out.appendingPathComponent(texture))
 let anchorX=(c["pivotX"] as! Double)*Double(w),anchorY=(1-(c["pivotY"] as! Double))*Double(h)
 precondition(anchorX.isFinite && anchorY.isFinite, "Invalid pivot")
 c["pivotX"]=(anchorX-Double(box[0])+Double(pad))*scale/Double(ow)
 c["pivotY"]=1-(anchorY-Double(box[1])+Double(pad))*scale/Double(oh)
 c["bodyHeightPixels"]=body*scale;c["headHeightPixels"]=(c["headHeightPixels"] as! NSNumber).doubleValue*scale
 c["frameWidth"]=ow;c["frameHeight"]=oh;c["runtimeScale"]=scale;c["authoringTexture"]=texture
 clips[index]=c;bytes+=ow*cols*oh*rows*4
}
manifest["clips"]=clips;manifest["uncompressedTextureBytes"]=bytes
try JSONSerialization.data(withJSONObject:manifest,options:[.prettyPrinted,.sortedKeys]).write(to:out.appendingPathComponent("manifest.json"))
print("runtime RGBA bytes=\(bytes) MiB=\(Double(bytes)/1048576)")
