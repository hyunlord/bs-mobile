using UnityEngine;

namespace Game.View
{
    public sealed class FoundationStatus : MonoBehaviour
    {
        public string Message { get; set; } = "Verifying game data...";
        public bool Failed { get; set; }
        public static readonly Color Ground = new Color32(231, 229, 223, 255);
        private GUIStyle style;

        private void OnGUI()
        {
            if (style == null) style = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 16 };
            style.normal.textColor = Failed ? new Color32(143, 56, 53, 255) : new Color32(37, 42, 45, 255);
            var safe = Screen.safeArea;
            GUI.Label(new Rect(safe.x + 16, Screen.height - safe.yMax + 24, safe.width - 32, safe.height - 48), Message, style);
        }
    }
}
