using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// Quiets the real room while a creature is hiding: passthrough dims and loses some colour
    /// during trials so the static is the brightest thing in view, then the room comes back for
    /// feedback and the score. The room stays visible the whole time (seated, comfortable).
    /// </summary>
    public sealed class PassthroughMood : MonoBehaviour
    {
        public OVRPassthroughLayer Layer;
        public SpotSession Session;
        [Range(-1, 0)] public float TrialBrightness = -0.18f;
        [Range(-1, 0)] public float TrialSaturation = -0.55f;
        public float FadeSeconds = 0.6f;
        public bool Enabled = true;

        float b, s, lastB = 99, lastS = 99;

        void Update()
        {
            if (Layer == null || Session == null) return;
            bool focus = Enabled && Session.AwaitingChoice;
            float k = FadeSeconds <= 0 ? 1 : Time.deltaTime / FadeSeconds;
            b = Mathf.MoveTowards(b, focus ? TrialBrightness : 0f, k);
            s = Mathf.MoveTowards(s, focus ? TrialSaturation : 0f, k);
            if (Mathf.Abs(b - lastB) < 0.002f && Mathf.Abs(s - lastS) < 0.002f) return;
            Layer.SetBrightnessContrastSaturation(b, 0f, s);
            lastB = b; lastS = s;
        }
    }
}
