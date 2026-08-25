using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Domain.Validation;
using UnityEditor;

namespace PWManager.Tests
{
    public sealed class ShowPlanningServiceTests
    {
        private const string CatalogPath = "Assets/Game/Data/Static/GameStaticContentCatalog.asset";

        [Test]
        public void Confirm_ValidMatchAndPromo_ProducesCompleteSnapshot()
        {
            var save = CreateInitialSave();
            var service = Service(10);
            var show = service.CreateDraft(save, save.Schedules[0].Id, "Opening Night", save.VenueContracts[0].Id, 60);
            var matchEvent = service.AddMatch(save, show.Id, Match(save, 0, 1), 40);
            var promoEvent = service.AddPromo(save, show.Id, Promo(save, 2), 20);

            service.Confirm(save, show.Id);

            Assert.That(show.Status, Is.EqualTo(ShowStatus.Confirmed));
            Assert.That(show.ShowVersion, Is.EqualTo(1));
            Assert.That(show.OpeningEventId, Is.EqualTo(matchEvent.Id));
            Assert.That(show.MainEventId, Is.EqualTo(promoEvent.Id));
            Assert.That(show.AttendanceVarianceBasisPoints, Is.InRange(9500, 10500));
            Assert.That(show.EstimatedCost, Is.EqualTo(save.VenueContracts[0].ProductionCost));
            Assert.That(GameSaveValidator.Validate(save), Is.Empty);
        }

        [Test]
        public void AddMatch_WithoutGimmick_AssignsStandardGimmick()
        {
            var save = CreateInitialSave();
            var service = Service();
            var show = service.CreateDraft(save, save.Schedules[0].Id, "Standard Match", save.VenueContracts[0].Id, 60);
            var match = Match(save, 0, 1);

            service.AddMatch(save, show.Id, match, 60);

            Assert.That(match.MatchGimmickId, Is.EqualTo("gimmick_000"));
        }

        [Test]
        public void AddMatch_LegacyTagFields_ExpandsToExplicitSides()
        {
            var save = CreateInitialSave();
            var service = Service();
            var show = service.CreateDraft(save, save.Schedules[0].Id, "Legacy Tag", save.VenueContracts[0].Id, 60);
            var match = Match(save, 0, 1);
            match.MatchTypeId = "matchtype_002";
            match.TeamCount = 2;
            match.MembersPerTeam = 2;
            match.ParticipantIds = save.Wrestlers.Take(4).Select(x => x.Id).ToList();
            match.WinnerId = match.ParticipantIds[0];
            match.LoserTargetId = match.ParticipantIds[2];

            service.AddMatch(save, show.Id, match, 60);

            Assert.That(match.Sides.Count, Is.EqualTo(2));
            Assert.That(match.Sides.All(x => x.MemberIds.Count == 2), Is.True);
            Assert.That(match.WinningSideId, Is.EqualTo(match.Sides[0].Id));
            Assert.That(match.FinishPerformerId, Is.EqualTo(match.ParticipantIds[0]));
        }

        [Test]
        public void AddMatch_DuplicateParticipantAcrossSides_IsRejectedImmediately()
        {
            var save = CreateInitialSave();
            var service = Service();
            var show = service.CreateDraft(save, save.Schedules[0].Id, "Duplicate Booking", save.VenueContracts[0].Id, 60);
            var wrestlerId = save.Wrestlers[0].Id;
            var match = new MatchPlanState
            {
                MatchTypeId = "matchtype_001",
                Sides =
                {
                    new MatchSideState { Id = "side-a", MemberIds = { wrestlerId } },
                    new MatchSideState { Id = "side-b", MemberIds = { wrestlerId } }
                },
                WinningSideId = "side-a",
                FinishPerformerId = wrestlerId,
                LoserTargetId = wrestlerId,
                FinishType = MatchFinishType.Pinfall
            };

            Assert.Throws<ArgumentException>(() => service.AddMatch(save, show.Id, match, 60));
            Assert.That(save.MatchPlans, Is.Empty);
            Assert.That(save.ShowEvents, Is.Empty);
        }

