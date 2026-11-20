using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;

namespace PWManager.Tests
{
    public sealed class AllHiddenEffectsTests
    {
        [TestCase(1, 20, true)]
        [TestCase(20, 20, false)]
        [TestCase(1, 1, false)]
        public void MediaExpression_RequiresProfessionalismGateAndActualIgnoredProblem(int professionalism, int media, bool expected)
        {
            var save = Unhappy(); var person = save.Wrestlers[0]; person.Hidden.Professionalism = professionalism; person.Hidden.MediaExpression = media;
            save.Promotion.Audience = FanAudienceService.CreateInitial(save.CurrentDate, 10000); person.Fame.Value = 100;
            save.CurrentDate = save.WrestlerBehaviors.Single().NextActionDate.Value;
            new WrestlerBehaviorService(random: new ConstantRoll(.5)).ProcessDay(save);
            var state = save.WrestlerBehaviors.Single();
            Assert.That(state.Events.Any(e => e.Kind == WrestlerBehaviorEventKind.PublicComplaint), Is.EqualTo(expected));
            Assert.That(save.Promotion.Audience.Mark.Satisfaction < 50, Is.EqualTo(expected));
            var before = JsonUtility.ToJson(save); new WrestlerBehaviorService(random: new ConstantRoll(.5)).ProcessDay(save);
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void Sociability_CreatesRealRelationshipAfterActualSharedMatch()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            foreach (var w in save.Wrestlers) { w.PromotionId = save.Promotion.Id; w.Roster.ActivityState = RosterActivityState.Active; w.Hidden = new() { Sociability = 20 }; }
            var result = ShowExecutionServiceTests.Service().Execute(save, "show", 73, 1000, 30);
            save.Shows[0].Status = ShowStatus.ResultsReviewed;
            new WrestlerBehaviorService(random: new Rolls(0, 0, 0, 0, 0, 0, 0, 0)).PrepareShow(save, result, new[] { "a", "b" }).Apply();
            Assert.That(save.LockerRoomRelationships, Has.Count.EqualTo(1));
            Assert.That(save.LockerRoomEvents, Is.Not.Empty);
            Assert.That(save.StoryRelationships, Is.Empty);
        }

        [Test]
        public void Loyalty_UsesRealEmploymentAndRelationship_NotFirstRecruitmentOrBlindAcceptance()
        {
            var save = ContractSave(); var person = save.Wrestlers[0]; person.Hidden = new() { Loyalty = 1 };
            var demand = ContractNegotiationService.Demand(save, person.Id, ContractType.Wrestler);
            float Chance() => ContractNegotiationService.Acceptance(save, person.Id, ContractType.Wrestler, demand.SigningBonus, demand.MonthlySalary);
            var first = Chance(); person.Hidden.Loyalty = 20; Assert.That(Chance(), Is.EqualTo(first));
            person.PromotionId = save.Promotion.Id; save.Contracts.Add(new() { Id = Guid.NewGuid().ToString(), PersonId = person.Id, PromotionId = save.Promotion.Id,
                Type = ContractType.Wrestler, Status = ContractStatus.Active, StartDate = save.CurrentDate.AddDays(-180), EndDate = save.CurrentDate.AddDays(30) });
            person.Hidden.Loyalty = 1; var low = Chance(); person.Hidden.Loyalty = 20; Assert.That(Chance(), Is.GreaterThan(low).And.LessThan(100));
            save.WrestlerBehaviors.Add(new() { PromotionId = save.Promotion.Id, WrestlerId = person.Id, Trust = 0 });
            var distrustful = Chance(); person.Hidden.Loyalty = 1; Assert.That(Chance(), Is.EqualTo(distrustful));
        }
        [Test]
        public void MoneyOrientation_ChangesSensitivityToRelativePay_NotTheDemandItself()
        {
            var save = ContractSave(); var person = save.Wrestlers[0];
            person.Hidden = new() { MoneyOrientation = 1 };
            var demand = ContractNegotiationService.Demand(save, person.Id, ContractType.Wrestler);
            float Chance() => ContractNegotiationService.Acceptance(save, person.Id, ContractType.Wrestler, demand.SigningBonus, demand.MonthlySalary * 3 / 4);
            var low = Chance(); person.Hidden.MoneyOrientation = 20; var high = Chance();
            Assert.That(high, Is.LessThan(low)); Assert.That(ContractNegotiationService.Demand(save, person.Id, ContractType.Wrestler).MonthlySalary, Is.EqualTo(demand.MonthlySalary));
        }

