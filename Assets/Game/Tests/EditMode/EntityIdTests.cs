using NUnit.Framework;
using PWManager.Domain.Identifiers;

namespace PWManager.Tests
{
    public sealed class EntityIdTests
    {
        [TestCase("trait_001")]
        [TestCase("venue_002")]
        [TestCase("matchtype_999")]
        public void StaticId_WithExpectedFormat_IsValid(string id)
        {
            Assert.That(EntityId.IsValidStaticId(id), Is.True);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Wrestler_Kim_001")]
        [TestCase("wrestler")]
        [TestCase("wrestler-kim-001")]
        [TestCase("_wrestler_001")]
        [TestCase("wrestler_kim_001")]
        [TestCase("wrestler_01")]
        [TestCase("wrestler_1000")]
        [TestCase("wrestler_abc")]
        public void StaticId_WithInvalidFormat_IsRejected(string id)
        {
            Assert.That(EntityId.IsValidStaticId(id), Is.False);
        }

        [Test]
        public void RuntimeId_IsUniqueGuidInDFormat()
        {
            var first = EntityId.CreateRuntimeId();
            var second = EntityId.CreateRuntimeId();

            Assert.That(EntityId.IsValidRuntimeId(first), Is.True);
            Assert.That(EntityId.IsValidRuntimeId(second), Is.True);
            Assert.That(second, Is.Not.EqualTo(first));
        }
    }
}
