using System;
using Cyclopea.Core;
static class LadderCheck {
  public static bool Run() {
    bool ok = true;
    // 1. ladder climbs for a normal observer
    var ladder = new SkillLadder(15); var obs = new SimulatedObserver(60, seed: 3); int g = 0;
    while (!ladder.IsFinished && g++ < 2000) ladder.Respond(obs.Respond(ladder.CurrentArcsec));
    var p = ladder.BuildProfile();
    Console.WriteLine("\nladder (observer 60\"): stereoMeasured=" + p.StereoMeasured + " trials=" + g);
    foreach (var kv in p.ThresholdArcsec)
      Console.WriteLine("  " + SkillLadder.Describe(kv.Key, kv.Value, p.AtFloor[kv.Key], 0.6));
    ok &= p.StereoMeasured && p.ThresholdArcsec.Count == 4;
    // 2. chance-level player -> no stereo number
    var l2 = new SkillLadder(15); var rng = new Random(5); g = 0;
    while (!l2.IsFinished && g++ < 2000) l2.Respond(rng.NextDouble() < 0.25);
    var p2 = l2.BuildProfile();
    Console.WriteLine("ladder (chance player): stereoMeasured=" + p2.StereoMeasured + " trials=" + g);
    ok &= !p2.StereoMeasured;
    // 3. secret code round trip
    string code = SecretCode.Encode("I LOVE YOU", 4, 31337);
    bool dec = SecretCode.TryDecode(code, out var s);
    Console.WriteLine("secret: " + code + " -> " + (dec ? s.Text + " @" + s.DisparityArcsec + "\" seed " + s.Seed : "FAIL"));
    ok &= dec && s.Text == "I LOVE YOU" && s.Seed == 31337;
    char[] c = code.ToCharArray(); c[7] = c[7] == 'A' ? 'B' : 'A';
    ok &= !SecretCode.TryDecode(new string(c), out _);
    Console.WriteLine("secret corruption rejected: " + !SecretCode.TryDecode(new string(c), out _));
    // 4. patches
    var pts = PatchLayout.Place(new PatchLayout.Rect { X = -0.6, Y = 0, W = 1.2, H = 0.8 }, new PatchLayout.Point(0, 0.1), 0.6, 4, 0.12, 0.03, 7);
    Console.WriteLine("patches placed: " + pts.Count);
    ok &= pts.Count == 4;
    return ok;
  }
}
