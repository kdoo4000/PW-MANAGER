using NUnit.Framework;
using PWManager.Domain.Data;

namespace PWManager.Tests
{
    public sealed class GameDataRulesTests
    {
        [TestCase(GameDataCategory.State)]
        [TestCase(GameDataCategory.Record)]
        public void PersistedCategories_AreSaveData(GameDataCategory category)
        {
            Assert.That(GameDataRules.IsPersisted(category), Is.True);
        }

        [TestCase(GameDataCategory.Static)]
        [TestCase(GameDataCategory.Balance)]
        public void AuthoredCategories_AreProjectContent(GameDataCategory category)
        {
            Assert.That(GameDataRules.IsAuthoredContent(category), Is.True);
        }

        [Test]
        public void DerivedData_IsNeitherSavedNorAuthored()
        {
            Assert.That(GameDataRules.IsPersisted(GameDataCategory.Derived), Is.False);
            Assert.That(GameDataRules.IsAuthoredContent(GameDataCategory.Derived), Is.False);
        }
    }
}
