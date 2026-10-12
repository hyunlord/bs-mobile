import AppKit
let source=NSBitmapImageRep(data:try Data(contentsOf:URL(fileURLWithPath:CommandLine.arguments[1])))!
let dest=NSBitmapImageRep(bitmapDataPlanes:nil,pixelsWide:source.pixelsWide,pixelsHigh:source.pixelsHigh,bitsPerSample:8,samplesPerPixel:4,hasAlpha:true,isPlanar:false,colorSpaceName:.deviceRGB,bitmapFormat:.alphaNonpremultiplied,bytesPerRow:0,bitsPerPixel:32)!
var clear=0,opaque=0,semi=0
for y in 0..<source.pixelsHigh { for x in 0..<source.pixelsWide {
 let i=y*source.bytesPerRow+x*4, o=y*dest.bytesPerRow+x*4
 let a=Int(source.bitmapData![i+3]);if a==0 {clear+=1} else if a==255 {opaque+=1} else {semi+=1}
 let bg = ((x/24+y/24)%2==0) ? [255,0,255]:[40,220,40]
 for c in 0..<3 {dest.bitmapData![o+c]=UInt8((Int(source.bitmapData![i+c])*a+bg[c]*(255-a))/255)}
 dest.bitmapData![o+3]=255
} }
try dest.representation(using:.png,properties:[:])!.write(to:URL(fileURLWithPath:CommandLine.arguments[2]))
print("alpha clear=\(clear) opaque=\(opaque) partial=\(semi)")
