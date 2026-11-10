using System;
using System.Collections.Generic;
using Cyclopea.Core;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// Critter Whack, the main game. A 3×3 grid of static lies on the real desk; critters pop up in it that
    /// only two eyes can see, and you slap them (or pinch, point and pull the trigger, or look and hold)
    /// before they duck back. Sixty-second rounds, combos, a best score, and critter kinds that unlock as
    /// you catch more:
    ///   Normal     rises, waits, ducks.
    ///   Trickster  a flat outline that one eye can see too. It doesn't float: leave it.
    ///   Hopper     (15 caught) ducks and pops up in the next tile.
    ///   Shy        (40 caught) barely rises: finer depth.
    ///   Golden     (80 caught) rare, quick and fine: big points.
    /// How far the critters rise adapts to the player (a 2-down/1-up staircase on their hits), so a
    /// round is always about three catches in four, and the finest rise caught is the round's depth line.
    /// Every time a critter appears, moves or leaves, the dots of all nine tiles are redrawn at once, so a
    /// single eye never sees anything change in one place: only two eyes together can find them.
    /// </summary>
    public sealed class WhackRound : MonoBehaviour
    {
        public enum Kind { Normal, Trickster, Hopper, Shy, Golden }

        public SpotSession Session;
        public StereoPatch[] Tiles;
        public LineRenderer[] Frames;
        public CreatureDirector Creatures;
        public SoundBank Sounds;
        public AudioSource Audio;

        [Header("Round")]
        public float RoundSeconds = 60f;
        public float CountdownSeconds = 3f;
        public float TileSize = 0.085f, TilePitch = 0.10f;
        [Tooltip("Picks of one tile closer together than this are one gesture seen by two inputs.")]
        public float DebounceSeconds = 0.2f;
        [Tooltip("Tests: no countdown, and critters keep coming without the clock running out.")]
        public bool NoClock;

        [Header("Difficulty")]
        public double StartArcsec = 1920, FloorArcsec = 15;

        public const string BestKey = "cyc_whack_best", TotalKey = "cyc_whack_total", LevelKey = "cyc_whack_level";
        public const int HopperAt = 15, ShyAt = 40, GoldenAt = 80;

        public struct Result
        {
            public int Score, Caught, Escaped, Fooled, Whiffs, BestCombo, TotalCaught;
            public bool PersonalBest;
            public double FinestArcsec;   // finest rise caught (NaN if none)
            public double ViewingDistanceM;
        }

        public event Action<Result> Finished;

        enum Phase { Off, Countdown, Play }
        Phase phase = Phase.Off;

        sealed class Mole
        {
            public int Tile;
            public Kind Kind;
            public float Start, End;
            public double Arcsec;
            public bool Hopped;
        }

        readonly List<Mole> moles = new List<Mole>();
        readonly float[] lastPick = new float[9];
        readonly Color[] flash = new Color[9];
        readonly float[] flashUntil = new float[9];
        System.Random rng = new System.Random();
        Staircase stair;
        float countdownEnds, endsAt, nextSpawn, startedAt;
        int lastCount = -1;
        double finest = double.NaN;

        public bool Running => phase != Phase.Off;
        public bool Playing => phase == Phase.Play;
        /// <summary>When the player last chose a tile (or the round started): the session's "can't pinch?" assist reads it.</summary>
        public float LastActionAt { get; private set; }
        public int Score { get; private set; }
        public int Combo { get; private set; }
        public int BestCombo { get; private set; }
        public int Caught { get; private set; }
        public int Escaped { get; private set; }
        public int Fooled { get; private set; }
        public int Whiffs { get; private set; }
        public float TimeLeft => phase == Phase.Play ? Mathf.Max(0, endsAt - Time.time) : RoundSeconds;
        public double LevelArcsec => stair != null ? stair.CurrentArcsec : StartArcsec;
        public static int TotalCaught => PlayerPrefs.GetInt(TotalKey, 0);
        public static int Best => PlayerPrefs.GetInt(BestKey, 0);

        /// <summary>For tests and autoplay: the tile a critter is up in right now (-1 if none), and its kind.</summary>
        public int UpTile(out Kind kind)
        {
            foreach (var m in moles)
                if (Time.time >= m.Start && Time.time < m.End) { kind = m.Kind; return m.Tile; }
            kind = Kind.Normal;
            return -1;
        }

        /// <summary>Every tile with a real (floating) critter up right now.</summary>
        public void RealUp(List<int> into)
        {
            into.Clear();
            foreach (var m in moles)
                if (m.Kind != Kind.Trickster && Time.time >= m.Start && Time.time < m.End) into.Add(m.Tile);
        }

        void Start()
        {
            if (phase == Phase.Off) SetTilesShown(false);
            if (Tiles != null && Session != null) foreach (var t in Tiles) if (t != null && t.Head == null) t.Head = Session.Head;
            if (Tiles != null) RefreshAll();
        }

        // ---------- Round ----------

        /// <summary>Lays the grid on the surface and starts a round with a short countdown.</summary>
        public void Begin(SurfaceFinder.Surface s)
        {
            Place(s);
            moles.Clear();
            Score = Combo = BestCombo = Caught = Escaped = Fooled = Whiffs = 0;
            finest = double.NaN;
            double start = PlayerPrefs.HasKey(LevelKey) ? Math.Min(StartArcsec, PlayerPrefs.GetFloat(LevelKey) * 2.0) : StartArcsec;
            stair = new Staircase(new Staircase.Config
            {
                StartArcsec = Math.Max(FloorArcsec, start), CeilingArcsec = StartArcsec, FloorArcsec = FloorArcsec,
                DescentStepLog = 0.15, InitialStepLog = 0.15, FineStepLog = 0.1, DownRule = 2,
                ReversalsToStop = 100000, MaxTrials = 100000, FloorTrialsToStop = 100000
            });
            for (int i = 0; i < Tiles.Length; i++) { lastPick[i] = -10; flashUntil[i] = 0; }
            SetTilesShown(true);
            RefreshAll();
            if (Creatures != null) Creatures.BeginSession();
            phase = Phase.Countdown;
            countdownEnds = Time.time + (NoClock ? 0 : CountdownSeconds);
            lastCount = -1;
        }

        /// <summary>Stops the round now without a result (leaving the screen, a break).</summary>
        public void Abort()
        {
            if (phase == Phase.Off) return;
            phase = Phase.Off;
            moles.Clear();
            ClearTiles();
            SetTilesShown(false);
        }

        void Update()
        {
            if (phase == Phase.Off) return;
            if (Session != null && Session.Paused)
            {
                // Headset off or the system menu: the round's clock and every critter wait.
                float d = Time.deltaTime;
                countdownEnds += d; endsAt += d; nextSpawn += d;
                foreach (var m in moles) { m.Start += d; m.End += d; }
                return;
            }
            float now = Time.time;
            if (phase == Phase.Countdown)
            {
                int left = Mathf.CeilToInt(countdownEnds - now);
                if (left != lastCount)
                {
                    lastCount = left;
                    if (left > 0)
                    {
                        Session.SetTitle(Verb() + "\nLeave the flat ones.\n" + left);
                        Play(Sounds != null ? Sounds.UiSelect : null);
                    }
                }
                if (now >= countdownEnds)
                {
                    phase = Phase.Play;
                    startedAt = now;
                    LastActionAt = now;
                    endsAt = now + RoundSeconds;
                    nextSpawn = now + 0.3f;
                    Play(Sounds != null ? Sounds.LevelUp : null);
                }
                return;
            }

            float progress = NoClock ? 0.5f : Mathf.Clamp01((now - startedAt) / RoundSeconds);
            Step(now, progress);
            Show(now);
            Session.SetTitle(Hud());
            if (!NoClock && now >= endsAt) End();
        }

        void Step(float now, float progress)
        {
            bool changed = false;
            for (int i = moles.Count - 1; i >= 0; i--)
            {
                var m = moles[i];
                // A hopper ducks halfway and pops up in a free neighbouring tile.
                if (m.Kind == Kind.Hopper && !m.Hopped && now >= m.Start + (m.End - m.Start) * 0.5f)
                {
                    int to = FreeNeighbour(m.Tile);
                    m.Hopped = true;
                    if (to >= 0)
                    {
                        Reshape(Tiles[to]);
                        m.Tile = to;
                        m.End = now + Mathf.Max(0.9f, (m.End - m.Start) * 0.6f);
                        changed = true;
                    }
                }
                if (now >= m.End)
                {
                    moles.RemoveAt(i);
                    changed = true;
                    if (m.Kind == Kind.Trickster) continue;   // left alone: right
                    Escaped++;
                    Combo = 0;
                    if (Counts(m.Kind)) stair.Respond(false);
                    if (Creatures != null && Sounds != null) Creatures.SayAt(Sounds.CreatureDive, Tiles[m.Tile].transform.position);
                }
            }

            int maxUp = progress < 0.3f ? 1 : progress < 0.7f ? 2 : 3;
            if (now >= nextSpawn && moles.Count < maxUp)
            {
                int tile = FreeTile();
                if (tile >= 0)
                {
                    var m = new Mole { Tile = tile, Kind = PickKind(now - startedAt), Start = now };
                    double level = stair.CurrentArcsec;
                    m.Arcsec = m.Kind == Kind.Shy ? Math.Max(FloorArcsec, level * 0.6)
                             : m.Kind == Kind.Golden ? Math.Max(FloorArcsec, level * 0.5)
                             : level;
                    float up = Mathf.Lerp(2.6f, 1.6f, progress) + (m.Arcsec < 240 ? 0.6f : 0f) + (m.Kind == Kind.Shy ? 0.5f : 0f);
                    if (m.Kind == Kind.Golden) up = 1.4f;
                    if (m.Kind == Kind.Trickster) up = 2.2f;
                    m.End = now + up;
                    Reshape(Tiles[tile]);
                    moles.Add(m);
                    changed = true;
                }
                nextSpawn = now + Mathf.Lerp(1.3f, 0.7f, progress) * (0.8f + 0.4f * (float)rng.NextDouble());
            }
            if (changed) RefreshAll();
        }

        static bool Counts(Kind k) => k == Kind.Normal || k == Kind.Hopper;

        /// <summary>Tests: the next critter is this kind.</summary>
        public Kind? ForceNext;

        Kind PickKind(float elapsed)
        {
            if (ForceNext.HasValue) { var k = ForceNext.Value; ForceNext = null; return k; }
            int total = TotalCaught + Caught;
            double w0 = 1.0;
            double wT = elapsed > 8f ? 0.2 : 0;
            double wH = total >= HopperAt ? 0.3 : 0;
            double wS = total >= ShyAt ? 0.25 : 0;
            double wG = total >= GoldenAt ? 0.06 : 0;
            double r = rng.NextDouble() * (w0 + wT + wH + wS + wG);
            if ((r -= wT) < 0) return Kind.Trickster;
            if ((r -= wH) < 0) return Kind.Hopper;
            if ((r -= wS) < 0) return Kind.Shy;
            if ((r -= wG) < 0) return Kind.Golden;
            return Kind.Normal;
        }

        /// <summary>A tile was chosen: slapped, pinched, pointed at, looked at.</summary>
        public void Whack(StereoPatch p, string method)
        {
            if (phase != Phase.Play || p == null) return;
            int t = Array.IndexOf(Tiles, p);
            if (t < 0) return;
            float now = Time.time;
            if (now - lastPick[t] < DebounceSeconds) return;
            lastPick[t] = now;
            LastActionAt = now;

            Mole m = moles.Find(x => x.Tile == t && now >= x.Start && now < x.End);
            if (m == null)
            {
                Whiffs++;
                Combo = 0;
                Flash(t, new Color(0.7f, 0.72f, 0.8f, 0.6f));
                Play(Sounds != null ? Sounds.UiHover : null);
                return;
            }
            moles.Remove(m);
            if (m.Kind == Kind.Trickster)
            {
                Fooled++;
                Combo = 0;
                Score = Math.Max(0, Score - 150);
                Flash(t, new Color(1f, 0.6f, 0.2f, 1f));
                Play(Sounds != null ? Sounds.PickWrong : null);
                Session.Toast("Flat fake! Real critters float.", 2f);
                RefreshAll();
                return;
            }

            Caught++;
            Combo++;
            BestCombo = Math.Max(BestCombo, Combo);
            int pts = m.Kind == Kind.Golden ? 500 : m.Kind == Kind.Shy ? 200 : m.Kind == Kind.Hopper ? 150 : 100;
            Score += pts * Multiplier;
            if (Counts(m.Kind)) stair.Respond(true);
            if (double.IsNaN(finest) || m.Arcsec < finest) finest = m.Arcsec;
            Flash(t, new Color(0.45f, 0.75f, 1f, 1f));
            Play(Sounds != null ? (m.Kind == Kind.Golden ? Sounds.PersonalBest : Sounds.PickRight) : null);
            // It comes out of the dots where it floated and goes to the nest (or onto the finger that caught it).
            var tile = Tiles[t];
            tile.Arcsec = 0; tile.MotionArcsec = 0;
            if (Creatures != null) Creatures.Reveal(tile, m.Arcsec, m.Arcsec, true);
            RefreshAll();
        }

        /// <summary>×1, then ×2 from three in a row, ×3 from six… up to ×5.</summary>
        public int Multiplier => Math.Min(5, 1 + Combo / 3);

        void End()
        {
            phase = Phase.Off;
            moles.Clear();
            ClearTiles();
            SetTilesShown(false);

            int best = Best;
            bool pb = Score > best && Score > 0;
            int total = TotalCaught + Caught;
            PlayerPrefs.SetInt(TotalKey, total);
            if (pb) PlayerPrefs.SetInt(BestKey, Score);
            if (stair != null) PlayerPrefs.SetFloat(LevelKey, (float)stair.CurrentArcsec);
            PlayerPrefs.Save();
            if (Creatures != null) Creatures.Persist();
            Play(Sounds != null ? (pb ? Sounds.PersonalBest : Sounds.ScoreReveal) : null);
            Debug.Log("[Cyclopea] whack round: " + Score + " points, caught " + Caught + ", escaped " + Escaped +
                      ", fooled " + Fooled + ", whiffs " + Whiffs + ", best combo " + BestCombo + ", level " + LevelArcsec.ToString("0") + "″");
            Finished?.Invoke(new Result
            {
                Score = Score, Caught = Caught, Escaped = Escaped, Fooled = Fooled, Whiffs = Whiffs, BestCombo = BestCombo,
                TotalCaught = total, PersonalBest = pb, FinestArcsec = finest, ViewingDistanceM = MeanDistance()
            });
        }

        /// <summary>Tests: end the round now as if the clock ran out.</summary>
        public void EndNow() { if (phase != Phase.Off) End(); }

        // ---------- Words ----------

        /// <summary>What to do, in the words of the input this player uses.</summary>
        public static string Verb() =>
            Options.LookAndHold ? "Look at the critters that float, and hold."
            : ControllerSelector.InUse ? "Point at the critters that float\nand pull the trigger."
            : "Slap the critters that float.";

        string Hud()
        {
            int secs = Mathf.CeilToInt(TimeLeft);
            string hud = Score.ToString("N0") + (Multiplier > 1 ? "   ×" + Multiplier : "") + "\n" + (secs / 60) + ":" + (secs % 60).ToString("00");
            // A new player keeps the instruction in view until the first few catches.
            if (TotalCaught + Caught < 3) hud = Verb() + "\n" + hud;
            return hud;
        }

        /// <summary>The score card after a round.</summary>
        public static string CardText(Result r)
        {
            var lines = new List<string> { "Round over: " + r.Score.ToString("N0") + " points" };
            lines.Add(r.Caught + " caught" + (r.BestCombo >= 3 ? " · best streak " + r.BestCombo : "") + (r.Fooled > 0 ? " · fooled " + r.Fooled : ""));
            if (!double.IsNaN(r.FinestArcsec))
                lines.Add("Finest catch: a " + (DisparityMath.PopOutMeters(r.FinestArcsec, r.ViewingDistanceM) * 1000).ToString("0.0") + " mm rise");
            lines.Add(r.PersonalBest ? "New best score!" : "Best: " + Math.Max(Best, r.Score).ToString("N0"));
            string next = NextUnlock(r.TotalCaught);
            if (next.Length > 0) lines.Add(next);
            return string.Join("\n", lines);
        }

        /// <summary>"Hoppers join at 15 caught: 6 to go." ("" once everything is unlocked.)</summary>
        public static string NextUnlock(int total)
        {
            if (total < HopperAt) return "Hoppers join at " + HopperAt + " caught: " + (HopperAt - total) + " to go";
            if (total < ShyAt) return "Shy critters join at " + ShyAt + " caught: " + (ShyAt - total) + " to go";
            if (total < GoldenAt) return "Golden critters join at " + GoldenAt + " caught: " + (GoldenAt - total) + " to go";
            return "";
        }

        // ---------- Tiles ----------

        public void Place(SurfaceFinder.Surface s)
        {
            if (Tiles == null) return;
            var rot = SurfaceFinder.QuadRotation(s);
            for (int i = 0; i < Tiles.Length; i++)
            {
                int col = i % 3 - 1, row = 1 - i / 3;
                var t = Tiles[i].transform;
                t.SetPositionAndRotation(s.Center + s.Right * col * TilePitch + s.Away * row * TilePitch + s.Normal * 0.001f, rot);
                t.localScale = new Vector3(TileSize, TileSize, 1f);
            }
        }

        void SetTilesShown(bool on)
        {
            if (Tiles == null) return;
            foreach (var t in Tiles)
            {
                if (t == null) continue;
                var r = t.GetComponent<Renderer>(); if (r != null) r.enabled = on;
                var c = t.GetComponent<Collider>(); if (c != null) c.enabled = on;
            }
            if (Frames != null) foreach (var f in Frames) if (f != null) f.enabled = on;
        }

        void ClearTiles()
        {
            if (Tiles == null) return;
            foreach (var t in Tiles) if (t != null) { t.Arcsec = 0; t.MotionArcsec = 0; t.Decoy = false; }
        }

        /// <summary>New dots in all nine tiles at once: one eye sees the whole grid change, never one tile.</summary>
        void RefreshAll()
        {
            foreach (var t in Tiles) if (t != null) t.Seed = (float)(rng.NextDouble() * 1000.0);
        }

        /// <summary>A new outline for the next critter in this tile (invisible until it floats).</summary>
        void Reshape(StereoPatch p)
        {
            p.MaskCenter = new Vector2(0.5f + (float)(rng.NextDouble() - 0.5) * 0.1f, 0.5f + (float)(rng.NextDouble() - 0.5) * 0.1f);
            p.MaskRadius = 0.3f;
            p.MaskWobble = 0.08f + (float)rng.NextDouble() * 0.14f;
            p.MaskLobes = 3 + rng.Next(0, 4);
        }

        void Show(float now)
        {
            bool motion = Session != null && Session.PreferredCue == SessionLog.Cue.Motion;
            for (int i = 0; i < Tiles.Length; i++)
            {
                var t = Tiles[i];
                if (t == null) continue;
                Mole m = moles.Find(x => x.Tile == i && now >= x.Start && now < x.End);
                bool trick = m != null && m.Kind == Kind.Trickster;
                double a = m != null && !trick ? m.Arcsec : 0;
                t.Decoy = trick;
                t.Arcsec = motion ? 0 : a;
                t.MotionArcsec = motion ? a : 0;
                if (Frames != null && i < Frames.Length && Frames[i] != null)
                {
                    var c = now < flashUntil[i] ? flash[i] : new Color(0.78f, 0.88f, 1f, 0.22f);
                    Frames[i].startColor = Frames[i].endColor = c;
                }
            }
        }

        void Flash(int t, Color c) { flash[t] = c; flashUntil[t] = Time.time + 0.35f; }

        int FreeTile()
        {
            int start = rng.Next(0, Tiles.Length);
            for (int k = 0; k < Tiles.Length; k++)
            {
                int i = (start + k) % Tiles.Length;
                if (!moles.Exists(m => m.Tile == i) && Time.time - lastPick[i] > 0.5f) return i;
            }
            return -1;
        }

        int FreeNeighbour(int t)
        {
            int c = t % 3, r = t / 3;
            var options = new List<int>();
            for (int dc = -1; dc <= 1; dc++)
                for (int dr = -1; dr <= 1; dr++)
                {
                    if ((dc == 0) == (dr == 0)) continue;   // the four orthogonal neighbours
                    int nc = c + dc, nr = r + dr;
                    if (nc < 0 || nc > 2 || nr < 0 || nr > 2) continue;
                    int n = nr * 3 + nc;
                    if (!moles.Exists(m => m.Tile == n)) options.Add(n);
                }
            return options.Count == 0 ? -1 : options[rng.Next(options.Count)];
        }

        double MeanDistance()
        {
            double s = 0; int n = 0;
            foreach (var t in Tiles) if (t != null) { s += t.LastViewingDistance; n++; }
            return n > 0 ? s / n : 0.6;
        }

        void Play(AudioClip c) { if (Audio != null && c != null) Audio.PlayOneShot(c); }
    }
}
