using UnityEngine;
namespace Game.View
{
    public static class PortraitTokens
    {
        public const float Width=390,Height=844,Padding=12,Touch=44,Action=48,Equipment=26,HudHeight=76;
        public const int Title=24,Heading=18,Body=15,Small=14,Caption=12;
        public const float StickRadius=52,StickThumb=34.666667f;
        public const float HudInset=8;
        public static readonly Color CreamButtonInk = new Color32(0x3B,0x2A,0x18,255);
        public static readonly Color DisabledButtonInk = new Color32(112,108,100,140);
        // Same multiplier UiShell gives disabled buttons; applied to the art itself so it shows without a colour-tint fade.
        public static readonly Color DisabledButtonTint = new Color(.6f,.63f,.64f);
    }
}
