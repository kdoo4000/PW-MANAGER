using System;
using System.Collections.Generic;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Data.Catalogs;
using PWManager.Data.Loading;
using UnityEngine;

namespace PWManager.Tests
{
    public sealed class OperatingEffectsTests
    {
        [Test]
        public void EmptyFanGroup_CarriesSmallPositiveAcquisitionsUntilOnePersonIsReached()
        {
            var save = Save(0);
            for (var week = 0; week < 3; week++)
            {
                save.Promotion.Audience.Mark.PendingAcquisition = .4;
                save.CurrentDate = save.CurrentDate.AddDays(7); FanAudienceService.AdvanceWeek(save, save.CurrentDate);
            }
            Assert.That(save.Promotion.Audience.Mark.Followers, Is.EqualTo(1));
        }

        [TestCase(0)]
        [TestCase(100)]
        [TestCase(10000)]
        public void NewFans_ArePeopleReachedAndDoNotRequireExistingFans(long followers)
        {
            var save = Save(followers); var result = Result(80);
            FanAudienceService.Apply(save, save.Shows[0], result);
            var pending = save.Promotion.Audience.Mark.PendingAcquisition + save.Promotion.Audience.Casual.PendingAcquisition + save.Promotion.Audience.Hardcore.PendingAcquisition;
            Assert.That(pending, Is.EqualTo(96).Within(.001));
            save.CurrentDate = save.CurrentDate.AddDays(7); FanAudienceService.AdvanceWeek(save, save.CurrentDate);
            Assert.That(save.Promotion.Audience.TotalFollowers, Is.GreaterThan(followers));
        }

        [Test]
        public void FanAcquisition_PendingPeopleSurviveReloadAndCannotBeCountedTwice()
        {
            var save = Save(0); FanAudienceService.Apply(save, save.Shows[0], Result(80));
            save = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            save.CurrentDate = save.CurrentDate.AddDays(7); FanAudienceService.AdvanceWeek(save, save.CurrentDate);
            var count = save.Promotion.Audience.TotalFollowers;
            Assert.That(count, Is.EqualTo(95));
            FanAudienceService.AdvanceWeek(save, save.CurrentDate);
            Assert.That(save.Promotion.Audience.TotalFollowers, Is.EqualTo(count));
            Assert.That(save.Promotion.Audience.Mark.PendingAcquisition, Is.Zero);
        }

        [Test]
        public void PromotionTeam_IncreasesDemandAndAcquisitionFromActualCatalogValues()
        {
            var catalog = new StaticContentRegistry(Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog"));
            var save = Save(2500); var show = save.Shows[0];
            var demand = FanAudienceService.BaseDemand(save, show, 30);
            FanAudienceService.Apply(save, show, Result(80)); var fans = save.Promotion.Audience.Mark.PendingAcquisition;
            var upgraded = Save(2500); upgraded.StaffDepartments.Add(new StaffDepartmentState { DepartmentType = StaffDepartmentType.Promotion, CurrentLevel = 5 });
            Assert.That(FanAudienceService.BaseDemand(upgraded, upgraded.Shows[0], 30), Is.GreaterThan(demand));
            FanAudienceService.Apply(upgraded, upgraded.Shows[0], Result(80));
            Assert.That(upgraded.Promotion.Audience.Mark.PendingAcquisition, Is.EqualTo(fans * catalog.PromotionTeamLevels["promotionlevel_005"].PositiveFanGainMultiplier).Within(.001));
        }

        [Test]
        public void MedicalTeam_ShortensNewRealInjuryButDoesNotChangeKayfabeAbsence()
        {
            var save = Save(2500); save.StaffDepartments.Add(new StaffDepartmentState { DepartmentType = StaffDepartmentType.Medical, CurrentLevel = 5 });
            var result = new MatchResultState { Id = "result", ParticipantProfiles = { new MatchParticipantProfileState { MemberIds = { "a" } } },
                InjuryChanges = { new MatchInjuryChangeState { SourceResultId = "result", WrestlerId = "a", Severity = InjuryStatus.Moderate, RecoveryDays = 21 } } };
            MatchHealth.ApplyResult(save, result, save.CurrentDate);
            Assert.That(save.Wrestlers[0].Condition.RecoveryDate.Value, Is.EqualTo(save.CurrentDate.AddDays(17)));
            result.InjuryChanges[0].IsKayfabe = true; MatchHealth.ApplyResult(save, result, save.CurrentDate);
            Assert.That(save.Wrestlers[0].KayfabeInjury.ReturnDate.Value, Is.EqualTo(save.CurrentDate.AddDays(21)));
        }

        [Test]
        public void CommentaryTeam_ImprovesLiveReactionWithoutChangingTechnicalQuality()
        {
            var save = Save(2500); var show = save.Shows[0];
            var match = new MatchResultState { ShowEventId = "event", FinalMatchQuality = 12 };
            var first = new ShowResultState(); FanAudienceService.Evaluate(save, show, first, new[] { match }, Array.Empty<PromoResultState>());
            save.StaffDepartments.Add(new StaffDepartmentState { DepartmentType = StaffDepartmentType.Commentary, CurrentLevel = 1 });
            var second = new ShowResultState(); FanAudienceService.Evaluate(save, show, second, new[] { match }, Array.Empty<PromoResultState>());
            Assert.That(second.FanSatisfaction.Light, Is.GreaterThan(first.FanSatisfaction.Light));
            Assert.That(match.FinalMatchQuality, Is.EqualTo(12));
        }

        static ShowResultState Result(float score) => new() { FanExposure = 1, FanSatisfaction = new FanReactionResultState { IsCalculated = true, Family = score, Light = score, Mania = score }, FinancialSettlement = new FinancialSettlementState { Attendance = 100 } };
        static GameSave Save(long followers)
        {
            var save = new GameSave { CurrentDate = new GameDate(2026, 6, 1), Promotion = new PromotionState { Audience = FanAudienceService.CreateInitial(new GameDate(2026, 6, 1), followers) } };
            save.Wrestlers.Add(new WrestlerState { Id = "a" });
            save.Shows.Add(new ShowState { Id = "show", Date = save.CurrentDate, TimelineEventIds = { "event" } });
            save.ShowEvents.Add(new ShowEventState { Id = "event", EventType = ShowEventType.Match, DetailId = "plan", PlannedDuration = 120 });
            save.MatchPlans.Add(new MatchPlanState { Id = "plan", ParticipantIds = { "a" } });
            return save;
        }
    }
}
