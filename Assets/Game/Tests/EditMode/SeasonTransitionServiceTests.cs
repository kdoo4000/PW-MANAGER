using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;

namespace PWManager.Tests
{
    public sealed class SeasonTransitionServiceTests
    {
        [TestCase(TransactionType.Salary)]
        [TestCase(TransactionType.LatePayment)]
        [TestCase(TransactionType.MatchAppearanceFee)]
        [TestCase(TransactionType.PromoAppearanceFee)]
        public void ClosingDebt_CountsActualPaymentsAndRefreshesExistingResult(TransactionType type)
        {
            var save = CreateSave();
            var payment = new SalaryPaymentState { Id = Id(), DueDate = save.CurrentDate, Amount = 1000, RemainingAmount = 0 };
            save.SalaryPayments.Add(payment);
            var service = new SeasonTransitionService();
            var first = service.GetOrCreateResult(save);
            Assert.That(first.OutstandingSalary, Is.EqualTo(1000));
            var paid = AddTransaction(save, save.CurrentDate, -600); paid.Type = type; paid.ReasonId = payment.Id;
            var voided = AddTransaction(save, save.CurrentDate, -400); voided.Type = type; voided.ReasonId = payment.Id; voided.IsVoided = true;
            var later = AddTransaction(save, save.CurrentDate.AddDays(1), -400); later.Type = type; later.ReasonId = payment.Id;
            var foreign = AddTransaction(save, save.CurrentDate, -400); foreign.Type = type; foreign.ReasonId = payment.Id; foreign.PromotionId = Id();
            var partial = service.GetOrCreateResult(save);
            Assert.That(partial.Id, Is.EqualTo(first.Id)); Assert.That(partial.OutstandingSalary, Is.EqualTo(400));
            var final = AddTransaction(save, save.CurrentDate, -400); final.Type = type; final.ReasonId = payment.Id;
            var before = save.Transactions.Select(t => JsonUtility.ToJson(t)).ToArray();
            Assert.That(service.GetOrCreateResult(save).OutstandingSalary, Is.Zero);
            Assert.That(save.SeasonResults, Has.Count.EqualTo(1));
            Assert.That(save.Transactions.Select(t => JsonUtility.ToJson(t)), Is.EqualTo(before));
        }

        [Test]
        public void ClosingPrestige_IncludesMilestoneBonusImmediatelyAfterSeasonConfirmation()
        {
            var save = CreateSave(); save.CurrentDate = new GameDate(2027, 4, 7);
            foreach (var wrestler in WrestlerCareerServiceTests.Create().Wrestlers)
            { wrestler.PromotionId = save.Promotion.Id; save.Wrestlers.Add(wrestler); }
            var w = save.Wrestlers[0]; w.Fame = WrestlerFameRules.Create(4999);
            save.Contracts.Add(new() { Id = Id(), PersonId = w.Id, Type = ContractType.Wrestler, WasRookieWhenHired = true,
                StartDate = new GameDate(2026, 6, 1), EndDate = new GameDate(2027, 5, 31), Status = ContractStatus.Active, MonthlySalary = 100 });
            var ctx = WrestlerCareerServiceTests.AddMatch(save); ctx.ShowResult.HasFameSnapshot = true; ctx.ShowResult.FameReach = 10000;
            save.MatchResults[0].ActualMatchDuration = 10; new WrestlerCareerService().PrepareShow(ctx).Apply();
            ctx.Show.Status = ShowStatus.Completed; save.Schedules.Single(x => x.Id == ctx.Show.ScheduleId).Status = ScheduleStatus.Completed;
            Assert.That(save.Promotion.PromotionPrestige, Is.Zero); save.CurrentDate = new GameDate(2027, 5, 31);
            new SeasonTransitionService().ConfirmNextSeason(save, SeasonTransitionService.DefaultSettings(save));
            Assert.That(save.Promotion.PromotionPrestige, Is.EqualTo(20));
            Assert.That(save.SeasonResults.Single().Prestige, Is.EqualTo(20));
        }

