using System;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    public sealed class WorldAnnouncements : IDisposable
    {
        const float NoticeSeconds = 1.8f;
        const float BannerHeight = 96;
        const float Padding = 24;
        static readonly string[] SeasonNames = { "봄", "여름", "가을", "겨울" };
        readonly Camera camera;
        readonly GameObject root;
        readonly RectTransform banner;
        readonly Text label;
        readonly Image icon;
        readonly Font font;
        readonly ArtCatalog art;
        float remaining;
        bool boss;
        bool suppressed;
        int pendingSeason = -1;
        public string Message => label.text;
        public bool Visible => !suppressed && banner.gameObject.activeSelf;
        public bool AtBottom { get; private set; }

        public WorldAnnouncements(Camera camera, ArtCatalog art)
        {
            this.camera = camera; this.art = art;
            font = FontProvider.Create(new[] { "봄 여름 가을 겨울 보스 출현" });
            root = new GameObject("World announcements", typeof(RectTransform), typeof(Canvas));
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = 1; canvas.sortingOrder = 5;
            banner = UiShell.Rect("Announcement", root.transform);
            banner.anchorMin = banner.anchorMax = new Vector2(0.5f, 0.5f);
            var paper = banner.gameObject.AddComponent<Image>(); paper.sprite = art.ResolveRole("ui.hint").Sprite; paper.raycastTarget = false;
            var iconRect = UiShell.Rect("Boss icon", banner); iconRect.anchorMin = iconRect.anchorMax = new Vector2(0, 0.5f);
            iconRect.pivot = new Vector2(0, 0.5f); iconRect.anchoredPosition = new Vector2(Padding, 0); iconRect.sizeDelta = Vector2.one * 64;
            icon = iconRect.gameObject.AddComponent<Image>(); icon.preserveAspect = true; icon.raycastTarget = false;
            var textRect = UiShell.Rect("Announcement text", banner); UiShell.Stretch(textRect, Padding);
            label = textRect.gameObject.AddComponent<Text>(); label.font = font; label.fontSize = 36;
            label.color = GameVisualTokens.Ink; label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false;
            banner.gameObject.SetActive(false);
        }
        public void ShowSeason(int season)
        {
            if (season < 0 || season >= SeasonNames.Length) throw new ArgumentOutOfRangeException(nameof(season));
            if (boss && remaining > 0) { pendingSeason = season; return; }
            boss = false; label.text = SeasonNames[season]; icon.gameObject.SetActive(false);
            remaining = NoticeSeconds; banner.gameObject.SetActive(true);
        }
        public void ShowBoss(string sourceId)
        {
            if (!boss && remaining > 0)
                for (var i = 0; i < SeasonNames.Length; i++) if (label.text == SeasonNames[i]) pendingSeason = i;
            boss = true; label.text = "보스 출현"; icon.sprite = art.Resolve("enemy", sourceId, "idle").Sprite;
            icon.gameObject.SetActive(true); remaining = NoticeSeconds; banner.gameObject.SetActive(true);
        }
        public void SetSuppressed(bool value)
        {
            suppressed = value; root.GetComponent<Canvas>().enabled = !value;
        }
        public void Present(float seconds, Rect safeAreaPixels, Vector2 lordScreenPoint)
        {
            if (suppressed) return;
            remaining = Mathf.Max(0, remaining - Mathf.Max(0, seconds));
            if (remaining <= 0)
            {
                boss = false;
                if (pendingSeason >= 0) { var season = pendingSeason; pendingSeason = -1; ShowSeason(season); }
                else banner.gameObject.SetActive(false);
            }
            var pixels = camera.pixelRect;
            var scale = Mathf.Max(0.01f, pixels.width / 900f);
            banner.localScale = Vector3.one * scale;
            banner.sizeDelta = new Vector2(Mathf.Min(600, safeAreaPixels.width / scale - Padding * 2), BannerHeight);
            AtBottom = lordScreenPoint.y > safeAreaPixels.yMin + safeAreaPixels.height * 2 / 3;
            var centerY = AtBottom ? safeAreaPixels.yMin + (Padding + BannerHeight / 2) * scale : safeAreaPixels.yMax - (Padding + BannerHeight / 2) * scale;
            banner.anchoredPosition = new Vector2(safeAreaPixels.center.x - pixels.center.x, centerY - pixels.center.y);
        }
        public void Dispose()
        {
            if (Application.isPlaying) { UnityEngine.Object.Destroy(root); UnityEngine.Object.Destroy(font); }
            else { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(font); }
        }
    }
}
