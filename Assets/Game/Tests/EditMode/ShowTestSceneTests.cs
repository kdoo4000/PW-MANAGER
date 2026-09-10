using System.Linq;
using NUnit.Framework;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Domain.Validation;
using UnityEngine;

namespace PWManager.Tests
{
    public sealed class ShowTestSceneTests
    {
        [Test]
        public void RealRoster_CanBookWatchAndSettleOneShow()
        {
            var catalog = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog");
            Assert.That(catalog, Is.Not.Null);
            var save = ShowTestSaveFactory.Create(catalog, 20260903);
            Assert.That(save.Wrestlers, Has.Count.EqualTo(105));
            Assert.That(save.Wrestlers.Select(x => x.Identity.RingName), Does.Contain("Roman Reigns"));
            Assert.That(save.Wrestlers.Select(x => x.Identity.RingName), Does.Contain("Cody Rhodes"));
            Assert.That(save.Wrestlers.Count(x => x.Identity.Gender == WrestlerGender.Male), Is.EqualTo(67));
            Assert.That(save.Wrestlers.Count(x => x.Identity.Gender == WrestlerGender.Female), Is.EqualTo(38));
            Assert.That(save.Wrestlers.Select(x => x.Identity.RingName).Distinct(), Has.Count.EqualTo(105));
            Assert.That(WrestlerOverallCalculator.Match(save.Wrestlers.Single(x => x.Identity.RingName == "Gunther")),
                Is.GreaterThan(WrestlerOverallCalculator.Match(save.Wrestlers.Single(x => x.Identity.RingName == "Akira Tozawa"))));
            Assert.That(save.Wrestlers, Has.All.Matches<WrestlerState>(x =>
                WrestlerOverallCalculator.Match(x) <= x.Growth.MatchPotentialCap + .01f));
            Assert.That(save.Wrestlers, Has.All.Matches<WrestlerState>(x =>
                WrestlerOverallCalculator.Promo(x.Attributes, KayfabeAlignment.Tweener, PromoDisposition.Balanced) <= x.Growth.PromoPotentialCap + .01f));
            Assert.That(save.Shows, Has.Count.EqualTo(1));
            Assert.That(save.Schedules, Has.Count.EqualTo(1));
            Assert.That(GameSaveValidator.Validate(save), Is.Empty);
            var show = save.Shows[0];
            Assert.That(show.Name, Is.EqualTo("WWE Raw vs SmackDown"));
            Assert.That(show.Date, Is.EqualTo(save.CurrentDate));
            Assert.That(show.TimelineEventIds, Is.Empty);
            var content = new StaticContentRegistry(catalog);
            var planning = new ShowPlanningService(content);
            var first = save.Wrestlers[0].Id;
            var second = save.Wrestlers[1].Id;
            planning.AddMatch(save, show.Id, new MatchPlanState
            {
                MatchTypeId = "matchtype_001", MatchGimmickId = "gimmick_000",
                ParticipantIds = { first, second }, WinnerId = first, LoserTargetId = second, FinishType = MatchFinishType.Pinfall
            }, 15);
            show.DurationLimit = show.CalculatePlannedDuration(save.ShowEvents);
            planning.Confirm(save, show.Id);
            show.Status = ShowStatus.InProgress;
            var venue = content.Venues[save.VenueContracts[0].VenueId];
            var result = new ShowExecutionService(planning, new MatchEvaluator(content, content, findMove: content.GetMoveRules), new PromoEvaluator())
                .Execute(save, show.Id, 73, venue.Capacity, venue.BaseTicketPrice);
            Assert.That(save.MatchResults.Single().NarrativeLines, Is.Not.Empty);
            Assert.That(save.MatchResults.Single().SimulationBeats.All(x => x.DurationSeconds > 0), Is.True);
            Assert.That(save.MatchResults.Single().MoveResults, Has.Count.EqualTo(4));
            var restored = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            Assert.That(restored.MatchResults.Single().MoveResults.Select(x => (x.MoveId, x.ActorId, x.TargetId, x.Result)),
                Is.EqualTo(save.MatchResults.Single().MoveResults.Select(x => (x.MoveId, x.ActorId, x.TargetId, x.Result))));
            Assert.That(restored.MatchResults.Single().NarrativeLines.Select(x => x.Text),
                Is.EqualTo(save.MatchResults.Single().NarrativeLines.Select(x => x.Text)));
            show.Status = ShowStatus.ResultsReviewed;
            new ShowResultApplicationService().Apply(save, result.Id);
            Assert.That(show.Status, Is.EqualTo(ShowStatus.Completed));
            Assert.That(GameSaveValidator.Validate(save), Is.Empty);
        }
    }
}