        [Test]
        public void UpdateMatch_ReplacesSelectedMatchAndDuration()
        {
            var save = CreateInitialSave();
            var service = Service();
            var show = service.CreateDraft(save, save.Schedules[0].Id, "Editable Match", save.VenueContracts[0].Id, 60);
            var showEvent = service.AddMatch(save, show.Id, Match(save, 0, 1), 40);
            var replacement = Match(save, 2, 3);
            replacement.MatchGimmickId = "gimmick_004";

            service.UpdateMatch(save, show.Id, showEvent.Id, replacement, 60);

            Assert.That(save.MatchPlans.Single().Id, Is.EqualTo(showEvent.DetailId));
            Assert.That(save.MatchPlans.Single().MatchGimmickId, Is.EqualTo("gimmick_004"));
            Assert.That(save.MatchPlans.Single().ParticipantIds, Is.EqualTo(new[] { save.Wrestlers[2].Id, save.Wrestlers[3].Id }));
            Assert.That(showEvent.PlannedDuration, Is.EqualTo(60));
        }

        [Test]
        public void Validate_ExplicitSides_UsesGimmickCompatibilityAndWinningSide()
        {
            var save = CreateInitialSave();
            var service = Service();
            var show = service.CreateDraft(save, save.Schedules[0].Id, "Cage Tag", save.VenueContracts[0].Id, 60);
            var match = new MatchPlanState
            {
                MatchTypeId = "matchtype_002",
                MatchGimmickId = "gimmick_001",
                FinishType = MatchFinishType.Escape,
                Sides =
                {
                    new MatchSideState { Id = "side-a", MemberIds = { save.Wrestlers[0].Id, save.Wrestlers[1].Id } },
                    new MatchSideState { Id = "side-b", MemberIds = { save.Wrestlers[2].Id, save.Wrestlers[3].Id } }
                },
                WinningSideId = "side-a",
                FinishPerformerId = save.Wrestlers[0].Id,
                LoserTargetId = save.Wrestlers[2].Id
            };
            service.AddMatch(save, show.Id, match, 60);

            var issues = service.Validate(save, show.Id);

            Assert.That(issues.Any(x => x.Code is "match.gimmick" or "match.finish-type" or "match.winner-side"), Is.False);
        }

        [Test]
        public void Validate_FinishPerformerOutsideWinningSide_IsRejected()
        {
            var save = CreateInitialSave();
            var service = Service();
            var show = service.CreateDraft(save, save.Schedules[0].Id, "Invalid Finish", save.VenueContracts[0].Id, 60);
            var match = Match(save, 0, 1);
            service.AddMatch(save, show.Id, match, 60);
            match.FinishPerformerId = save.Wrestlers[1].Id;

            var issues = service.Validate(save, show.Id);

            Assert.That(issues.Any(x => x.Code == "match.finish-performer"), Is.True);
        }

        [Test]
        public void Confirm_DurationDoesNotExactlyMatch_IsRejected()
        {
            var save = CreateInitialSave();
            var service = Service();
            var show = service.CreateDraft(save, save.Schedules[0].Id, "Short Card", save.VenueContracts[0].Id, 60);
            service.AddMatch(save, show.Id, Match(save, 0, 1), 55);

            var exception = Assert.Throws<InvalidOperationException>(() => service.Confirm(save, show.Id));

            Assert.That(exception.Message, Does.Contain("must equal limit"));
            Assert.That(show.Status, Is.EqualTo(ShowStatus.Review));
        }

        [Test]
        public void Validate_UnavailableMatchParticipant_ReturnsBlockingError()
        {
            var save = CreateInitialSave();
            save.Wrestlers[0].Condition.Availability = WrestlerAvailability.MatchUnavailable;
            var service = Service();
            var show = service.CreateDraft(save, save.Schedules[0].Id, "Unavailable Card", save.VenueContracts[0].Id, 60);
            service.AddMatch(save, show.Id, Match(save, 0, 1), 60);

            var issues = service.Validate(save, show.Id);

            Assert.That(issues.Any(x => x.Code == "participant.availability" && x.Severity == ShowValidationSeverity.Error), Is.True);
        }

        [Test]
        public void Validate_TagTeamWithOnlyTwoParticipants_ReturnsBlockingError()
        {
            var save = CreateInitialSave();
            var service = Service();
            var show = service.CreateDraft(save, save.Schedules[0].Id, "Invalid Tag Match", save.VenueContracts[0].Id, 60);
            var match = Match(save, 0, 1);
            match.MatchTypeId = "matchtype_002";
            service.AddMatch(save, show.Id, match, 60);

            var issues = service.Validate(save, show.Id);

            Assert.That(issues.Any(x => x.Code == "match.participants" && x.Message.Contains("4 to 8")), Is.True);
        }

