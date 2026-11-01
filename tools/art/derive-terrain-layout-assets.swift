// Derive byte-checked atlas crops and a lossless river from catalog dependencies.
import Foundation
import CoreGraphics
import ImageIO
import UniformTypeIdentifiers
import CryptoKit
let args=CommandLine.arguments
precondition(args.count == 3,"usage: derive-terrain-layout-assets layout.json cache")
let catalog=try JSONSerialization.jsonObject(with:Data(contentsOf:URL(fileURLWithPath:args[1]))) as! [String:Any]
let cache=URL(fileURLWithPath:args[2])
func hash(_ d:Data)->String { SHA256.hash(data:d).map{String(format:"%02x",$0)}.joined() }
func image(_ sha:String)->CGImage { CGImageSourceCreateImageAtIndex(CGImageSourceCreateWithURL(cache.appendingPathComponent(sha+".png") as CFURL,nil)!,0,nil)! }
func save(_ im:CGImage,_ asset:[String:Any]) throws {
 let source=asset["source"] as! [String:Any], expected=source["sha256"] as! String
 let data=NSMutableData(),dest=CGImageDestinationCreateWithData(data,UTType.png.identifier as CFString,1,nil)!
 CGImageDestinationAddImage(dest,im,nil);precondition(CGImageDestinationFinalize(dest))
 let bytes=data as Data, actual=hash(bytes)
 precondition(actual == expected,"derived PNG SHA mismatch \(asset["id"]!): \(actual) expected \(expected)")
 try bytes.write(to:cache.appendingPathComponent(expected+".png"),options:.withoutOverwriting)
}
let assets=catalog["assets"] as! [[String:Any]], lookup=Dictionary(uniqueKeysWithValues:assets.map{($0["id"] as! String,$0)})
for asset in assets {
 let source=asset["source"] as! [String:Any],sha=source["sha256"] as! String
 if FileManager.default.fileExists(atPath:cache.appendingPathComponent(sha+".png").path) { continue }
 if let crop=source["cropRect"] as? [Double] {
   let atlas=image(source["containerSha256"] as! String)
   let c=CGContext(data:nil,width:atlas.width,height:atlas.height,bitsPerComponent:8,bytesPerRow:atlas.width*4,space:CGColorSpace(name:CGColorSpace.sRGB)!,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue)!
   c.draw(atlas,in:CGRect(x:0,y:0,width:atlas.width,height:atlas.height))
   let src=c.data!.assumingMemoryBound(to:UInt8.self),w=Int(crop[2]),h=Int(crop[3])
   var rgba=[UInt8](repeating:0,count:w*h*4)
   for y in 0..<h { for x in 0..<w { for k in 0..<4 { rgba[(y*w+x)*4+k]=src[((y+Int(crop[1]))*atlas.width+x+Int(crop[0]))*4+k] } } }
   let data=Data(rgba)
   let result=CGImage(width:w,height:h,bitsPerComponent:8,bitsPerPixel:32,bytesPerRow:w*4,space:CGColorSpace(name:CGColorSpace.sRGB)!,bitmapInfo:CGBitmapInfo(rawValue:CGImageAlphaInfo.premultipliedLast.rawValue),provider:CGDataProvider(data:data as CFData)!,decode:nil,shouldInterpolate:true,intent:.defaultIntent)!
   try save(result,asset)
 } else if let composition=asset["composition"] as? [String:Any] {
   let size=composition["size"] as! [Int],w=size[0],h=size[1]
   let c=CGContext(data:nil,width:w,height:h,bitsPerComponent:8,bytesPerRow:w*4,space:CGColorSpace(name:CGColorSpace.sRGB)!,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue)!
   c.interpolationQuality = .none;c.setShouldAntialias(false)
   for part in composition["parts"] as! [[String:Any]] {
      let a=lookup[part["assetId"] as! String]!,src=a["source"] as! [String:Any],im=image(src["sha256"] as! String),pos=part["topLeft"] as! [Double]
      let px = pos[0], py = Double(h) - pos[1] - Double(im.height)
      let rect = CGRect(x:px,y:py,width:Double(im.width),height:Double(im.height))
      c.draw(im,in:rect)
   }
   try save(c.makeImage()!,asset)
 }
}
print("All derived catalog assets match original PNG byte SHA-256")
