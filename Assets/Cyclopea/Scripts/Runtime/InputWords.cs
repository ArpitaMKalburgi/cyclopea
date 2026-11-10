namespace Cyclopea
{
    /// <summary>
    /// The game's words are written for hands ("Pinch it."). With a controller in hand they must say what
    /// that player actually does, so every line on the title and status text is adapted as it is shown.
    /// </summary>
    public static class InputWords
    {
        public static string ForControllers(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf("inch", System.StringComparison.Ordinal) < 0) return s;
            return s.Replace("Point at it and pinch.", "Point at it and pull the trigger.")
                    .Replace("Pinch it.", "Point at it and pull the trigger.")
                    .Replace("and pinch", "and pull the trigger")
                    .Replace("Pinch any", "Pull the trigger on any")
                    .Replace("pinch any", "pull the trigger on any")
                    .Replace("Pinch the", "Pull the trigger on the")
                    .Replace("pinch the", "pull the trigger on the")
                    .Replace("Can't pinch?", "Can't use the trigger?")
                    .Replace("Pinch", "Pull the trigger")
                    .Replace("pinch", "pull the trigger");
        }
    }
}
