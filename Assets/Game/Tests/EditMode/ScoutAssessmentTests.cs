using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEditor;
using UnityEngine;

namespace PWManager.Tests
{
    public sealed class ScoutAssessmentTests
    {
        [TestCase(1, 0)]
        [TestCase(2, 3)]
        [TestCase(3, 7)]
        [TestCase(4, 12)]
        [TestCase(5, 17)]
        public void Reports_RevealRandomDetailedStatsAndKeepTheirSavedAssessment(int level, int count)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>("Assets/Game/Data/Static/GameStaticContentCatalog.asset");
            var save = GameSave.CreateNew(7, new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc));
            save.Promotion = new PromotionState { Id = Guid.NewGuid().ToString(), Name = "Test" };
            var team = new StaffDepartmentState { Id = Guid.NewGuid().ToString(), DepartmentType = StaffDepartmentType.Scout, CurrentLevel = level, UnlockedLevel = level };
            save.StaffDepartments.Add(team);
            var service = new ScoutingService(new WrestlerGenerator(new StaticContentRegistry(catalog), 7), catalog.ScoutTeamLevels, random: new System.Random(7));
            var assignment = service.Start(save, new ScoutSearchConditions()); save.CurrentDate = assignment.CompleteDate;
            var reports = service.CompleteDue(save);
            foreach (var report in reports)
            {
                Assert.That(report.ValueEstimates.Take(17).Count(x => x.IsFullyRevealed), Is.EqualTo(count));
                Assert.That(report.ValueEstimates.All(x => x.HasEvaluation), Is.True);
                var wrestler = save.Wrestlers.Single(x => x.Id == report.WrestlerId);
                var actual = new[] { wrestler.Attributes.RingPsychology, wrestler.Attributes.RingImprovisation,
                    wrestler.Attributes.Technical, wrestler.Attributes.Brawling, wrestler.Attributes.Power, wrestler.Attributes.HighFlying,
                    wrestler.Attributes.SpotWork, wrestler.Attributes.SpecialtyMatches, wrestler.Attributes.Selling, wrestler.Attributes.Stamina,
                    wrestler.Attributes.Charisma, wrestler.Attributes.MicWork, wrestler.Attributes.Improvisation, wrestler.Attributes.Acting,
                    wrestler.Attributes.FaceWork, wrestler.Attributes.HeelWork, wrestler.Attributes.Comedy };
                foreach (var value in report.ValueEstimates.Take(17))
                {
                    if (value.IsFullyRevealed)
                        Assert.That(ScoutAssessmentRules.Display(value), Is.EqualTo(WrestlerOverallCalculator.Grade(actual[(int)value.Type])));
                    else Assert.That(Math.Abs((int)value.Assessment - (int)ScoutAssessmentRules.Assess(actual[(int)value.Type])), Is.LessThanOrEqualTo(1));
                }
            }
            if (level is > 1 and < 5)
                Assert.That(reports.Select(x => string.Join(",", x.ValueEstimates.Where(v => v.IsFullyRevealed && (int)v.Type < 17).Select(v => v.Type))).Distinct().Count(), Is.GreaterThan(1));
            var before = reports.SelectMany(x => x.ValueEstimates).Select(ScoutAssessmentRules.Display).ToArray();
            team.CurrentLevel = 5; service.CompleteDue(save);
            var restored = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            Assert.That(restored.ScoutCandidates.SelectMany(x => x.ValueEstimates).Select(ScoutAssessmentRules.Display), Is.EqualTo(before));
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
        public void UncertainEvaluation_MistakesOnlyBelowLevelProbabilityAndOneTierAway(int level)
        {
            foreach (var actual in new[] { 1f, 4f, 8f, 12f, 16f, 20f })
            {
                var mistaken = ScoutAssessmentRules.Evaluate(ScoutValueType.Power, actual, level, false, new FixedRandom(ScoutAssessmentRules.MistakeChance(level) - .00001));
                var correct = ScoutAssessmentRules.Evaluate(ScoutValueType.Power, actual, level, false, new FixedRandom(ScoutAssessmentRules.MistakeChance(level)));
                Assert.That(Math.Abs((int)mistaken.Assessment - (int)ScoutAssessmentRules.Assess(actual)), Is.EqualTo(1));
                Assert.That(correct.Assessment, Is.EqualTo(ScoutAssessmentRules.Assess(actual)));
                Assert.That(ScoutAssessmentRules.Display(mistaken), Does.Not.Contain("~").And.Not.EqualTo("미확인"));
            }
        }

        [TestCase(1f, ScoutAssessment.VeryLow)] [TestCase(3.99f, ScoutAssessment.VeryLow)]
        [TestCase(4f, ScoutAssessment.Low)] [TestCase(7.99f, ScoutAssessment.Low)]
        [TestCase(8f, ScoutAssessment.Average)] [TestCase(11.99f, ScoutAssessment.Average)]
        [TestCase(12f, ScoutAssessment.High)] [TestCase(15.99f, ScoutAssessment.High)]
        [TestCase(16f, ScoutAssessment.VeryHigh)] [TestCase(20f, ScoutAssessment.VeryHigh)]
        public void Assessment_UsesFiveBands(float actual, ScoutAssessment expected) => Assert.That(ScoutAssessmentRules.Assess(actual), Is.EqualTo(expected));

        [TestCase(14.96f)] [TestCase(15.999f)] [TestCase(20f)]
        public void FullyRevealed_NeverMisjudgesOrRoundsToAnotherGrade(float actual)
        {
            var value = ScoutAssessmentRules.Evaluate(ScoutValueType.Power, actual, 2, true, new FixedRandom(0));
            Assert.That(value.Minimum, Is.EqualTo(actual)); Assert.That(value.Maximum, Is.EqualTo(actual));
            Assert.That(ScoutAssessmentRules.Display(value), Is.EqualTo(WrestlerOverallCalculator.Grade(actual)));
        }

        private sealed class FixedRandom : System.Random
        {
            private readonly double value;
            public FixedRandom(double value) => this.value = value;
            public override double NextDouble() => value;
        }
    }
}
