using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PWManager.Data.Catalogs;
using PWManager.Data.Definitions;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEditor;

namespace PWManager.Tests
{
    public sealed class WrestlerGeneratorTests
    {
        private const string CatalogPath = "Assets/Game/Data/Static/GameStaticContentCatalog.asset";
        private StaticContentRegistry content;

        [SetUp]
        public void SetUp()
        {
            content = new StaticContentRegistry(AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath));
        }

        [TestCase(1)]
        [TestCase(42)]
        [TestCase(20260819)]
        [TestCase(9090)]
        public void InitialCandidates_MeetConfirmedCompositionRules(int seed)
        {
            var candidates = CreateGenerator(seed).GenerateInitialCandidates(new GameDate(2026, 1, 1));

            Assert.That(candidates, Has.Count.EqualTo(24));
            Assert.That(candidates.Select(x => x.Identity.LegalName).Distinct().ToList(), Has.Count.EqualTo(24));
            AssertGroup(candidates.Where(x => x.Identity.Gender == WrestlerGender.Male).ToList());
            AssertGroup(candidates.Where(x => x.Identity.Gender == WrestlerGender.Female).ToList());
        }

        [Test]
        public void Candidate_UsesLegalNameAndValidStaticContent()
        {
            var createdDate = new GameDate(2026, 1, 1);
            var candidate = CreateGenerator(17).GenerateCandidate(WrestlerGender.Female, createdDate);

            Assert.That(candidate.Identity.RingName, Is.EqualTo(candidate.Identity.LegalName));
            Assert.That(DaysBetween(createdDate, candidate.Identity.ExpiryDate.Value), Is.EqualTo(364));
            Assert.That(candidate.Presentation.SignatureMoveIds, Has.Count.EqualTo(1));
            Assert.That(candidate.Presentation.FinisherMoveIds, Has.Count.EqualTo(1));
            Assert.That(candidate.Presentation.FinisherMoveIds[0], Is.Not.EqualTo(candidate.Presentation.SignatureMoveIds[0]));
            Assert.That(content.Styles.ContainsKey(candidate.Presentation.WrestlingStyleId), Is.True);

            foreach (var moveId in candidate.Presentation.SignatureMoveIds.Concat(candidate.Presentation.FinisherMoveIds))
                AssertMoveRequirement(candidate, content.Moves[moveId]);

            foreach (var traitId in candidate.Presentation.TraitIds)
            foreach (var otherId in candidate.Presentation.TraitIds.Where(x => x != traitId))
            {
                Assert.That(content.Traits[traitId].ConflictingTraitIds, Does.Not.Contain(otherId));
                Assert.That(content.Traits[otherId].ConflictingTraitIds, Does.Not.Contain(traitId));
            }
        }

        [Test]
        public void SameSeed_ProducesSameGameplayData()
        {
            var date = new GameDate(2026, 1, 1);
            var first = CreateGenerator(42).GenerateCandidate(WrestlerGender.Male, date);
            var second = CreateGenerator(42).GenerateCandidate(WrestlerGender.Male, date);

            Assert.That(second.Identity.LegalName, Is.EqualTo(first.Identity.LegalName));
            Assert.That(second.Identity.BirthDate, Is.EqualTo(first.Identity.BirthDate));
            Assert.That(WrestlerOverallCalculator.Match(second), Is.EqualTo(WrestlerOverallCalculator.Match(first)).Within(.001f));
            Assert.That(WrestlerOverallCalculator.Promo(second), Is.EqualTo(WrestlerOverallCalculator.Promo(first)).Within(.001f));
            Assert.That(second.Presentation.WrestlingStyleId, Is.EqualTo(first.Presentation.WrestlingStyleId));
            Assert.That(second.Presentation.TraitIds, Is.EqualTo(first.Presentation.TraitIds));
            Assert.That(second.Presentation.SignatureMoveIds, Is.EqualTo(first.Presentation.SignatureMoveIds));
            Assert.That(second.Presentation.FinisherMoveIds, Is.EqualTo(first.Presentation.FinisherMoveIds));
        }

