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
    /// Creatures that move into your room: new kinds hop off the nest at the score card, live on a real
    /// surface (or beside the squares without a scan), and are back in the same place next launch.
    /// The sweep seats a player at every table of every MR Utility Kit sample room and fills the room
    /// with all 16 kinds, checking every home. Writes Tools/colony-sweep.md.
    /// </summary>
    public class ColonyPlayTests
    {
        const string Scene = "Assets/Cyclopea/Scenes/Cyclopea.unity";

        IEnumerator LoadScene()
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                Scene, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return null;
#endif
            for (int i = 0; i < 5; i++) yield return null;
        }

        [UnitySetUp]
        public IEnumerator Load()
        {
            LogAssert.ignoreFailingMessages = true;
            PlayerPrefs.SetInt(SpotSession.NoticeKey, 1);
            PlayerPrefs.SetInt(SpotSession.TutorialKey, 1);
            PlayerPrefs.DeleteKey(CreatureDirector.CollectionKey);
            PlayerPrefs.DeleteKey(RoomColony.ColonyKey);
            yield return LoadScene();
        }

        [UnityTearDown]
        public IEnumerator Clean()
        {
            PlayerPrefs.DeleteKey(RoomColony.ColonyKey);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ANewKindMovesInAndIsThereNextLaunch()
        {
            var s = Object.FindFirstObjectByType<SpotSession>();
            var d = Object.FindFirstObjectByType<CreatureDirector>();
            var colony = Object.FindFirstObjectByType<RoomColony>();
            Assert.NotNull(colony, "scene has the colony");
            colony.MoveInDelaySeconds = 0f; colony.MoveInHopSeconds = 0.3f; colony.MoveInStaggerSeconds = 0.05f;
            if (!colony.Spawned) colony.SpawnAll();
            Assert.That(colony.Living, Is.EqualTo(0));

            d.Reveal(s.Patches[0], 1920, 1920, true);
            d.Reveal(s.Patches[1], 100, 100, true);   // a rarer kind
            d.Reveal(s.Patches[2], 1920, 1920, true);   // same kind as the first, maybe: one resident per kind
            int kinds = d.Collection.KindsFound;
            Assert.That(colony.Pending, Is.EqualTo(kinds), "each new kind waits for the score card");

            colony.MoveInNow();
            float t0 = Time.time;
            while ((colony.Pending > 0 || colony.MovingIn > 0) && Time.time - t0 < 5f) yield return null;
            Assert.That(colony.Living, Is.EqualTo(kinds), "they moved in");
            Assert.That(colony.Colony.Count, Is.EqualTo(kinds));
            Assert.That(colony.Colony.Residents.All(r => r.OnDesk), "no room scan in the editor: they live beside the squares");
            var before = Object.FindObjectsByType<CreatureBody>(FindObjectsSortMode.None)
                .Where(b => b.name.StartsWith("Resident")).Select(b => b.transform.position).OrderBy(p => p.x).ToList();
            Assert.That(before.Count, Is.EqualTo(kinds));
            foreach (var p in before)
                foreach (var q in s.Patches)
                {
                    Vector3 local = q.transform.InverseTransformPoint(p);
                    Assert.False(Mathf.Abs(local.x) < 0.5f && Mathf.Abs(local.y) < 0.5f, "a resident sits on a square");
                }

            var home = Object.FindFirstObjectByType<HomeCard>();
            StringAssert.Contains(kinds + " creature", home.TitleText(HomeCard.Page.Home), "the home card says so");

            // Next launch: same residents, same places.
            yield return LoadScene();
            colony = Object.FindFirstObjectByType<RoomColony>();
            t0 = Time.time;
            while (!colony.Spawned && Time.time - t0 < 12f) yield return null;   // waits for the squares, and for a scan that never comes
            Assert.True(colony.Spawned, "residents appear at launch without a room scan");
            yield return null;
            Assert.That(colony.Living, Is.EqualTo(kinds), "still living here the next day");
            var after = Object.FindObjectsByType<CreatureBody>(FindObjectsSortMode.None)
                .Where(b => b.name.StartsWith("Resident")).Select(b => b.transform.position).OrderBy(p => p.x).ToList();
            for (int i = 0; i < kinds; i++)
                Assert.That(Vector3.Distance(before[i], after[i]), Is.LessThan(0.03f), "same spot (they breathe and hop a little)");
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator EveryShippedRoomHasHomesForAllSixteen()
        {
#if UNITY_EDITOR
            Assert.NotNull(MRUK.Instance, "MRUK in scene");
            var rooms = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Packages/com.meta.xr.mrutilitykit/Core/Rooms/Prefabs" })
                .Select(UnityEditor.AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToList();
            var jsons = UnityEditor.AssetDatabase.FindAssets("t:TextAsset", new[] { "Packages/com.meta.xr.mrutilitykit/Core/Rooms/Json" })
                .Select(UnityEditor.AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".json")).OrderBy(p => p).ToList();
            var rules = RoomColony.Rules.Default;
            int roomCount = 0, seats = 0, homesFound = 0, bad = 0, roundTripFails = 0, fullRooms = 0;
            var short_ = new Dictionary<string, int>();
            var problems = new List<string>();
            var perRoom = new StringBuilder();
            foreach (var path in rooms.Concat(jsons))
            {
                var task = path.EndsWith(".json")
                    ? MRUK.Instance.LoadSceneFromJsonString(File.ReadAllText(path))
                    : MRUK.Instance.LoadSceneFromPrefab(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path));
                float until = Time.realtimeSinceStartup + 20f;
                while (!task.IsCompleted && Time.realtimeSinceStartup < until) yield return null;
                yield return null;
                foreach (var room in MRUK.Instance.Rooms.ToList())
                {
                    roomCount++;
                    string name = Path.GetFileNameWithoutExtension(path);
                    int roomSeats = 0, roomHomes = 0;
                    foreach (var (head, fwd) in TableSeats(room))
                    {
                        if (!SurfaceFinder.ChooseInRoom(room, head, fwd, 0.62f, 0.14f, out var play, out _, out _)) continue;
                        seats++; roomSeats++;
                        var rng = new System.Random(seats);
                        var taken = new List<Vector3>();
                        for (int k = 0; k < Core.Creatures.Kinds; k++)
                        {
                            if (!RoomColony.ChooseHome(room, head, play, 0.14f, taken, rng, rules, out var pos, out var anchor, out string why))
                            {
                                short_[why] = (short_.TryGetValue(why, out int n) ? n : 0) + 1;
                                break;   // the rest would go beside the squares
                            }
                            taken.Add(pos);
                            homesFound++; roomHomes++;
                            string where = $"{name} seat {roomSeats} home {k}";
                            // Saved against the anchor and found again in the same place.
                            var key = RoomColony.AnchorKey(anchor);
                            var again = RoomColony.FindAnchor(room, key);
                            Vector3 local = anchor.transform.InverseTransformPoint(pos);
                            if (again == null || Vector3.Distance(again.transform.TransformPoint(local), pos) > 0.001f)
                            { roundTripFails++; problems.Add(where + ": anchor key " + key + " did not resolve"); }
                            // Checks, independent of the chooser's own.
                            float dist = Vector3.Distance(head, pos);
                            bool b = false;
                            if (dist < rules.MinDistanceM - 0.01f || dist > rules.MaxDistanceM + 0.01f) { problems.Add(where + $": distance {dist:0.00}"); b = true; }
                            if (room.IsPositionInSceneVolume(pos + Vector3.up * 0.03f, 0.005f)) { problems.Add(where + ": inside furniture"); b = true; }
                            if (!room.IsPositionInRoom(pos + Vector3.up * 0.03f)) { problems.Add(where + ": outside the room"); b = true; }
                            Vector3 sight = pos + Vector3.up * 0.03f - head;
                            if (room.Raycast(new Ray(head, sight.normalized), sight.magnitude - 0.03f, out RaycastHit hit, out MRUKAnchor blocker)
                                && blocker != anchor) { problems.Add(where + ": hidden behind " + (blocker != null ? blocker.name : "?")); b = true; }
                            if (b) bad++;
                        }
                    }
                    if (roomSeats > 0 && roomHomes == roomSeats * Core.Creatures.Kinds) fullRooms++;
                    perRoom.AppendLine($"| {name} | {roomSeats} | {(roomSeats > 0 ? (roomHomes / (float)roomSeats).ToString("0.0") : "-")} |");
                }
            }

            var md = new StringBuilder();
            md.AppendLine("# Colony sweep (creatures moving into MR Utility Kit sample rooms)");
            md.AppendLine();
            md.AppendLine($"Generated by `ColonyPlayTests`. {roomCount} rooms; a player seated at every table edge where the squares go on a table; each seat fills the room with all {Core.Creatures.Kinds} kinds.");
            md.AppendLine();
            md.AppendLine("| Measure | Result |");
            md.AppendLine("|---|---|");
            md.AppendLine($"| Seats | {seats} |");
            md.AppendLine($"| Homes found on real surfaces | {homesFound} of {seats * Core.Creatures.Kinds} ({(seats > 0 ? 100.0 * homesFound / (seats * Core.Creatures.Kinds) : 0):0} %); the rest live beside the squares |");
            md.AppendLine($"| Rooms where every seat housed all 16 | {fullRooms} |");
            md.AppendLine($"| Invalid homes (inside furniture, outside the room, out of sight, too near or far) | {bad} |");
            md.AppendLine($"| Saved homes that did not resolve to the same place | {roundTripFails} |");
            md.AppendLine($"| Why a seat ran out of spots | {string.Join("; ", short_.OrderByDescending(k => k.Value).Select(k => k.Key + " ×" + k.Value))} |");
            md.AppendLine();
            md.AppendLine("| Room | Seats | Homes per seat |");
            md.AppendLine("|---|---|---|");
            md.Append(perRoom);
            if (problems.Count > 0)
            {
                md.AppendLine();
                md.AppendLine("## Problems");
                foreach (var p in problems.Take(60)) md.AppendLine("- " + p);
            }
            Directory.CreateDirectory("Tools");
            File.WriteAllText("Tools/colony-sweep.md", md.ToString());
            Debug.Log("[Cyclopea colony sweep] " + md.ToString().Split('\n').Take(12).Aggregate((a, b) => a + "\n" + b));
            Assert.That(seats, Is.GreaterThan(50), "enough seats tested");
            Assert.That(bad, Is.EqualTo(0), string.Join("; ", problems.Take(10)));
            Assert.That(roundTripFails, Is.EqualTo(0));
#else
            yield return null;
#endif
        }

        /// <summary>One seat at each edge of each table: eye 45 cm above the top, 45 cm back, facing it (as RoomSweepTests).</summary>
        static IEnumerable<(Vector3 head, Vector3 fwd)> TableSeats(MRUKRoom room)
        {
            foreach (var a in room.Anchors)
            {
                if (a == null || !a.PlaneRect.HasValue || !a.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE)) continue;
                Rect r = a.PlaneRect.Value;
                Vector3 top = a.transform.position;
                var edges = new[] { new Vector2(r.center.x, r.yMin), new Vector2(r.center.x, r.yMax),
                                    new Vector2(r.xMin, r.center.y), new Vector2(r.xMax, r.center.y) };
                foreach (var e in edges)
                {
                    Vector3 edge = a.transform.TransformPoint(new Vector3(e.x, e.y, 0));
                    Vector3 outward = Vector3.ProjectOnPlane(edge - a.transform.TransformPoint(new Vector3(r.center.x, r.center.y, 0)), Vector3.up);
                    if (outward.sqrMagnitude < 1e-4f) continue;
                    outward.Normalize();
                    Vector3 head = edge + outward * 0.45f;
                    head.y = top.y + 0.45f;
                    if (!room.IsPositionInRoom(head) || room.IsPositionInSceneVolume(head, 0.05f)) continue;
                    yield return (head, -outward);
                }
            }
        }
    }
}
