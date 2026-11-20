using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using Random = System.Random;

namespace PWManager.Tests
{
    public sealed class ContractTerminationServiceTests
    {
        [Test]
        public void Quote_ExcludesChargedMonthAndIncludesBindingRenewal()
        {
            var save = CreateSave(); var contract = save.Contracts[0];
            save.ProcessedIds.Add($"salary:{contract.Id}:20260701");
            Assert.That(Quote(save).RemainingSalary, Is.EqualTo(31000));
            save.Contracts.Add(new ContractState { Id = Id(), PersonId = contract.PersonId, Type = contract.Type,
                StartDate = new GameDate(2027, 6, 1), EndDate = new GameDate(2028, 5, 31), MonthlySalary = 4000, Status = ContractStatus.Scheduled });
            Assert.That(Quote(save).RemainingSalary, Is.EqualTo(79000));
            Assert.That(Quote(save).MutualCompensation, Is.EqualTo(55300));
        }

        [Test]
        public void Quote_ProrratesUnchargedDaysAndHandlesLeapMonth()
        {
            var save = CreateSave();
            Assert.That(Quote(save).RemainingSalary, Is.EqualTo(32600));
            save.CurrentDate = new GameDate(2028, 2, 15); save.Contracts[0].MonthlySalary = 2900;
            save.Contracts[0].EndDate = new GameDate(2028, 5, 31);
            Assert.That(Quote(save).RemainingSalary, Is.EqualTo(10100));
            save.CurrentDate = save.Contracts[0].EndDate;
            Assert.That(Quote(save).RemainingSalary, Is.Zero);
        }

        [Test]
        public void Quote_SalaryLedgerDoesNotDoubleChargeExistingUnpaidSalary()
        {
            var save = CreateSave(); AddDebt(save);
            Assert.That(Quote(save).RemainingSalary, Is.EqualTo(31000));
            Assert.That(SalaryPaymentService.Outstanding(save), Is.EqualTo(100));
        }

        [Test]
        public void Acceptance_OverpaidUnhappyPersonCanPreferGuaranteedSalary()
        {
            var save = CreateSave(); var market = ContractNegotiationService.Demand(save, save.Wrestlers[0].Id, ContractType.Wrestler).MonthlySalary;
            save.Contracts[0].MonthlySalary = market;
            Assert.That(Quote(save).AcceptancePercent, Is.EqualTo(70));
            save.Wrestlers[0].Condition.Satisfaction = 0;
            Assert.That(Quote(save).AcceptancePercent, Is.EqualTo(80));
            save.Contracts[0].MonthlySalary = market * 2;
            Assert.That(Quote(save).AcceptancePercent, Is.EqualTo(20));
            save.Contracts[0].MonthlySalary = market / 2;
            Assert.That(Quote(save).AcceptancePercent, Is.EqualTo(95));
        }

        [Test]
        public void UnilateralRelease_PaysOncePreservesDebtAndTerminatesRenewal()
        {
            var save = CreateSave(); AddDebt(save);
            save.Contracts.Add(new ContractState { Id = Id(), PersonId = save.Wrestlers[0].Id, Status = ContractStatus.Scheduled,
                StartDate = new GameDate(2027, 6, 1), EndDate = new GameDate(2028, 5, 31), MonthlySalary = 4000 });
            var service = new ContractTerminationService();
            var result = service.Submit(save, save.Wrestlers[0].Id, ContractType.Wrestler, false);
            Assert.That(result, Is.Not.Null); Assert.That(result.Accepted, Is.True);
            Assert.That(save.Transactions.Single().Amount, Is.EqualTo(-79000));
            Assert.That(save.Contracts.All(x => x.Status == ContractStatus.Terminated), Is.True);
            Assert.That(save.Wrestlers[0].Roster.ActivityState, Is.EqualTo(RosterActivityState.Released));
            Assert.That(SalaryPaymentService.Outstanding(save), Is.EqualTo(100));
            Assert.That(service.Submit(save, save.Wrestlers[0].Id, ContractType.Wrestler, false), Is.SameAs(result));
            Assert.That(save.Transactions, Has.Count.EqualTo(1));
            new SalaryPaymentService().ProcessDay(save, new GameDate(2026, 8, 1));
            Assert.That(save.SalaryPayments, Has.Count.EqualTo(1));
            Assert.That(save.Contracts.All(x => x.Status == ContractStatus.Terminated), Is.True);
        }

