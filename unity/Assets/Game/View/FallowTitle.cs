using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    public static class FallowTitle
    {
        public const string Glyphs = "휴경의 왕국 새봄의 터 돌본 땅에 다시 봄이 깃든다 시작하기 출정 준비 목표 재료 미선택 곡물 목재 특허장 선택됨 돌아가기 설정 빈칸 잠시 멈춤 레벨";

        public static void Show(UiShell ui, string selectedTarget, IReadOnlyDictionary<string, string> targets,
            Func<string, string> toolName, Action<string> selectTarget, Action start, Action settings)
        {
            var root = UiShell.Rect("Fallow title", ui.Content); UiShell.Stretch(root);
            var backdrop = UiShell.Rect("Kingdom illustration", root); UiShell.Stretch(backdrop);
            var texture = Resources.Load<Texture2D>("Fallow/title-background");
            if(texture != null)
            {
                var image = backdrop.gameObject.AddComponent<RawImage>(); image.texture = texture; image.raycastTarget = false;
                var fit = backdrop.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectRatio = (float)texture.width / texture.height; fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            }
            else ui.Surface(backdrop, "ui.panel");
            var logoTexture = Resources.Load<Texture2D>("Fallow/title-logo");
            if(logoTexture != null)
            {
                var logoFrame = UiShell.Rect("Kingdom logo area", root);
                logoFrame.anchorMin = new Vector2(.14f, .735f); logoFrame.anchorMax = new Vector2(.86f, .955f);
                logoFrame.offsetMin = logoFrame.offsetMax = Vector2.zero;
                var logo = UiShell.Rect("휴경의 왕국", logoFrame); UiShell.Stretch(logo);
                var image = logo.gameObject.AddComponent<RawImage>(); image.texture = logoTexture; image.raycastTarget = false;
                var fit = logo.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectRatio = (float)logoTexture.width / logoTexture.height; fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            }
            else
            {
                var title = ui.Label(root, "휴경의 왕국", UiTokens.FallowTitleLogo, 150);
                Center(title.rectTransform, .81f, 850, 150); title.color = GameVisualTokens.Attack;
                title.alignment = TextAnchor.MiddleCenter; title.fontStyle = FontStyle.Bold;
                var titleShadow = title.gameObject.AddComponent<Shadow>(); titleShadow.effectColor = GameVisualTokens.Ink; titleShadow.effectDistance = new Vector2(2, -4);
            }
            var caption = ui.Label(root, "돌본 땅에 다시 봄이 깃든다", UiTokens.Small, 36);
            Center(caption.rectTransform, logoTexture != null ? .71f : .745f, 700, 36); caption.alignment = TextAnchor.MiddleCenter; caption.color = GameVisualTokens.Ink;
            var captionShadow = caption.gameObject.AddComponent<Shadow>(); captionShadow.effectColor = GameVisualTokens.Attack; captionShadow.effectDistance = new Vector2(0, -1);
            var startButton = ui.Button(root, "시작하기", start);
            Center((RectTransform)startButton.transform, .115f, 450, 100);
            FallowUiSurface.Button(ui, startButton, false);
            startButton.GetComponentInChildren<Text>().fontSize = UiTokens.Title;
            var preparation = ui.Button(root, "출정 준비 · " + (selectedTarget == null ? "목표 재료 미선택" : MaterialName(selectedTarget)),
                () => ShowPreparation(ui, root, selectedTarget, targets, toolName, selectTarget));
            Center((RectTransform)preparation.transform, .045f, 530, UiTokens.TouchHeight);
            FallowUiSurface.Button(ui, preparation, true);
            preparation.GetComponentInChildren<Text>().fontSize = UiTokens.Caption;
            var options = ui.Button(root, "설정", settings);
            FallowUiSurface.Button(ui, options, true);
            var optionsRect = (RectTransform)options.transform;
            optionsRect.anchorMin = optionsRect.anchorMax = new Vector2(1, 0);
            optionsRect.pivot = new Vector2(1, 0); optionsRect.anchoredPosition = new Vector2(-24, 24);
            optionsRect.sizeDelta = new Vector2(112, UiTokens.TouchHeight);
        }

        static void ShowPreparation(UiShell ui, RectTransform title, string selectedTarget,
            IReadOnlyDictionary<string, string> targets, Func<string, string> toolName, Action<string> selectTarget)
        {
            title.gameObject.SetActive(false);
            var panel = ui.Panel("Departure preparation");
            ui.Label(panel, "새봄의 터", UiTokens.Title, 72);
            ui.Label(panel, "목표 재료\n관련 도구가 초반 성장 선택에 등장합니다.", UiTokens.Body, 96);
            foreach(var target in targets)
            {
                var id = target.Key;
                var choice = ui.Button(panel, (selectedTarget == id ? "선택됨 · " : "") + MaterialName(id) + " · " + toolName(target.Value), () => selectTarget(id));
                FallowUiSurface.Button(ui, choice, selectedTarget != id);
            }
            var back = ui.Button(panel, "돌아가기", () => { panel.parent.parent.gameObject.SetActive(false); UnityEngine.Object.Destroy(panel.parent.parent.gameObject); title.gameObject.SetActive(true); });
            FallowUiSurface.Button(ui, back, true);
        }

        static string MaterialName(string id) => id == "meta:grain" ? "곡물" : id == "meta:timber" ? "목재" : id == "meta:charter" ? "특허장" : "재료";
        static void Center(RectTransform rect, float y, float width, float height)
        { rect.anchorMin = rect.anchorMax = new Vector2(.5f, y); rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(width, height); }
    }
}
