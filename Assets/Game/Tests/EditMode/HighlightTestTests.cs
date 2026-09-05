using NUnit.Framework;
using PWManager.Presentation;

namespace PWManager.Tests
{
    public sealed class HighlightTestTests
    {
        [Test]
        public void NearFall_HoldsTwoUntilKickout_AndEndsWithoutDeclaringWinner()
        {
            Assert.That(HighlightTestBootstrap.CueAt(-1), Is.EqualTo(0));
            Assert.That(HighlightTestBootstrap.CueAt(7), Is.EqualTo(3));
            Assert.That(HighlightTestBootstrap.CueAt(8.1f), Is.EqualTo(4));
            Assert.That(HighlightTestBootstrap.CueAt(9.49f), Is.EqualTo(4));
            Assert.That(HighlightTestBootstrap.CueAt(9.5f), Is.EqualTo(5));
            Assert.That(HighlightTestBootstrap.CueAt(HighlightTestBootstrap.Duration), Is.EqualTo(6));
            Assert.That(HighlightTestBootstrap.CueAt(100), Is.EqualTo(6));
        }
    }
}
