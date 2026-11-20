using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Domain.Validation;
using PWManager.Infrastructure.Save;
using PWManager.Presentation;
using UnityEngine;

namespace PWManager.Tests
{
    public sealed class SponsorMissionTests
    {
        public static GameSave CreateSave()
        {
            var catalog = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog");
            var roster = new WrestlerGenerator(new StaticContentRegistry(catalog), 84).GenerateInitialCandidates(new GameDate(2026, 6, 1));
            return new GameStartService().CreateInitialSave(new GameStartRequest
            {
                PromotionName = "Sponsor Test", InitialCash = 100000, WorldSeed = 84, UtcNow = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                WrestlerContracts = roster.Take(8).Select(w => new InitialWrestlerContractInput { Wrestler = w, ContractEndYear = 2027, SigningBonus = 1000, MonthlySalary = 500 }).ToList(),
                RegularVenueId = "venue_001", RegularVenueProductionCost = 500, RegularShowFrequency = RegularShowFrequency.Monthly, PpvFrequency = PpvFrequency.EveryFourMonths
            });
        }

        [Test]
        public void Offers_UseThirtyCompaniesTenDistinctOffersAndFixedTargets()
        {
            var companies = SponsorSelectionView.Companies(); Assert.That(companies, Has.Count.EqualTo(30));
            foreach (var company in companies) Assert.That(Resources.Load<Texture2D>(company.LogoResource), Is.Not.Null, company.Name);
            var save = CreateSave(); var service = new SponsorMissionService(); var season = service.PrepareOffers(save, companies);
            Assert.That(season.Offers, Has.Count.EqualTo(10)); Assert.That(season.Offers.Select(x => x.CompanyId).Distinct().Count(), Is.EqualTo(10));
            Assert.That(season.Offers.All(x => x.Goals.Count == x.Difficulty && x.Goals[0].Target > x.Goals[0].Progress), Is.True);
            Assert.That(season.Offers.Select(x => x.BasePayment).Distinct().Count(), Is.GreaterThan(1));
            Assert.That(season.Offers.First(x => x.Difficulty == 3).CompletionPayment, Is.GreaterThan(season.Offers.First(x => x.Difficulty == 2).CompletionPayment));
            var snapshot = JsonUtility.ToJson(season); save.Promotion.PromotionPrestige += 100;
            Assert.That(service.PrepareOffers(save, companies), Is.SameAs(season)); Assert.That(JsonUtility.ToJson(season), Is.EqualTo(snapshot));
        }

