using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Meta.XR.MRUtilityKit;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Cyclopea.PlayTests
{
    /// <summary>
    /// "Holding up in rooms the developer never tested": loads every room that ships with MR Utility
    /// Kit (50 furnished prefab rooms + 8 scanned-mesh rooms), seats a player at every table and at
    /// sampled spots elsewhere, and checks each placement the SurfaceFinder makes:
    ///   - the whole 2x2 grid lies on the chosen surface, nothing scanned sits on it,
    ///     nothing blocks the line of sight, and it is within reach;
    ///   - the grid and its title fit in a narrow field of view (Meta VR Glasses design area);
    ///   - when no surface qualifies, the virtual easel is never inside furniture or behind a wall.
    /// Writes Tools/room-sweep.md.
    /// </summary>
    public class RoomSweepTests
    {
        const string Scene = "Assets/Cyclopea/Scenes/Cyclopea.unity";
        const float SeatedEyeAboveTable = 0.45f;
        const float Reach = 0.62f, GridHalf = 0.14f;
        // Critter Whack's nine tiles (WhackRound: pitch 0.10, size 0.085) and the Menu button beside them.
        const float TileHalf = 0.10f + 0.085f * 0.5f, MenuSize = 0.04f;
        const float FovHalfLimitDeg = 27f;   // Meta VR Glasses design area is 58 x 54 deg: half-height 27
        const float TitleOffset = (0.12f + 0.035f) + 0.05f;   // SpotSession.Place: step * 2 + 0.05

        class Stats
        {
            public int Seats, Table, Wall, Easel, Bad, TableSeats, TableSeatsOnTable, Turn;
            public readonly Dictionary<string, int> TableMisses = new Dictionary<string, int>();
            public float WorstGridDeg, WorstTitleDeg, WorstMenuDeg;
            public readonly List<string> Problems = new List<string>();
        }

        [UnitySetUp]
        public IEnumerator Load()
        {
            LogAssert.ignoreFailingMessages = true;
            PlayerPrefs.SetInt(SpotSession.NoticeKey, 1);   // these tests start past the first-launch notice
            PlayerPrefs.SetInt(SpotSession.TutorialKey, 1); // ...and past the tutorial creature
            PlayerPrefs.DeleteKey(CreatureDirector.CollectionKey);
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                Scene, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return null;
#endif
            for (int i = 0; i < 5; i++) yield return null;
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator EveryShippedRoomGetsAValidPlacement()
        {
#if UNITY_EDITOR
            Assert.NotNull(MRUK.Instance, "MRUK in scene");
            var rooms = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Packages/com.meta.xr.mrutilitykit/Core/Rooms/Prefabs" })
                .Select(UnityEditor.AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToList();
            var jsons = UnityEditor.AssetDatabase.FindAssets("t:TextAsset", new[] { "Packages/com.meta.xr.mrutilitykit/Core/Rooms/Json" })
                .Select(UnityEditor.AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".json")).OrderBy(p => p).ToList();
            Assert.That(rooms.Count + jsons.Count, Is.GreaterThanOrEqualTo(50), "MRUK sample rooms present");

            var total = new Stats();
            var perRoom = new StringBuilder();
            int roomCount = 0, acousticRooms = 0;
            float clutterSum = 0f;
            foreach (var path in rooms.Concat(jsons))
            {
                System.Threading.Tasks.Task<MRUK.LoadDeviceResult> task;
                if (path.EndsWith(".json"))
                    task = MRUK.Instance.LoadSceneFromJsonString(File.ReadAllText(path));
                else
                    task = MRUK.Instance.LoadSceneFromPrefab(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path));
                float until = Time.realtimeSinceStartup + 20f;
                while (!task.IsCompleted && Time.realtimeSinceStartup < until) yield return null;
                yield return null;

                foreach (var room in MRUK.Instance.Rooms.ToList())
                {
                    roomCount++;
                    var st = new Stats();
                    Sweep(room, Path.GetFileNameWithoutExtension(path), st);
                    var fit = RoomAcoustics.FitRoom(room);
                    Assert.That(fit.Size.x >= 1.5f && fit.Size.y >= 2f && fit.Size.z >= 1.5f && fit.Clutter >= 0f && fit.Clutter <= 1f,
                                "acoustic room fits " + path);
                    acousticRooms++; clutterSum += fit.Clutter;
                    Merge(total, st);
                    perRoom.AppendLine($"| {Path.GetFileNameWithoutExtension(path)} | {st.Seats} | {st.Table} | {st.Wall} | {st.Easel} | " +
                                       $"{(st.TableSeats > 0 ? $"{st.TableSeatsOnTable}/{st.TableSeats}" : "-")} | {st.Bad} |");
                }
            }

            var md = new StringBuilder();
            md.AppendLine("# Room sweep (MR Utility Kit sample rooms)");
            md.AppendLine();
            md.AppendLine($"Generated by `RoomSweepTests`. {roomCount} rooms from {rooms.Count} furnished prefabs and {jsons.Count} scanned-mesh JSON scenes.");
            md.AppendLine("Seats: one at every side of every table (eye 45 cm above the top, 45 cm back, facing it), plus 12 seeded spots per room facing random directions, eye at 1.2 m.");
            md.AppendLine();
            md.AppendLine("| Measure | Result |");
            md.AppendLine("|---|---|");
            md.AppendLine($"| Seats tested | {total.Seats} |");
            md.AppendLine($"| Placed on a scanned table / wall / virtual easel | {total.Table} / {total.Wall} / {total.Easel} |");
            md.AppendLine($"| Seats at a table that got that table (or another clear one) | {total.TableSeatsOnTable} of {total.TableSeats} ({Pct(total.TableSeatsOnTable, total.TableSeats)}) |");
            md.AppendLine($"| Seats facing an obstacle within 45 cm in every direction tried (game asks the player to turn) | {total.Turn} |");
            md.AppendLine($"| Invalid placements (off the surface, cluttered, blocked, too near or far, easel inside furniture) | {total.Bad} |");
            md.AppendLine($"| Why a table seat didn't get a table | {string.Join(", ", total.TableMisses.OrderByDescending(k => k.Value).Select(k => k.Key + " " + k.Value))} |");
            md.AppendLine($"| Widest angle from gaze centre to a grid corner | {total.WorstGridDeg:0.0}° (limit {FovHalfLimitDeg}°) |");
            md.AppendLine($"| Widest angle from gaze centre to the title | {total.WorstTitleDeg:0.0}° (limit {FovHalfLimitDeg}°) |");
            md.AppendLine($"| Widest angle from gaze centre to the Menu button | {total.WorstMenuDeg:0.0}° (limit {FovHalfLimitDeg}°) |");
            md.AppendLine($"| Meta XR Audio room model sized from the scan | {acousticRooms} rooms, mean clutter {(acousticRooms > 0 ? clutterSum / acousticRooms : 0):0.00} |");
            md.AppendLine();
            md.AppendLine("| Room | Seats | Table | Wall | Easel | Table seats on a table | Invalid |");
            md.AppendLine("|---|---|---|---|---|---|---|");
            md.Append(perRoom);
            if (total.Problems.Count > 0)
            {
                md.AppendLine();
                md.AppendLine("## Problems");
                foreach (var p in total.Problems.Take(60)) md.AppendLine("- " + p);
            }
            Directory.CreateDirectory("Tools");
            File.WriteAllText("Tools/room-sweep.md", md.ToString());
            Debug.Log("[Cyclopea room sweep] " + md.ToString().Split('\n').Take(14).Aggregate((a, b) => a + "\n" + b));

            Assert.That(total.Bad, Is.EqualTo(0), "invalid placements: " + string.Join("; ", total.Problems.Take(10)));
            Assert.That(total.WorstGridDeg, Is.LessThanOrEqualTo(FovHalfLimitDeg));
            Assert.That(total.WorstTitleDeg, Is.LessThanOrEqualTo(FovHalfLimitDeg));
            Assert.That(total.WorstMenuDeg, Is.LessThanOrEqualTo(FovHalfLimitDeg), "the Menu button stays inside the design area");
#else
            yield return null;
#endif
        }

        static string Pct(int a, int b) => b == 0 ? "-" : (100.0 * a / b).ToString("0") + " %";

        static void Merge(Stats t, Stats s)
        {
            t.Seats += s.Seats; t.Table += s.Table; t.Wall += s.Wall; t.Easel += s.Easel; t.Bad += s.Bad;
            t.TableSeats += s.TableSeats; t.TableSeatsOnTable += s.TableSeatsOnTable;
            t.WorstGridDeg = Mathf.Max(t.WorstGridDeg, s.WorstGridDeg);
            t.WorstTitleDeg = Mathf.Max(t.WorstTitleDeg, s.WorstTitleDeg);
            t.WorstMenuDeg = Mathf.Max(t.WorstMenuDeg, s.WorstMenuDeg);
            t.Problems.AddRange(s.Problems);
            t.Turn += s.Turn;
            foreach (var kv in s.TableMisses) t.TableMisses[kv.Key] = (t.TableMisses.TryGetValue(kv.Key, out int n) ? n : 0) + kv.Value;
        }

        static void Sweep(MRUKRoom room, string name, Stats st)
        {
            var seats = new List<(Vector3 head, Vector3 fwd, bool atTable)>();
            foreach (var a in room.Anchors)
            {
                if (a == null || !a.PlaneRect.HasValue || !a.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE)) continue;
                Rect r = a.PlaneRect.Value;
                Vector3 top = a.transform.position;
                // Midpoints of the four edges in the plane's local frame.
                var edges = new[] { new Vector2(r.center.x, r.yMin), new Vector2(r.center.x, r.yMax),
                                    new Vector2(r.xMin, r.center.y), new Vector2(r.xMax, r.center.y) };
                foreach (var e in edges)
                {
                    Vector3 edge = a.transform.TransformPoint(new Vector3(e.x, e.y, 0));
                    Vector3 outward = Vector3.ProjectOnPlane(edge - a.transform.TransformPoint(new Vector3(r.center.x, r.center.y, 0)), Vector3.up);
                    if (outward.sqrMagnitude < 1e-4f) continue;
                    outward.Normalize();
                    Vector3 head = edge + outward * 0.45f;
                    head.y = top.y + SeatedEyeAboveTable;
                    if (!room.IsPositionInRoom(head) || room.IsPositionInSceneVolume(head, 0.05f)) continue;
                    seats.Add((head, -outward, true));
                }
            }
            var rng = new System.Random(name.GetHashCode());
            Bounds b = room.GetRoomBounds();
            for (int i = 0, tries = 0; i < 12 && tries < 400; tries++)
            {
                var head = new Vector3(Mathf.Lerp(b.min.x, b.max.x, (float)rng.NextDouble()), b.min.y + 1.2f,
                                       Mathf.Lerp(b.min.z, b.max.z, (float)rng.NextDouble()));
                if (!room.IsPositionInRoom(head) || room.IsPositionInSceneVolume(head, 0.1f)) continue;
                float yaw = (float)rng.NextDouble() * 360f;
                seats.Add((head, Quaternion.Euler(0, yaw, 0) * Vector3.forward, false));
                i++;
            }

            foreach (var (head, fwd, atTable) in seats)
            {
                st.Seats++;
                if (atTable) st.TableSeats++;
                SurfaceFinder.Surface s;
                if (SurfaceFinder.ChooseInRoom(room, head, fwd, Reach, GridHalf, out s, out string why, out MRUKAnchor chosen))
                {
                    if (s.Kind == SurfaceFinder.Kind.Table) { st.Table++; if (atTable) st.TableSeatsOnTable++; }
                    else st.Wall++;
                    if (atTable && s.Kind != SurfaceFinder.Kind.Table) Miss(st, "chose a wall");
                    Check(room, name, head, s, st, true, chosen);
                }
                else
                {
                    if (atTable) Miss(st, why);
                    s = SurfaceFinder.Easel(head, fwd, room, GridHalf, out bool clear);
                    if (!clear) { st.Turn++; continue; }
                    st.Easel++;
                    Check(room, name, head, s, st, false, null);
                }
            }
        }

        static void Miss(Stats st, string why) => st.TableMisses[why] = (st.TableMisses.TryGetValue(why, out int n) ? n : 0) + 1;

        static void Check(MRUKRoom room, string name, Vector3 head, SurfaceFinder.Surface s, Stats st, bool onSurface, MRUKAnchor chosen)
        {
            string where = $"{name} @({head.x:0.00},{head.y:0.00},{head.z:0.00}) {s.Kind}";
            float d = Vector3.Distance(head, s.Center);
            bool bad = false;
            if (float.IsNaN(d) || d < SurfaceFinder.MinDistance - 0.01f || d > Reach + 0.26f) { st.Problems.Add(where + $": distance {d:0.00} m"); bad = true; }

            // Line of sight to the centre and corners (the chosen surface itself may be hit only at the point).
            foreach (var p in SurfaceFinder.GridCorners(s, GridHalf).Append(s.Center))
            {
                Vector3 q = p + s.Normal * 0.01f;
                Vector3 v = q - head;
                if (room.Raycast(new Ray(head, v.normalized), v.magnitude - 0.02f, out RaycastHit hit, out MRUKAnchor anchor)
                    && !(onSurface && anchor != null && (anchor == chosen || Vector3.Distance(hit.point, q) < 0.03f)))
                {
                    st.Problems.Add(where + $": blocked by {(anchor != null ? anchor.name : "?")}");
                    bad = true;
                    break;
                }
            }
            if (!onSurface && room.IsPositionInSceneVolume(s.Center, 0.02f)) { st.Problems.Add(where + ": easel inside furniture"); bad = true; }

            // Field of view: angular radius of the layout when looking at the grid centre.
            Vector3 gaze = (s.Center - head).normalized;
            float grid = SurfaceFinder.GridCorners(s, GridHalf).Max(c => Vector3.Angle(gaze, c - head));
            Vector3 title = s.Center + s.Away * TitleOffset;
            float tdeg = Vector3.Angle(gaze, title - head);
            float mdeg = SpotSession.MenuCorners(s, TileHalf, MenuSize).Max(c => Vector3.Angle(gaze, c - head));
            st.WorstGridDeg = Mathf.Max(st.WorstGridDeg, grid);
            st.WorstTitleDeg = Mathf.Max(st.WorstTitleDeg, tdeg);
            st.WorstMenuDeg = Mathf.Max(st.WorstMenuDeg, mdeg);
            if (bad) st.Bad++;
        }
    }
}
