using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    public static class FallowUiSurface
    {
        public static void Button(UiShell ui, Button button, bool dark)
        {
            button.image.enabled = false;
            var rect = UiShell.Rect("Paper surface", button.transform); UiShell.Stretch(rect); rect.SetAsFirstSibling();
            var image = ui.Surface(rect, "ui.hint"); image.raycastTarget = true;
            image.pixelsPerUnitMultiplier = UiTokens.FallowFrameMultiplier;
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = dark ? UiTokens.FallowCharcoal : Color.white;
            colors.highlightedColor = colors.normalColor * 1.15f;
            colors.pressedColor = colors.normalColor * .8f;
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = colors.normalColor * .65f;
            button.colors = colors;
            button.GetComponentInChildren<Text>().color = !button.interactable ? UiTokens.FallowMuted : dark ? GameVisualTokens.Attack : GameVisualTokens.Ink;
        }
    }
}