        [TestCase(2, 2)]
        [TestCase(3, 2)]
        [TestCase(2, 3)]
        [TestCase(2, 4)]
        [TestCase(4, 2)]
        public void Validate_SupportedTagComposition_IsAccepted(int membersPerTeam, int teamCount)
        {
            var save = CreateInitialSave();
            var service = Service();
            var show = service.CreateDraft(save, save.Schedules[0].Id, "Tag Card", save.VenueContracts[0].Id, 60);
            var match = Match(save, 0, 1);
            match.MatchTypeId = "matchtype_002";
            match.MembersPerTeam = membersPerTeam;
            match.TeamCount = teamCount;
            match.ParticipantIds = save.Wrestlers.Take(membersPerTeam * teamCount).Select(x => x.Id).ToList();
            match.WinnerId = match.ParticipantIds[0];
            match.LoserTargetId = match.ParticipantIds[1];
            service.AddMatch(save, show.Id, match, 60);

            var issues = service.Validate(save, show.Id);

            Assert.That(issues.Any(x => x.Code is "match.participants" or "match.teams"), Is.False);
        }

        [TestCase(MatchFinishType.Disqualification)]
        [TestCase(MatchFinishType.CountOut)]
        public void Validate_IndividualThreeWay_RejectsDqAndCountOut(MatchFinishType finishType)
        {
            var save = CreateInitialSave();
            var service = Service();
            var show = service.CreateDraft(save, save.Schedules[0].Id, "Three Way", save.VenueContracts[0].Id, 60);
            var match = Match(save, 0, 1);
            match.ParticipantIds.Add(save.Wrestlers[2].Id);
            match.FinishType = finishType;
            service.AddMatch(save, show.Id, match, 60);

            Assert.That(service.Validate(save, show.Id).Any(x => x.Code == "match.finish-type"), Is.True);
        }

        [TestCase(MatchFinishType.Disqualification)]
        [TestCase(MatchFinishType.CountOut)]
        public void Validate_ThreeWayTag_RejectsDqAndCountOut(MatchFinishType finishType)
        {
            var save = CreateInitialSave();
            var service = Service();
            var show = service.CreateDraft(save, save.Schedules[0].Id, "Three Way Tag", save.VenueContracts[0].Id, 60);
            var match = Match(save, 0, 1);
            match.MatchTypeId = "matchtype_002";
            match.MembersPerTeam = 2;
            match.TeamCount = 3;
            match.ParticipantIds = save.Wrestlers.Take(6).Select(x => x.Id).ToList();
            match.FinishType = finishType;
            service.AddMatch(save, show.Id, match, 60);

            Assert.That(service.Validate(save, show.Id).Any(x => x.Code == "match.finish-type"), Is.True);
        }

        [TestCase(MatchFinishType.Disqualification)]
        [TestCase(MatchFinishType.CountOut)]
        public void Validate_ThreeOnThreeTag_AllowsDqAndCountOut(MatchFinishType finishType)
        {
            var save = CreateInitialSave();
            var service = Service();
            var show = service.CreateDraft(save, save.Schedules[0].Id, "Three On Three", save.VenueContracts[0].Id, 60);
            var match = Match(save, 0, 1);
            match.MatchTypeId = "matchtype_002";
            match.MembersPerTeam = 3;
            match.TeamCount = 2;
            match.ParticipantIds = save.Wrestlers.Take(6).Select(x => x.Id).ToList();
            match.FinishType = finishType;
            service.AddMatch(save, show.Id, match, 60);

            Assert.That(service.Validate(save, show.Id).Any(x => x.Code == "match.finish-type"), Is.False);
        }

        [Test]
        public void Validate_TagTeamWithFiveParticipants_ReturnsUnevenTeamsError()
        {
            var save = CreateInitialSave();
            var service = Service();
            var show = service.CreateDraft(save, save.Schedules[0].Id, "Uneven Tag Match", save.VenueContracts[0].Id, 60);
            var match = Match(save, 0, 1);
            match.MatchTypeId = "matchtype_002";
            match.ParticipantIds = save.Wrestlers.Take(5).Select(x => x.Id).ToList();
            match.WinnerId = match.ParticipantIds[0];
            match.LoserTargetId = match.ParticipantIds[1];
            service.AddMatch(save, show.Id, match, 60);

            var issues = service.Validate(save, show.Id);

            Assert.That(issues.Any(x => x.Code == "match.teams"), Is.True);
        }

