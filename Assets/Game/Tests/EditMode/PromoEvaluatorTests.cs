using System;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Tests
{
    public sealed class PromoEvaluatorTests
    {
        [Test]
        public void Evaluate_MultipleSpeakers_UsesSixtyFortyPerformanceRule()
        {
            var save = CreateSave();
            var evaluator = new PromoEvaluator(() => "11111111111111111111111111111111");
            var input = Input();
            input.ParticipantPerformances.Add(new PromoParticipantPerformance { WrestlerId = "a", Score = 80f });
            input.ParticipantPerformances.Add(new PromoParticipantPerformance { WrestlerId = "b", Score = 60f });
            input.PrimarySpeakerId = "a";
            input.RoleFit = 5f;
            input.PurposeFit = 3f;

            var result = evaluator.Evaluate(save, input);

            Assert.That(result.PromoScore, Is.EqualTo(80f).Within(0.001f));
            Assert.That(result.EvaluationReasons[0].Contribution, Is.EqualTo(72f).Within(0.001f));
            Assert.That(result.ResultSeed, Is.EqualTo(7));
        }

        [Test]
        public void Evaluate_SameInput_ProducesSameGameplayValues()
        {
            var save = CreateSave();
            var evaluator = new PromoEvaluator(() => "11111111111111111111111111111111");
            var input = Input();
            input.ParticipantPerformances.Add(new PromoParticipantPerformance { WrestlerId = "a", Score = 70f });
            input.ParticipantPerformances.Add(new PromoParticipantPerformance { WrestlerId = "b", Score = 50f });
            input.PrimarySpeakerId = "a";
            input.ResultVariance = 0.5f;

            var first = evaluator.Evaluate(save, input);
            var second = evaluator.Evaluate(save, input);

            Assert.That(second.PromoScore, Is.EqualTo(first.PromoScore));
            Assert.That(second.EvaluationReasons[0].Contribution, Is.EqualTo(first.EvaluationReasons[0].Contribution));
        }

        [Test]
        public void Evaluate_OutOfRangeAdjustment_IsRejected()
        {
            var save = CreateSave();
            var input = Input();
            input.ParticipantPerformances.Add(new PromoParticipantPerformance { WrestlerId = "a", Score = 70f });
            input.PrimarySpeakerId = "a";
            input.RoleFit = 11f;

            Assert.Throws<ArgumentOutOfRangeException>(() => new PromoEvaluator().Evaluate(save, input));
        }

        [Test]
        public void Evaluate_VideoPackageWithoutSpeaker_UsesProductionPerformance()
        {
            var save = CreateSave();
            save.PromoPlans[0].Presentation = PromoPresentation.VideoPackage;
            var input = Input();
            input.ProductionPerformance = 65f;

            var result = new PromoEvaluator(() => "11111111111111111111111111111111").Evaluate(save, input);

            Assert.That(result.PromoScore, Is.EqualTo(65f));
        }

        [Test]
        public void Evaluate_SponsorAdvertisementWithoutRequirement_IsBlocked()
        {
            var save = CreateSave();
            save.PromoPlans[0].Purpose = PromoPurpose.SponsorAdvertisement;
            var input = Input();
            input.ProductionPerformance = 65f;

            Assert.Throws<InvalidOperationException>(() => new PromoEvaluator().Evaluate(save, input));
        }

        private static PromoEvaluationInput Input() => new() { ShowEventId = "event", ResultSeed = 7 };

        private static GameSave CreateSave()
        {
            var save = new GameSave();
            save.Wrestlers.Add(new WrestlerState { Id = "a" });
            save.Wrestlers.Add(new WrestlerState { Id = "b" });
            save.Shows.Add(new ShowState { Id = "show", ShowVersion = 1, Status = ShowStatus.Confirmed });
            save.PromoPlans.Add(new PromoPlanState
            {
                Id = "promo", Purpose = PromoPurpose.Rivalry, Presentation = PromoPresentation.InRingMic,
                ParticipantIds = { "a", "b" }
            });
            save.ShowEvents.Add(new ShowEventState
            {
                Id = "event", ShowId = "show", EventType = ShowEventType.Promo,
                DetailId = "promo", PlannedDuration = 10
            });
            return save;
        }
    }
}