        [Test]
        public void Growth_OnlySettlesOnConfirmation_AndDateExitDoesNotRepeat()
        {
            var save = CreateSave(); AddGrowthWrestler(save);
            var w = save.Wrestlers[0]; var before = JsonUtility.ToJson(w);
            var service = new SeasonTransitionService(); service.GetOrCreateResult(save);
            Assert.That(JsonUtility.ToJson(w), Is.EqualTo(before)); Assert.That(save.GrowthQuarterResults, Is.Empty);
            service.ConfirmNextSeason(save, SeasonTransitionService.DefaultSettings(save));
            Assert.That(save.GrowthQuarterResults, Has.Count.EqualTo(1));
            Assert.That(w.Growth.GrowthProgress.Power, Is.Zero);
            new TimeFlowService().AdvanceTo(save, save.CurrentDate.AddDays(1));
            Assert.That(save.GrowthQuarterResults, Has.Count.EqualTo(1));
        }

        [Test]
        public void GrowthIdCollisionWithPreparedCalendar_LeavesWholeSeasonUntouched()
        {
            var probe = CreateSave(); AddGrowthWrestler(probe); var ids = new System.Collections.Generic.List<string>();
            new SeasonTransitionService(() => { var id = Id(); ids.Add(id); return id; }).ConfirmNextSeason(probe, SeasonTransitionService.DefaultSettings(probe));
            var save = CreateSave(); AddGrowthWrestler(save); var before = JsonUtility.ToJson(save); var index = 0;
            Assert.Throws<InvalidOperationException>(() => new SeasonTransitionService(() => ++index == ids.Count ? ids[0] : index == 1 ? ids[0] : Id())
                .ConfirmNextSeason(save, SeasonTransitionService.DefaultSettings(save)));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        private static void AddGrowthWrestler(GameSave save)
        {
            var w = new WrestlerState { Id = Id() }; foreach (var f in typeof(WrestlerAttributesState).GetFields()) f.SetValue(w.Attributes, 10f);
            w.Growth.MatchPotentialCap = w.Growth.PromoPotentialCap = 20; w.Growth.GrowthProgress.Power = 50;
            w.Identity.CreatedDate = new GameDate(2026, 6, 1); save.Wrestlers.Add(w);
            save.LastGrowthQuarterEnd = new(new GameDate(2027, 2, 28));
        }

        [Test]
        public void OptionalPolicyRoundTrip_WithoutSeasonIdentityDoesNotBlockTime()
        {
            var save = new GameSave { CurrentDate = new GameDate(2026, 7, 1),
                Promotion = new PromotionState { Id = Id(), Name = "No season", InitialCash = 1000 } };
            var restored = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            Assert.That(SeasonTransitionService.RequiresSetup(restored), Is.False,
                $"Optional policy after round trip: {JsonUtility.ToJson(restored.SeasonPolicy)}");
            Assert.That(new TimeFlowService().AdvanceTo(restored, restored.CurrentDate.AddDays(1)).State, Is.EqualTo(TimeFlowState.Completed));
        }

        [Test]
        public void LastShow_MustFinishBeforeClosingAndTimeCannotCrossSeasonEnd()
        {
            var save = CreateSave(); save.Shows[0].Status = ShowStatus.Confirmed; save.Schedules[0].Status = ScheduleStatus.Confirmed;
            Assert.That(SeasonTransitionService.RequiresSetup(save), Is.True);
            Assert.That(SeasonTransitionService.CanClose(save), Is.False);
            Assert.Throws<InvalidOperationException>(() => new SeasonTransitionService().GetOrCreateResult(save));
            save.Shows[0].Status = ShowStatus.Completed; save.Schedules[0].Status = ScheduleStatus.Completed;
            Assert.That(SeasonTransitionService.CanClose(save), Is.True);
            var result = new TimeFlowService().AdvanceTo(save, save.CurrentDate.AddDays(1));
            Assert.That(result.State, Is.EqualTo(TimeFlowState.Blocked));
            Assert.That(save.CurrentDate, Is.EqualTo(new GameDate(2027, 5, 31)));
        }

        [Test]
        public void Result_UsesSeasonDateBoundariesAndExcludesVoidsAndOtherPromotions()
        {
            var save = CreateSave();
            AddTransaction(save, new GameDate(2026, 5, 31), -100);
            AddTransaction(save, new GameDate(2026, 6, 1), 1000);
            AddTransaction(save, new GameDate(2027, 5, 31), -300);
            AddTransaction(save, new GameDate(2027, 6, 1), -999);
            AddTransaction(save, save.CurrentDate, 777).IsVoided = true;
            AddTransaction(save, save.CurrentDate, 888).PromotionId = Id();
            var result = new SeasonTransitionService().GetOrCreateResult(save);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Income, Is.EqualTo(1000)); Assert.That(result.Expenses, Is.EqualTo(300));
            Assert.That(result.ClosingCash, Is.EqualTo(100600));
            Assert.That(result.CompletedShows, Is.EqualTo(1)); Assert.That(result.CompletedPpvs, Is.EqualTo(1));
        }

