using Game.View;
using UnityEngine;

namespace Game.App
{
    public sealed class MetaGrowthPulse : MonoBehaviour
    {
        private float age;
        private void Update()
        {
            age+=Time.unscaledDeltaTime;
            var progress=Mathf.Clamp01(age/GameVisualTokens.EmphasisSeconds);
            transform.localScale=Vector3.one*Mathf.Lerp(UiTokens.GrowthStartScale,1,Mathf.SmoothStep(0,1,progress));
            if(progress>=1)Destroy(this);
        }
    }
}
