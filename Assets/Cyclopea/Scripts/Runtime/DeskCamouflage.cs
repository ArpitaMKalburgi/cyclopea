using System.Collections;
using System.Collections.Generic;
using Cyclopea.Core;
using Meta.XR;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// Round 2 hides the creatures in the colours of the player's own desk. After the first Depth
    /// Score, the passthrough camera (Passthrough Camera API, Horizon OS v74+) takes one frame,
    /// samples the real surface under the squares, and the static is re-tinted to match it. Only
    /// colour changes; dot positions stay random, so one eye still sees nothing. Without camera
    /// permission or support, the default palette is kept and nothing else changes.
    /// </summary>
    public sealed class DeskCamouflage : MonoBehaviour
    {
        public SpotSession Session;
        public PassthroughCameraAccess CameraAccess;   // disabled until needed
        public float SampleRadiusM = 0.12f;

        public bool Ready { get; private set; }
        public Camouflage.Palette Palette { get; private set; } = Camouflage.Default;
        public string Status { get; private set; } = "idle";

        void OnEnable()
        {
            if (Session == null) return;
            Session.Finished += OnFinished;
            Session.RoundStarting += OnRound;
        }

        void OnDisable()
        {
            if (Session == null) return;
            Session.Finished -= OnFinished;
            Session.RoundStarting -= OnRound;
        }

        void OnFinished(SpotSession.Result r)
        {
            if (!Ready && r.Round == 1) StartCoroutine(Capture());
        }

        void OnRound(int round)
        {
            bool camo = Ready && round >= 2;
            var p = camo ? Palette : Camouflage.Default;
            StereoPatch.BackgroundColor = ToColor(p.Background);
            StereoPatch.DotColor = ToColor(p.Dots);
            if (camo && Session != null) Session.Toast("Round " + round + ": they're hiding in your desk's colours", 4f);
        }

        IEnumerator Capture()
        {
            if (CameraAccess == null || !PassthroughCameraAccess.IsSupported) { Status = "unsupported"; yield break; }
#if UNITY_ANDROID && !UNITY_EDITOR
            const string perm = OVRPermissionsRequester.PassthroughCameraAccessPermission;
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(perm))
            {
                Status = "asking";
                bool? granted = null;
                var cb = new UnityEngine.Android.PermissionCallbacks();
                cb.PermissionGranted += _ => granted = true;
                cb.PermissionDenied += _ => granted = false;
                cb.PermissionDeniedAndDontAskAgain += _ => granted = false;
                UnityEngine.Android.Permission.RequestUserPermission(perm, cb);
                float until = Time.time + 30f;
                while (granted == null && Time.time < until) yield return null;
                if (granted != true) { Status = "denied"; yield break; }
            }
#endif
            CameraAccess.enabled = true;
            float timeout = Time.time + 5f;
            while (!(CameraAccess.IsPlaying && CameraAccess.IsUpdatedThisFrame) && Time.time < timeout) yield return null;
            if (!CameraAccess.IsPlaying) { Status = "no frames"; CameraAccess.enabled = false; yield break; }

            var pixels = Sample();
            CameraAccess.enabled = false;   // one frame is all it needs; stop the stream to save power
            if (pixels.Count < 8) { Status = "surface not in view"; yield break; }
            Palette = Camouflage.FromPixels(pixels);
            Ready = true;
            Status = "ready " + Palette.Background + " / " + Palette.Dots;
            Debug.Log("[Cyclopea] desk camouflage " + Status);
        }

        List<Camouflage.Rgb> Sample()
        {
            var result = new List<Camouflage.Rgb>();
            var colors = CameraAccess.GetColors();
            var res = CameraAccess.CurrentResolution;
            if (!colors.IsCreated || res.x <= 0 || res.y <= 0 || Session == null || Session.Surface == null) return result;
            var s = Session.Surface.Current;
            for (int iy = -3; iy <= 3; iy++)
            for (int ix = -3; ix <= 3; ix++)
            {
                Vector3 w = s.Center + s.Right * (ix / 3f * SampleRadiusM) + s.Away * (iy / 3f * SampleRadiusM);
                Vector2 vp = CameraAccess.WorldToViewportPoint(w);
                if (vp.x < 0 || vp.x > 1 || vp.y < 0 || vp.y > 1) continue;
                int px = Mathf.Clamp((int)(vp.x * res.x), 0, res.x - 1);
                int py = Mathf.Clamp((int)(vp.y * res.y), 0, res.y - 1);
                for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int x = Mathf.Clamp(px + dx * 3, 0, res.x - 1), y = Mathf.Clamp(py + dy * 3, 0, res.y - 1);
                    Color32 c = colors[y * res.x + x];
                    result.Add(new Camouflage.Rgb(c.r / 255.0, c.g / 255.0, c.b / 255.0));
                }
            }
            return result;
        }

        static Color ToColor(Camouflage.Rgb c) => new Color((float)c.R, (float)c.G, (float)c.B, 1f);
    }
}
