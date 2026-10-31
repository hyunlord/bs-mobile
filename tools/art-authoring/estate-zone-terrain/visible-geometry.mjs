export function visibleGeometry(image, origin, zones) {
  let left=image.width, top=image.height, right=0, bottom=0;
  const occupied=new Set();
  for(let y=0;y<image.height;y++) for(let x=0;x<image.width;x++) {
    if(image.pixels[(y*image.width+x)*4+3]<=8) continue;
    left=Math.min(left,x); top=Math.min(top,y); right=Math.max(right,x+1); bottom=Math.max(bottom,y+1);
    if(!origin) continue;
    const px=origin[0]+x+.5, py=origin[1]+y+.5;
    const gx=(px/32+py/16)/2, gy=(py/16-px/32)/2;
    for(const z of zones) if(gx>=z.x&&gx<z.x+z.width&&gy>=z.y&&gy<z.y+z.height) occupied.add(z.id);
  }
  if(right===0) throw new Error('Empty approved sprite');
  return {left:left/image.width, top:top/image.height, width:(right-left)/image.width, height:(bottom-top)/image.height, occupiedZones:[...occupied].sort()};
}
