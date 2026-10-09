using System;
using Game.App;
using NUnit.Framework;

namespace Tests.EditMode
{
    public sealed class FoundationBootUriTests
    {
        [Test]
        public void MacApplicationPathBecomesAnEscapedFileUri()
        {
            const string path = "/Users/player/씨앗 #1/Sow and Siege.app/Contents/Resources/Data/StreamingAssets/data/profiles/first-playable.json";
            var actual = FoundationBoot.ToRequestUri(path);
            Assert.That(actual, Does.StartWith("file:///"));
            Assert.That(actual, Does.Contain("Sow%20and%20Siege.app"));
            Assert.That(actual, Does.Contain("%231"));
            Assert.That(new Uri(actual).LocalPath, Is.EqualTo(path));
        }

        [TestCase("jar:file:///data/app/com.hyunlord.sowsiege/base.apk!/assets/data/profiles/first-playable.json")]
        [TestCase("https://example.test/StreamingAssets/data/profiles/first-playable.json")]
        [TestCase("file:///Users/player/Sow%20and%20Siege.app/data/profile.json")]
        public void ExistingRequestSchemesRemainUnchanged(string path)
        {
            Assert.That(FoundationBoot.ToRequestUri(path), Is.EqualTo(path));
        }
    }
}
