using UnityEngine;
using UnityEngine.UI;
using SowSiege.Core;

namespace Game.View
{
    public sealed class UiDamageFeedback
    {
        readonly DamageTrailState state=new DamageTrailState();
        readonly RectTransform trail;
        readonly Image trailImage;
        readonly float leftInset,rightInset;
        readonly DamageEdgeGraphic edges;
        double previousTime;
        bool sampled;
        public UiDamageFeedback(UiShell ui,RectTransform healthFill)
        {
            leftInset=healthFill.offsetMin.x;rightInset=healthFill.offsetMax.x;
            trail=UiShell.Rect("Damage trail",healthFill.parent);
            trail.anchorMin=healthFill.anchorMin;trail.anchorMax=healthFill.anchorMax;
            trail.offsetMin=new Vector2(healthFill.offsetMax.x,healthFill.offsetMin.y);trail.offsetMax=healthFill.offsetMax;
            var image=trail.gameObject.AddComponent<Image>();
            image.type=Image.Type.Simple;
            image.color=Color.white;image.raycastTarget=false;
            trailImage=image;trailImage.enabled=false;
            trail.SetSiblingIndex(healthFill.GetSiblingIndex());
            var edgeRoot=UiShell.Rect("Damage screen edges",ui.Content);UiShell.Stretch(edgeRoot);edgeRoot.SetAsFirstSibling();
            edges=edgeRoot.gameObject.AddComponent<DamageEdgeGraphic>();edges.raycastTarget=false;edges.SetOpacity(0);
        }
        public void Present(RunFrame frame)
        {
            var now=Time.unscaledTimeAsDouble;
            var elapsed=sampled?(float)System.Math.Max(0,now-previousTime):0;
            previousTime=now;sampled=true;
            state.Present(frame.Lord.Health,frame.Lord.MaxHealth,frame.Tick,elapsed);
            trail.anchorMin=new Vector2(state.HealthFraction,0);
            trail.anchorMax=new Vector2(state.TrailFraction,1);
            var width=((RectTransform)trail.parent).rect.width;
            var start=Mathf.Max(leftInset,state.HealthFraction*width+rightInset);
            var offset=trail.offsetMin;offset.x=start-state.HealthFraction*width;trail.offsetMin=offset;
            trailImage.enabled=state.TrailFraction>state.HealthFraction&&state.TrailFraction*width+rightInset>start;
            edges.SetOpacity(state.EdgeOpacity);
        }
    }
}
