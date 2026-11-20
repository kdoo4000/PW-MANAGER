using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using PWManager.Data.Catalogs;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Tests
{
    public sealed class SponsorSelectionViewTests
    {
        [Test]
        public void AdvertisementPicker_UsesSelectedCompanyAndOnlyAppearsForAdvertisement()
        {
            var save = SponsorMissionTests.CreateSave(); var service = new SponsorMissionService(); var season = service.PrepareOffers(save, SponsorSelectionView.Companies());
            service.ConfirmSelection(save, season, season.Offers.Take(3).Select(x => x.Id));
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            var window = ScriptableObject.CreateInstance<UnityEditor.EditorWindow>(); window.ShowUtility();
            try
            {
                DashboardSession.SetActiveSave(save, true);
                var root = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard").CloneTree(); DashboardViewHost.MountMissingViews(root);
                window.rootVisualElement.Add(root);
                var type = typeof(DashboardController).Assembly.GetType("PWManager.Presentation.ShowPlanningController");
                var controller = Activator.CreateInstance(type, new object[] { root }); type.GetMethod("Bind").Invoke(controller, new object[] { save, false });
                var open = type.GetMethod("OpenShowEditor", BindingFlags.NonPublic | BindingFlags.Instance);
                open.Invoke(controller, new[] { Enum.Parse(open.GetParameters()[0].ParameterType, "Promo") });
                var purpose = root.Q<DropdownField>("show-editor-primary"); var field = root.Q<DropdownField>("show-editor-sponsor");
                Assert.That(field, Is.Not.Null); Assert.That(field.style.display.value, Is.EqualTo(DisplayStyle.None));
                purpose.index = (int)PromoPurpose.SponsorAdvertisement; Assert.That(field.style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(field.choices, Has.Count.EqualTo(3)); field.index = 1;
                Assert.That(type.GetMethod("SelectedSponsorRequirement", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, null), Is.EqualTo(season.Offers[1].Id));
                purpose.index = (int)PromoPurpose.CharacterIntroduction; Assert.That(field.style.display.value, Is.EqualTo(DisplayStyle.None));
            }
            finally { window.Close(); DashboardSession.SetActiveSave(previous, transient); }
        }

        [Test]
        public void NewGameLastStep_PreparesRealSavePreservesSelectionAndInvalidatesChangedSettings()
        {
            var go = new GameObject("Sponsor new game test"); go.SetActive(false);
            var controller = go.AddComponent<TitleScreenController>();
            var root = Resources.Load<VisualTreeAsset>("PWManagerUI/NewGameScreen").CloneTree();
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            void Set(string name, object value) => typeof(TitleScreenController).GetField(name, flags).SetValue(controller, value);
            object Get(string name) => typeof(TitleScreenController).GetField(name, flags).GetValue(controller);
            void Render() => typeof(TitleScreenController).GetMethod("RenderSponsorSelection", flags).Invoke(controller, null);
            try
            {
                Set("newGameRoot", root.Q("new-game-screen")); Set("root", root); Set("wizardMessage", root.Q<Label>("wizard-message")); Set("nextButton", root.Q<Button>("wizard-next")); Set("seed", 84);
                foreach (var pair in new[] { ("playerName", "player-name-input"), ("promotionName", "promotion-name-input"), ("promotionAbbreviation", "promotion-abbreviation-input"), ("playerBirthDate", "player-birth-date") }) Set(pair.Item1, root.Q<TextField>(pair.Item2));
                root.Q<TextField>("player-name-input").value = "Test Player"; root.Q<TextField>("promotion-name-input").value = "Test Sponsor";
                root.Q<TextField>("promotion-abbreviation-input").value = "TSP"; root.Q<TextField>("player-birth-date").value = "1994-06-01";
                foreach (var pair in new[] { ("playerRole", "player-role"), ("playerGender", "player-gender"), ("playerReputation", "player-reputation"), ("playerWrestlingType", "player-wrestling-type"), ("playerWrestlingStyle", "player-wrestling-style"), ("regularFrequency", "regular-frequency"), ("ppvFrequency", "ppv-frequency") })
                { var field = root.Q<DropdownField>(pair.Item2); field.choices = new List<string> { "0", "1" }; field.index = pair.Item1 == "playerRole" ? 1 : 0; Set(pair.Item1, field); }
                var height = root.Q<IntegerField>("player-height"); height.value = 180; Set("playerHeight", height);
                var weight = root.Q<IntegerField>("player-weight"); weight.value = 90; Set("playerWeight", weight);
                var catalog = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog"); Set("selectedVenue", catalog.Venues.First(x => x.Id == "venue_001"));
                var roster = SponsorMissionTests.CreateSave().Wrestlers; Set("candidates", roster); Set("selectedWrestlerIds", roster.Select(x => x.Id).ToHashSet());
                Render(); Assert.That(Get("sponsorPreview"), Is.Not.Null, root.Q<Label>("wizard-message").text);
                Assert.That(root.Q<Button>("wizard-next").enabledSelf, Is.False);
                foreach (var row in root.Q("sponsor-offers").Query<Button>().ToList().Take(3)) { Click(row); }
                var first = (GameSave)Get("sponsorPreview"); var offers = JsonUtility.ToJson(first.SponsorSeasons.Single());
                Assert.That(root.Q<Button>("wizard-next").enabledSelf, Is.True); Render();
                Assert.That(Get("sponsorPreview"), Is.SameAs(first)); Assert.That(JsonUtility.ToJson(first.SponsorSeasons.Single()), Is.EqualTo(offers));
                Assert.That(((SponsorSelectionView)Get("sponsorSelection")).SelectedIds, Has.Count.EqualTo(3));
                Assert.That(first.Transactions.Any(x => x.Type == TransactionType.SponsorRevenue), Is.False);
                typeof(TitleScreenController).GetMethod("RenderFinalReview", flags).Invoke(controller, null);
                var review = root.Q("final-review").Query<Label>().ToList().Select(x => x.text).ToList();
                Assert.That(review, Does.Contain("선택 스폰서")); Assert.That(review, Does.Contain("최종 시작 자금"));
                Assert.That(review.Any(x => x.Contains(first.SponsorSeasons.Single().Offers[0].CompanyName)), Is.True);
                root.Q<TextField>("promotion-name-input").value = "Changed Promotion"; Render();
                Assert.That(Get("sponsorPreview"), Is.Not.SameAs(first)); Assert.That(((SponsorSelectionView)Get("sponsorSelection")).SelectedIds, Is.Empty);
                Assert.That(root.Q<Button>("wizard-next").enabledSelf, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void Selection_ShowsTenLogosAndGoals_RequiresThreeWithoutMutatingSave()
        {
            var save = SponsorMissionTests.CreateSave(); var season = new SponsorMissionService().PrepareOffers(save, SponsorSelectionView.Companies());
            var before = JsonUtility.ToJson(save); var host = new VisualElement(); var view = new SponsorSelectionView(host, save, season);
            Assert.That(host.Q("sponsor-offers").Query<Button>().ToList(), Has.Count.EqualTo(10));
            Assert.That(host.Q("sponsor-offers").Query<Image>().ToList().All(x => x.image != null), Is.True);
            foreach (var offer in season.Offers.Take(3)) { Click(host.Q<Button>("sponsor-offer-" + offer.CompanyId)); }
            Assert.That(view.SelectedIds, Has.Count.EqualTo(3));
            Click(host.Q<Button>("sponsor-offer-" + season.Offers[3].CompanyId)); Assert.That(host.Q<Button>("sponsor-toggle").enabledSelf, Is.False);
            Assert.That(view.SelectedIds.Contains(season.Offers[3].Id), Is.False); Assert.That(view.SelectedIds.Count, Is.EqualTo(3));
            Assert.That(host.Q<Label>("sponsor-selection-total").text, Does.Contain("3 / 3"));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            Click(host.Q<Button>("sponsor-offer-" + season.Offers[0].CompanyId));
            Click(host.Q<Button>("sponsor-offer-" + season.Offers[3].CompanyId)); Assert.That(host.Q<Button>("sponsor-toggle").enabledSelf, Is.True);
        }

        [Test]
        public void NextSeasonSelection_CancelPreservesOriginal_ConfirmationCreatesSeasonAndPaysOnce()
        {
            var save = SponsorMissionTests.CreateSave();
            foreach (var show in save.Shows) show.Status = ShowStatus.Completed;
            foreach (var schedule in save.Schedules) schedule.Status = ScheduleStatus.Completed;
            save.CurrentDate = save.SeasonPolicy.EndDate; var policyId = save.SeasonPolicy.Id;
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            try
            {
                DashboardSession.SetActiveSave(save, true); var host = new VisualElement();
                var settings = SeasonTransitionService.DefaultSettings(save); var before = JsonUtility.ToJson(save);
                var overlay = SponsorSelectionView.OpenNextSeason(host, save, settings, null);
                Assert.That(overlay.Q<Button>("sponsor-confirm").enabledSelf, Is.False);
                Click(overlay.Q<Button>("sponsor-back")); Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
                overlay = SponsorSelectionView.OpenNextSeason(host, save, settings, null);
                foreach (var row in overlay.Q("sponsor-offers").Query<Button>().ToList().Take(3)) { Click(row); }
                Assert.That(overlay.Q<Button>("sponsor-confirm").enabledSelf, Is.True);
                Click(overlay.Q<Button>("sponsor-confirm"));
                Assert.That(host.Q("sponsor-overlay"), Is.Null); Assert.That(save.SeasonPolicy.Id, Is.Not.EqualTo(policyId));
                var season = save.SponsorSeasons.Single(); Assert.That(season.SelectionConfirmed, Is.True); Assert.That(season.Offers.Count(x => x.Selected), Is.EqualTo(3));
                Assert.That(save.Transactions.Count(x => x.Type == TransactionType.SponsorRevenue), Is.EqualTo(3));
                Assert.That(save.CurrentDate, Is.EqualTo(new GameDate(2027, 5, 31)));
                var progress = SponsorSelectionView.OpenActive(host); Assert.That(progress.Q("sponsor-offers").Query<Button>().ToList(), Has.Count.EqualTo(3));
                Assert.That(progress.Q("sponsor-toggle"), Is.Null);
            }
            finally { DashboardSession.SetActiveSave(previous, transient); }
        }

        [Test]
        public void NewGameAssets_IncludeLastSponsorStepAndDashboardEntry()
        {
            var root = Resources.Load<VisualTreeAsset>("PWManagerUI/NewGameScreen").CloneTree();
            Assert.That(root.Q("wizard-step-7"), Is.Not.Null);
            var titles = (string[])typeof(TitleScreenController).GetField("StepTitles", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Assert.That(titles[6], Is.EqualTo("스폰서 선택"));
            Assert.That(titles.Last(), Is.EqualTo("최종 검토"));
            var dashboard = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard").CloneTree();
            Assert.That(dashboard.Q<Button>("nav-finance"), Is.Not.Null);
            Assert.That(dashboard.Q<Button>("nav-sponsors"), Is.Null);
        }

        internal static void Click(Button button) => typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button.clickable, new object[] { null });
    }
}
