using System;
using Cyclopea.Core;
class P {
  static bool Table(double floor, double tol){
    bool ok=true;
    Console.WriteLine($"\n-- floor {floor}\" --\ntrue50\" | target79\" | mean est\" | meanLogErr | sdLog | trials | atFloor%");
    foreach (double t in new[]{20.0,30,60,120,300,800}) {
      double sumLog=0,sumSq=0,sumTrials=0; int fl=0; const int runs=50; double target=0;
      for (int r=0;r<runs;r++){
        var obs=new SimulatedObserver(t, seed:r+1); target=obs.ArcsecAtProbability(0.794);
        var st=new Staircase(new Staircase.Config{FloorArcsec=floor});
        while(!st.IsFinished) st.Respond(obs.Respond(st.CurrentArcsec));
        double est=st.ThresholdArcsec(); if(double.IsNaN(est)){Console.WriteLine("NaN!");ok=false;continue;}
        double e=Math.Log10(est)-Math.Log10(target); sumLog+=e; sumSq+=e*e; sumTrials+=st.Trials; if(st.AtFloor())fl++;
      }
      double mle=sumLog/runs, sd=Math.Sqrt(Math.Max(0,sumSq/runs-mle*mle)); double meanEst=Math.Pow(10,Math.Log10(target)+mle);
      bool pass=Math.Abs(mle)<tol; ok&=pass;
      Console.WriteLine($"{t,6} | {target,8:F1} | {meanEst,8:F1} | {mle,+8:F3} | {sd,5:F3} | {sumTrials/runs,6:F0} | {100.0*fl/runs,5:F0}  {(pass?"ok":"FAIL")}");
    }
    return ok;
  }
  static int Main(string[] args){ if (args.Length > 0 && args[0] == "bench") return Bench.Run(args);
    bool ok = Table(5, 0.1);            // algorithm unbiased with the floor out of the way
    ok &= Table(15, 0.25);              // Quest 3 floor: near-floor bias is a display limit, bounded
    var sup=new SimulatedObserver(5, seed:7); var s2=new Staircase(new Staircase.Config{FloorArcsec=15});
    while(!s2.IsFinished) s2.Respond(sup.Respond(s2.CurrentArcsec));
    Console.WriteLine($"\nsuper-threshold observer -> est {s2.ThresholdArcsec():F1}\" atFloor={s2.AtFloor()} trials={s2.Trials}"); ok&=s2.AtFloor() && s2.Trials<40;
    ok &= LadderCheck.Run();
    Console.WriteLine(ok?"ALL PASS":"FAILURES"); return ok?0:1; } }
