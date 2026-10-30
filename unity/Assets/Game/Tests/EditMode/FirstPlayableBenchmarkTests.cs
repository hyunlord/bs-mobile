using System;
using System.IO;
using Game.App;
using NUnit.Framework;

namespace Tests.EditMode
{
    public sealed class FirstPlayableBenchmarkTests
    {
        [Test] public void DefaultWaveFixtureIsUntouchedAndFirstPlayableIsExplicit()
        {
            Assert.That(FirstPlayableBenchmark.Parse(new[] { "--wave-benchmark", "input" }, "wave-1a"), Is.Null);
            Assert.Throws<ArgumentException>(() => WaveBenchmarkAccount.Parse(new[] { "--first-playable-benchmark" }), "Missing fixture must never fall back to the player save.");
            Assert.Throws<ArgumentException>(() => FirstPlayableBenchmark.Parse(new[] { "--first-playable-benchmark" }, "first-playable"));
            Assert.Throws<ArgumentException>(() => FirstPlayableBenchmark.Parse(new[] { "--wave-benchmark", "input" }, "first-playable"));
            Assert.That(FirstPlayableBenchmark.Parse(new[] { "--wave-benchmark", "input", "--first-playable-benchmark" }, "first-playable"), Is.Not.Null);
            Assert.Throws<ArgumentException>(() => FirstPlayableBenchmark.Parse(new[] { "--wave-benchmark", "input", "--first-playable-benchmark" }, "wave-1a"));
        }
        [TestCase("--offline-replay-capture")][TestCase("--wave-benchmark-capture")]
        [TestCase("--crowd-replay-benchmark")][TestCase("--recorded-replay-window")]
        [TestCase("--first-playable-benchmark")][TestCase("--camera-scale")]
        public void RejectsMixedFixtureOrCaptureModes(string flag)
            => Assert.Throws<ArgumentException>(() => FirstPlayableBenchmark.Parse(new[] { "--wave-benchmark", "input", "--first-playable-benchmark", flag }, "first-playable"));
        [Test] public void RejectsChangedInputCatalogTerminalOrCommandCount()
        {
            var mode = FirstPlayableBenchmark.Parse(new[] { "--wave-benchmark", "input", "--first-playable-benchmark" }, "first-playable");
            void Check(string replay, string recorded, string current, int tick, string hash, long commands)
                => mode.Validate(replay, recorded, current, tick, hash, commands);
            Assert.DoesNotThrow(() => Check(FirstPlayableBenchmark.ReplaySha256, FirstPlayableBenchmark.DataHash, FirstPlayableBenchmark.DataHash, 27000, FirstPlayableBenchmark.TerminalHash, 27052));
            Assert.Throws<InvalidDataException>(() => Check("wrong", FirstPlayableBenchmark.DataHash, FirstPlayableBenchmark.DataHash, 27000, FirstPlayableBenchmark.TerminalHash, 27052));
            Assert.Throws<InvalidDataException>(() => Check(FirstPlayableBenchmark.ReplaySha256, "wrong", FirstPlayableBenchmark.DataHash, 27000, FirstPlayableBenchmark.TerminalHash, 27052));
            Assert.Throws<InvalidDataException>(() => Check(FirstPlayableBenchmark.ReplaySha256, FirstPlayableBenchmark.DataHash, "wrong", 27000, FirstPlayableBenchmark.TerminalHash, 27052));
            Assert.Throws<InvalidDataException>(() => Check(FirstPlayableBenchmark.ReplaySha256, FirstPlayableBenchmark.DataHash, FirstPlayableBenchmark.DataHash, 26999, FirstPlayableBenchmark.TerminalHash, 27052));
            Assert.Throws<InvalidDataException>(() => Check(FirstPlayableBenchmark.ReplaySha256, FirstPlayableBenchmark.DataHash, FirstPlayableBenchmark.DataHash, 27000, "wrong", 27052));
            Assert.Throws<InvalidDataException>(() => Check(FirstPlayableBenchmark.ReplaySha256, FirstPlayableBenchmark.DataHash, FirstPlayableBenchmark.DataHash, 27000, FirstPlayableBenchmark.TerminalHash, 27051));
        }
    }
}
