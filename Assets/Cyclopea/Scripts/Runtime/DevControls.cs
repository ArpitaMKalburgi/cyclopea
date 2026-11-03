using Cyclopea.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cyclopea
{
    /// <summary>
    /// Switches for the Nov 4 device checks, reachable without controllers or menus:
    ///   left middle-finger pinch  : eye tint on/off (left eye red, right eye blue)
    ///   left ring-finger pinch    : cycle eyes Both -> Left only -> Right only (the one-eye control)
    ///   left pinky pinch          : flip disparity sign (if creatures sink instead of float)
    ///   right middle-finger pinch : toggle head-gaze dwell selection (eyes-only mode)
    /// Keyboard in the editor: T, E, O, H, plus N = next tester id, R = restart.
    /// The left index pinch is never used here; it stays the game's select.
    /// </summary>
    public sealed class DevControls : MonoBehaviour
    {
        public static SessionLog.EyeCondition Eyes = SessionLog.EyeCondition.Both;

        public OVRHand LeftHand, RightHand;
        public SpotSession Session;
        public HeadDwellSelector Dwell;
        public HandRaySelector HandRay;

        bool lm, lr, lp, rm;

        void Start() => Apply();

        void Update()
        {
            if (Edge(LeftHand, OVRHand.HandFinger.Middle, ref lm)) ToggleTint();
            if (Edge(LeftHand, OVRHand.HandFinger.Ring, ref lr)) CycleEyes();
            if (Edge(LeftHand, OVRHand.HandFinger.Pinky, ref lp)) FlipSign();
            if (Edge(RightHand, OVRHand.HandFinger.Middle, ref rm)) ToggleDwell();

            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.tKey.wasPressedThisFrame) ToggleTint();
            if (kb.eKey.wasPressedThisFrame) CycleEyes();
            if (kb.oKey.wasPressedThisFrame) FlipSign();
            if (kb.hKey.wasPressedThisFrame) ToggleDwell();
            if (kb.nKey.wasPressedThisFrame) NextTester();
            if (kb.rKey.wasPressedThisFrame && Session != null) Session.Restart();
        }

        static bool Edge(OVRHand hand, OVRHand.HandFinger f, ref bool down)
        {
            if (hand == null || !hand.IsTracked) { down = false; return false; }
            float s = hand.GetFingerPinchStrength(f);
            if (!down && s > 0.92f) { down = true; return true; }
            if (down && s < 0.4f) down = false;
            return false;
        }

        void ToggleTint()
        {
            StereoPatch.DebugEyeTint = !StereoPatch.DebugEyeTint;
            Say(StereoPatch.DebugEyeTint ? "Eye tint ON  (left red, right blue)" : "Eye tint off");
        }

        void CycleEyes()
        {
            Eyes = (SessionLog.EyeCondition)(((int)Eyes + 1) % 3);
            Apply();
            Say("Eyes: " + Eyes + (Eyes == SessionLog.EyeCondition.Both ? "" : "  (one-eye control: expect ~25 %)"));
            if (Session != null) Session.Restart();
        }

        void FlipSign()
        {
            StereoPatch.OffsetSign = -StereoPatch.OffsetSign;
            Say(StereoPatch.OffsetSign > 0 ? "Disparity: crossed (+1)" : "Disparity: uncrossed (-1)");
        }

        void ToggleDwell()
        {
            if (Dwell == null) return;
            Dwell.enabled = !Dwell.enabled;
            if (HandRay != null) HandRay.enabled = !Dwell.enabled;
            Say(Dwell.enabled ? "Eyes-only mode: look and hold" : "Hand mode: point and pinch");
        }

        void NextTester()
        {
            int id = PlayerPrefs.GetInt(SpotSession.SubjectKey, 1) + 1;
            PlayerPrefs.SetInt(SpotSession.SubjectKey, id);
            PlayerPrefs.Save();
            Say("Tester P" + id.ToString("00"));
            if (Session != null) Session.Restart();
        }

        static void Apply()
        {
            // One-eye control: the covered eye sees flat grey, the other sees the full static.
            StereoPatch.ContrastL = Eyes == SessionLog.EyeCondition.RightOnly ? 0f : 1f;
            StereoPatch.ContrastR = Eyes == SessionLog.EyeCondition.LeftOnly ? 0f : 1f;
        }

        void Say(string msg)
        {
            Debug.Log("[Cyclopea] " + msg);
            if (Session != null) Session.Toast(msg);
        }
    }
}