        [Test]
        public void UnconfirmedResult_RefreshesAfterRenewalPaymentsAndStoresIndependentPolicy()
        {
            var save = CreateSave(); var service = new SeasonTransitionService();
            var first = service.GetOrCreateResult(save); Assert.That(first, Is.Not.Null);
            AddTransaction(save, save.CurrentDate, -2000);
            var refreshed = service.GetOrCreateResult(save);
            Assert.That(refreshed.Id, Is.EqualTo(first.Id)); Assert.That(save.SeasonResults, Has.Count.EqualTo(1));
            Assert.That(refreshed.Expenses, Is.EqualTo(2000));
            Assert.That(refreshed.Policy, Is.Not.SameAs(save.SeasonPolicy));
            refreshed.Policy.PpvShowTitles[0].Title = "historical copy";
            Assert.That(save.SeasonPolicy.PpvShowTitles[0].Title, Is.EqualTo("이전 PPV"));
        }

        [Test]
        public void Defaults_CopyNamesDurationsAndPpvTierWithoutMutatingOldPolicy()
        {
            var save = CreateSave(); var settings = SeasonTransitionService.DefaultSettings(save);
            Assert.That(settings.RegularShowName, Is.EqualTo("이전 정기 쇼"));
            Assert.That(settings.RegularDurationMinutes, Is.EqualTo(180));
            Assert.That(settings.PpvFrequency, Is.EqualTo(PpvFrequency.Quarterly));
            var ppv = settings.Ppvs.Single(x => x.Month == 5);
            Assert.That(ppv.Type, Is.EqualTo(ScheduledShowType.PpvSignature));
            Assert.That(ppv.Title, Is.EqualTo("이전 PPV")); ppv.Title = "new";
            Assert.That(save.SeasonPolicy.PpvShowTitles[0].Title, Is.EqualTo("이전 PPV"));
        }

