using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace PWManager.Tests
{
    public sealed class OperationsScreenLayoutTests
    {
        private static GameSave Sample()
        {
            var save = GameSave.CreateNew(71, new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc));
            save.Promotion = new PromotionState { Id = Guid.NewGuid().ToString(), Name = "Pro Wrestling", InitialCash = 2000000, PromotionPrestige = 2500 };
            save.StaffDepartments.Add(new StaffDepartmentState { Id = Guid.NewGuid().ToString(), DepartmentType = StaffDepartmentType.Scout, CurrentLevel = 2, UnlockedLevel = 2 });
            var catalog = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog");
            var service = new ScoutingService(new WrestlerGenerator(new StaticContentRegistry(catalog), 71), catalog.ScoutTeamLevels, random: new System.Random(71));
            var assignment = service.Start(save, new ScoutSearchConditions()); save.CurrentDate = assignment.CompleteDate; service.CompleteDue(save);
            TrainingService.Initialize(save);
            foreach (var w in save.Wrestlers.Take(4))
            {
                w.PromotionId = save.Promotion.Id; w.Identity.ExpiryDate = default; w.Roster.ActivityState = RosterActivityState.Active;
                save.ScoutCandidates.RemoveAll(c => c.WrestlerId == w.Id);
                save.Contracts.Add(new ContractState { Id = Guid.NewGuid().ToString(), PersonId = w.Id, Type = ContractType.Wrestler, StartDate = save.CurrentDate,
                    EndDate = new GameDate(2027, 5, 31), Status = ContractStatus.Active, MonthlySalary = 100 });
                new TrainingService().SetPlan(save, w.Id, TrainingMode.Normal, WrestlerAttribute.Power);
            }
            for (var day = 0; day < 7; day++) { save.CurrentDate = save.CurrentDate.AddDays(1); new TrainingService().ProcessDay(save); }
            for (var i = 0; i < 4; i++) save.Wrestlers[i].Condition.Condition = 91 - i * 18;
            return save;
        }

        private static (EditorWindow Window, VisualElement Root, object Training, object Scouting) Open(GameSave save, Vector2 size)
        {
            var window = ScriptableObject.CreateInstance<EditorWindow>(); window.titleContent = new GUIContent("PW Manager UI 검증");
            window.ShowUtility(); window.position = new Rect(24, 60, size.x, size.y);
            var root = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard").CloneTree(); root.style.flexGrow = 1; window.rootVisualElement.Add(root);
            DashboardViewHost.MountMissingViews(root);
            foreach (var page in root.Q("dashboard-workspace").Children().Where(p => !p.ClassListContains("topbar"))) page.style.display = DisplayStyle.None;
            var training = Activator.CreateInstance(typeof(DashboardController).Assembly.GetType("PWManager.Presentation.DashboardTrainingController"), new object[] { root });
            var scouting = Activator.CreateInstance(typeof(DashboardController).Assembly.GetType("PWManager.Presentation.DashboardScoutingController"), new object[] { root });
            Render(training, save); Render(scouting, save);
            root.Q("training-content").style.display = DisplayStyle.Flex;
            return (window, root, training, scouting);
        }

        [Test]
        public void TrainingSelection_UpdatesGaugeAndAbilityAndProtectsSevereInjury()
        {
            var save = Sample(); var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            var selected = save.Wrestlers[1]; selected.Condition.InjuryStatus = InjuryStatus.Severe; selected.Attributes.Power = 20;
            var view = Open(save, new Vector2(1280, 800));
            try
            {
                DashboardSession.SetActiveSave(save, true);
                Select(view.Root.Q("training-select-" + selected.Id));
                Assert.That(view.Root.Q<ProfileMetricGauge>("training-condition").Value, Is.EqualTo(selected.Condition.Condition));
                Assert.That(view.Root.Q("training-detail").Q<Label>("ops-ability-Power").text, Is.EqualTo("SS"));
                var modes = view.Root.Q<DropdownField>("training-mode-" + selected.Id); modes.index = 2;
                Assert.That(view.Root.Q<Button>("training-apply-" + selected.Id).enabledSelf, Is.False);
                Assert.That(view.Root.Q<DropdownField>("training-focus-" + selected.Id).enabledSelf, Is.False);
                modes.index = 3; Click(view.Root.Q<Button>("training-apply-" + selected.Id));
                Assert.That(save.Training.Plans.Single(p => p.WrestlerId == selected.Id).Mode, Is.EqualTo(TrainingMode.Rest));
                Assert.That(view.Root.Q("training-history-panel").Query<Foldout>().ToList(), Is.Not.Empty);
            }
            finally { view.Window.Close(); DashboardSession.SetActiveSave(previous, transient); }
        }

        [Test]
        public void ScoutSelection_UsesSavedReportAndClearsDetailAfterRecruitment()
        {
            var save = Sample(); var report = save.ScoutCandidates[0]; var w = save.Wrestlers.Single(w => w.Id == report.WrestlerId);
            w.Attributes.Power = 20; var estimate = report.ValueEstimates.Single(e => e.Type == ScoutValueType.Power);
            estimate.IsFullyRevealed = false; estimate.Assessment = ScoutAssessment.Low;
            var view = Open(save, new Vector2(1280, 800));
            try
            {
                var before = JsonUtility.ToJson(save); Select(view.Root.Q("scout-candidate-" + w.Id));
                Assert.That(view.Root.Q("scouting-detail").Q<Label>("ops-ability-Power").text, Is.EqualTo("낮음"));
                Assert.That(view.Root.Q("scouting-detail").Query<Label>(className: "ops-value").ToList().Count, Is.GreaterThanOrEqualTo(25));
                Click(view.Root.Q<Button>("scouting-tab-assignment")); Render(view.Scouting, save);
                Assert.That(view.Root.Q("scouting-assignment-panel").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
                save.ScoutCandidates.Remove(report); Render(view.Scouting, save);
                Assert.That(view.Root.Q<Button>("scout-negotiate-" + w.Id), Is.Null);
                Assert.That(view.Root.Q("scouting-detail").Q<Label>("ops-ability-Power"), Is.Not.Null);
            }
            finally { view.Window.Close(); }
        }

        [UnityTest]
        public IEnumerator Screens_FitWideAndCompactWindows()
        {
            var save = Sample();
            foreach (var size in new[] { new Vector2(1280, 800), new Vector2(960, 640) })
            {
                var view = Open(save, size);
                try
                {
                    foreach (var page in new[] { "training", "scouting" })
                    {
                        view.Root.Q("training-content").style.display = page == "training" ? DisplayStyle.Flex : DisplayStyle.None;
                        view.Root.Q("scouting-content").style.display = page == "scouting" ? DisplayStyle.Flex : DisplayStyle.None;
                        view.Window.Focus(); view.Window.Repaint();
                        yield return null; yield return null;
                        var until = EditorApplication.timeSinceStartup + .15;
                        while (EditorApplication.timeSinceStartup < until) yield return null;
                        var content = view.Root.Q(page + "-content");
                        Assert.That(view.Window.position.width, Is.EqualTo(size.x).Within(1));
                        Assert.That(view.Window.position.height, Is.EqualTo(size.y).Within(1));
                        Assert.That(content.ClassListContains("ops-narrow"), Is.EqualTo(size.x < 1100));
                        var list = view.Root.Q(page == "training" ? "training-roster" : "scouting-results");
                        var detail = view.Root.Q(page + "-detail");
                        Assert.That(list.worldBound.width, Is.GreaterThan(200)); Assert.That(list.worldBound.height, Is.GreaterThan(60));
                        Assert.That(detail.worldBound.width, Is.GreaterThan(200)); Assert.That(detail.worldBound.height, Is.GreaterThan(60));
                        Assert.That(detail.worldBound.xMax, Is.LessThanOrEqualTo(content.worldBound.xMax + 1));
                        Assert.That(detail.worldBound.yMax, Is.LessThanOrEqualTo(content.worldBound.yMax + 1));
                        Assert.That(list.worldBound.xMax <= detail.worldBound.xMin + 1 || list.worldBound.yMax <= detail.worldBound.yMin + 1, Is.True, "List overlaps its detail pane.");
                    }
                }
                finally { view.Window.Close(); }
            }
        }

        private static void Render(object controller, GameSave save) => controller.GetType().GetMethod("Render").Invoke(controller, new object[] { save });
        private static void Click(Button button) => typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button.clickable, new object[] { null });
        private static void Select(VisualElement row) { using var click = ClickEvent.GetPooled(); click.target = row; row.SendEvent(click); }
    }
}
