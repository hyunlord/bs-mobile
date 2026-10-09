using UnityEngine;

namespace Game.P1Capture
{
    [DefaultExecutionOrder(32000)]
    public sealed class CapturePlayerLoop : MonoBehaviour
    {
        void LateUpdate() => P1Capture.PlayerLoopUpdate();
    }
}
