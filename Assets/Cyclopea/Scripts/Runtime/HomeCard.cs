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
    ///   Home     Play (Critter Whack) | Depth Check | Field guide | Options
    ///   Check    Two eyes | One eye (by moving your head) | – | Back
    ///   Options  Contrast | Choose by | Less movement | Back
    ///   Guide    which species and rarities have been found; any square goes back
    ///   Pause    Resume | Home | New round / Start over / Skip to the game | Options   (the Menu button, mid-game)
    ///   Card     Play again / Check again / One-eye mode | Home | Depth Check / Play | Options   (after a round)
    /// The same corner always does the same thing: top-left goes on, top-right goes home, bottom-right is
    /// Options or Back. Back returns to the page the player came from (home, the menu, or the score card).
    /// </summary>
    public sealed class HomeCard : MonoBehaviour
    {
        public enum Page { Home, Options, FieldGuide, Check, Pause, Card }

        public SpotSession Session;
        public ScoreAnchor Scores;
        public DevControls Dev;
        [Tooltip("The creatures living in the room; the home card says how many.")]
        public RoomColony Room;
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
        Page backTo = Page.Home;   // where Back goes: the last of Home, Pause or Card shown

        /// <summary>The page that Back (on Options, Depth Check or the field guide) returns to.</summary>
        public Page BackTo => backTo;

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
                // Readable from where you sit, and never wider than its square.
                var legible = go.AddComponent<LegibleText>();
                legible.Head = Session.Head;
                legible.TargetDmm = 20f;
                legible.FitInside = Session.Patches[i].transform;
                go.SetActive(false);
                Labels[i] = tm;
            }
        }

        public void Show(Page page)
        {
            EnsureLabels();
            Current = page;
            Showing = true;
            if (page == Page.Home || page == Page.Pause || page == Page.Card) backTo = page;
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
                    if (index == 0) { Hide(); Session.StartArcade(); }
                    else if (index == 1) Show(Page.Check);
                    else if (index == 2) Show(Page.FieldGuide);
                    else Show(Page.Options);
                    break;
                case Page.Check:
                    if (index == 0) { Hide(); Session.StartCue(SessionLog.Cue.Stereo); }
                    else if (index == 1) { Hide(); Session.StartCue(SessionLog.Cue.Motion); }
                    else if (index == 3) Show(backTo);
                    break;
                case Page.Options:
                    if (index == 0) Options.HighContrast = !Options.HighContrast;
                    else if (index == 1) Options.LookAndHold = !Options.LookAndHold;
                    else if (index == 2) Options.LessMovement = !Options.LessMovement;
                    else { Show(backTo); break; }
                    Options.Save();
                    Show(Page.Options);
                    Session.Toast(Said(index), 3f);
                    break;
                case Page.Pause:
                    if (index == 0) Session.Resume();
                    else if (index == 1) Session.MenuHome();
                    else if (index == 2) Session.MenuRestart();
                    else Show(Page.Options);
                    break;
                case Page.Card:
                    if (index == 0) Session.CardAgain();
                    else if (index == 1) Session.ShowHome();
                    else if (index == 2) { if (Session.CardIsArcade) Show(Page.Check); else Session.StartArcade(); }
                    else Show(Page.Options);
                    break;
                default:
                    Show(backTo);
                    break;
            }
        }

        static SessionLog.Cue Other(SessionLog.Cue c) => c == SessionLog.Cue.Stereo ? SessionLog.Cue.Motion : SessionLog.Cue.Stereo;

        string Label(Page page, int i)
        {
            switch (page)
            {
                case Page.Home:
                    return i == 0 ? "Play"
                         : i == 1 ? "Depth\nCheck"
                         : i == 2 ? "Field guide\n" + Kinds() + " of " + Creatures.Kinds
                         : "Options";
                case Page.Check:
                    return i == 0 ? "Two eyes" : i == 1 ? "One eye" : i == 2 ? "" : "Back";
                case Page.Options:
                    return i == 0 ? "Contrast\n" + (Options.HighContrast ? "High" : "Standard")
                         : i == 1 ? "Choose by\n" + (Options.LookAndHold ? "Look + hold" : "Pinch")
                         : i == 2 ? "Less\nmovement\n" + (Options.LessMovement ? "On" : "Off")
                         : "Back";
                case Page.Pause:
                    return i == 0 ? "Resume"
                         : i == 1 ? "Home"
                         : i == 2 ? (Session.MenuFrom == SpotSession.Activity.Tutorial ? "Skip to\nthe game"
                                   : Session.MenuFrom == SpotSession.Activity.Arcade ? "New\nround" : "Start\nover")
                         : "Options";
                case Page.Card:
                    return i == 0 ? (Session.CardIsArcade ? "Play\nagain" : Session.CardOffersMotion ? "One-eye\nmode" : "Check\nagain")
                         : i == 1 ? "Home"
                         : i == 2 ? (Session.CardIsArcade ? "Depth\nCheck" : "Play")
                         : "Options";
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
                    string checks = Health.CheckLine();
                    return "Options\nChoose a square to switch it." + (checks.Length > 0 ? "\n" + checks : "");
                case Page.FieldGuide:
                    return FieldGuide(Session.Creatures != null ? Session.Creatures.Collection : new CreatureCollection());
                case Page.Check:
                    return "Depth Check: how fine a step in depth you see.\nTwo eyes, or one eye by moving your head.\nAbout three minutes.";
                case Page.Pause:
                    return "Paused\n" + (Session.MenuFrom == SpotSession.Activity.Arcade ? "The clock is stopped. The critters wait."
                                       : Session.MenuFrom == SpotSession.Activity.Tutorial ? "The creature waits for you."
                                       : "The Depth Check waits for you.");
                case Page.Card:
                    return Session.CardText;
                default:
                    var sb = new StringBuilder("Cyclopea");
                    var cue = Session.PreferredCue;
                    void Line(string s) { if (!string.IsNullOrEmpty(s)) sb.Append('\n').Append(s); }
                    // One line for the streak and the best round, so the card stays inside the comfortable view.
                    string streak = Scores != null ? Goals.StreakLine(Scores.StreakState, DateTime.Now) : "";
                    string bestRound = WhackRound.Best > 0 ? "Best round " + WhackRound.Best.ToString("N0") : "";
                    Line(streak.Length > 0 && bestRound.Length > 0 ? streak + " · " + bestRound : streak + bestRound);
                    Line(Goals.BestLine(Session.Progress, cue));
                    Line(Goals.Next(Session.Creatures != null ? Session.Creatures.Collection : null));
                    if (Room != null) Line(Colony.HomeLine(Room.Colony));
                    Line(Health.PlayerLine());   // a fallback the player should know about (no scan, no passthrough…)
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
