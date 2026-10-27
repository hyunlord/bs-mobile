using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // One animated resident, separate from the cached static plan. No per-frame layout planning, sprite creation or allocation.
    public sealed class EstateCarryView : MonoBehaviour
    {
        readonly Sprite[] east = new Sprite[16], west = new Sprite[16];
        EstateCarryRoute route;
        EstateMapModel model;
        Image image;
        RectTransform rect;
        (RectTransform Rect, float Y)[] obstacles;
        int frame = -1; bool facingEast;

        public void Configure(EstateArt art, EstateMapModel map, Vector2[] points, List<(RectTransform Rect, float Y)> sorted)
        {
            model = map; route = new EstateCarryRoute(points); obstacles = sorted.ToArray();
            rect = (RectTransform)transform; image = gameObject.AddComponent<Image>();
            image.raycastTarget = false; image.material = art.Tint; image.color = Color.white;
            for (var i = 0; i < 16; i++)
            {
                east[i] = art.Get("worker-carry-se-" + i.ToString("00"));
                west[i] = art.Get("worker-carry-sw-" + i.ToString("00"));
            }
            RefreshAt(Time.unscaledTimeAsDouble);
        }

        void Update() => RefreshAt(Time.unscaledTimeAsDouble);
        public void RefreshAt(double presentationSeconds)
        {
            if (route == null || model == null) return;
            // Global presentation time survives page redraws at construction stage boundaries; no saved/Core state is touched.
            var pose = route.Sample(presentationSeconds);
            if (frame != pose.Frame || facingEast != pose.East)
            { frame = pose.Frame; facingEast = pose.East; image.sprite = (facingEast ? east : west)[frame]; }
            var pivot = image.sprite.pivot / image.sprite.rect.size;
            var point = model.GridToViewport(pose.Grid); var size = 66 * model.Scale;
            PortraitWidgets.Position(rect, point.x - pivot.x * size, point.y - (1 - pivot.y) * size, size, size);
            var y = EstateMapModel.Project(pose.Grid).y;
            // Reorder only on a foot-depth crossing; overlays and front rails remain above all residents.
            var target = -1;
            for (var i = 0; i < obstacles.Length; i++)
                if (obstacles[i].Rect != null && obstacles[i].Y > y)
                { target = obstacles[i].Rect.GetSiblingIndex(); break; }
            if (target < 0 && obstacles.Length > 0 && obstacles[obstacles.Length - 1].Rect != null)
                target = obstacles[obstacles.Length - 1].Rect.GetSiblingIndex() + 1;
            var current = rect.GetSiblingIndex();
            if (target >= 0) { if (current < target) target--; if (current != target) rect.SetSiblingIndex(target); }
        }
    }
}
