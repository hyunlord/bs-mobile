using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    // Four feathered edge strips leave the centre clear and render beneath all HUD siblings.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class DamageEdgeGraphic : MaskableGraphic
    {
        float opacity;
        public void SetOpacity(float value)
        {
            if(Mathf.Approximately(opacity,value))return;
            opacity=value;SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();if(opacity<=0)return;
            var r=rectTransform.rect;var width=Mathf.Min(r.width,r.height)*.055f;
            var red=new Color(.65f,.025f,.015f,opacity);var clear=new Color(red.r,red.g,red.b,0);
            Strip(mesh,new Vector2(r.xMin,r.yMin),new Vector2(r.xMin,r.yMax),new Vector2(r.xMin+width,r.yMax),new Vector2(r.xMin+width,r.yMin),red,clear);
            Strip(mesh,new Vector2(r.xMax,r.yMax),new Vector2(r.xMax,r.yMin),new Vector2(r.xMax-width,r.yMin),new Vector2(r.xMax-width,r.yMax),red,clear);
            Strip(mesh,new Vector2(r.xMin,r.yMax),new Vector2(r.xMax,r.yMax),new Vector2(r.xMax,r.yMax-width),new Vector2(r.xMin,r.yMax-width),red,clear);
            Strip(mesh,new Vector2(r.xMax,r.yMin),new Vector2(r.xMin,r.yMin),new Vector2(r.xMin,r.yMin+width),new Vector2(r.xMax,r.yMin+width),red,clear);
        }
        static void Strip(VertexHelper mesh,Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color outer,Color inner)
        {
            var start=mesh.currentVertCount;
            mesh.AddVert(a,outer,Vector2.zero);mesh.AddVert(b,outer,Vector2.zero);mesh.AddVert(c,inner,Vector2.zero);mesh.AddVert(d,inner,Vector2.zero);
            mesh.AddTriangle(start,start+1,start+2);mesh.AddTriangle(start,start+2,start+3);
        }
    }
}