        [Test]
        public void Adaptability_AdvancesChangedStyleThroughRealTimeFlow()
        {
            var low = ContractSave(); var person = low.Wrestlers[0]; person.Hidden = new() { Adaptability = 1 }; person.PromotionId = low.Promotion.Id;
            person.Presentation.WrestlingStyleId = "style_001";
            low.Contracts.Add(new() { Id = Guid.NewGuid().ToString(), PromotionId = low.Promotion.Id, PersonId = person.Id, Type = ContractType.Wrestler,
                Status = ContractStatus.Active, StartDate = low.CurrentDate, EndDate = low.CurrentDate.AddDays(300) });
            WrestlerAdaptationService.InitializeSave(low); person.Presentation.WrestlingStyleId = "style_002";
            var high = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(low)); high.Wrestlers[0].Hidden.Adaptability = 20;
            new TimeFlowService().AdvanceTo(low, low.CurrentDate.AddDays(1)); new TimeFlowService().AdvanceTo(high, high.CurrentDate.AddDays(1));
            Assert.That(high.Wrestlers[0].Adaptation.StyleReadiness, Is.GreaterThan(low.Wrestlers[0].Adaptation.StyleReadiness));
            Assert.That(high.Wrestlers[0].Adaptation.StyleId, Is.EqualTo("style_002"));
        }

        [Test]
        public void Collegiality_ProtectsOpponentInTheExistingAccidentRoll()
        {
            MatchEngineState State(int collegiality) => new() { AccidentsEnabled = true, Participants = new() {
                new() { Id = "a", Collegiality = collegiality, Attributes = new() { Technical = 15 } },
                new() { Id = "b", Attributes = new() { Technical = 15 } } } };
            MatchEngineEvent Event() => new() { PerformerId = "a", ReceiverId = "b", IsPhysicalAttack = true, Type = MatchActionType.Dive };
            var low = State(1); var high = State(20);
            MatchHealth.Observe(low, Event(), new Rolls(.9999, .009, 0));
            MatchHealth.Observe(high, Event(), new Rolls(.9999, .009, 0));
            Assert.That(low.Participants[1].RealInjury, Is.EqualTo(InjuryStatus.Minor));
            Assert.That(high.Participants[1].RealInjury, Is.EqualTo(InjuryStatus.None));
            Assert.That(low.Participants[0].RealInjury, Is.EqualTo(InjuryStatus.None));
        }

        [Test]
        public void RiskTaking_PrefersAnAvailableDifficultTechnique_WithoutBypassingInjuryRules()
        {
            var state = new MatchEngineState { TargetDurationSeconds = 600, ElapsedSeconds = 150, Phase = MatchPhase.Neutral,
                Participants = new() { new() { Id = "a", Attributes = new() { HighFlying = 15, Brawling = 15, Technical = 15 } },
                    new() { Id = "b", IsDowned = true, Posture = MatchPosture.Supine } } };
            var a = state.Participants[0]; var b = state.Participants[1];
            var action = ActionGenerator.Generate(state, a, b).First(x => x.Type == MatchActionType.Climb);
            a.RiskTaking = 1; var low = ActionEvaluator.Score(state, action);
            a.RiskTaking = 20; Assert.That(ActionEvaluator.Score(state, action), Is.GreaterThan(low));
            a.RealInjury = InjuryStatus.Moderate; a.RealInjuryRegion = BodyRegion.Legs;
            Assert.That(ActionGenerator.Generate(state, a, b).Any(x => x.Type == MatchActionType.Climb), Is.False);
        }

