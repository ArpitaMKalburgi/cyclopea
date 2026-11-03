using Meta.XR.ImmersiveDebugger;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// Everything worth tuning in the headset, exposed to Meta's Immersive Debugger: open its panel
    /// in a development build and drag sliders while looking at the static. The judge build ships
    /// with the debugger switched off, so none of this is visible to players.
    /// </summary>
    public sealed class TuningPanel : MonoBehaviour
    {
        const string Stereo = "Cyclopea stereo", Room = "Cyclopea room", Live = "Cyclopea live";

        public SpotSession Session;
        public PassthroughMood Mood;
        public DeskCamouflage Camouflage;
        public ScoreAnchor Scores;
        public PlatformServices Platform;
        public SurfaceFinder Surface;
        public RoomAcoustics Acoustics;

        [DebugMember(Category = Stereo, Min = -1, Max = 1, Description = "+1 crossed (creature floats), -1 uncrossed (sinks)")]
        public float DisparitySign { get => StereoPatch.OffsetSign; set => StereoPatch.OffsetSign = value >= 0 ? 1f : -1f; }

        [DebugMember(Category = Stereo, Min = 0.003f, Max = 0.012f, Description = "Dot grid cell, metres")]
        public float CellSizeM { get => StereoPatch.CellSizeM; set => StereoPatch.CellSizeM = value; }

        [DebugMember(Category = Stereo, Min = 0.0008f, Max = 0.004f, Description = "Dot radius, metres")]
        public float DotRadiusM { get => StereoPatch.DotRadiusM; set => StereoPatch.DotRadiusM = value; }

        [DebugMember(Category = Stereo, Min = 0, Max = 1)]
        public float ContrastLeftEye { get => StereoPatch.ContrastL; set => StereoPatch.ContrastL = value; }

        [DebugMember(Category = Stereo, Min = 0, Max = 1)]
        public float ContrastRightEye { get => StereoPatch.ContrastR; set => StereoPatch.ContrastR = value; }

        [DebugMember(Category = Stereo, Description = "Left eye red, right eye blue")]
        public bool EyeTint { get => StereoPatch.DebugEyeTint; set => StereoPatch.DebugEyeTint = value; }

        [DebugMember(Category = Stereo, Description = "Motion mode only: simulated head sway for players who can't move their head")]
        public bool AutoSway { get => StereoPatch.AutoSway; set => StereoPatch.AutoSway = value; }

        [DebugMember(Category = Room, Min = -1, Max = 0)]
        public float TrialBrightness { get => Mood != null ? Mood.TrialBrightness : 0; set { if (Mood != null) Mood.TrialBrightness = value; } }

        [DebugMember(Category = Room, Min = -1, Max = 0)]
        public float TrialSaturation { get => Mood != null ? Mood.TrialSaturation : 0; set { if (Mood != null) Mood.TrialSaturation = value; } }

        [DebugMember(Category = Room)]
        public bool RoomFocus { get => Mood != null && Mood.Enabled; set { if (Mood != null) Mood.Enabled = value; } }

        [DebugMember(Category = Live, Tweakable = false)]
        public float LevelArcsec => Session != null && Session.AwaitingChoice ? (float)Session.CurrentArcsec : 0f;

        [DebugMember(Category = Live, Tweakable = false)]
        public int Round => Session != null ? Session.Round : 0;

        [DebugMember(Category = Live, Tweakable = false)]
        public string Surfaces => Surface != null ? Surface.Current.Kind + " via " + Surface.Source : "-";

        [DebugMember(Category = Live, Tweakable = false)]
        public string Input => DevControls.Mode.ToString();

        [DebugMember(Category = Live, Tweakable = false)]
        public string Cue => Session != null ? Session.Cue.ToString() : "-";

        [DebugMember(Category = Live, Tweakable = false)]
        public string CamouflageStatus => Camouflage != null ? Camouflage.Status : "-";

        [DebugMember(Category = Live, Tweakable = false)]
        public string AnchorStatus => Scores != null ? Scores.Status : "-";

        [DebugMember(Category = Live, Tweakable = false)]
        public string RoomAudio => Acoustics != null ? Acoustics.Status : "-";

        [DebugMember(Category = Live, Tweakable = false)]
        public string PlatformStatus => Platform != null ? Platform.Status : "-";

        [DebugMember(Category = Live)]
        public void StereoMode() { if (Session != null) Session.StartCue(Cyclopea.Core.SessionLog.Cue.Stereo); }

        [DebugMember(Category = Live)]
        public void MotionMode() { if (Session != null) Session.StartCue(Cyclopea.Core.SessionLog.Cue.Motion); }

        [DebugMember(Category = Live)]
        public void RestartSession() { if (Session != null) Session.Restart(); }

        [DebugMember(Category = Live)]
        public void ReplaceOnSurface() { if (Surface != null) Surface.Refresh(); }
    }
}
