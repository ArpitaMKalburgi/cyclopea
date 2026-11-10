using System;
using Oculus.Platform;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// Meta Platform SDK: entitlement check on launch, plus achievements and a days-in-a-row
    /// leaderboard once they are created on the developer dashboard (switch them on here then).
    /// The leaderboard ranks consistency, never eyesight. Every call is optional: if the platform
    /// isn't available, the game plays the same.
    /// </summary>
    public sealed class PlatformServices : MonoBehaviour
    {
        public SpotSession Session;
        public ScoreAnchor Scores;
        public bool UseAchievements;            // create first_creature, camouflage_round, streak_7 on the dashboard first
        public bool UseLeaderboard;             // create "days_in_a_row" (higher is better) first
        public string LeaderboardName = "days_in_a_row";

        public string Status { get; private set; } = "idle";
        bool ready;

        void OnEnable()
        {
            if (Session != null) { Session.Finished += OnFinished; Session.ArcadeFinished += OnArcadeFinished; }
            if (Scores != null) Scores.StreakUpdated += OnStreak;
        }

        void OnDisable()
        {
            if (Session != null) { Session.Finished -= OnFinished; Session.ArcadeFinished -= OnArcadeFinished; }
            if (Scores != null) Scores.StreakUpdated -= OnStreak;
        }

        void Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                Status = "initialising";
                Oculus.Platform.Core.AsyncInitialize().OnComplete(m =>
                {
                    if (m.IsError) { Status = "init error"; return; }
                    ready = true;
                    Entitlements.IsUserEntitledToApplication().OnComplete(e =>
                        Status = e.IsError ? "not entitled (sideloaded dev build)" : "entitled");
                });
            }
            catch (Exception e) { Status = "init threw: " + e.Message; }
#else
            Status = "editor: platform off";
#endif
        }

        void OnArcadeFinished(WhackRound.Result r)
        {
            if (!ready || !UseAchievements) return;
            if (r.Caught > 0) Achievements.Unlock("first_creature");
        }

        void OnFinished(SpotSession.Result r)
        {
            if (!ready || !UseAchievements) return;
            Achievements.Unlock("first_creature");
            if (r.Round >= 2) Achievements.Unlock("camouflage_round");
        }

        void OnStreak(Cyclopea.Core.Streak.State s)
        {
            if (!ready) return;
            if (UseAchievements && s.Count >= 7) Achievements.Unlock("streak_7");
            if (UseLeaderboard) Leaderboards.WriteEntry(LeaderboardName, s.Best);
        }
    }
}