        [Test]
        public void Validate_IndividualWithSevenParticipants_ReturnsBlockingError()
        {
            var save = CreateInitialSave();
            var service = Service();
            var show = service.CreateDraft(save, save.Schedules[0].Id, "Seven Way", save.VenueContracts[0].Id, 60);
            var match = Match(save, 0, 1);
            match.MatchTypeId = "matchtype_001";
            match.ParticipantIds = save.Wrestlers.Take(7).Select(x => x.Id).ToList();
            match.WinnerId = match.ParticipantIds[0];
            match.LoserTargetId = match.ParticipantIds[1];
            service.AddMatch(save, show.Id, match, 60);

            var issues = service.Validate(save, show.Id);

            Assert.That(issues.Any(x => x.Code == "match.participants" && x.Message.Contains("2 to 6")), Is.True);
        }

        [Test]
        public void MoveEvent_ConfirmedShow_ReturnsToPreparingAndUpdatesCardPositions()
        {
            var save = CreateInitialSave();
            var service = Service(11);
            var show = service.CreateDraft(save, save.Schedules[0].Id, "Reordered Card", save.VenueContracts[0].Id, 60);
            var first = service.AddMatch(save, show.Id, Match(save, 0, 1), 40);
            var second = service.AddPromo(save, show.Id, Promo(save, 2), 20);
            service.Confirm(save, show.Id);
            var variance = show.AttendanceVarianceBasisPoints;

            service.MoveEvent(save, show.Id, 1, 0);

            Assert.That(show.Status, Is.EqualTo(ShowStatus.Preparing));
            Assert.That(show.OpeningEventId, Is.EqualTo(second.Id));
            Assert.That(show.MainEventId, Is.EqualTo(first.Id));
            service.Confirm(save, show.Id);
            Assert.That(show.ShowVersion, Is.EqualTo(2));
            Assert.That(show.AttendanceVarianceBasisPoints, Is.EqualTo(variance));
        }

        [Test]
        public void RemoveEvent_RemovesTimelineEventAndOwnedDetail()
        {
            var save = CreateInitialSave();
            var service = Service();
            var show = service.CreateDraft(save, save.Schedules[0].Id, "Editable Card", save.VenueContracts[0].Id, 60);
            var matchEvent = service.AddMatch(save, show.Id, Match(save, 0, 1), 40);
            var promoEvent = service.AddPromo(save, show.Id, Promo(save, 2), 20);

            service.RemoveEvent(save, show.Id, matchEvent.Id);

            Assert.That(show.TimelineEventIds, Is.EqualTo(new[] { promoEvent.Id }));
            Assert.That(save.ShowEvents.Any(x => x.Id == matchEvent.Id), Is.False);
            Assert.That(save.MatchPlans, Is.Empty);
            Assert.That(show.OpeningEventId, Is.EqualTo(promoEvent.Id));
            Assert.That(show.MainEventId, Is.EqualTo(promoEvent.Id));
        }

        private static MatchPlanState Match(GameSave save, int first, int second) => new()
        {
            MatchTypeId = "matchtype_001",
            ParticipantIds = { save.Wrestlers[first].Id, save.Wrestlers[second].Id },
            WinnerId = save.Wrestlers[first].Id,
            LoserTargetId = save.Wrestlers[second].Id,
            FinishType = MatchFinishType.Pinfall
        };

        private static PromoPlanState Promo(GameSave save, int participant) => new()
        {
            Purpose = PromoPurpose.CharacterIntroduction,
            Presentation = PromoPresentation.InRingMic,
            ParticipantIds = { save.Wrestlers[participant].Id }
        };

        private static GameSave CreateInitialSave()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath);
            var candidates = new WrestlerGenerator(new StaticContentRegistry(catalog), 4567)
                .GenerateInitialCandidates(new GameDate(2026, 6, 1));
            return new GameStartService().CreateInitialSave(new GameStartRequest
            {
                PromotionName = "Show Test", InitialCash = 100000, WorldSeed = 4567,
                UtcNow = new DateTime(2026, 8, 20, 1, 0, 0, DateTimeKind.Utc),
                WrestlerContracts = candidates.Take(8).Select(x => new InitialWrestlerContractInput
                {
                    Wrestler = x, ContractEndYear = 2027, MonthlySalary = 500
                }).ToList(),
                RegularVenueId = "venue_001", RegularVenueProductionCost = 500,
                RegularShowFrequency = RegularShowFrequency.Monthly,
                PpvFrequency = PpvFrequency.EveryFourMonths
            });
        }

        private static ShowPlanningService Service(int? seed = null)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath);
            return new ShowPlanningService(new StaticContentRegistry(catalog), randomSeed: seed);
        }
    }
}
