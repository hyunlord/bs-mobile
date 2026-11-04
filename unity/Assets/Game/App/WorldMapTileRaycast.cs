using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    public sealed class WorldMapTileRaycast : Image
    {
        public override bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, eventCamera, out var point)) return false;
            var rect = rectTransform.rect;
            return rect.width > 0 && rect.height > 0 && Mathf.Abs((point.x - rect.center.x) / (rect.width * .5f))
                + Mathf.Abs((point.y - rect.center.y) / (rect.height * .5f)) <= 1;
        }
    }
}
