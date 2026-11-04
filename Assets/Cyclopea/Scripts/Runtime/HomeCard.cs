using System;
using System.Text;
using Cyclopea.Core;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// The daily Depth Check's home: what to work toward (streak, best score, next creature), with the four
    /// squares of static as its buttons. Because the buttons are the squares, every way of choosing a square
    /// works here too: pinch, point, look and hold, thumb tap, voice. Look and hold is always on while a menu
    /// shows, so a player who cannot pinch can still reach Options and switch to it for play.
    ///   Home     Play | the other cue (one-eye mode / two-eye mode) | Field guide | Options
    ///   Options  Contrast | Choose by | Less movement | Back
    ///   Guide    which species and rarities have been found; any square goes back
    /// </summary>
    public sealed class HomeCard : MonoBehaviour
    {
        public enum Page { Home, Options, FieldGuide }

        public SpotSession Session;
        public ScoreAnchor Scores;
        public DevControls Dev;
        [Tooltip("How faint the static gets behind a label.")]
        public float StaticBrightness = 0.22f;
        public float LabelCharSize = 0.0016f;   // about the title's letter size: ~8 mm at ~60 cm
        public int LabelFontSize = 48;
        [Tooltip("Picks closer together than this are one gesture seen by two inputs (say pinch and look-and-hold).")]
        public float DebounceSeconds = 0.35f;

        public bool Showing { get; private set; }
        public Page Current { get; private set; }
        public TextMesh[] Labels { get; private set; }

        float lastPick = -10f;

        void EnsureLabels()
        {
            if (Labels != null) return;
            int n = Session.Patches.Length;
            Labels = new TextMesh[n];
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("MenuLabel" + (i + 1));
                go.transform.SetParent(transform, false);
                var tm = go.AddComponent<TextMesh>();
                tm.font = font;
                tm.fontSize = LabelFontSize;
                tm.characterSize = LabelCharSize;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.color = Color.white;
                go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                go.SetActive(false);
                Labels[i] = tm;
            }
        }

        public void Show(Page page)
        {
            EnsureLabels();
            Current = page;
            Showing = true;
            for (int i = 0; i < Labels.Length; i++)
            {
                Labels[i].text = Label(page, i);
                Labels[i].fontStyle = Options.HighContrast ? FontStyle.Bold : FontStyle.Normal;
                Labels[i].gameObject.SetActive(Labels[i].text.Length > 0);
            }
            foreach (var p in Session.Patches) if (p != null) p.Brightness = StaticBrightness;
            Session.SetTitle(TitleText(page));
            LateUpdate();
        }

        public void Hide()
        {
            if (!Showing) return;
            Showing = false;
            if (Labels == null) return;
            foreach (var l in Labels) if (l != null) l.gameObject.SetActive(false);
            foreach (var p in Session.Patches) if (p != null) p.Brightness = 1f;
        }

        /// <summary>A square was chosen while the card shows.</summary>
        public void Pick(int index, string method)
        {
            if (Time.unscaledTime - lastPick < DebounceSeconds) return;
            lastPick = Time.unscaledTime;
            switch (Current)
            {
                case Page.Home:
                    if (index == 0) { Hide(); Session.StartCue(Session.PreferredCue); }
                    else if (index == 1) { Hide(); Session.StartCue(Other(Session.PreferredCue)); }
                    else if (index == 2) Show(Page.FieldGuide);
                    else Show(Page.Options);
                    break;
                case Page.Options:
                    if (index == 0) Options.HighContrast = !Options.HighContrast;
                    else if (index == 1) Options.LookAndHold = !Options.LookAndHold;
                    else if (index == 2) Options.LessMovement = !Options.LessMovement;
                    else { Show(Page.Home); break; }
                    Options.Save();
                    Show(Page.Options);
                    Session.Toast(Said(index), 3f);
                    break;
                default:
                    Show(Page.Home);
                    break;
            }
        }

        static SessionLog.Cue Other(SessionLog.Cue c) => c == SessionLog.Cue.Stereo ? SessionLog.Cue.Motion : SessionLog.Cue.Stereo;

        string Label(Page page, int i)
        {
            switch (page)
            {
                case Page.Home:
                    var cue = Session.PreferredCue;
                    return i == 0 ? "Play" + (cue == SessionLog.Cue.Motion ? "\none-eye mode" : "")
                         : i == 1 ? (cue == SessionLog.Cue.Stereo ? "One-eye\nmode" : "Two-eye\nmode")
                         : i == 2 ? "Field guide\n" + Kinds() + " of " + Creatures.Kinds
                         : "Options";
                case Page.Options:
                    return i == 0 ? "Contrast\n" + (Options.HighContrast ? "High" : "Standard")
                         : i == 1 ? "Choose by\n" + (Options.LookAndHold ? "Look + hold" : "Pinch")
                         : i == 2 ? "Less\nmovement\n" + (Options.LessMovement ? "On" : "Off")
                         : "Back";
                default:
                    return i == Labels.Length - 1 ? "Back" : "";
            }
        }

        static string Said(int option) =>
            option == 0 ? (Options.HighContrast ? "High contrast: black and white static, darker room." : "Standard contrast.")
          : option == 1 ? (Options.LookAndHold ? "Look at a square and hold your gaze to choose it." : "Pinch to choose. Look and hold still works in menus.")
          : Options.LessMovement ? "Less movement: no reaching, and one-eye mode sways for you." : "Less movement off.";

        int Kinds() => Session.Creatures != null ? Session.Creatures.Collection.KindsFound : 0;

        public string TitleText(Page page)
        {
            switch (page)
            {
                case Page.Options:
                    return "Options\nChoose a square to switch it.";
                case Page.FieldGuide:
                    return FieldGuide(Session.Creatures != null ? Session.Creatures.Collection : new CreatureCollection());
                default:
                    var sb = new StringBuilder("Daily Depth Check");
                    var cue = Session.PreferredCue;
                    void Line(string s) { if (!string.IsNullOrEmpty(s)) sb.Append('\n').Append(s); }
                    if (Scores != null) Line(Goals.StreakLine(Scores.StreakState, DateTime.Now));
                    Line(Goals.BestLine(Session.Progress, cue));
                    Line(Goals.Next(Session.Creatures != null ? Session.Creatures.Collection : null));
                    return sb.ToString();
            }
        }

        /// <summary>One row per species, one dot per rarity (filled once found), Common to Elusive.</summary>
        public static string FieldGuide(CreatureCollection c)
        {
            var sb = new StringBuilder("Field guide: " + c.KindsFound + " of " + Creatures.Kinds);
            for (int s = 0; s < Creatures.SpeciesCount; s++)
            {
                sb.Append('\n').Append((Creatures.Species)s).Append("  ");
                for (int r = 0; r < Creatures.RarityCount; r++)
                    sb.Append(c.Count((Creatures.Species)s, (Creatures.Rarity)r) > 0 ? " ●" : " ○");
            }
            sb.Append("\nCommon → Elusive: found at finer depth");
            return sb.ToString();
        }

        void LateUpdate()
        {
            if (!Showing || Session == null || Session.Head == null) return;
            for (int i = 0; i < Labels.Length && i < Session.Patches.Length; i++)
            {
                var p = Session.Patches[i];
                if (p == null || !Labels[i].gameObject.activeSelf) continue;
                Vector3 pos = p.transform.position - p.transform.forward * 0.012f;
                Labels[i].transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - Session.Head.position, Vector3.up));
            }
        }
    }
}
