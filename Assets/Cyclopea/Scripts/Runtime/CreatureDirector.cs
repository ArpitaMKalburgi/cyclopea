using System;
using System.Collections.Generic;
using Cyclopea.Core;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// What happens once a creature is found. It condenses out of the static where it floated, looks at
    /// you, hops onto your real index fingertip (Interaction SDK hand joints) and rides along, then
    /// settles in a small nest beside the squares. A wrong pick makes the real one peek out and duck back.
    /// Also runs the first-launch tutorial creature, and keeps the field guide (which kinds were found).
    /// </summary>
    public sealed class CreatureDirector : MonoBehaviour
    {
        public const string CollectionKey = "cyc_collection_v1";

        public Material Material;
        public Transform Head;
        public FingerTips Tips;
        public StereoPatch[] Patches;
        [Tooltip("Spatialised source (Meta XR Audio); moved to the creature for its hops.")]
        public AudioSource Voice;

        [Header("Timing")]
        public float EmergeSeconds = 0.4f;
        public float HopSeconds = 0.42f;
        public float RideSeconds = 2.2f;
        [Tooltip("Tutorial: how long the creature waits for a finger before it hides anyway.")]
        public float IntroWaitSeconds = 7f;

        [Header("Reach and size")]
        [Tooltip("A tracked fingertip within this distance of a found creature gets climbed onto.")]
        public float ClimbReach = 0.75f;
        [Tooltip("Tutorial: how close the fingertip must come before the creature hops on.")]
        public float IntroReach = 0.3f;
        public int NestSize = 5;
        public float RideScale = 0.6f, NestScale = 0.55f;

        public CreatureCollection Collection { get; private set; } = new CreatureCollection();
        /// <summary>species, rarity, first of its kind.</summary>
        public event Action<Creatures.Species, Creatures.Rarity, bool> Collected;
        public int FoundThisSession { get; private set; }
        public readonly List<string> NewThisSession = new List<string>();

        enum Mode { Emerge, Look, HopToTip, Ride, HopToNest, Rest, Leave, Peek, IntroWait, IntroHopToTip, IntroRide, IntroHopToPatch, IntroDive }

        sealed class Actor
        {
            public CreatureBody Body;
            public Mode Mode;
            public bool Intro;
            public float T0, LostSince = -1f;
            public Vector3 From, LastTip;
            public Quaternion FromRot;
            public float FromScale = 1f;
            public StereoPatch Patch;
            public Action Done;
        }

        readonly List<Actor> actors = new List<Actor>();
        readonly List<Actor> nest = new List<Actor>();
        AudioClip clipHop, clipLand, clipDive;
        bool introSkip;

        // ---------- Test and automation hooks ----------
        public CreatureBody Latest => actors.Count > 0 ? actors[actors.Count - 1].Body : null;
        public bool Riding => actors.Exists(a => a.Mode == Mode.Ride || a.Mode == Mode.IntroRide);
        public bool IntroRunning => actors.Exists(a => a.Intro);
        public int NestCount => nest.Count;
        public int Alive => actors.Count;

        void Awake()
        {
            try { Collection = CreatureCollection.Parse(PlayerPrefs.GetString(CollectionKey, "")); }
            catch (Exception) { Collection = new CreatureCollection(); }
            clipHop = Tones.Chirp(520, 780, 0.06f);
            clipLand = Tones.Chirp(900, 1250, 0.07f);
            clipDive = Tones.Chirp(1200, 420, 0.18f);
        }

        /// <summary>New session: per-session counts restart (the field guide keeps everything).</summary>
        public void BeginSession()
        {
            FoundThisSession = 0;
            NewThisSession.Clear();
        }

        /// <summary>Writes the field guide to disk (end of session, between trials, never mid-hop).</summary>
        public void Persist()
        {
            try { PlayerPrefs.Save(); }
            catch (Exception e) { Debug.LogWarning("[Cyclopea] collection save failed: " + e.Message); }
        }

        /// <summary>Score-card line for this session ("" when nothing was found).</summary>
        public string SessionLine() => CreatureCollection.SessionLine(FoundThisSession, Collection, NewThisSession);

        // ---------- Found / missed ----------

        /// <summary>
        /// A pick on the trial whose creature hid in <paramref name="p"/>. Correct: it comes out, is
        /// collected at the rarity of <paramref name="foundArcsec"/>, and climbs onto a fingertip if one is
        /// near. Wrong: it peeks out and ducks back. It appears exactly where it was seen floating: at
        /// <paramref name="shownArcsec"/> of disparity, or at <paramref name="heldHeightM"/> above the surface
        /// when the square held it at a fixed height (Catch).
        /// </summary>
        public CreatureBody Reveal(StereoPatch p, double foundArcsec, double shownArcsec, bool correct, bool collect = true,
                                   double heldHeightM = 0)
        {
            if (p == null || Material == null) return null;
            var rarity = Creatures.FromArcsec(foundArcsec);
            var a = Spawn(p, rarity, shownArcsec, heldHeightM);
            a.Mode = correct ? Mode.Emerge : Mode.Peek;
            if (correct && collect) Collect(a.Body.Species, rarity);
            return a.Body;
        }

        // ---------- Tutorial ----------

        /// <summary>
        /// First launch: a creature built from <paramref name="hideIn"/>'s outline appears beside the
        /// squares, waits for a finger to climb onto, then hops into that square and sinks into the static.
        /// <paramref name="dived"/> runs once it has vanished (the session then shows it floating there).
        /// </summary>
        public void IntroBegin(StereoPatch hideIn, Action dived)
        {
            if (hideIn == null || Material == null) { dived?.Invoke(); return; }
            introSkip = false;
            var a = Spawn(hideIn, Creatures.Rarity.Common, 0);
            a.Intro = true;
            a.Done = dived;
            a.Mode = Mode.Emerge;
            // In the middle of the four squares, sitting on the static: inside the FoV design area, so a
            // first-timer can't miss it.
            GridCenterPose(out var pos, out var rot);
            a.Body.transform.SetPositionAndRotation(pos, rot);
        }

        /// <summary>A pinch during the tutorial wait: the creature goes and hides now.</summary>
        public void IntroSkip() => introSkip = true;

        // ---------- Internals ----------

        Actor Spawn(StereoPatch p, Creatures.Rarity rarity, double shownArcsec, double heldHeightM = 0)
        {
            var body = CreatureBody.Create(Material, transform, p.MaskRadius * p.transform.lossyScale.x,
                                           p.MaskWobble, p.MaskLobes, p.Seed, rarity);
            body.Look = Head;
            body.Dissolve = 1f;
            double shift = heldHeightM > 0 ? Reach.ShiftForHeight(heldHeightM, p.LastEyeHeight, StereoPatch.Ipd)
                         : shownArcsec > 0 ? DisparityMath.ShiftMeters(shownArcsec, p.LastViewingDistance)
                         : 0;
            SeenPose(p, shift, out var pos, out float scale);
            body.transform.SetPositionAndRotation(pos, SurfaceRot(p));
            body.transform.localScale = Vector3.one * scale;
            var a = new Actor { Body = body, T0 = Time.time, Patch = p };
            actors.Add(a);
            return a;
        }

        /// <summary>
        /// Where the outline was seen floating for a surface shift <paramref name="shiftM"/>: on the sightline
        /// from the head to the outline's centre, z·s/(e+s) out from the surface (see Reach). Scaled by
        /// (z-d)/z, so from the head its footprint covers exactly the outline that floated. Without a head,
        /// straight out of the surface.
        /// </summary>
        void SeenPose(StereoPatch p, double shiftM, out Vector3 pos, out float scale)
        {
            Vector3 m = MaskPoint(p);
            pos = m;
            scale = 1f;
            if (shiftM <= 0) return;
            if (Head == null)
            {
                pos = m - p.transform.forward * Mathf.Min((float)Reach.HeightForShift(shiftM, p.LastEyeHeight, StereoPatch.Ipd), 0.15f);
                return;
            }
            Vector3 toHead = Head.position - m;
            float z = toHead.magnitude;
            if (z < 1e-4f) return;
            float d = Mathf.Min((float)Reach.SightlineDistance(shiftM, z, StereoPatch.Ipd), 0.5f * z);
            pos = m + toHead / z * d;
            scale = (z - d) / z;
        }

        void Collect(Creatures.Species s, Creatures.Rarity r)
        {
            bool isNew = Collection.Add(s, r);
            FoundThisSession++;
            if (isNew) NewThisSession.Add(Creatures.Name(s, r));
            // In memory only: a disk write per catch can drop a frame on Quest. Unity flushes PlayerPrefs
            // on pause and quit, and Persist() runs at the end of every session.
            try { PlayerPrefs.SetString(CollectionKey, Collection.Serialize()); }
            catch (Exception e) { Debug.LogWarning("[Cyclopea] collection save failed: " + e.Message); }
            Collected?.Invoke(s, r, isNew);
        }

        /// <summary>Centre of the hidden outline on the patch (the patch quad spans -0.5..0.5 in local XY).</summary>
        static Vector3 MaskPoint(StereoPatch p) =>
            p.transform.TransformPoint(new Vector3(p.MaskCenter.x - 0.5f, p.MaskCenter.y - 0.5f, 0f));

        /// <summary>Creature local X = patch +u, Z = patch +v, Y = out of the surface toward the viewer.</summary>
        static Quaternion SurfaceRot(StereoPatch p) => Quaternion.LookRotation(p.transform.up, -p.transform.forward);

        /// <summary>Nest slot beside the right-hand column of squares, facing the player.</summary>
        void NestPose(int slot, int count, out Vector3 pos, out Quaternion rot)
        {
            pos = transform.position; rot = Quaternion.identity;
            if (Patches == null || Patches.Length < 4 || Patches[0] == null) return;
            Vector3 p0 = Patches[0].transform.position, p1 = Patches[1].transform.position, p2 = Patches[2].transform.position;
            Vector3 right = (p1 - p0).normalized, away = (p0 - p2).normalized;
            Vector3 normal = -Patches[0].transform.forward;
            float half = Patches[0].transform.lossyScale.x * 0.5f;
            Vector3 column = (p1 + Patches[3].transform.position) * 0.5f + right * (half + 0.06f);
            float spread = 0.045f;
            pos = column + away * ((slot - (Mathf.Max(count, NestSize) - 1) * 0.5f) * -spread) + normal * 0.002f;
            Vector3 toHead = Head != null ? Vector3.ProjectOnPlane(Head.position - pos, normal) : -away;
            rot = Quaternion.LookRotation(toHead.sqrMagnitude > 1e-6f ? toHead : -away, normal);
        }

        void GridCenterPose(out Vector3 pos, out Quaternion rot)
        {
            pos = transform.position; rot = Quaternion.identity;
            if (Patches == null || Patches.Length < 4 || Patches[0] == null) return;
            Vector3 normal = -Patches[0].transform.forward;
            pos = Vector3.zero;
            foreach (var p in Patches) pos += p.transform.position;
            pos = pos / Patches.Length + normal * 0.003f;
            Vector3 toHead = Head != null ? Vector3.ProjectOnPlane(Head.position - pos, normal) : Patches[0].transform.up;
            rot = Quaternion.LookRotation(toHead.sqrMagnitude > 1e-6f ? toHead : Patches[0].transform.up, normal);
        }

        Quaternion RideRot(Vector3 at)
        {
            Vector3 toHead = Head != null ? Vector3.ProjectOnPlane(Head.position - at, Vector3.up) : Vector3.forward;
            return Quaternion.LookRotation(toHead.sqrMagnitude > 1e-6f ? toHead : Vector3.forward, Vector3.up);
        }

        static Vector3 Seat(Pose tip) => tip.position + Vector3.up * 0.006f;

        static float Smooth(float u) { u = Mathf.Clamp01(u); return u * u * (3f - 2f * u); }

        void Begin(Actor a, Mode m)
        {
            a.Mode = m;
            a.T0 = Time.time;
            a.From = a.Body.transform.position;
            a.FromRot = a.Body.transform.rotation;
            a.FromScale = a.Body.transform.localScale.x;
            a.LostSince = -1f;
            if (m == Mode.HopToTip || m == Mode.HopToNest || m == Mode.IntroHopToTip || m == Mode.IntroHopToPatch)
                Say(clipHop, a.Body.transform.position);
        }

        /// <summary>Arc toward a (possibly moving) target; returns progress 0..1.</summary>
        float Hop(Actor a, Vector3 to, Quaternion toRot, float toScale, float seconds, Vector3 up)
        {
            float u = Mathf.Clamp01((Time.time - a.T0) / seconds);
            float e = Smooth(u);
            float height = 0.06f + 0.25f * Vector3.Distance(a.From, to) * 0.3f;
            var t = a.Body.transform;
            t.SetPositionAndRotation(Vector3.Lerp(a.From, to, e) + up * (4f * u * (1f - u) * height),
                                     Quaternion.Slerp(a.FromRot, toRot, e));
            t.localScale = Vector3.one * Mathf.Lerp(a.FromScale, toScale, e);
            a.Body.Squash = u < 0.15f ? -0.35f : u > 0.9f ? 0.3f : 0f;
            return u;
        }

        void Say(AudioClip c, Vector3 at)
        {
            if (Voice == null || c == null) return;
            Voice.transform.position = at;
            Voice.PlayOneShot(c, 0.6f);
        }

        void Update()
        {
            for (int i = actors.Count - 1; i >= 0; i--)
            {
                var a = actors[i];
                if (a.Body == null) { actors.RemoveAt(i); nest.Remove(a); continue; }
                if (Step(a)) continue;
                // Finished: remove.
                actors.RemoveAt(i);
                nest.Remove(a);
                Destroy(a.Body.gameObject);
            }
        }

        /// <summary>Advances one creature; false when it is gone.</summary>
        bool Step(Actor a)
        {
            float t = Time.time - a.T0;
            var tr = a.Body.transform;
            switch (a.Mode)
            {
                case Mode.Emerge:
                    a.Body.Dissolve = 1f - Smooth(t / EmergeSeconds);
                    a.Body.Squash = -0.15f * (1f - Smooth(t / EmergeSeconds));
                    if (t >= EmergeSeconds)
                    {
                        a.Body.Dissolve = 0f;
                        Begin(a, a.Intro ? Mode.IntroWait : Mode.Look);
                    }
                    return true;

                case Mode.Look:
                    a.Body.Squash = 0.2f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.25f));
                    if (t < 0.25f) return true;
                    if (Tips != null && Tips.TryNearest(tr.position, ClimbReach, out var tip))
                    {
                        Begin(a, Mode.HopToTip);
                        a.LastTip = tip.position;
                    }
                    else Begin(a, Mode.HopToNest);
                    return true;

                case Mode.HopToTip:
                case Mode.IntroHopToTip:
                {
                    if (Tips == null || !Tips.TryFollow(a.LastTip, 0.25f, out var tp))
                    {
                        Begin(a, a.Intro ? Mode.IntroHopToPatch : Mode.HopToNest);
                        return true;
                    }
                    a.LastTip = tp.position;
                    if (Hop(a, Seat(tp), RideRot(tp.position), RideScale, HopSeconds, Vector3.up) >= 1f)
                    {
                        Say(clipLand, tr.position);
                        Begin(a, a.Intro ? Mode.IntroRide : Mode.Ride);
                    }
                    return true;
                }

                case Mode.Ride:
                case Mode.IntroRide:
                {
                    float dt = Time.deltaTime;
                    if (Tips != null && Tips.TryFollow(a.LastTip, 0.12f, out var tp))
                    {
                        a.LastTip = tp.position;
                        a.LostSince = -1f;
                        float k = 1f - Mathf.Exp(-25f * dt);
                        tr.SetPositionAndRotation(Vector3.Lerp(tr.position, Seat(tp), k),
                                                  Quaternion.Slerp(tr.rotation, RideRot(tp.position), k));
                    }
                    else if (a.LostSince < 0f) a.LostSince = Time.time;
                    a.Body.Squash = 0.06f * Mathf.Sin(t * 9f);
                    float ride = a.Intro ? RideSeconds * 0.6f : RideSeconds;
                    bool lost = a.LostSince >= 0f && Time.time - a.LostSince > 0.3f;
                    if (t >= ride || lost || (a.Intro && introSkip))
                        Begin(a, a.Intro ? Mode.IntroHopToPatch : Mode.HopToNest);
                    return true;
                }

                case Mode.HopToNest:
                {
                    int slot = nest.Count;
                    NestPose(slot, slot + 1, out var pos, out var rot);
                    if (Hop(a, pos, rot, NestScale, HopSeconds * 1.2f, rot * Vector3.up) >= 1f)
                    {
                        nest.Add(a);
                        Begin(a, Mode.Rest);
                        while (nest.Count > NestSize)
                        {
                            var oldest = nest[0];
                            nest.RemoveAt(0);
                            Begin(oldest, Mode.Leave);
                        }
                    }
                    return true;
                }

                case Mode.Rest:
                {
                    int slot = nest.IndexOf(a);
                    NestPose(slot, nest.Count, out var pos, out var rot);
                    float k = 1f - Mathf.Exp(-8f * Time.deltaTime);
                    tr.SetPositionAndRotation(Vector3.Lerp(tr.position, pos, k), Quaternion.Slerp(tr.rotation, rot, k));
                    a.Body.Squash = 0.03f * Mathf.Sin(t * 2.2f + slot);
                    return true;
                }

                case Mode.Leave:
                    a.Body.Dissolve = Smooth(t / 0.5f);
                    return t < 0.5f;

                case Mode.Peek:
                    // Out to three-quarters, a look, then back into the static.
                    a.Body.Dissolve = t < 0.3f ? 1f - 0.75f * Smooth(t / 0.3f)
                                    : t < 0.8f ? 0.25f
                                    : 0.25f + 0.75f * Smooth((t - 0.8f) / 0.35f);
                    a.Body.Squash = 0.15f * Mathf.Sin(t * 12f) * (t < 0.8f ? 1f : 0f);
                    return t < 1.15f;

                case Mode.IntroWait:
                    a.Body.Squash = 0.05f * Mathf.Sin(t * 3f);
                    if (Tips != null && Tips.TryNearest(tr.position, IntroReach, out var near))
                    {
                        Begin(a, Mode.IntroHopToTip);
                        a.LastTip = near.position;
                    }
                    else if (t >= IntroWaitSeconds || introSkip) Begin(a, Mode.IntroHopToPatch);
                    return true;

                case Mode.IntroHopToPatch:
                {
                    var p = a.Patch;
                    if (Hop(a, MaskPoint(p), SurfaceRot(p), 1f, HopSeconds * 1.4f, -p.transform.forward) >= 1f)
                    {
                        Say(clipDive, tr.position);
                        Begin(a, Mode.IntroDive);
                    }
                    return true;
                }

                case Mode.IntroDive:
                {
                    float u = Smooth(t / 0.45f);
                    a.Body.Dissolve = u;
                    tr.position = a.From + a.Patch.transform.forward * (0.01f * u);
                    if (t < 0.45f) return true;
                    var done = a.Done;
                    a.Done = null;
                    done?.Invoke();
                    return false;
                }
            }
            return true;
        }
    }
}
