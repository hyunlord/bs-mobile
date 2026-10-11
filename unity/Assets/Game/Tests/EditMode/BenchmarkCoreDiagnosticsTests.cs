using System;
using System.IO;
using Game.App;
using NUnit.Framework;
using SowSiege.Core;

namespace Game.Tests
{
    public sealed class BenchmarkCoreDiagnosticsTests
    {
        [Test]
        public void NestedStagesKeepOneTickAndUnavailableAllocationSentinels()
        {
            var probe = new BenchmarkCoreDiagnostics(2, false);
            probe.Begin(RuntimeStage.Enemy); probe.End(RuntimeStage.Enemy);
            Assert.That(probe.Count, Is.Zero);
            probe.BeginTick(10800, 123);
            probe.Begin(RuntimeStage.Enemy);
            probe.Begin(RuntimeStage.Spawn); probe.End(RuntimeStage.Spawn);
            probe.Begin(RuntimeStage.Movement); probe.End(RuntimeStage.Movement);
            probe.Begin(RuntimeStage.Separation);
            probe.Begin(RuntimeStage.Collect); probe.End(RuntimeStage.Collect);
            probe.Begin(RuntimeStage.Residual);
            probe.Begin(RuntimeStage.Clear); probe.End(RuntimeStage.Clear);
            probe.End(RuntimeStage.Residual);
            probe.End(RuntimeStage.Separation); probe.End(RuntimeStage.Enemy);
            probe.Begin(RuntimeStage.Work);
            probe.Begin(RuntimeStage.Harvest); probe.End(RuntimeStage.Harvest);
            probe.End(RuntimeStage.Work);
            probe.EndTick();
            probe.RecordSnapshot(10800, 123, .25);
            var writer = new StringWriter(); probe.WriteCsv(writer);
            var lines = writer.ToString().Trim().Split('\n');
            var values = lines[1].Trim().Split(',');
            Assert.That(probe.Count, Is.EqualTo(1)); Assert.That(values.Length, Is.EqualTo(27));
            Assert.That(values[0], Is.EqualTo("123")); Assert.That(values[1], Is.EqualTo("10800"));
            Assert.That(values[3], Is.EqualTo("-1"));
            Assert.That(values[14], Is.EqualTo("0.25"));
            Assert.That(values[18], Is.EqualTo("-1"));
            foreach (var index in new[] { 20, 22, 24, 26 }) Assert.That(values[index], Is.EqualTo("-1"));
            Assert.That(lines[0].Split(',')[25], Is.EqualTo("clearMs"));
            Assert.That(long.Parse(values[16]), Is.GreaterThanOrEqualTo(long.Parse(values[15])));
            Assert.Throws<InvalidOperationException>(() => probe.RecordSnapshot(10801, 123, .1));
            for (var i = 9; i < 14; i++) Assert.That(values[i], Is.EqualTo("-1"));
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            Assert.That(double.Parse(values[4], culture), Is.GreaterThanOrEqualTo(double.Parse(values[5], culture)));
            Assert.That(double.Parse(values[5], culture), Is.GreaterThanOrEqualTo(double.Parse(values[6], culture)));
            Assert.That(double.Parse(values[5], culture), Is.GreaterThanOrEqualTo(double.Parse(values[19], culture)));
            Assert.That(double.Parse(values[6], culture), Is.GreaterThanOrEqualTo(double.Parse(values[25], culture)));
            Assert.That(double.Parse(values[7], culture), Is.GreaterThanOrEqualTo(double.Parse(values[21], culture)));
            Assert.That(double.Parse(values[4], culture), Is.GreaterThanOrEqualTo(double.Parse(values[23], culture)));
        }
        [Test]
        public void CapacityFailureIsExplicitInsteadOfDroppingRows()
        {
            var probe = new BenchmarkCoreDiagnostics(1, false);
            probe.BeginTick(1, 2); probe.EndTick();
            Assert.Throws<InvalidOperationException>(() => probe.BeginTick(2, 3));
            Assert.That(probe.Count, Is.EqualTo(1));
        }
    }
}
