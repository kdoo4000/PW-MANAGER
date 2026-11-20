using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Tests
{
    public sealed class ChampionshipManagementViewTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void DivisionControl_FiltersChampionsAndSavesCreationAndEditing(bool tag)
        {
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            var ui = new GameObject("Division Championship Test UI");
            try
            {
                var save = new GameSave { Promotion = new PromotionState { Id = "promotion", Name = "PW" }, CurrentDate = new GameDate(2026, 6, 1) };
                foreach (var id in new[] { "a", "b", "c", "d" })
                {
                    var wrestler = new WrestlerState { Id = id, PromotionId = "promotion" };
                    wrestler.Identity.RingName = id;
                    wrestler.Identity.Gender = id is "c" or "d" ? WrestlerGender.Female : WrestlerGender.Male;
                    save.Wrestlers.Add(wrestler);
                }
                save.TagTeams.Add(new TagTeamState { Id = "men", PromotionId = "promotion", Name = "Men", MemberIds = new() { "a", "b" } });
                save.TagTeams.Add(new TagTeamState { Id = "women", PromotionId = "promotion", Name = "Women", MemberIds = new() { "c", "d" } });
                save.TagTeams.Add(new TagTeamState { Id = "mixed", PromotionId = "promotion", Name = "Mixed", MemberIds = new() { "a", "c" } });
                DashboardSession.SetActiveSave(save, true);
                var document = ui.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("PWManagerRuntime/PWManagerPanelSettings");
                document.visualTreeAsset = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard");
                var host = document.rootVisualElement; ChampionshipManagementView.Open(host);
                host.Q<TextField>("championship-name").value = "Title";
                if (tag) host.Q<DropdownField>("championship-type").index = 1;
                host.Q<DropdownField>("championship-division").index = (int)ChampionshipDivision.Open;
                host.Q<Toggle>("championship-holder-" + (tag ? "men" : "a")).value = true;
                host.Q<DropdownField>("championship-division").index = (int)ChampionshipDivision.Women;
                Assert.That(host.Q<Toggle>("championship-holder-" + (tag ? "men" : "a")), Is.Null);
                Assert.That(host.Q<Toggle>("championship-holder-mixed"), Is.Null);
                Assert.That(host.Q<Toggle>("championship-holder-vacant").value, Is.True);
                host.Q<Toggle>("championship-holder-" + (tag ? "women" : "c")).value = true;
                Click(host.Q<Button>("championship-create"));
                var title = save.Championships.Single();
                Assert.That(title.Division, Is.EqualTo(ChampionshipDivision.Women));
                Assert.That(title.HolderId, Is.EqualTo(tag ? "women" : "c"));
                Assert.That(host.Query<Label>().ToList().Any(x => x.text == "여성부"), Is.True);
                Click(host.Q<Button>("championship-edit"));
                Assert.That(host.Q<DropdownField>("championship-division").index, Is.EqualTo((int)ChampionshipDivision.Women));
                host.Q<DropdownField>("championship-division").index = (int)ChampionshipDivision.Men;
                host.Q<Toggle>("championship-holder-" + (tag ? "men" : "a")).value = true;
                Click(host.Q<Button>("championship-save"));
                Assert.That(title.Division, Is.EqualTo(ChampionshipDivision.Men));
                Assert.That(title.HolderId, Is.EqualTo(tag ? "men" : "a"));
                ChampionshipManagementView.Open(host);
                Assert.That(host.Q<Label>("championship-division-" + title.Id).text, Does.StartWith("남성부"));
            }
            finally { Object.DestroyImmediate(ui); DashboardSession.SetActiveSave(previous, transient); }
        }

        [Test]
        public void VisibleControls_CreateWorldChampionAndVacate()
        {
            var previous = DashboardSession.ActiveSave;
            var transient = DashboardSession.IsTransient;
            var ui = new GameObject("Championship Test UI");
            try
            {
                var save = new GameSave { Promotion = new PromotionState { Id = "promotion" }, CurrentDate = new GameDate(2026, 6, 1) };
                save.Wrestlers.Add(new WrestlerState { Id = "a", PromotionId = "promotion" });
                save.Wrestlers[0].Identity.RingName = "선수";
                DashboardSession.SetActiveSave(save, true);
                var document = ui.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("PWManagerRuntime/PWManagerPanelSettings");
                document.visualTreeAsset = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard");
                var host = document.rootVisualElement;
                ChampionshipManagementView.Open(host);
                host.Q<TextField>("championship-name").value = "월드";
                host.Q<Toggle>("championship-world").value = true;
                var row = host.Query<VisualElement>(className: "story-member").ToList().Last();
                Assert.That(row.Q<Button>(className: "wrestler-link"), Is.Not.Null);
                row.Q<Toggle>().value = true;
                Click(host.Q<Button>("championship-create"));
                Assert.That(save.Championships.Single().HolderId, Is.EqualTo("a"));
                Assert.That(save.Championships.Single().IsWorldChampionship, Is.True);
                Click(host.Q<Button>("championship-edit"));
                host.Q<Toggle>("championship-holder-vacant").value = true;
                Click(host.Q<Button>("championship-save"));
                Assert.That(save.Championships[0].HolderId, Is.Null);
                Assert.That(save.Championships[0].Reigns[0].LostDate.HasValue, Is.True);
            }
            finally { Object.DestroyImmediate(ui); DashboardSession.SetActiveSave(previous, transient); }
        }

        [Test]
        public void TagSelection_HidesAndClearsWorldFlagAndSupportsAssignedTeam()
        {
            var previous = DashboardSession.ActiveSave;
            var transient = DashboardSession.IsTransient;
            var ui = new GameObject("Tag Championship Test UI");
            try
            {
                var save = new GameSave { Promotion = new PromotionState { Id = "promotion" }, CurrentDate = new GameDate(2026, 6, 1) };
                foreach (var id in new[] { "a", "b" }) save.Wrestlers.Add(new WrestlerState { Id = id, PromotionId = "promotion" });
                save.TagTeams.Add(new TagTeamState { Id = "team", PromotionId = "promotion", Name = "팀", MemberIds = new() { "a", "b" } });
                DashboardSession.SetActiveSave(save, true);
                var document = ui.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("PWManagerRuntime/PWManagerPanelSettings");
                document.visualTreeAsset = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard");
                var host = document.rootVisualElement; ChampionshipManagementView.Open(host);
                host.Q<Toggle>("championship-world").value = true;
                host.Q<DropdownField>("championship-type").index = 1;
                Assert.That(host.Q<Toggle>("championship-world").value, Is.False);
                Assert.That(host.Q<Toggle>("championship-world").style.display.value, Is.EqualTo(DisplayStyle.None));
                host.Q<TextField>("championship-name").value = "태그";
                host.Query<VisualElement>(className: "story-member").ToList().Last().Q<Toggle>().value = true;
                Click(host.Q<Button>("championship-create"));
                Assert.That(save.Championships.Single().Type, Is.EqualTo(ChampionshipType.TagTeam));
                Assert.That(save.Championships.Single().HolderId, Is.EqualTo("team"));
                Assert.That(save.Championships.Single().IsWorldChampionship, Is.False);
            }
            finally { Object.DestroyImmediate(ui); DashboardSession.SetActiveSave(previous, transient); }
        }

        private static void Click(Button button) => typeof(Clickable)
            .GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(button.clickable, new object[] { null });

        [Test]
        public void TitleMatchHistory_ShowsPrestigeOutcomeAndLinkedParticipants()
        {
            var previous = DashboardSession.ActiveSave;
            var transient = DashboardSession.IsTransient;
            try
            {
                var save = new GameSave { Promotion = new PromotionState { Id = "promotion", Name = "WWE" }, CurrentDate = new GameDate(2026, 6, 1) };
                foreach (var id in new[] { "a", "b" }) save.Wrestlers.Add(new WrestlerState { Id = id, PromotionId = "promotion" });
                var title = ChampionshipService.Create(save, "World", ChampionshipType.Individual, true, "a");
                title.MatchHistory.Add(new ChampionshipMatchState { Date = save.CurrentDate, SourceResultId = "result",
                    ParticipantIds = new() { "a", "b" }, ChallengerIds = new() { "b" }, WinnerMemberIds = new() { "a" },
                    ChampionBeforeId = "a", ChampionAfterId = "a", SuccessfulDefense = true,
                    MatchQuality = 60, FanSatisfaction = 80, PrestigeBefore = 60, PrestigeAfter = 61 });
                title.Prestige = 61;
                DashboardSession.SetActiveSave(save, true);
                var host = new VisualElement(); ChampionshipManagementView.OpenProfile(host, title.Id);
                Assert.That(host.Query<Label>().ToList().Any(x => x.text == "61.0 / 100"), Is.True);
                Click(host.Q<Button>("championship-tab-matches"));
                var history = host.Q<ScrollView>("championship-detail");
                Assert.That(history.Query<Label>().ToList().Any(x => x.text.Contains("방어 성공")), Is.True);
                Assert.That(history.Query<Label>().ToList().Any(x => x.text == "80.0"), Is.True);
                Assert.That(history.Query<Button>(className: "wrestler-link").ToList(), Has.Count.EqualTo(3));
            }
            finally { DashboardSession.SetActiveSave(previous, transient); }
        }

    }
}
