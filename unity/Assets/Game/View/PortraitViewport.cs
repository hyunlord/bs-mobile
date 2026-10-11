using UnityEngine;
namespace Game.View
{
    public readonly struct PortraitViewport
    {
        public readonly Rect Pixels, SafePixels;
        public readonly float Scale;
        public PortraitViewport(Rect pixels,Rect safe,float scale){Pixels=pixels;SafePixels=safe;Scale=scale;}
        public static PortraitViewport Calculate(Vector2 screen,Rect safe,bool mobile)
        {
            var width=Mathf.Max(1,screen.x);var height=Mathf.Max(1,screen.y);
            var fullPhone=mobile&&height>=width;
            var scale=fullPhone?width/390f:Mathf.Min(width/390f,height/844f);
            var pixels=fullPhone?new Rect(0,0,width,height):new Rect((width-390*scale)*.5f,(height-844*scale)*.5f,390*scale,844*scale);
            if(safe.width<=0||safe.height<=0)safe=new Rect(0,0,width,height);
            var clipped=Rect.MinMaxRect(Mathf.Max(pixels.xMin,safe.xMin),Mathf.Max(pixels.yMin,safe.yMin),Mathf.Min(pixels.xMax,safe.xMax),Mathf.Min(pixels.yMax,safe.yMax));
            if(clipped.width<=0||clipped.height<=0)clipped=pixels;
            return new PortraitViewport(pixels,clipped,scale);
        }
    }
}
