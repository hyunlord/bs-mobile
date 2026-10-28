import { createHash } from 'node:crypto';
export const sha = bytes => createHash('sha256').update(bytes).digest('hex');
export function crop(image, {x,y,width,height}) {
  const pixels=Buffer.alloc(width*height*4);
  for(let row=0;row<height;row++)image.pixels.copy(pixels,row*width*4,((y+row)*image.width+x)*4,((y+row)*image.width+x+width)*4);
  return {width,height,pixels};
}
// Texture coordinates may relocate historical aliases; decoded pictures and all other role metadata must not change.
export function unaffectedContract(manifest,image,approved) {
  const roles=manifest.roles.filter(r=>r.atlas==='fx'&&!approved.has(r.id)).map(({frames,...role})=>({
    ...role,frames:frames.map(f=>({width:f.width,height:f.height,rgba:sha(crop(image,f).pixels)}))
  }));
  return {roles:roles.length,sha256:sha(JSON.stringify(roles))};
}
export function outsideHash(image,rects) {
  const pixels=Buffer.from(image.pixels);
  for(const [x,y,w,h] of rects)for(let row=y;row<y+h;row++)pixels.fill(0,(row*image.width+x)*4,(row*image.width+x+w)*4);
  return sha(pixels);
}
