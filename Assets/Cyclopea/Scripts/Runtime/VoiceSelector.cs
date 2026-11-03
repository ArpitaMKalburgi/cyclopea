using Cyclopea.Core;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// Say the square instead of pinching it: "top left", "bottom right", "two", "play again".
    /// Feed it transcripts from any speech-to-text source. The shipping source is Meta's Voice SDK
    /// dictation (Wit.ai client token in the Voice SDK config, see TOOLS.md); until that token
    /// exists this component simply receives nothing and the game is unchanged.
    /// </summary>
    public sealed class VoiceSelector : PatchSelector
    {
        public StereoPatch[] Patches;
        public SpotSession Session;

        public string LastHeard { get; private set; } = "";

        /// <summary>Hook this to the speech source's full-transcription event.</summary>
        public void OnTranscript(string text)
        {
            LastHeard = text ?? "";
            if (!isActiveAndEnabled) return;
            var c = VoiceCommands.Parse(text);
            if (c.Again && Session != null && Session.IsDone) { Session.Restart(); return; }
            if (c.Square >= 0 && Patches != null && c.Square < Patches.Length) Raise(Patches[c.Square], "voice");
        }
    }
}
