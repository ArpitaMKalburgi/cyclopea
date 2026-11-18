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
        [Tooltip("Photosensitivity: the grid redraws on its own (a critter appears, hops or ducks) at most once per this many " +
                 "seconds, so the static never changes more than 3 times a second. A catch still answers at once.")]
        public float MinRedrawGapSeconds = 0.34f;
        [Tooltip("A brand-new player's critters stay up this much longer; it fades to nothing by BeginnerUntil caught.")]
        public float BeginnerExtraSeconds = 1.2f;
        public int BeginnerUntil = 40;

        [Header("Fun")]
        [Tooltip("The last seconds of a round are a frenzy: points ×2, critters come faster, the clock ticks. " +
                 "Only in rounds longer than twice this.")]
        public float FrenzySeconds = 10f;
        [Tooltip("Before golden critters are unlocked, one golden preview critter per round shows up in this " +
                 "window (seconds into play), at the player's own level: a surprise worth 500.")]
        public Vector2 PreviewGoldenWindow = new Vector2(20f, 40f);
        [Tooltip("The preview only comes in rounds at least this long (tests' short rounds stay predictable).")]
        public float PreviewMinRoundSeconds = 45f;
        [Tooltip("Each catch in a streak sounds one semitone higher, up to this many.")]
        public int ComboSemitones = 7;
        [Tooltip("Points float up from the tile that was caught.")]
        public bool PointPopups = true;

        /// <summary>1 for a brand-new player, 0 from BeginnerUntil critters caught (all rounds together).</summary>
        public float Beginner => BeginnerUntil <= 0 ? 0f : Mathf.Clamp01(1f - (TotalCaught + Caught) / (float)BeginnerUntil);

        public const string BestKey = "cyc_whack_best", TotalKey = "cyc_whack_total", LevelKey = "cyc_whack_level";
        public const int HopperAt = 15, ShyAt = 40, GoldenAt = 80;
        /// <summary>Levels never run out: after the last new kind, a new level every this many caught.</summary>
        public const int LevelEvery = 60;

        /// <summary>1 at the start, 2 with hoppers, 3 with shy critters, 4 with goldens, then one more every 60 caught.</summary>
        public static int Level(int total) =>
            total < HopperAt ? 1 : total < ShyAt ? 2 : total < GoldenAt ? 3 : 4 + (total - GoldenAt) / LevelEvery;

        /// <summary>The total caught at which the next level starts.</summary>
        public static int NextLevelAt(int total) =>
            total < HopperAt ? HopperAt : total < ShyAt ? ShyAt : total < GoldenAt ? GoldenAt
            : GoldenAt + ((total - GoldenAt) / LevelEvery + 1) * LevelEvery;

        /// <summary>How much faster the round runs: 1 up to level 4, then 6 % more per level, never beyond 1.6.</summary>
        public static float Pace(int level) => Mathf.Min(1.6f, 1f + 0.06f * Mathf.Max(0, level - 4));

        public struct Result
        {
            public int Score, Caught, Escaped, Fooled, Whiffs, BestCombo, TotalCaught;
            public bool PersonalBest;
            public double FinestArcsec;   // finest rise caught (NaN if none)
            public double ViewingDistanceM;
            // For the play log (observability): how the round ended and was played, and how the headset ran.
            public string Ended;          // "clock", or "left" (the menu's Home or start over, a break)
            public double Seconds;        // in play, pauses not counted
            public int FrenzyCaught, Golden;
            public double LevelArcsec, Beginner;
            public string Inputs;         // "slap:21,pinch:2"
            public bool FastHands, Clean;
            public FrameStats Frames;
        }

        public event Action<Result> Finished;
        /// <summary>Every round that was played, finished by the clock or left from the menu (the play log listens).</summary>
        public event Action<Result> Recorded;

        enum Phase { Off, Countdown, Play }
        Phase phase = Phase.Off;

        sealed class Mole
        {
            public int Tile;
            public Kind Kind;
            public float Start, End;
            public double Arcsec;
            public bool Hopped;
            public bool Preview;      // the golden preview before goldens are unlocked
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

        // What the play log records about a round, and the frenzy / golden preview / pop-ups.
        readonly FrameStats frames = new FrameStats();
        readonly Dictionary<string, int> inputs = new Dictionary<string, int>();
        float playSeconds, roundStartTime, previewAt = -1f;
        double beginnerAtStart;
        bool skipFrame, frenzyOn, previewDone;
        int lastTick = -1;

        /// <summary>This round's frame times so far (the play log and tests read it).</summary>
        public FrameStats FrameTimes => frames;
        /// <summary>The round is in its last FrenzySeconds: points ×2.</summary>
        public bool Frenzy => phase == Phase.Play && !NoClock && RoundSeconds > 2f * FrenzySeconds && TimeLeft <= FrenzySeconds;
        public int FrenzyCaught { get; private set; }
        public int GoldenCaught { get; private set; }
        /// <summary>The last pop-up's words (tests).</summary>
        public string LastPopup { get; private set; } = "";
        /// <summary>The pitch the last catch sounded at (1 = the first catch of a streak).</summary>
        public float LastCatchPitch { get; private set; } = 1f;
        /// <summary>The preview golden critter of this round has shown up.</summary>
        public bool PreviewShown => previewDone;

        public bool Running => phase != Phase.Off;
        public bool Playing => phase == Phase.Play;
        /// <summary>The 3-2-1 before play: no critters up yet (the coach may show the slap).</summary>
        public bool Counting => phase == Phase.Countdown;
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

        /// <summary>For tests: whether any critter (fakes too) is up in this tile now, and its kind.</summary>
        public bool TryKindAt(int tile, out Kind kind)
        {
            float now = Time.time;
            var m = moles.Find(x => x.Tile == tile && now >= x.Start && now < x.End);
            kind = m != null ? m.Kind : Kind.Normal;
            return m != null;
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
            // The pop-ups exist before play, so nothing is built mid-round (no hitch on a catch).
            while (PointPopups && pops.Count < PopPool) { var p = MakePop(); if (p == null) break; pops.Add(p); }
        }

        // ---------- Round ----------

        /// <summary>Lays the grid on the surface and starts a round with a short countdown.</summary>
        public void Begin(SurfaceFinder.Surface s)
        {
            Place(s);
            moles.Clear();
            hidden = false;
            fastHandsUsed = false;
            GameRedraws = 0;
            ShortestGameRedrawGap = float.PositiveInfinity;
            Score = Combo = BestCombo = Caught = Escaped = Fooled = Whiffs = 0;
            FrenzyCaught = GoldenCaught = 0;
            finest = double.NaN;
            playSeconds = 0;
            inputs.Clear();
            frames.Reset(RefreshHz());
            roundStartTime = Time.time;
            beginnerAtStart = Beginner;
            frenzyOn = false;
            lastTick = -1;
            previewDone = false;
            // Not unlocked yet: one golden critter shows up as a preview, somewhere in the middle of the round.
            previewAt = TotalCaught < GoldenAt && RoundSeconds >= PreviewMinRoundSeconds && !NoClock
                ? Mathf.Lerp(PreviewGoldenWindow.x, PreviewGoldenWindow.y, (float)rng.NextDouble())
                : -1f;
            HidePopups();
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

        /// <summary>
        /// Stops the round now without a score (leaving from the menu, a break). With <paramref name="keepCatches"/>
        /// the critters caught so far still count toward the next kind's unlock; no best score is set.
        /// </summary>
        public void Abort(bool keepCatches = false)
        {
            if (phase == Phase.Off) return;
            phase = Phase.Off;
            hidden = false;
            SetFastHands(false);
            moles.Clear();
            ClearTiles();
            SetTilesShown(false);
            HidePopups();
            if (keepCatches)
                Recorded?.Invoke(MakeResult("left", TotalCaught + Caught, false));
            if (keepCatches && Caught > 0)
            {
                PlayerPrefs.SetInt(TotalKey, TotalCaught + Caught);
                if (stair != null) PlayerPrefs.SetFloat(LevelKey, (float)stair.CurrentArcsec);
                PlayerPrefs.Save();
                if (Creatures != null) Creatures.Persist();
            }
            if (keepCatches)
                Debug.Log("[Cyclopea] whack round left: " + Score + " points, caught " + Caught + ", escaped " + Escaped +
                          ", fooled " + Fooled + ", whiffs " + Whiffs + " (catches kept, no score)");
        }

        bool hidden;

        /// <summary>The menu is over the grid: the tiles step aside (the clock already waits while the session is paused).</summary>
        public void SetHidden(bool on)
        {
            if (phase == Phase.Off) { hidden = false; return; }
            hidden = on;
            SetFastHands(!on && phase == Phase.Play);
            SetTilesShown(!on);
            if (on) HidePopups();
            if (!on) RefreshAll();
        }

        /// <summary>A real (floating) critter is up in this tile right now.</summary>
        public bool IsRealUp(StereoPatch tile)
        {
            if (phase != Phase.Play || tile == null) return false;
            int t = Array.IndexOf(Tiles, tile);
            float now = Time.time;
            return t >= 0 && moles.Exists(m => m.Tile == t && m.Kind != Kind.Trickster && now >= m.Start && now < m.End);
        }

        void Update()
        {
            if (phase == Phase.Off) return;
            if ((Session != null && Session.Paused) || hidden)
            {
                // Headset off, the system menu or the game's menu: the round's clock and every critter wait.
                float d = Time.deltaTime;
                countdownEnds += d; endsAt += d; nextSpawn += d; LastActionAt += d;
                foreach (var m in moles) { m.Start += d; m.End += d; }
                skipFrame = true;   // the first frame back may span the time away: not a frame-rate sample
                return;
            }
            float now = Time.time;
            UpdatePopups(now);
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
                    SetFastHands(true);
                    skipFrame = true;
                    startedAt = now;
                    LastActionAt = now;
                    endsAt = now + RoundSeconds;
                    nextSpawn = now + 0.3f;
                    Play(Sounds != null ? Sounds.LevelUp : null);
                }
                return;
            }

            playSeconds += Time.deltaTime;
            if (skipFrame) skipFrame = false;
            else frames.Add(Time.unscaledDeltaTime);
            UpdateFrenzy();

            float progress = NoClock ? 0.5f : Mathf.Clamp01((now - startedAt) / RoundSeconds);
            Step(now, progress);
            Show(now);
            Session.SetTitle(Hud());
            if (!NoClock && now >= endsAt) End();
        }

        void Step(float now, float progress)
        {
            bool changed = false;
            // Changes the game makes by itself wait for the next allowed redraw (at most 3 a second).
            bool mayRedraw = now - lastRedraw >= MinRedrawGapSeconds;
            for (int i = moles.Count - 1; i >= 0; i--)
            {
                var m = moles[i];
                if (!mayRedraw && now >= m.End)
                {
                    // Not yet: it stays up until the next redraw, so one eye never sees it go on its own.
                    m.End = lastRedraw + MinRedrawGapSeconds + 0.001f;
                    continue;
                }
                // A hopper ducks halfway and pops up in a free neighbouring tile.
                if (mayRedraw && m.Kind == Kind.Hopper && !m.Hopped && now >= m.Start + (m.End - m.Start) * 0.5f)
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
                    if (m.Kind == Kind.Golden && Session != null) Session.Toast("A golden one got away!", 1.8f);
                    if (Creatures != null && Sounds != null) Creatures.SayAt(Sounds.CreatureDive, Tiles[m.Tile].transform.position);
                }
            }

            // New players get slower critters and fewer at once: finding the one that floats takes a few
            // rounds to learn (first playtest, Nov 10: 11 caught, 34 escaped). The ease fades by 40 caught.
            float ease = Beginner;
            int level = Level(TotalCaught + Caught);
            float pace = Pace(level);
            int maxUp = progress < 0.3f ? 1 : progress < 0.7f ? 2 : 3;
            if (level >= 6 && progress >= 0.7f) maxUp = 4;
            if (ease > 0.5f) maxUp = Mathf.Min(maxUp, 2);
            if (mayRedraw && now >= nextSpawn && moles.Count < maxUp)
            {
                int tile = FreeTile();
                if (tile >= 0)
                {
                    bool preview = !previewDone && previewAt >= 0f && playSeconds >= previewAt;
                    var m = new Mole { Tile = tile, Kind = preview ? Kind.Golden : PickKind(now - startedAt), Start = now, Preview = preview };
                    if (preview) previewDone = true;
                    double depth = stair.CurrentArcsec;
                    // A preview golden floats at the player's own level: a surprise, not a test they can't pass yet.
                    m.Arcsec = m.Kind == Kind.Shy ? Math.Max(FloorArcsec, depth * 0.6)
                             : m.Kind == Kind.Golden && !m.Preview ? Math.Max(FloorArcsec, depth * 0.5)
                             : depth;
                    float up = Mathf.Lerp(2.6f, 1.6f, progress) + (m.Arcsec < 240 ? 0.6f : 0f) + (m.Kind == Kind.Shy ? 0.5f : 0f);
                    if (m.Kind == Kind.Golden && !m.Preview) up = 1.4f;
                    if (m.Kind == Kind.Trickster) up = 2.2f;
                    up /= 1f + 0.5f * (pace - 1f);                 // later levels: a little less time each
                    if (Options.LessMovement) up *= 1.5f;         // less movement: critters wait longer for you
                    up += BeginnerExtraSeconds * ease;
                    m.End = now + up;
                    Reshape(Tiles[tile]);
                    moles.Add(m);
                    changed = true;
                    // A golden one shimmers as it rises: you hear that one is up, never where (only two eyes find it).
                    if (m.Kind == Kind.Golden && Sounds != null && Audio != null && Sounds.NewKind != null) Audio.PlayOneShot(Sounds.NewKind, 0.6f);
                }
                float gap = Mathf.Lerp(1.3f, 0.7f, progress) * (1f + 0.4f * ease) * (0.8f + 0.4f * (float)rng.NextDouble()) / pace;
                if (Frenzy) gap *= 0.75f;
                nextSpawn = now + gap;
            }
            if (changed) RefreshAll(byGame: true);
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
            string how = string.IsNullOrEmpty(method) ? "other" : method;
            inputs[how] = inputs.TryGetValue(how, out int n) ? n + 1 : 1;

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
                Popup(t, "-150", new Color(1f, 0.62f, 0.25f, 1f));
                Play(Sounds != null ? Sounds.PickWrong : null);
                Session.Toast("Flat fake! Real critters float.", 2f);
                RefreshAll();
                return;
            }

            Caught++;
            Milestones.Mark("first_catch", method);
            Combo++;
            BestCombo = Math.Max(BestCombo, Combo);
            int pts = m.Kind == Kind.Golden ? 500 : m.Kind == Kind.Shy ? 200 : m.Kind == Kind.Hopper ? 150 : 100;
            bool frenzy = Frenzy;
            int gained = pts * Multiplier * (frenzy ? 2 : 1);
            Score += gained;
            if (frenzy) FrenzyCaught++;
            if (m.Kind == Kind.Golden) GoldenCaught++;
            if (Counts(m.Kind)) stair.Respond(true);
            if (double.IsNaN(finest) || m.Arcsec < finest) finest = m.Arcsec;
            Flash(t, new Color(0.45f, 0.75f, 1f, 1f));
            string mult = Multiplier > 1 || frenzy ? "  ×" + (Multiplier * (frenzy ? 2 : 1)) : "";
            if (m.Kind == Kind.Golden)
            {
                Popup(t, "Golden!\n+" + gained.ToString("N0"), new Color(1f, 0.84f, 0.3f, 1f));
            }
            else Popup(t, "+" + gained.ToString("N0") + mult, frenzy ? new Color(1f, 0.55f, 0.75f, 1f) : new Color(0.62f, 0.85f, 1f, 1f));
            // A streak climbs the scale: each catch in a row sounds a semitone higher.
            LastCatchPitch = Mathf.Pow(2f, Mathf.Min(Combo - 1, Mathf.Max(0, ComboSemitones)) / 12f);
            PlayCatch(Sounds != null ? (m.Kind == Kind.Golden ? Sounds.PersonalBest : Sounds.PickRight) : null, m.Kind == Kind.Golden ? 1f : LastCatchPitch);
            // It comes out of the dots where it floated and goes to the nest (or onto the finger that caught it).
            var tile = Tiles[t];
            tile.Arcsec = 0; tile.MotionArcsec = 0;
            if (Creatures != null) Creatures.Reveal(tile, m.Arcsec, m.Arcsec, true);
            // After the reveal, so a "new in your field guide" line doesn't push it off: the preview is the rarer news.
            if (m.Preview && Session != null) Session.Toast("A golden critter! They come for good at " + GoldenAt + " caught.", 3f);
            RefreshAll();
        }

        /// <summary>×1, then ×2 from three in a row, ×3 from six… up to ×5.</summary>
        public int Multiplier => Math.Min(5, 1 + Combo / 3);

        void End()
        {
            phase = Phase.Off;
            SetFastHands(false);
            moles.Clear();
            ClearTiles();
            SetTilesShown(false);
            HidePopups();

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
                      ", fooled " + Fooled + ", whiffs " + Whiffs + ", best combo " + BestCombo + ", level " + LevelArcsec.ToString("0") + "″" +
                      ", frenzy " + FrenzyCaught + ", golden " + GoldenCaught);
            Debug.Log("[Cyclopea] round frames: " + frames.Line());
            var result = MakeResult("clock", total, pb);
            Recorded?.Invoke(result);
            Finished?.Invoke(result);
        }

        Result MakeResult(string ended, int total, bool pb) => new Result
        {
            Score = Score, Caught = Caught, Escaped = Escaped, Fooled = Fooled, Whiffs = Whiffs, BestCombo = BestCombo,
            TotalCaught = total, PersonalBest = pb, FinestArcsec = finest, ViewingDistanceM = MeanDistance(),
            Ended = ended, Seconds = playSeconds, FrenzyCaught = FrenzyCaught, Golden = GoldenCaught,
            LevelArcsec = LevelArcsec, Beginner = beginnerAtStart, Inputs = InputsLine(), FastHands = fastHandsUsed,
            Clean = Health.CriticalFaultAt < roundStartTime, Frames = frames
        };

        string InputsLine()
        {
            var l = new List<KeyValuePair<string, int>>(inputs);
            l.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : string.CompareOrdinal(a.Key, b.Key));
            var parts = new List<string>();
            foreach (var kv in l) parts.Add(kv.Key + ":" + kv.Value);
            return string.Join(",", parts);
        }

        static float RefreshHz()
        {
            try { float hz = UnityEngine.XR.XRDevice.refreshRate; if (hz > 1) return hz; } catch (Exception) { }
            var r = Screen.currentResolution.refreshRateRatio.value;
            return r > 1 ? (float)r : 72f;
        }

        // ---------- Frenzy ----------

        void UpdateFrenzy()
        {
            bool on = Frenzy;
            if (on && !frenzyOn)
            {
                frenzyOn = true;
                Play(Sounds != null ? Sounds.LevelUp : null);
                Debug.Log("[Cyclopea] frenzy: last " + FrenzySeconds.ToString("0") + " s, points ×2");
            }
            if (!on) return;
            // The last five seconds tick.
            int secs = Mathf.CeilToInt(TimeLeft);
            if (secs <= 5 && secs != lastTick && secs > 0)
            {
                lastTick = secs;
                if (Sounds != null && Audio != null && Sounds.UiSelect != null) Audio.PlayOneShot(Sounds.UiSelect, 0.5f);
            }
        }

        // ---------- Pop-ups and the catch sound ----------
        // Points float up from the tile that was caught and fade. Text only: the static itself never changes for
        // them, so they add nothing to the three-redraws-a-second limit and nothing one eye could use.

        sealed class Pop { public TextMesh Text; public LegibleText Legible; public Vector3 From, Up; public float Born; public Color Color; }
        readonly List<Pop> pops = new List<Pop>();
        const float PopSeconds = 0.9f, PopRise = 0.035f;
        const int PopPool = 4;
        AudioSource catchAudio;

        void Popup(int tile, string words, Color c)
        {
            LastPopup = words;
            if (!PointPopups || Tiles == null || tile < 0 || tile >= Tiles.Length || Tiles[tile] == null) return;
            Pop p = null;
            foreach (var q in pops) if (!q.Text.gameObject.activeSelf) { p = q; break; }
            if (p == null)
            {
                if (pops.Count >= PopPool) { p = pops[0]; foreach (var q in pops) if (q.Born < p.Born) p = q; }
                else { p = MakePop(); if (p == null) return; pops.Add(p); }
            }
            var t = Tiles[tile].transform;
            Vector3 normal = -t.forward;
            p.From = t.position + normal * 0.012f;
            p.Up = normal;
            p.Born = Time.time;
            p.Color = c;
            p.Text.text = words;
            p.Text.gameObject.SetActive(true);
            PlacePop(p, 0f);
        }

        Pop MakePop()
        {
            var go = new GameObject("Points");
            go.transform.SetParent(transform, false);
            var tm = go.AddComponent<TextMesh>();
            var src = Session != null && Session.Status != null ? Session.Status : null;
            tm.font = src != null && src.font != null ? src.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (tm.font == null) { Destroy(go); return null; }
            tm.fontSize = 64;
            tm.characterSize = 0.001f;
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold;
            go.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
            var leg = go.AddComponent<LegibleText>();
            leg.Head = Session != null ? Session.Head : null;
            leg.TargetDmm = 20f;
            go.SetActive(false);
            return new Pop { Text = tm, Legible = leg };
        }

        void UpdatePopups(float now)
        {
            foreach (var p in pops)
            {
                if (!p.Text.gameObject.activeSelf) continue;
                float age = (now - p.Born) / PopSeconds;
                if (age >= 1f) { p.Text.gameObject.SetActive(false); continue; }
                PlacePop(p, age);
            }
        }

        void PlacePop(Pop p, float age)
        {
            // Less movement: the points stay where they appear and only fade.
            float rise = Options.LessMovement ? 0f : PopRise * (1f - (1f - age) * (1f - age));
            Vector3 pos = p.From + p.Up * rise;
            var head = Session != null ? Session.Head : null;
            p.Text.transform.SetPositionAndRotation(pos, head != null ? Quaternion.LookRotation(pos - head.position, Vector3.up) : Quaternion.identity);
            var c = p.Color;
            c.a = age < 0.6f ? 1f : Mathf.Clamp01(1f - (age - 0.6f) / 0.4f);
            p.Text.color = c;
        }

        void HidePopups()
        {
            foreach (var p in pops) if (p.Text != null) p.Text.gameObject.SetActive(false);
        }

        /// <summary>Pop-ups showing now (tests).</summary>
        public int PopupsShown { get { int n = 0; foreach (var p in pops) if (p.Text != null && p.Text.gameObject.activeSelf) n++; return n; } }

        void PlayCatch(AudioClip c, float pitch)
        {
            if (c == null || Audio == null) return;
            if (catchAudio == null)
            {
                // Its own source, so a raised pitch never bends the other sounds playing.
                catchAudio = gameObject.AddComponent<AudioSource>();
                catchAudio.playOnAwake = false;
                catchAudio.spatialBlend = Audio.spatialBlend;
                catchAudio.outputAudioMixerGroup = Audio.outputAudioMixerGroup;
                catchAudio.volume = Audio.volume;
            }
            catchAudio.pitch = pitch;
            catchAudio.PlayOneShot(c);
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
            if (Frenzy) hud = "Frenzy! Points ×2\n" + hud;
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
            int at = NextLevelAt(total);
            return "Level " + Level(total) + ". Level " + (Level(total) + 1) + " at " + at + " caught: " + (at - total) + " to go";
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

        [Tooltip("Meta's Fast Motion Mode (high-frequency hand tracking) while a round is in play, for fast slaps. Off in " +
                 "menus and the Depth Check, whose fingertip touches want the steadier default tracking.")]
        public bool FastHandsInRounds = true;
        bool fastHands, fastHandsUsed;

        /// <summary>True while this round has asked for Fast Motion Mode (the system treats it as a hint).</summary>
        public bool FastHands => fastHands;

        void SetFastHands(bool on)
        {
            on &= FastHandsInRounds;
            if (on == fastHands) return;
            fastHands = on;
            if (on) fastHandsUsed = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var r = OVRPlugin.RequestFastMotionMode(on);
                Debug.Log("[Cyclopea] fast hands " + (on ? "on" : "off") + " (" + r + ")");
            }
            catch (Exception e) { Debug.LogWarning("[Cyclopea] fast hands: " + e.Message); }
#endif
        }

        void OnDisable() => SetFastHands(false);

        float lastRedraw = -10f;

        /// <summary>Redraws the game made by itself this round, and the shortest gap between two of them (tests: ≥ MinRedrawGapSeconds).</summary>
        public int GameRedraws { get; private set; }
        public float ShortestGameRedrawGap { get; private set; } = float.PositiveInfinity;

        /// <summary>New dots in all nine tiles at once: one eye sees the whole grid change, never one tile.</summary>
        void RefreshAll(bool byGame = false)
        {
            float now = Time.time;
            if (byGame)
            {
                if (GameRedraws > 0) ShortestGameRedrawGap = Mathf.Min(ShortestGameRedrawGap, now - lastGameRedraw);
                GameRedraws++;
                lastGameRedraw = now;
            }
            lastRedraw = now;
            foreach (var t in Tiles) if (t != null) t.Seed = (float)(rng.NextDouble() * 1000.0);
        }

        float lastGameRedraw = -10f;

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
