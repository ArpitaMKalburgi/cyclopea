using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// Meta XR Audio's room model, sized from the player's own scanned room (MR Utility Kit): the
    /// creature's chirp reverberates like the real room it hid in. Furniture in the scan makes the
    /// room "cluttered", which diffuses the reflections the way real furniture does. Without a scan
    /// the room model follows the listener at a typical small-room size.
    /// </summary>
    public sealed class RoomAcoustics : MonoBehaviour
    {
        public MetaXRAudioRoomAcousticProperties Room;
        public string Status { get; private set; } = "default room";

        static readonly MRUKAnchor.SceneLabels Furniture =
            MRUKAnchor.SceneLabels.TABLE | MRUKAnchor.SceneLabels.COUCH | MRUKAnchor.SceneLabels.BED |
            MRUKAnchor.SceneLabels.STORAGE | MRUKAnchor.SceneLabels.SCREEN | MRUKAnchor.SceneLabels.LAMP |
            MRUKAnchor.SceneLabels.PLANT | MRUKAnchor.SceneLabels.OTHER;

        public struct Fit
        {
            public Vector3 Center, Size;
            public float Clutter;
            public int FurnitureCount;
        }

        void Start()
        {
            ApplyDefault();
            if (MRUK.Instance != null) MRUK.Instance.RegisterSceneLoadedCallback(OnSceneLoaded);
        }

        void ApplyDefault()
        {
            if (Room == null) return;
            Room.lockPositionToListener = true;
            Room.width = 4f; Room.height = 2.6f; Room.depth = 4f;
            Room.clutterFactor = 0.4f;
        }

        void OnSceneLoaded()
        {
            var room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
            if (room == null || Room == null) return;
            var f = FitRoom(room);
            Room.lockPositionToListener = false;
            Room.transform.position = f.Center;
            Room.width = f.Size.x; Room.height = f.Size.y; Room.depth = f.Size.z;
            Room.clutterFactor = f.Clutter;
            Status = $"scanned room {f.Size.x:0.0} x {f.Size.y:0.0} x {f.Size.z:0.0} m, {f.FurnitureCount} furniture, clutter {f.Clutter:0.00}";
        }

        /// <summary>Shoebox that matches a scanned room. Pure function of the room; the room sweep test checks it on every sample room.</summary>
        public static Fit FitRoom(MRUKRoom room)
        {
            Bounds b = room.GetRoomBounds();
            int n = 0;
            foreach (var a in room.Anchors)
                if (a != null && a.VolumeBounds.HasValue && a.HasAnyLabel(Furniture)) n++;
            float floorArea = Mathf.Max(1f, b.size.x * b.size.z);
            return new Fit
            {
                Center = b.center,
                Size = new Vector3(Mathf.Clamp(b.size.x, 1.5f, 30f), Mathf.Clamp(b.size.y, 2f, 8f), Mathf.Clamp(b.size.z, 1.5f, 30f)),
                Clutter = Mathf.Clamp01(n / (floorArea * 0.6f)),
                FurnitureCount = n
            };
        }
    }
}