        [Test]
        public void Confirmation_GeneratesNextSeasonOnceAndPreservesPastShowsAndCurrentDate()
        {
            var save = CreateSave(); var oldPolicyId = save.SeasonPolicy.Id; var oldShow = save.Shows[0];
            var request = SeasonTransitionService.DefaultSettings(save);
            request.RegularShowName = "새 정기 쇼";
            var service = new SeasonTransitionService(); var next = service.ConfirmNextSeason(save, request);
            Assert.That(next, Is.Not.Null); Assert.That(next.StartDate, Is.EqualTo(new GameDate(2027, 6, 1)));
            Assert.That(next.EndDate, Is.EqualTo(new GameDate(2028, 5, 31)));
            Assert.That(save.CurrentDate, Is.EqualTo(new GameDate(2027, 5, 31)));
            Assert.That(save.Shows, Does.Contain(oldShow)); Assert.That(save.SeasonResults.Single().Policy.Id, Is.EqualTo(oldPolicyId));
            Assert.That(save.Shows.Where(x => x.Date.CompareTo(next.StartDate) >= 0).All(x => x.Status == ShowStatus.Draft), Is.True);
            Assert.That(save.Shows.First(x => x.ShowType == ScheduledShowType.Regular).Name, Does.Contain("#1"));
            var count = save.Shows.Count;
            Assert.That(service.ConfirmNextSeason(save, request).Id, Is.EqualTo(next.Id));
            Assert.That(save.Shows, Has.Count.EqualTo(count)); Assert.That(SeasonTransitionService.RequiresSetup(save), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InvalidIdsOrSettings_DoNotPartiallyGenerateSeason(bool invalidId)
        {
            var save = CreateSave(); var request = SeasonTransitionService.DefaultSettings(save);
            if (!invalidId) request.RegularDurationMinutes = 61;
            var before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => new SeasonTransitionService(invalidId ? () => null : null).ConfirmNextSeason(save, request));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void AlreadyUnlockedVenue_DoesNotChargeExpansionCostAgain()
        {
            var save = CreateSave(); save.Promotion.UnlockedIds.Add("venue_003");
            var request = SeasonTransitionService.DefaultSettings(save);
            request.VenueId = "venue_003"; request.VenueRequiredPrestige = 1000; request.VenueUnlockCost = 300000;
            var service = new SeasonTransitionService();
            var next = service.ConfirmNextSeason(save, request);
            service.ConfirmNextSeason(save, request);
            Assert.That(save.Transactions, Is.Empty);
            Assert.That(save.Promotion.CalculateCurrentCash(save.Transactions), Is.EqualTo(100000));
            Assert.That(save.VenueContracts.Single(x => x.Id == next.RegularVenueContractId).VenueId, Is.EqualTo("venue_003"));
        }

        [TestCase(999, false)]
        [TestCase(1000, true)]
        public void VenueUnlock_UsesActualPrestigeInsteadOfSeasonTier(long prestige, bool expected)
        {
            var save = CreateSave(); save.Promotion.InitialCash = 1000000;
            PromotionQuarterService.Initialize(save); save.Promotion.PromotionPrestige = prestige;
            save.LastCareerQuarterEnd = new(save.CurrentDate);
            var request = SeasonTransitionService.DefaultSettings(save);
            foreach (var ppv in request.Ppvs) ppv.Type = ScheduledShowType.PpvRegular;
            request.VenueId = "venue_003"; request.VenueRequiredPrestige = 1000; request.VenueUnlockCost = 300000;
            save.Promotion.UnlockedIds.Add(PromotionProgressionRules.TierId(8));
            var before = JsonUtility.ToJson(save);
            if (expected)
                new SeasonTransitionService().ConfirmNextSeason(save, request);
            else
            {
                var error = Assert.Throws<InvalidOperationException>(() => new SeasonTransitionService().ConfirmNextSeason(save, request));
                Assert.That(error.Message, Does.Contain("단체 명성이 부족"));
                Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            }
            Assert.That(save.Promotion.UnlockedIds.Contains("venue_003"), Is.EqualTo(expected));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void VenueUnlock_WithInsufficientCashOrInvalidPreparedId_LeavesSeasonUntouched(bool invalidId)
        {
            var save = CreateSave(); save.Promotion.PromotionPrestige = 1000;
            if (invalidId) save.Promotion.InitialCash = 1000000;
            PromotionQuarterService.Initialize(save);
            var request = SeasonTransitionService.DefaultSettings(save);
            request.VenueId = "venue_003"; request.VenueRequiredPrestige = 1000; request.VenueUnlockCost = 300000;
            var before = JsonUtility.ToJson(save);
            var error = Assert.Throws<InvalidOperationException>(() =>
                new SeasonTransitionService(invalidId ? () => null : null).ConfirmNextSeason(save, request));
            if (!invalidId) Assert.That(error.Message, Does.Contain("자금이 부족"));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void LegacyPastDate_RequiresConsentToCancelMissedShowAndDoesNotRewindTime()
        {
            var save = CreateSave(); save.CurrentDate = new GameDate(2027, 7, 15); save.Shows[0].Status = ShowStatus.Draft; save.Schedules[0].Status = ScheduleStatus.Confirmed;
            var request = SeasonTransitionService.DefaultSettings(save); var service = new SeasonTransitionService();
            new InboxService().Publish(save, $"show-card-required:{save.Schedules[0].Id}", InboxMessageType.Decision, InboxPriority.Required,
                "show", "old decision", "", InboxTargetType.Show, save.Shows[0].Id);
            var before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => service.ConfirmNextSeason(save, request));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            request.CancelMissedShows = true;
            var next = service.ConfirmNextSeason(save, request);
            Assert.That(next, Is.Not.Null); Assert.That(save.CurrentDate, Is.EqualTo(new GameDate(2027, 7, 15)));
            Assert.That(save.Schedules[0].Status, Is.EqualTo(ScheduleStatus.Cancelled));
            Assert.That(save.Shows[0].Status, Is.EqualTo(ShowStatus.Draft));
            Assert.That(save.SeasonResults[0].CancelledShows, Is.EqualTo(1));
            Assert.That(save.InboxMessages[0].Status, Is.EqualTo(InboxMessageStatus.Resolved));
            Assert.That(save.Shows.Skip(1).All(x => x.Date.CompareTo(save.CurrentDate) >= 0), Is.True);
        }

        [Test]
        public void LegacyResult_ReconstructsYearEndDebtEvenIfPaidLater()
        {
            var save = CreateSave(); save.CurrentDate = new GameDate(2027, 7, 15);
            var manager = new ManagerState { Id = Id(), PromotionId = save.Promotion.Id }; save.Managers.Add(manager);
            var contract = new ContractState { Id = Id(), PersonId = manager.Id, Type = ContractType.Manager,
                StartDate = new GameDate(2026, 6, 1), EndDate = new GameDate(2027, 5, 31), Status = ContractStatus.Expired }; save.Contracts.Add(contract);
            var payment = new SalaryPaymentState { Id = Id(), ContractId = contract.Id, DueDate = new GameDate(2027, 5, 1), Amount = 100, RemainingAmount = 0 }; save.SalaryPayments.Add(payment);
            var transaction = AddTransaction(save, new GameDate(2027, 6, 1), -100); transaction.Type = TransactionType.LatePayment; transaction.ReasonId = payment.Id;
            var result = new SeasonTransitionService().GetOrCreateResult(save);
            Assert.That(result, Is.Not.Null); Assert.That(result.OutstandingSalary, Is.EqualTo(100));
            Assert.That(result.ObservedDate, Is.EqualTo(save.CurrentDate)); Assert.That(save.SalaryPayments[0].RemainingAmount, Is.Zero);
        }

        private static TransactionRecord AddTransaction(GameSave save, GameDate date, long amount)
        {
            var transaction = new TransactionRecord { Id = Id(), PromotionId = save.Promotion.Id, Date = date, Amount = amount };
            save.Transactions.Add(transaction); return transaction;
        }

        [Test]
        public void PreparedNextSeason_RefreshesLastDaySpendingBeforeDateAdvances()
        {
            var save = CreateSave(); var service = new SeasonTransitionService();
            service.ConfirmNextSeason(save, SeasonTransitionService.DefaultSettings(save));
            AddTransaction(save, save.CurrentDate, -1500);
            Assert.That(new TimeFlowService().AdvanceTo(save, save.CurrentDate.AddDays(1)).State, Is.EqualTo(TimeFlowState.Completed));
            Assert.That(save.SeasonResults.Single().Expenses, Is.EqualTo(1500));
            Assert.That(save.SeasonResults.Single().ClosingCash, Is.EqualTo(98500));
        }
        private static string Id() => Guid.NewGuid().ToString("D");
        private static GameSave CreateSave()
        {
            var save = GameSave.CreateNew(15, DateTime.UtcNow); save.CurrentDate = new GameDate(2027, 5, 31);
            save.Promotion = new PromotionState { Id = Id(), Name = "Test", Abbreviation = "TEST", InitialCash = 100000 };
            var venue = new VenueContractState { Id = Id(), VenueId = "venue_001", ProductionCost = 100,
                StartDate = new GameDate(2026, 6, 1), EndDate = save.CurrentDate, Status = VenueContractStatus.Reserved }; save.VenueContracts.Add(venue);
            var schedule = new ScheduleState { Id = Id(), Date = save.CurrentDate, Status = ScheduleStatus.Completed, ShowType = ScheduledShowType.PpvSignature }; save.Schedules.Add(schedule);
            save.Shows.Add(new ShowState { Id = Id(), ScheduleId = schedule.Id, Date = schedule.Date, ShowType = schedule.ShowType, Status = ShowStatus.Completed });
            save.SeasonPolicy = new SeasonPolicyState { Id = Id(), StartDate = new GameDate(2026, 6, 1), EndDate = save.CurrentDate,
                RegularShowFrequency = RegularShowFrequency.Monthly, PpvFrequency = PpvFrequency.Quarterly, RegularVenueContractId = venue.Id,
                RegularShowName = "이전 정기 쇼", RegularShowDurationMinutes = 180, SignaturePpvScheduleId = schedule.Id,
                PpvShowTitles = new() { new PpvTitlePolicyState { Month = 5, Title = "이전 PPV", DurationMinutes = 240 } } };
            save.Promotion.SeasonPolicyId = save.SeasonPolicy.Id; return save;
        }
    }
}
