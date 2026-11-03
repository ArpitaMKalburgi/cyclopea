using System;
using System.Collections.Generic;

namespace Cyclopea.Core
{
    /// <summary>
    /// The ladder of stereo skills a session climbs. Each rung is its own staircase so a
    /// player gets a profile (one threshold per skill), not a single number stuck at the
    /// display floor.
    ///   Spot  — is a creature here? 4-AFC pinch the living patch.
    ///   Sort  — two creatures, grab the nearer. Level = disparity *difference*.
    ///   Shape — which silhouette? Relief exists only in disparity. 4-AFC.
    ///   Catch — reach to its depth; coarse disparities only, scored by depth error.
    /// Engine-independent; the Unity layer asks what to show and reports what happened.
    /// </summary>
    public sealed class SkillLadder
    {
        public enum Rung { Spot, Sort, Shape, Catch }

        public sealed class RungConfig
        {
            public Rung Rung;
            public Staircase.Config Stair;
            public int Alternatives;         // 4 for Spot/Shape, 2 for Sort, 1 for Catch
            public bool Enabled = true;
        }

        public sealed class Profile
        {
            public readonly Dictionary<Rung, double> ThresholdArcsec = new Dictionary<Rung, double>();
            public readonly Dictionary<Rung, bool> AtFloor = new Dictionary<Rung, bool>();
            public readonly Dictionary<Rung, int> Trials = new Dictionary<Rung, int>();
            public bool StereoMeasured;      // false => parallax scaffold path, honest card
        }

        readonly List<RungConfig> rungs;
        readonly Dictionary<Rung, Staircase> stairs = new Dictionary<Rung, Staircase>();
        int index;
        int spotMissesAtCeiling;

        public const int CeilingMissesForNoStereo = 6;

        public SkillLadder(double floorArcsec, IEnumerable<RungConfig> configs = null)
        {
            rungs = new List<RungConfig>(configs ?? Default(floorArcsec));
            foreach (var r in rungs) stairs[r.Rung] = new Staircase(r.Stair);
        }

        public static IEnumerable<RungConfig> Default(double floorArcsec)
        {
            yield return new RungConfig
            {
                Rung = Rung.Spot, Alternatives = 4,
                Stair = new Staircase.Config { StartArcsec = 1920, FloorArcsec = floorArcsec }
            };
            yield return new RungConfig
            {
                Rung = Rung.Sort, Alternatives = 2,
                // 2-AFC: the staircase still targets 79 %; chance is 50 %, so start large.
                Stair = new Staircase.Config { StartArcsec = 1920, FloorArcsec = floorArcsec * 2, DownRule = 3 }
            };
            yield return new RungConfig
            {
                Rung = Rung.Shape, Alternatives = 4,
                // Relief needs structure, not just detection: floor and start sit higher.
                Stair = new Staircase.Config { StartArcsec = 1920, FloorArcsec = floorArcsec * 4 }
            };
            yield return new RungConfig
            {
                Rung = Rung.Catch, Alternatives = 1,
                // Hands resolve ~1 cm, so Catch lives at >= ~480" (13 mm at 0.6 m) and stops there.
                Stair = new Staircase.Config { StartArcsec = 1920, FloorArcsec = 480, ReversalsToStop = 6 }
            };
        }

        public bool IsFinished => index >= rungs.Count;
        public Rung CurrentRung => rungs[Math.Min(index, rungs.Count - 1)].Rung;
        public int CurrentAlternatives => rungs[Math.Min(index, rungs.Count - 1)].Alternatives;
        public double CurrentArcsec => stairs[CurrentRung].CurrentArcsec;
        public Staircase CurrentStaircase => stairs[CurrentRung];

        /// <summary>Report a trial on the current rung. Advances to the next rung when its staircase finishes.</summary>
        public void Respond(bool correct)
        {
            if (IsFinished) return;
            var stair = stairs[CurrentRung];

            // No-stereo detector: repeated misses at the ceiling on Spot means the player cannot
            // see disparity at all; stop measuring and hand over to the parallax scaffold.
            if (CurrentRung == Rung.Spot && !correct &&
                stair.CurrentArcsec >= stair.Config_CeilingArcsec() - 1e-6)
            {
                spotMissesAtCeiling++;
                if (spotMissesAtCeiling >= CeilingMissesForNoStereo)
                {
                    index = rungs.Count; // finish the ladder; profile reports StereoMeasured = false
                    return;
                }
            }

            stair.Respond(correct);
            while (!IsFinished && (stairs[CurrentRung].IsFinished || !rungs[index].Enabled))
                index++;
        }

        public Profile BuildProfile()
        {
            var p = new Profile { StereoMeasured = spotMissesAtCeiling < CeilingMissesForNoStereo };
            foreach (var r in rungs)
            {
                var s = stairs[r.Rung];
                if (s.Trials == 0) continue;
                p.ThresholdArcsec[r.Rung] = s.ThresholdArcsec();
                p.AtFloor[r.Rung] = s.AtFloor();
                p.Trials[r.Rung] = s.Trials;
            }
            return p;
        }

        /// <summary>
        /// Human line for the Depth Score card, e.g. "Spot: 42″ — a 1.2 mm step at your desk".
        /// Reports "≤ floor" rather than a false precision below the display limit.
        /// </summary>
        public static string Describe(Rung rung, double arcsec, bool atFloor, double viewingDistanceM)
        {
            if (double.IsNaN(arcsec)) return rung + ": not measured";
            double mm = DisparityMath.PopOutMeters(arcsec, viewingDistanceM) * 1000;
            string limit = rung == Rung.Catch ? "hand limit" : "display limit";
            string val = atFloor ? "≤ " + arcsec.ToString("0") + "″ (" + limit + ")" : arcsec.ToString("0") + "″";
            return rung + ": " + val + " — a " + mm.ToString("0.0") + " mm step at " +
                   (viewingDistanceM * 100).ToString("0") + " cm";
        }
    }

    public static class MotionScore
    {
        /// <summary>
        /// Score line for the motion-parallax path. The level runs on the same arcsecond scale as
        /// stereo (so one staircase serves both), but what the player found is a depth step seen by
        /// moving the head, so the card speaks in millimetres and never in arcseconds.
        /// </summary>
        public static string Describe(double levelArcsec, bool atFloor, double viewingDistanceM)
        {
            if (double.IsNaN(levelArcsec)) return "Motion: not measured";
            double mm = DisparityMath.PopOutMeters(levelArcsec, viewingDistanceM) * 1000;
            return "Motion: " + (atFloor ? "≤ " : "") + "a " + mm.ToString("0.0") + " mm step at " +
                   (viewingDistanceM * 100).ToString("0") + " cm, found by moving your head";
        }
    }

    static class StaircaseConfigAccess
    {
        // Staircase keeps its config private; the ceiling is the only value the ladder needs.
        public static double Config_CeilingArcsec(this Staircase s) => s.CeilingArcsec;
    }
}
