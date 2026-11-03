using System;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// Head pose as an Interaction SDK gaze source. Quest 3 and 3S have no eye tracking, so ISDK's
    /// GazeInteractor is fed from the head instead; on a headset with eye tracking, ISDK's EyeGaze
    /// drops in here with no other change.
    /// </summary>
    public sealed class HeadGaze : MonoBehaviour, IGaze
    {
        public Transform Head;
        public event Action WhenUpdated = delegate { };
        public bool IsTrackedDataValid => Head != null && isActiveAndEnabled;

        public bool TryGetWorldPose(out Pose pose)
        {
            if (Head == null) { pose = Pose.identity; return false; }
            pose = new Pose(Head.position, Head.rotation);
            return true;
        }

        void LateUpdate() => WhenUpdated();
    }
}
