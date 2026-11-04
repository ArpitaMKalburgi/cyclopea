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
        [Tooltip("High-contrast option: the room stays this much darker at all times, so the static and text stand out.")]
        [Range(-1, 0)] public float HighContrastBrightness = -0.35f;
        public bool Enabled = true;

        float b, s, lastB = 99, lastS = 99;

        void Update()
        {
            if (Layer == null || Session == null) return;
            bool focus = Enabled && Session.AwaitingChoice;
            float k = FadeSeconds <= 0 ? 1 : Time.deltaTime / FadeSeconds;
            float hc = Options.HighContrast ? HighContrastBrightness : 0f;
            b = Mathf.MoveTowards(b, Mathf.Max(-1f, (focus ? TrialBrightness : 0f) + hc), k);
            s = Mathf.MoveTowards(s, focus ? TrialSaturation : 0f, k);
            if (Mathf.Abs(b - lastB) < 0.002f && Mathf.Abs(s - lastS) < 0.002f) return;
            Layer.SetBrightnessContrastSaturation(b, 0f, s);
            lastB = b; lastS = s;
        }
    }
}
