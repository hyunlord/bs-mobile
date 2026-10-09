using UnityEngine;
using UnityEngine.InputSystem;
using PlayerInput = SowSiege.Core.PlayerInput;

namespace Game.Input
{
    public sealed class DesktopControls
    {
        private bool waitingForRelease;

        public void Reset() => waitingForRelease = true;

        public PlayerInput Sample(FloatingStick stick, bool paused)
        {
            var direction = Vector2.zero;
#if UNITY_STANDALONE || UNITY_EDITOR
            var keyboard = Keyboard.current;
            if (keyboard != null)
                direction = new Vector2((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
#endif
            return ResolveInput(stick.Sample, stick.Active, direction, paused);
        }

        public PlayerInput ResolveInput(PlayerInput pointer, bool pointerActive, Vector2 keyboard, bool paused)
        {
            if (paused) waitingForRelease = true;
            else if (keyboard == Vector2.zero) waitingForRelease = false;
            return ResolveMovement(pointer, pointerActive, waitingForRelease ? Vector2.zero : keyboard, paused);
        }

        public static PlayerInput ResolveMovement(PlayerInput pointer, bool pointerActive, Vector2 keyboard, bool paused)
        {
            if (paused) return default;
            if (pointerActive) return pointer;
            var direction = Vector2.ClampMagnitude(keyboard, 1);
            return new PlayerInput((short)Mathf.RoundToInt(direction.x * PlayerInput.Scale),
                (short)Mathf.RoundToInt(direction.y * PlayerInput.Scale));
        }

        public static Vector2Int FitWindow(Vector2Int preset, Vector2Int desktop)
        {
            var scale = Mathf.Min(1, Mathf.Min(Mathf.Max(1, desktop.x - 80) / (float)preset.x,
                Mathf.Max(1, desktop.y - 120) / (float)preset.y));
            return new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(preset.x * scale)),
                Mathf.Max(1, Mathf.RoundToInt(preset.y * scale)));
        }

        public static void InitializeWindow() => ApplyPreset(new Vector2Int(900, 1950));

        public static void UpdateWindow()
        {
#if UNITY_STANDALONE && !UNITY_EDITOR
            var keyboard = Keyboard.current;
            if (keyboard == null || !Application.isFocused) return;
            if (keyboard.f1Key.wasPressedThisFrame) ApplyPreset(new Vector2Int(900, 1950));
            else if (keyboard.f2Key.wasPressedThisFrame) ApplyPreset(new Vector2Int(1080, 2520));
            else if (keyboard.f3Key.wasPressedThisFrame) ApplyPreset(new Vector2Int(2184, 1968));
#endif
        }

        private static void ApplyPreset(Vector2Int preset)
        {
#if UNITY_STANDALONE && !UNITY_EDITOR
            var size = FitWindow(preset, Screen.mainWindowDisplayInfo.workArea.size);
            Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
#endif
        }
    }
}
