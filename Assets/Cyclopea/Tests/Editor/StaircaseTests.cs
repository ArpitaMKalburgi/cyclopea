using Cyclopea.Core;
using NUnit.Framework;

namespace Cyclopea.Tests
{
    public class StaircaseTests
    {
        [TestCase(30.0)]
        [TestCase(60.0)]
        [TestCase(120.0)]
        [TestCase(300.0)]
        [TestCase(800.0)]
        public void ConvergesOnObserverThreshold(double trueThreshold)
        {
            // 3-down/1-up tracks the 79.4% point; the observer's cumulative normal puts
            // that point slightly above its 50% "threshold" — allow 0.2 log units (×1.6).
            const double toleranceLog = 0.2;
            const int runs = 20;
            double sumLogErr = 0;

            for (int run = 0; run < runs; run++)
            {
                var observer = new SimulatedObserver(trueThreshold, seed: run + 1);
                var stair = new Staircase(new Staircase.Config { FloorArcsec = 15 });
                while (!stair.IsFinished)
                    stair.Respond(observer.Respond(stair.CurrentArcsec));

                double est = stair.ThresholdArcsec();
                Assert.False(double.IsNaN(est), "Staircase ended without a threshold estimate");
                sumLogErr += DisparityMath.Log10(est) - DisparityMath.Log10(trueThreshold);
            }

            double meanLogErr = sumLogErr / runs;
            Assert.That(System.Math.Abs(meanLogErr), Is.LessThan(toleranceLog),
                $"Mean log error {meanLogErr:F3} for threshold {trueThreshold}\"");
        }

        [Test]
        public void ReportsFloorWhenObserverBeatsDisplay()
        {
            var observer = new SimulatedObserver(5.0, seed: 7); // better than any headset can show
            var stair = new Staircase(new Staircase.Config { FloorArcsec = 15 });
            while (!stair.IsFinished)
                stair.Respond(observer.Respond(stair.CurrentArcsec));
            Assert.True(stair.AtFloor(), "A super-threshold observer must be reported as ≤ floor");
        }

        [Test]
        public void StopsAfterConfiguredReversals()
        {
            var stair = new Staircase(new Staircase.Config { ReversalsToStop = 4 });
            bool correct = true;
            int guard = 0;
            while (!stair.IsFinished && guard++ < 500)
            {
                for (int i = 0; i < 3; i++) stair.Respond(correct);
                correct = !correct;
            }
            Assert.That(stair.Reversals, Is.GreaterThanOrEqualTo(4));
        }

        [Test]
        public void DisparityGeometryMatchesHandCalculation()
        {
            // 15" at 0.6 m, IPD 63 mm → 0.42 mm pop-out, 0.044 mm lateral shift (see IDEA.md).
            Assert.That(DisparityMath.PopOutMeters(15, 0.6) * 1000, Is.EqualTo(0.415).Within(0.01));
            Assert.That(DisparityMath.ShiftMeters(15, 0.6) * 1000, Is.EqualTo(0.0436).Within(0.001));
            Assert.That(DisparityMath.PixelArcsec(25), Is.EqualTo(144).Within(0.01));
        }
    }
}
