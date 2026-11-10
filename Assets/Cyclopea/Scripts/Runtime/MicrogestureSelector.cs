using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// Thumb-only play for limited arm mobility (Meta microgestures): swipe the thumb along the
    /// index finger to move a highlight across the 2x2 grid, tap to pick. The hand can rest
    /// anywhere the cameras can see it. Left/right swipes move across; forward/back move between
    /// the far and near rows of the grid.
    /// </summary>
    public sealed class MicrogestureSelector : PatchSelector
    {
        public OVRMicrogestureEventSource Left, Right;
        public StereoPatch[] Patches;   // grid order: 0 far-left, 1 far-right, 2 near-left, 3 near-right
        [Tooltip("The Menu button beside the near row: swipe back from the near row to reach it, forward to leave it.")]
        public StereoPatch Menu;
        public float ShowSeconds = 6f;

        const int OnMenu = 4;

        bool MenuShown
        {
            get
            {
                if (Menu == null) return false;
                var c = Menu.GetComponent<Collider>();
                return c != null && c.enabled;
            }
        }

        int cursor = -1;
        float lastGesture = -100f;

        public bool Active => cursor >= 0 && Time.time - lastGesture < ShowSeconds;

        void OnEnable()
        {
            if (Left != null) Left.WhenGestureRecognized += OnGesture;
            if (Right != null) Right.WhenGestureRecognized += OnGesture;
        }

        void OnDisable()
        {
            if (Left != null) Left.WhenGestureRecognized -= OnGesture;
            if (Right != null) Right.WhenGestureRecognized -= OnGesture;
            Hovered = null;
        }

        /// <summary>Pure grid navigation, exposed for tests.</summary>
        public static int Move(int cursor, OVRHand.MicrogestureType g)
        {
            if (cursor < 0) return 0;
            int col = cursor % 2, row = cursor / 2;
            switch (g)
            {
                case OVRHand.MicrogestureType.SwipeLeft: col = 0; break;
                case OVRHand.MicrogestureType.SwipeRight: col = 1; break;
                case OVRHand.MicrogestureType.SwipeForward: row = 0; break;
                case OVRHand.MicrogestureType.SwipeBackward: row = 1; break;
            }
            return row * 2 + col;
        }

        void OnGesture(OVRHand.MicrogestureType g)
        {
            if (Patches == null || Patches.Length < 4) return;
            bool wasActive = Active;
            lastGesture = Time.time;
            // The first gesture only shows the highlight, so a stray tap or swipe never answers a trial.
            if (!wasActive) { if (cursor < 0 || (cursor == OnMenu && !MenuShown)) cursor = 0; return; }
            if (cursor == OnMenu)
            {
                if (g == OVRHand.MicrogestureType.ThumbTap && MenuShown) Raise(Menu, "microgesture");
                else if (g != OVRHand.MicrogestureType.SwipeBackward) cursor = 2;
                return;
            }
            if (g == OVRHand.MicrogestureType.ThumbTap) { Raise(Patches[cursor], "microgesture"); return; }
            if (g == OVRHand.MicrogestureType.SwipeBackward && cursor >= 2 && MenuShown) { cursor = OnMenu; return; }
            cursor = Move(cursor, g);
        }

        void Update()
        {
            if (cursor == OnMenu && !MenuShown) cursor = 2;
            Hovered = !Active || Patches == null ? null
                    : cursor == OnMenu ? Menu
                    : cursor < Patches.Length ? Patches[cursor] : null;
        }
    }
}
