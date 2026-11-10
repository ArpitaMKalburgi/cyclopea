using System;
using Cyclopea.Core;
static class Bench {
  public static int Run(string[] a) {
    int n = a.Length > 1 ? int.Parse(a[1]) : 1000;
    var t = DateTime.Now;
    var rr = MeasurementBenchmark.Run(15, 20261110, MeasurementBenchmark.DefaultGroups(n));
    Console.WriteLine(rr.Markdown());
    Console.WriteLine($"({(DateTime.Now - t).TotalSeconds:F1} s)");
    return 0;
  }
}
