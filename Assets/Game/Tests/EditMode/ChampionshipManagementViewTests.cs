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
                var assignment = host.Query<Foldout>().ToList().First();
                assignment.value = true;
                assignment.Query<VisualElement>(className: "story-member").ToList().First().Q<Toggle>().value = true;
                Click(assignment.Query<Button>().ToList().Single(x => x.text == "적용"));
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
                var host = new VisualElement(); ChampionshipManagementView.Open(host);
                Assert.That(host.Query<Label>().ToList().Any(x => x.text == "위상 61.0 / 100"), Is.True);
                var history = host.Query<Foldout>().ToList().Single(x => x.text.StartsWith("타이틀전 기록"));
                Assert.That(history.Query<Label>().ToList().Any(x => x.text.Contains("방어 성공")), Is.True);
                Assert.That(history.Query<Label>().ToList().Any(x => x.text.Contains("팬 만족도 80.0")), Is.True);
                Assert.That(history.Query<Button>(className: "wrestler-link").ToList(), Has.Count.EqualTo(3));
            }
            finally { DashboardSession.SetActiveSave(previous, transient); }
        }

    }
}