        [Test]
        public void GeneratedAttributes_StayInRangeAndNearTheirTargets()
        {
            var generator = CreateGenerator(99);
            for (var i = 0; i < 100; i++)
            {
                var candidate = generator.GenerateCandidate(i % 2 == 0 ? WrestlerGender.Male : WrestlerGender.Female, new GameDate(2026, 1, 1));
                var values = MatchValues(candidate).Concat(PromoValues(candidate)).ToArray();
                Assert.That(values, Has.All.InRange(1f, 20f));
                Assert.That(WrestlerOverallCalculator.MatchTotal(candidate.Attributes), Is.LessThanOrEqualTo(candidate.Growth.MatchPotentialCap * 10f));
                Assert.That(WrestlerOverallCalculator.PromoTotal(candidate.Attributes), Is.LessThanOrEqualTo(candidate.Growth.PromoPotentialCap * 7f));
            }
        }

        private WrestlerGenerator CreateGenerator(int seed)
        {
            var id = 0;
            return new WrestlerGenerator(content, seed, () => $"wrestler_test_{++id:000}");
        }

        private static void AssertGroup(IReadOnlyCollection<WrestlerState> group)
        {
            Assert.That(group, Has.Count.EqualTo(12));
            Assert.That(group.Min(x => WrestlerOverallCalculator.Match(x)), Is.GreaterThanOrEqualTo(7f));
            Assert.That(group.Average(x => WrestlerOverallCalculator.Match(x)), Is.GreaterThanOrEqualTo(10f));
            Assert.That(group.Count(x => WrestlerOverallCalculator.Match(x) >= 10f), Is.GreaterThanOrEqualTo(3));
            Assert.That(group.Count(x => WrestlerOverallCalculator.Match(x) >= 14f), Is.GreaterThanOrEqualTo(1));
            Assert.That(group.Count(x => WrestlerOverallCalculator.Promo(x) >= 10f), Is.GreaterThanOrEqualTo(2));
            Assert.That(group.Count(x => x.Identity.Background == WrestlerBackground.Rookie), Is.GreaterThanOrEqualTo(3));
            Assert.That(group.Count(x => x.Identity.Background == WrestlerBackground.OtherPromotion), Is.GreaterThanOrEqualTo(2));
            Assert.That(group.Count(x => x.Growth.MatchPotentialCap >= 12f), Is.GreaterThanOrEqualTo(2));
            Assert.That(group.Count(x => x.Growth.PromoPotentialCap >= 12f), Is.GreaterThanOrEqualTo(2));
            Assert.That(group.Select(x => x.Presentation.WrestlingStyleId).Distinct().ToList(), Has.Count.GreaterThanOrEqualTo(4));
        }

        private static IEnumerable<float> MatchValues(WrestlerState x) => new[]
        {
            x.Attributes.RingPsychology, x.Attributes.RingImprovisation, x.Attributes.Technical, x.Attributes.Brawling,
            x.Attributes.Power, x.Attributes.HighFlying, x.Attributes.SpotWork, x.Attributes.SpecialtyMatches,
            x.Attributes.Selling, x.Attributes.Stamina
        };

        private static IEnumerable<float> PromoValues(WrestlerState x) => new[]
        {
            x.Attributes.Charisma, x.Attributes.MicWork, x.Attributes.Improvisation, x.Attributes.Acting,
            x.Attributes.FaceWork, x.Attributes.HeelWork, x.Attributes.Comedy
        };

        private static void AssertMoveRequirement(WrestlerState wrestler, MoveDefinition move)
        {
            var actual = move.RequiredStat switch
            {
                MoveRequiredStat.None => 20f,
                MoveRequiredStat.Brawling => wrestler.Attributes.Brawling,
                MoveRequiredStat.Power => wrestler.Attributes.Power,
                MoveRequiredStat.Technical => wrestler.Attributes.Technical,
                MoveRequiredStat.HighFlying => wrestler.Attributes.HighFlying,
                _ => 0f
            };
            Assert.That(actual, Is.GreaterThanOrEqualTo(move.RequiredStatValue));
        }

        private static int DaysBetween(GameDate first, GameDate second)
        {
            return (new DateTime(second.Year, second.Month, second.Day) - new DateTime(first.Year, first.Month, first.Day)).Days;
        }
    }
}
