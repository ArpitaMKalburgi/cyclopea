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

        public AudioClip Chirp(int species) =>
            CreatureChirps != null && CreatureChirps.Length > 0 ? CreatureChirps[Mathf.Abs(species) % CreatureChirps.Length] : null;
    }
}