        [Test]
        public void Selection_RequiresThreeAndPaysOnce_PreservesSaveAndRejectsAlteration()
        {
            var save = CreateSave(); var service = new SponsorMissionService(); var season = service.PrepareOffers(save, SponsorSelectionView.Companies());
            var before = JsonUtility.ToJson(save);
            foreach (var ids in new[] { new[] { season.Offers[0].Id }, new[] { season.Offers[0].Id, season.Offers[0].Id, season.Offers[1].Id }, new[] { "missing", season.Offers[0].Id, season.Offers[1].Id } })
                Assert.Throws<InvalidOperationException>(() => service.ConfirmSelection(save, season, ids));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            var chosen = season.Offers.Take(3).Select(x => x.Id).ToArray(); var cash = save.Promotion.CalculateCurrentCash(save.Transactions);
            service.ConfirmSelection(save, season, chosen); service.ConfirmSelection(save, season, chosen);
            Assert.That(save.Promotion.CalculateCurrentCash(save.Transactions), Is.EqualTo(cash + season.Offers.Take(3).Sum(x => x.BasePayment)));
            Assert.Throws<InvalidOperationException>(() => service.ConfirmSelection(save, season, season.Offers.Skip(1).Take(3).Select(x => x.Id)));
            Assert.That(GameSaveValidator.Validate(save), Is.Empty);
            var directory = Path.Combine(Path.GetTempPath(), "sponsor-save-" + Guid.NewGuid().ToString("N"));
            try
            {
                var storage = new SaveService(directory); storage.Save("sponsor", save); var restored = storage.Load("sponsor");
                Assert.That(JsonUtility.ToJson(restored.SponsorSeasons.Single()), Is.EqualTo(JsonUtility.ToJson(season)));
                service.ConfirmSelection(restored, restored.SponsorSeasons.Single(), chosen);
                Assert.That(restored.Transactions.Count, Is.EqualTo(save.Transactions.Count));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [TestCase(0)]
        [TestCase(2503)]
        [TestCase(1000000)]
        public void RandomPayments_StayInsideBandsAndKeepHarderTotalRewardsHigher(long prestige)
        {
            foreach (var seed in new[] { 7, 84, 215, 999 })
            {
                var save = CreateSave(); save.WorldSeed = seed; save.Promotion.PromotionPrestige = prestige;
                var reference = 5000L + Math.Min(1000000L, prestige * 2);
                var season = new SponsorMissionService().PrepareOffers(save, SponsorSelectionView.Companies());
                foreach (var offer in season.Offers)
                {
                    Assert.That(offer.BasePayment, Is.InRange(reference * .95, reference * 1.05));
                    var reward = reference * (offer.Difficulty == 1 ? 2 : offer.Difficulty == 2 ? 4 : 7);
                    Assert.That(offer.CompletionPayment, Is.InRange(reward * .90, reward * 1.10));
                    Assert.That(offer.BasePayment % 10, Is.Zero); Assert.That(offer.CompletionPayment % 10, Is.Zero);
                }
                Assert.That(season.Offers.Any(x => x.BasePayment % 100 != 0 || x.CompletionPayment % 100 != 0), Is.True);
                for (var difficulty = 1; difficulty < 3; difficulty++)
                    Assert.That(season.Offers.Where(x => x.Difficulty == difficulty + 1).Min(x => x.BasePayment + x.CompletionPayment),
                        Is.GreaterThan(season.Offers.Where(x => x.Difficulty == difficulty).Max(x => x.BasePayment + x.CompletionPayment)));
                var restored = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
                Assert.That(JsonUtility.ToJson(new SponsorMissionService().PrepareOffers(restored, SponsorSelectionView.Companies())), Is.EqualTo(JsonUtility.ToJson(season)));
            }
        }

        [Test]
        public void SharedGrowth_PaysEverySelectedSponsorWithoutDuplicateReward()
        {
            var save = CreateSave(); var service = new SponsorMissionService(); var season = service.PrepareOffers(save, SponsorSelectionView.Companies());
            var easy = season.Offers.Where(x => x.Difficulty == 1).Take(3).ToArray();
            foreach (var offer in easy) { offer.Goals[0].Type = SponsorGoalType.Prestige; offer.Goals[0].Progress = 0; offer.Goals[0].Target = 100; }
            service.ConfirmSelection(save, season, easy.Select(x => x.Id));
            save.Promotion.PromotionPrestige = 99; service.ProcessDay(save); Assert.That(easy.Any(x => x.RewardPaid), Is.False);
            save.Promotion.PromotionPrestige = 100; service.ProcessDay(save); var count = save.Transactions.Count;
            service.ProcessDay(save); Assert.That(easy.All(x => x.RewardPaid), Is.True); Assert.That(save.Transactions.Count, Is.EqualTo(count));
        }

        [Test]
        public void ActualShows_CountOncePerShow_AdvertisementsBelongToChosenCompany()
        {
            var save = CreateSave(); var service = new SponsorMissionService(); var season = service.PrepareOffers(save, SponsorSelectionView.Companies());
            var selected = season.Offers.Where(x => x.Difficulty == 2).Take(3).ToArray();
            foreach (var offer in selected) { offer.Goals[0].Type = SponsorGoalType.Prestige; offer.Goals[0].Progress = 0; offer.Goals[0].Target = 1; offer.Goals[1] = new SponsorGoalState { Type = SponsorGoalType.Advertisement, Target = 2 }; }
            service.ConfirmSelection(save, season, selected.Select(x => x.Id)); save.Promotion.PromotionPrestige = 1;
            var show = save.Shows[0]; save.CurrentDate = show.Date;
            var plan = new PromoPlanState { Id = Guid.NewGuid().ToString(), Purpose = PromoPurpose.SponsorAdvertisement, SponsorRequirementId = selected[0].Id }; save.PromoPlans.Add(plan);
            var promo = new PromoResultState { Id = Guid.NewGuid().ToString(), PromoPlanId = plan.Id }; save.PromoResults.Add(promo);
            var result = new ShowResultState { Id = Guid.NewGuid().ToString(), ShowId = show.Id, ShowVersion = show.ShowVersion }; result.PromoResultIds.Add(promo.Id);
            service.ProcessShow(save, result); Assert.That(selected[0].Goals[1].Progress, Is.Zero);
            show.Status = ShowStatus.Completed; service.ProcessShow(save, result); service.ProcessShow(save, result);
            Assert.That(selected[0].Goals[1].Progress, Is.EqualTo(1)); Assert.That(selected[1].Goals[1].Progress, Is.Zero);
            Assert.That(selected[0].RewardPaid, Is.False);
            var nextShow = save.Shows[1]; nextShow.Status = ShowStatus.Completed; save.CurrentDate = nextShow.Date; result.ShowId = nextShow.Id; result.ShowVersion = nextShow.ShowVersion;
            service.ProcessShow(save, result); Assert.That(selected[0].RewardPaid, Is.True);
        }

        [Test]
        public void Deadline_AllowsLastDayThenFailsWithoutTakingBackBasePayment()
        {
            var save = CreateSave(); var service = new SponsorMissionService(); var season = service.PrepareOffers(save, SponsorSelectionView.Companies());
            var chosen = season.Offers.Where(x => x.Difficulty == 1).Take(3).ToArray();
            foreach (var offer in chosen) { offer.Goals[0].Type = SponsorGoalType.Prestige; offer.Goals[0].Progress = 0; offer.Goals[0].Target = 100; }
            service.ConfirmSelection(save, season, chosen.Select(x => x.Id)); var baseCash = save.Promotion.CalculateCurrentCash(save.Transactions);
            save.CurrentDate = season.EndDate; service.ProcessDay(save); Assert.That(chosen.Any(x => x.Failed), Is.False);
            chosen[0].Goals[0].Target = 1; save.Promotion.PromotionPrestige = 1; service.ProcessDay(save); Assert.That(chosen[0].RewardPaid, Is.True);
            save.CurrentDate = season.EndDate.AddDays(1); save.Promotion.PromotionPrestige = 100; service.ProcessDay(save);
            Assert.That(chosen.Skip(1).All(x => x.Failed && !x.RewardPaid), Is.True);
            Assert.That(save.Promotion.CalculateCurrentCash(save.Transactions), Is.EqualTo(baseCash + chosen[0].CompletionPayment));
        }

        [Test]
        public void StyleAndGiantGoals_UseActualMatchSnapshotsAndTitleVictory()
        {
            var save = CreateSave(); var service = new SponsorMissionService(); var season = service.PrepareOffers(save, SponsorSelectionView.Companies());
            var offer = season.Offers.First(x => x.Difficulty == 3);
            offer.Goals[0] = new SponsorGoalState { Type = SponsorGoalType.Prestige, Target = 1 };
            offer.Goals[1] = new SponsorGoalState { Type = SponsorGoalType.GiantMatch, Target = 2 };
            offer.Goals[2] = new SponsorGoalState { Type = SponsorGoalType.StyleChampion, StyleId = "style_005", Target = 1 };
            service.ConfirmSelection(save, season, new[] { offer.Id }.Concat(season.Offers.Where(x => x.Difficulty == 1).Take(2).Select(x => x.Id)));
            save.Promotion.PromotionPrestige = 1;
            var giant = new MatchResultState { Id = Guid.NewGuid().ToString() };
            foreach (var w in save.Wrestlers.Take(2))
            {
                giant.ParticipantProfiles.Add(new MatchParticipantProfileState { SideId = w.Id, MemberIds = new() { w.Id } });
                giant.WrestlerPerformances.Add(new WrestlerMatchPerformanceState { WrestlerId = w.Id, WrestlingStyleId = "style_006" });
                w.Presentation.WrestlingStyleId = "style_007";
            }
            var champion = save.Wrestlers[2]; var titleMatch = new MatchResultState { Id = Guid.NewGuid().ToString() };
            titleMatch.WrestlerPerformances.Add(new WrestlerMatchPerformanceState { WrestlerId = champion.Id, WrestlingStyleId = "style_005" });
            save.MatchResults.AddRange(new[] { giant, titleMatch });
            var show = save.Shows[0]; show.Status = ShowStatus.Completed; save.CurrentDate = show.Date;
            save.Championships.Add(new ChampionshipState { Reigns = new() { new ChampionshipReignState { SourceResultId = titleMatch.Id, MemberIds = new() { champion.Id }, WonDate = show.Date } } });
            var result = new ShowResultState { ShowId = show.Id, ShowVersion = show.ShowVersion, MatchResultIds = new() { giant.Id, titleMatch.Id } };
            service.ProcessShow(save, result);
            Assert.That(offer.Goals[1].Progress, Is.EqualTo(1)); Assert.That(offer.Goals[2].Progress, Is.EqualTo(1)); Assert.That(offer.RewardPaid, Is.False);
            show = save.Shows[1]; show.Status = ShowStatus.Completed; save.CurrentDate = show.Date;
            result.ShowId = show.Id; result.ShowVersion = show.ShowVersion; service.ProcessShow(save, result);
            Assert.That(offer.RewardPaid, Is.True);
        }

        [Test]
        public void InjuredNamedWrestler_FailsUnfinishedMissionWithoutReplacement()
        {
            var save = CreateSave(); var service = new SponsorMissionService(); var season = service.PrepareOffers(save, SponsorSelectionView.Companies());
            var chosen = season.Offers.Take(3).ToArray(); var offer = chosen[1];
            offer.Goals[1] = new SponsorGoalState { Type = SponsorGoalType.WrestlerAppearance, WrestlerId = save.Wrestlers[0].Id, Target = 3 };
            service.ConfirmSelection(save, season, chosen.Select(x => x.Id)); save.Wrestlers[0].Condition.InjuryStatus = InjuryStatus.Severe;
            service.ProcessDay(save); Assert.That(offer.Failed, Is.True); Assert.That(offer.RewardPaid, Is.False);
        }

        [Test]
        public void Migration_DoesNotAwardSponsorsRetroactively()
        {
            var save = CreateSave(); var cash = save.Promotion.CalculateCurrentCash(save.Transactions); save.SaveVersion = 17; save.SponsorSeasons = null;
            new SaveVersion17To18Migration().Migrate(save);
            Assert.That(save.SponsorSeasons, Is.Empty); Assert.That(save.Promotion.CalculateCurrentCash(save.Transactions), Is.EqualTo(cash));
        }
    }
}