        [Test]
        public void BigMatchPerformance_ChangesActualPpvExecutionWithoutChangingOriginalAttributes()
        {
            var low = ShowExecutionServiceTests.CreateSave();
            foreach (var w in low.Wrestlers) w.Hidden = new() { BigMatchPerformance = 1 };
            low.Shows[0].ShowType = low.Schedules[0].ShowType = ScheduledShowType.PpvRegular;
            var high = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(low));
            foreach (var w in high.Wrestlers) w.Hidden.BigMatchPerformance = 20;
            var attributes = JsonUtility.ToJson(high.Wrestlers[0].Attributes);
            var first = ShowExecutionServiceTests.Service().Execute(low, "show", 73, 1000, 30);
            var second = ShowExecutionServiceTests.Service().Execute(high, "show", 73, 1000, 30);
            var a = low.MatchResults.Single(); var b = high.MatchResults.Single();
            Assert.That(b.WrestlerPerformances[0].EffectiveRoutine, Is.GreaterThan(a.WrestlerPerformances[0].EffectiveRoutine));
            Assert.That(b.EngineState.Participants[0].Attributes.Technical, Is.GreaterThan(a.EngineState.Participants[0].Attributes.Technical));
            Assert.That(JsonUtility.ToJson(high.Wrestlers[0].Attributes), Is.EqualTo(attributes));
            Assert.That(b.WinnerId, Is.EqualTo(a.WinnerId));
        }

        private sealed class Rolls : System.Random
        {
            private readonly double[] values; private int index;
            public Rolls(params double[] values) => this.values = values;
            public override double NextDouble() => values[index++];
        }
        private sealed class ConstantRoll : System.Random
        { private readonly double value; public ConstantRoll(double value) => this.value = value; public override double NextDouble() => value; }
        internal static GameSave Unhappy()
        {
            var save = ContractSave(); var person = save.Wrestlers[0]; person.PromotionId = save.Promotion.Id; person.Roster.ActivityState = RosterActivityState.Active;
            person.Hidden = new() { Ambition = 20, Assertiveness = 20 };
            save.Contracts.Add(new() { Id = Guid.NewGuid().ToString(), PromotionId = save.Promotion.Id, PersonId = person.Id, Type = ContractType.Wrestler,
                Status = ContractStatus.Active, StartDate = save.CurrentDate, EndDate = save.CurrentDate.AddDays(300), HasExpectedRole = true, ExpectedRole = ExpectedRole.Midcarder });
            var service = new WrestlerBehaviorService(random: new ConstantRoll(0));
            for (var i = 0; i < 4; i++)
            {
                save.CurrentDate = save.CurrentDate.AddDays(7);
                var show = new ShowState { Id = Guid.NewGuid().ToString(), Date = save.CurrentDate, ShowVersion = 1, Status = ShowStatus.ResultsReviewed }; save.Shows.Add(show);
                var result = new ShowResultState { Id = Guid.NewGuid().ToString(), ShowId = show.Id, ShowVersion = 1, HasAvailabilitySnapshot = true,
                    AvailableWrestlerIds = new() { person.Id } }; save.ShowResults.Add(result);
                service.PrepareShow(save, result, Array.Empty<string>()).Apply(); show.Status = ShowStatus.Completed;
            }
            save.CurrentDate = save.WrestlerBehaviors.Single().Concerns.Single().ResponseDueDate.Value.AddDays(1); service.ProcessDay(save);
            return save;
        }
        private static GameSave ContractSave()
        {
            var save = GameSave.CreateNew(71, DateTime.UtcNow); save.Promotion = new() { Id = Guid.NewGuid().ToString(), Name = "Hidden contract", InitialCash = 1000000 };
            save.Wrestlers.Add(new() { Id = Guid.NewGuid().ToString(), Condition = new() { Satisfaction = 50 },
                Attributes = new() { Brawling = 12, Power = 12, HighFlying = 12, Technical = 12, Selling = 12, Charisma = 12, MicWork = 12 } });
            return save;
        }
    }
}