        [Test]
        public void MutualRejection_PreservesContractAndAllowsRetryOnlyOnDaySeven()
        {
            var save = CreateSave(); save.Contracts[0].MonthlySalary = ContractNegotiationService.Demand(save, save.Wrestlers[0].Id, ContractType.Wrestler).MonthlySalary;
            var service = new ContractTerminationService(random: new Random(0));
            var result = service.Submit(save, save.Wrestlers[0].Id, ContractType.Wrestler, true);
            Assert.That(result, Is.Not.Null); Assert.That(result.Accepted, Is.False);
            Assert.That(save.Transactions, Is.Empty); Assert.That(save.Contracts[0].Status, Is.EqualTo(ContractStatus.Active));
            var restored = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            restored.CurrentDate = save.CurrentDate.AddDays(6);
            Assert.Throws<InvalidOperationException>(() => service.Submit(restored, restored.Wrestlers[0].Id, ContractType.Wrestler, true));
            restored.CurrentDate = save.CurrentDate.AddDays(7);
            Assert.That(new ContractTerminationService(random: new Random(1)).Submit(restored, restored.Wrestlers[0].Id, ContractType.Wrestler, true).Accepted, Is.True);
        }

        [Test]
        public void DefaultRoll_IsStableAcrossReloadAndUnilateralCanFollowRejection()
        {
            var save = CreateSave(); var restored = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            var first = new ContractTerminationService().Submit(save, save.Wrestlers[0].Id, ContractType.Wrestler, true);
            var second = new ContractTerminationService().Submit(restored, restored.Wrestlers[0].Id, ContractType.Wrestler, true);
            Assert.That(first, Is.Not.Null); Assert.That(second.RollPercent, Is.EqualTo(first.RollPercent));
            Assert.That(second.Accepted, Is.EqualTo(first.Accepted));
            if (!first.Accepted) Assert.That(new ContractTerminationService().Submit(save, save.Wrestlers[0].Id, ContractType.Wrestler, false).Accepted, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InsufficientFunds_DoNotChangeHistoryOrContracts(bool mutual)
        {
            var save = CreateSave(); save.Promotion.InitialCash = 1; var before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => new ContractTerminationService(random: new Random(1)).Submit(save, save.Wrestlers[0].Id, ContractType.Wrestler, mutual));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [TestCase(0)]
        [TestCase(1)]
        public void InvalidGeneratedId_DoesNotPartiallyReleaseOrCharge(int nullIndex)
        {
            var save = CreateSave(); var before = JsonUtility.ToJson(save); var index = 0;
            Assert.Throws<InvalidOperationException>(() => new ContractTerminationService(() => index++ == nullIndex ? null : Id())
                .Submit(save, save.Wrestlers[0].Id, ContractType.Wrestler, false));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void UpcomingBookingOrRunningShow_BlocksReleaseWithoutChanges()
        {
            var save = CreateSave(); var id = save.Wrestlers[0].Id;
            var show = new ShowState { Id = Id(), Date = save.CurrentDate.AddDays(1), Status = ShowStatus.Draft }; save.Shows.Add(show);
            var match = new MatchPlanState { Id = Id(), ParticipantIds = new() { id } }; save.MatchPlans.Add(match);
            var entry = new ShowEventState { Id = Id(), ShowId = show.Id, DetailId = match.Id }; save.ShowEvents.Add(entry); show.TimelineEventIds.Add(entry.Id);
            var before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => new ContractTerminationService().Submit(save, id, ContractType.Wrestler, false));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            save.ShowEvents.Clear(); show.TimelineEventIds.Clear(); show.Status = ShowStatus.InProgress;
            Assert.Throws<InvalidOperationException>(() => new ContractTerminationService().Submit(save, id, ContractType.Wrestler, false));
        }

        [Test]
        public void Release_ClosesTitleAndTeamAndRelatedStoryWithoutDeletingHistory()
        {
            var save = CreateSave(); var wrestler = save.Wrestlers[0];
            var other = new WrestlerState { Identity = new WrestlerIdentityState { Id = Id() }, PromotionId = save.Promotion.Id }; save.Wrestlers.Add(other);
            var team = new TagTeamState { Id = Id(), PromotionId = save.Promotion.Id, Status = TagTeamStatus.Active, MemberIds = new() { wrestler.Id, other.Id } }; save.TagTeams.Add(team);
            wrestler.Roster.ActiveTagTeamId = other.Roster.ActiveTagTeamId = team.Id;
            var title = ChampionshipService.Create(save, "Test", ChampionshipType.Individual, holderId: wrestler.Id);
            var story = new StorylineState { Id = Id(), FirstWrestlerId = wrestler.Id, SecondWrestlerId = other.Id }; save.Storylines.Add(story);
            Assert.That(new ContractTerminationService().Submit(save, wrestler.Id, ContractType.Wrestler, false), Is.Not.Null);
            Assert.That(title.HolderId, Is.Null); Assert.That(title.Reigns.Single().LostDate.Value, Is.EqualTo(save.CurrentDate));
            Assert.That(team.Status, Is.EqualTo(TagTeamStatus.Inactive)); Assert.That(other.Roster.ActiveTagTeamId, Is.Null.Or.Empty);
            Assert.That(story.Status, Is.EqualTo(StorylineStatus.Completed)); Assert.That(save.Wrestlers, Has.Count.EqualTo(2));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void RealSidesPromoTargetsAndIndirectGroupBookings_BlockBeforeMutation(int kind)
        {
            var save = CreateSave(); var id = save.Wrestlers[0].Id;
            var team = new TagTeamState { Id = Id(), Status = TagTeamStatus.Active, MemberIds = new() { id, "other" } }; save.TagTeams.Add(team);
            var story = new StorylineState { Id = Id(), PartyType = StoryPartyType.TagTeam, FirstGroupId = team.Id, SecondGroupId = "enemy" }; save.Storylines.Add(story);
            var show = new ShowState { Id = Id(), Status = ShowStatus.Confirmed, Date = save.CurrentDate.AddDays(1) }; save.Shows.Add(show);
            var entry = new ShowEventState { Id = Id(), ShowId = show.Id, DetailId = Id(), EventType = kind == 0 || kind == 2 ? ShowEventType.Match : ShowEventType.Promo };
            save.ShowEvents.Add(entry); show.TimelineEventIds.Add(entry.Id);
            if (entry.EventType == ShowEventType.Match)
                save.MatchPlans.Add(new MatchPlanState { Id = entry.DetailId, Sides = new() { new MatchSideState { MemberIds = new() { kind == 0 ? id : "other" } } },
                    StorylineId = kind == 2 ? story.Id : null });
            else save.PromoPlans.Add(new PromoPlanState { Id = entry.DetailId, TargetIds = kind == 1 ? new() { id } : new(), GroupId = kind == 3 ? team.Id : null });
            var before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => new ContractTerminationService().Submit(save, id, ContractType.Wrestler, false));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [TestCase(3)]
        [TestCase(4)]
        public void StableLeaderRelease_ReplacesLeaderOrPreservesInactiveHistory(int size)
        {
            var save = CreateSave(); var id = save.Wrestlers[0].Id;
            for (var i = 1; i < size; i++) save.Wrestlers.Add(new WrestlerState { Id = Id(), PromotionId = save.Promotion.Id });
            var stable = new StableState { Id = Id(), Status = TagTeamStatus.Active, LeaderId = id,
                MemberIds = save.Wrestlers.Select(x => x.Id).ToList() }; save.Stables.Add(stable);
            foreach (var wrestler in save.Wrestlers) wrestler.Roster.ActiveStableId = stable.Id;
            new ContractTerminationService().Submit(save, id, ContractType.Wrestler, false);
            if (size == 4)
            {
                Assert.That(stable.MemberIds, Has.Count.EqualTo(3)); Assert.That(stable.MemberIds, Does.Contain(stable.LeaderId));
                Assert.That(stable.LeaderId, Is.Not.EqualTo(id)); Assert.That(stable.Status, Is.EqualTo(TagTeamStatus.Active));
            }
            else
            {
                Assert.That(stable.MemberIds, Has.Count.EqualTo(3)); Assert.That(stable.LeaderId, Is.EqualTo(id));
                Assert.That(stable.Status, Is.EqualTo(TagTeamStatus.Inactive));
                Assert.That(save.Wrestlers.All(x => string.IsNullOrEmpty(x.Roster.ActiveStableId)), Is.True);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TeamChampionRelease_VacatesTitleAndRehireGetsANewSettlement(bool inactiveTeam)
        {
            var save = CreateSave(); var id = save.Wrestlers[0].Id;
            var other = new WrestlerState { Id = Id(), PromotionId = save.Promotion.Id }; save.Wrestlers.Add(other);
            var team = new TagTeamState { Id = Id(), PromotionId = save.Promotion.Id, Status = TagTeamStatus.Active,
                MemberIds = new() { id, other.Id } }; save.TagTeams.Add(team);
            var title = ChampionshipService.Create(save, "Tag", ChampionshipType.TagTeam, holderId: team.Id);
            if (inactiveTeam) team.Status = TagTeamStatus.Inactive;
            var first = new ContractTerminationService().Submit(save, id, ContractType.Wrestler, false);
            Assert.That(title.HolderId, Is.Null); Assert.That(title.Reigns.Single().LostDate.HasValue, Is.True);
            var demand = ContractNegotiationService.Demand(save, id, ContractType.Wrestler);
            Assert.That(new ContractNegotiationService(random: new Random(1)).Submit(save, id, ContractType.Wrestler, demand.SigningBonus, demand.MonthlySalary).Accepted, Is.True);
            var second = new ContractTerminationService().Submit(save, id, ContractType.Wrestler, false);
            Assert.That(second.Id, Is.Not.EqualTo(first.Id)); Assert.That(second.ContractIds, Does.Not.Contain(first.ContractIds.Single()));
            ContractTerminationService.ValidateState(save);
        }

        [Test]
        public void PlayerCharacterRelease_IsBlockedAndDuplicateIdsAreAtomic()
        {
            var save = CreateSave(); var id = save.Wrestlers[0].Id;
            save.Player = new PlayerCharacterState { WrestlerId = id }; var before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => new ContractTerminationService().Submit(save, id, ContractType.Wrestler, false));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            save.Player = null; before = JsonUtility.ToJson(save); var repeated = Id();
            Assert.Throws<InvalidOperationException>(() => new ContractTerminationService(() => repeated).Submit(save, id, ContractType.Wrestler, false));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        private static ContractTerminationQuote Quote(GameSave save) => ContractTerminationService.Quote(save, save.Wrestlers[0].Id, ContractType.Wrestler);
        private static string Id() => Guid.NewGuid().ToString("D");
        private static void AddDebt(GameSave save)
        {
            save.Contracts[0].Status = ContractStatus.Overdue; save.Contracts[0].LastSalaryPenaltyDate = new OptionalGameDate(new GameDate(2026, 7, 1));
            save.SalaryPayments.Add(new SalaryPaymentState { Id = Id(), ContractId = save.Contracts[0].Id,
                DueDate = new GameDate(2026, 7, 1), Amount = 3100, RemainingAmount = 100, WasOverdue = true });
        }
        private static GameSave CreateSave()
        {
            var save = GameSave.CreateNew(13, DateTime.UtcNow); save.CurrentDate = new GameDate(2026, 7, 15);
            save.Promotion = new PromotionState { Id = Id(), Name = "Test", InitialCash = 1000000 };
            var wrestler = new WrestlerState { Identity = new WrestlerIdentityState { Id = Id(), RingName = "Test" }, PromotionId = save.Promotion.Id,
                Condition = new WrestlerConditionState { Satisfaction = 50 } }; save.Wrestlers.Add(wrestler);
            save.Contracts.Add(new ContractState { Id = Id(), PersonId = wrestler.Id, StartDate = new GameDate(2026, 6, 1),
                EndDate = new GameDate(2027, 5, 31), MonthlySalary = 3100, Status = ContractStatus.Active }); return save;
        }
    }
}
