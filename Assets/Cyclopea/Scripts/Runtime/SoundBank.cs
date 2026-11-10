using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// Every game sound in one place: Assets/Cyclopea/Audio, synthesised by Tools/make_sounds.py, all in
    /// D major pentatonic so sounds that overlap stay musical. An empty slot falls back to a plain tone.
    /// </summary>
    public sealed class SoundBank : MonoBehaviour
    {
        public AudioClip StaticBed, PickRight, PickWrong, CreatureCondense, CreatureHop, CreatureDive, CreaturePeek,
            NewKind, PersonalBest, ScoreReveal, LevelUp, UiHover, UiSelect, BreakChime, EyeClose, EyeOpen,
            CatchHold, CatchSuccess;
        [Tooltip("One voice per creature species.")]
        public AudioClip[] CreatureChirps = new AudioClip[0];

        /// <summary>How many clips are not assigned (each falls back to a plain tone; reported by SystemCheck).</summary>
        public int MissingCount()
        {
            int n = 0;
            foreach (var f in GetType().GetFields())
                if (f.FieldType == typeof(AudioClip) && f.GetValue(this) as AudioClip == null) n++;
            if (CreatureChirps == null || CreatureChirps.Length == 0) n++;
            else foreach (var c in CreatureChirps) if (c == null) n++;
            return n;
        }

        public AudioClip Chirp(int species) =>
            CreatureChirps != null && CreatureChirps.Length > 0 ? CreatureChirps[Mathf.Abs(species) % CreatureChirps.Length] : null;
    }
}
