using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
using UnityEngine.UIElements;

namespace PWManager.Tests
{
    public sealed class ScoutingFlowTests
    {
        [Test]
        public void Version22File_MigratesReportExpiryAndPreservesPeopleAfterExpiry()
        {
            var save = Create(); var service = Service(); var assignment = service.Start(save, new ScoutSearchConditions());
            save.CurrentDate = assignment.CompleteDate; service.CompleteDue(save);
            var expiry = save.ScoutCandidates[0].ExpiryDate.Value;
            foreach (var report in save.ScoutCandidates)
            {
                save.Wrestlers.Single(x => x.Id == report.WrestlerId).Identity.ExpiryDate = report.ExpiryDate;
                report.ExpiryDate = default;
            }
            save.SaveVersion = 22;
            var identities = save.Wrestlers.Select(x => x.Id).ToArray();
            var directory = Path.Combine(Path.GetTempPath(), "pwmanager-world-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "legacy.json"), JsonUtility.ToJson(save));
                var storage = new SaveService(directory); var restored = storage.Load("legacy");
                Assert.That(restored.SaveVersion, Is.EqualTo(GameSave.CurrentSaveVersion));
                Assert.That(restored.ScoutCandidates.All(x => x.ExpiryDate.Value.Equals(expiry)), Is.True);
                Assert.That(restored.Wrestlers.All(x => !x.Identity.ExpiryDate.HasValue), Is.True);
                restored.CurrentDate = expiry.AddDays(1); service.ProcessDay(restored);
                Assert.That(GameSaveValidator.Validate(restored), Is.Empty);
                storage.Save("legacy", restored);
                Assert.That(storage.Load("legacy").Wrestlers.Select(x => x.Id), Is.EqualTo(identities));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test]
        public void ExpiredCandidate_WithCareerHistoryKeepsItsPersonReference()
        {
            var save = Create(); var service = Service(); var assignment = service.Start(save, new ScoutSearchConditions());
            save.CurrentDate = assignment.CompleteDate; service.ProcessDay(save);
            foreach (var candidate in save.Wrestlers) candidate.Status.IsRookie = false;
            var wrestler = save.Wrestlers[0]; wrestler.Status.IsRookie = true;
            wrestler.Status.DebutDate = new GameDate(save.CurrentDate.Year - 1, save.CurrentDate.Month, save.CurrentDate.Day);
            new WrestlerCareerService().PrepareDue(save).Apply();
            Assert.That(save.CareerEvents.Any(x => x.WrestlerId == wrestler.Id), Is.True);
            save.CurrentDate = save.ScoutCandidates.Single(x => x.WrestlerId == wrestler.Id).ExpiryDate.Value.AddDays(1);
            service.ProcessDay(save);
            Assert.That(save.Wrestlers.Any(x => x.Id == wrestler.Id), Is.True);
            Assert.That(GameSaveValidator.Validate(save), Is.Empty);
            Assert.Throws<InvalidOperationException>(() => ContractNegotiationService.Demand(save, wrestler.Id, ContractType.Wrestler));
        }

        private static GameSave Create()
        {
            var save = GameSave.CreateNew(7, DateTime.UtcNow); save.CurrentDate = new GameDate(2026, 9, 7);
            save.Promotion = new PromotionState { Id = Guid.NewGuid().ToString("D"), Name = "Test", InitialCash = 10000000 };
            save.StaffDepartments.Add(new StaffDepartmentState { Id = Guid.NewGuid().ToString("D"), DepartmentType = StaffDepartmentType.Scout, CurrentLevel = 1, UnlockedLevel = 1 });
            var catalog = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog");
            var generator = new WrestlerGenerator(new StaticContentRegistry(catalog), 71);
            var population = Enumerable.Range(0, 6).Select(_ => generator.GenerateCandidate(WrestlerGender.Female, save.CurrentDate))
                .OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
            foreach (var wrestler in population)
            {
                wrestler.Identity.ExpiryDate = default;
                foreach (var field in typeof(WrestlerAttributesState).GetFields()) field.SetValue(wrestler.Attributes, 10f);
                wrestler.Growth.MatchPotentialCap = wrestler.Growth.PromoPotentialCap = 20;
            }
            save.Wrestlers.AddRange(population);
            save.UndiscoveredWrestlerIds.AddRange(population.Select(x => x.Id));
            save.WorldPopulationInitialized = true;
            return save;
        }

        private static ScoutingService Service(Func<string> candidateId = null)
        {
            var catalog = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog");
            return new ScoutingService(new WrestlerGenerator(new StaticContentRegistry(catalog), 7, candidateId), catalog.ScoutTeamLevels, random: new System.Random(7));
        }

        [Test]
        public void DailyTimeFlow_DeliversOneReportAfter7Days_RecruitsAndPreservesSave()
        {
            var save = Create(); var service = Service(); var assignment = service.Start(save, new ScoutSearchConditions { HasGender = true, Gender = WrestlerGender.Female });
            var time = new TimeFlowService(processScouting: service.ProcessDay);
            Assert.That(time.AdvanceTo(save, assignment.CompleteDate.AddDays(-1)).State, Is.EqualTo(TimeFlowState.Completed));
            Assert.That(save.ScoutCandidates, Is.Empty);
            Assert.That(time.AdvanceTo(save, assignment.CompleteDate).State, Is.EqualTo(TimeFlowState.Completed));
            Assert.That(save.ScoutCandidates, Has.Count.EqualTo(6)); Assert.That(save.Wrestlers.All(x => x.Identity.Gender == WrestlerGender.Female), Is.True);
            service.ProcessDay(save);
            Assert.That(save.InboxMessages.Count(x => x.SourceEventId == "scouting-report:" + assignment.Id), Is.EqualTo(1));
            var wrestler = save.Wrestlers[0]; var demand = ContractNegotiationService.Demand(save, wrestler.Id, ContractType.Wrestler);
            var accepted = new ContractNegotiationService(random: new FixedRandom(0)).Submit(save, wrestler.Id, ContractType.Wrestler, demand.SigningBonus, demand.MonthlySalary);
            Assert.That(accepted.Accepted, Is.True); Assert.That(wrestler.Roster.ActivityState, Is.EqualTo(RosterActivityState.Active));
            Assert.That(wrestler.Identity.ExpiryDate.HasValue, Is.False); Assert.That(save.ScoutCandidates.Any(x => x.WrestlerId == wrestler.Id), Is.False);
            var daysInMonth = DateTime.DaysInMonth(save.CurrentDate.Year, save.CurrentDate.Month);
            var initialSalary = (long)Math.Ceiling((decimal)demand.MonthlySalary * (daysInMonth - save.CurrentDate.Day + 1) / daysInMonth);
            Assert.That(save.Promotion.CalculateCurrentCash(save.Transactions), Is.EqualTo(10000000 - demand.SigningBonus - initialSalary));
            Assert.That(save.SalaryPayments.Single().Amount, Is.EqualTo(initialSalary));
            Assert.That(save.Contracts.Single().MatchAppearanceFee, Is.EqualTo(demand.MatchAppearanceFee));
            var directory = Path.Combine(Path.GetTempPath(), "pwmanager-scout-" + Guid.NewGuid().ToString("N"));
            try
            {
                var storage = new SaveService(directory); storage.Save("scouting", save); var restored = storage.Load("scouting");
                Assert.That(GameSaveValidator.Validate(restored), Is.Empty); Assert.That(restored.ScoutCandidates, Has.Count.EqualTo(5));
                Assert.That(restored.ScoutAssignments.Single().Status, Is.EqualTo(ScoutAssignmentStatus.Completed));
                restored.CurrentDate = restored.ScoutCandidates[0].ExpiryDate.Value.AddDays(1);
                service.ProcessDay(restored);
                Assert.That(restored.ScoutCandidates.Count(x => x.IsExpired(restored.CurrentDate)), Is.EqualTo(5));
                Assert.That(restored.Wrestlers, Has.Count.EqualTo(6));
                Assert.That(restored.Wrestlers.Any(x => x.Id == wrestler.Id), Is.True);
                storage.Save("scouting", restored);
                Assert.That(storage.Load("scouting").Wrestlers, Has.Count.EqualTo(6));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test]
        public void WorldInitializationFailure_DoesNotLeavePartialPeopleOrAssignment()
        {
            var save = Create(); var service = Service(() => "11111111-1111-4111-8111-111111111111");
            save.Wrestlers.Clear(); save.UndiscoveredWrestlerIds.Clear(); save.WorldPopulationInitialized = false;
            var before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => service.Start(save, new ScoutSearchConditions()));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void SavedPendingAssignment_ResumesWithoutChangingConditionsOrCompletionDate()
        {
            var save = Create(); var service = Service(); var conditions = new ScoutSearchConditions { HasGender = true, Gender = WrestlerGender.Female };
            var assignment = service.Start(save, conditions); conditions.Gender = WrestlerGender.Male;
            new TimeFlowService(processScouting: service.ProcessDay).AdvanceTo(save, save.CurrentDate.AddDays(3));
            var restored = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            Assert.That(restored.ScoutAssignments.Single().CompleteDate, Is.EqualTo(assignment.CompleteDate));
            new TimeFlowService(processScouting: service.ProcessDay).AdvanceTo(restored, assignment.CompleteDate);
            Assert.That(restored.ScoutCandidates, Has.Count.EqualTo(6));
            Assert.That(restored.Wrestlers.All(x => x.Identity.Gender == WrestlerGender.Female), Is.True);
        }

        [Test]
        public void DashboardMenu_OpensScoutingAndCanReturnToHome()
        {
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            var go = new GameObject("Scouting navigation test"); go.SetActive(false);
            try
            {
                DashboardSession.SetActiveSave(Create(), true);
                var document = go.AddComponent<UIDocument>();
                document.visualTreeAsset = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard");
                document.panelSettings = Resources.Load<PanelSettings>("PWManagerRuntime/PWManagerPanelSettings");
                var controller = go.AddComponent<DashboardController>(); go.SetActive(true);
                if (typeof(DashboardController).GetField("document", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller) == null)
                    typeof(DashboardController).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                var root = document.rootVisualElement;
                Assert.That(root.Q<Button>("nav-training").parent.IndexOf(root.Q<Button>("nav-training")), Is.EqualTo(root.Q<Button>("nav-scout").parent.IndexOf(root.Q<Button>("nav-scout")) + 1));
                Assert.That(Resources.Load("PWManagerUI/VectorIcons/training"), Is.Not.Null);
                Assert.That(root.Q<ScrollView>("sidebar-menu").Contains(root.Q<Button>("nav-training")), Is.True);
                var click = typeof(DashboardController).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance);
                using (var evt = ClickEvent.GetPooled())
                { evt.target = root.Q<Button>("nav-scout"); click.Invoke(controller, new object[] { evt }); }
                Assert.That(root.Q("scouting-content").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(root.Q<Button>("nav-scout").ClassListContains("active"), Is.True);
                using (var evt = ClickEvent.GetPooled())
                { evt.target = root.Q<Button>("nav-training"); click.Invoke(controller, new object[] { evt }); }
                Assert.That(root.Q("training-content").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(root.Q("scouting-content").style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(root.Q("icon-training").ClassListContains("nav-icon-training"), Is.True);
                typeof(DashboardController).GetMethod("NavigateBack", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                Assert.That(root.Q("scouting-content").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                typeof(DashboardController).GetMethod("NavigateForward", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                Assert.That(root.Q("training-content").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                using (var evt = ClickEvent.GetPooled())
                { evt.target = root.Q<Button>("nav-staff"); click.Invoke(controller, new object[] { evt }); }
                Assert.That(root.Q("training-content").style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(root.Q<Label>("dashboard-page-title").text, Is.EqualTo("스태프·경기장"));
                Assert.That(root.Q<Button>("facilities-tab-venues").text, Is.EqualTo("경기장"));
                Assert.That(root.Q<Button>("nav-staff").Q<Label>(className: "nav-label").text, Is.EqualTo("스태프·경기장"));
                using (var evt = ClickEvent.GetPooled())
                { evt.target = root.Q<Button>("nav-home"); click.Invoke(controller, new object[] { evt }); }
                Assert.That(root.Q("scouting-content").style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(root.Q("overview-content").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            }
            finally
            {
                var controller = go.GetComponent<DashboardController>();
                if (controller != null) typeof(DashboardController).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                UnityEngine.Object.DestroyImmediate(go); DashboardSession.SetActiveSave(previous, transient);
            }
        }

        [Test]
        public void ExpiredCandidate_WithRejectedNegotiation_IsHiddenAndCannotBeRecruited()
        {
            var save = Create(); var service = Service(); var assignment = service.Start(save, new ScoutSearchConditions());
            save.CurrentDate = assignment.CompleteDate; service.ProcessDay(save);
            var wrestler = save.Wrestlers[0]; var demand = ContractNegotiationService.Demand(save, wrestler.Id, ContractType.Wrestler);
            var rejected = new ContractNegotiationService(random: new FixedRandom(.999)).Submit(save, wrestler.Id, ContractType.Wrestler, demand.SigningBonus, demand.MonthlySalary);
            Assert.That(rejected.Accepted, Is.False);
            save.CurrentDate = save.ScoutCandidates.Single(x => x.WrestlerId == wrestler.Id).ExpiryDate.Value.AddDays(1); service.ProcessDay(save);
            Assert.That(save.ContractNegotiations.Single().PersonId, Is.EqualTo(wrestler.Id));
            Assert.That(GameSaveValidator.Validate(save), Is.Empty);
            Assert.Throws<InvalidOperationException>(() => ContractNegotiationService.Demand(save, wrestler.Id, ContractType.Wrestler));
            var root = Dashboard(); var controller = ScoutController(root); Render(controller, save);
            Assert.That(root.Q<ScrollView>("scouting-results").Query<Button>(className: "wrestler-link").ToList(), Is.Empty);
        }

        [Test]
        public void Screen_StartsOneAssignment_ShowsLinkedCandidates_AndOpensContract()
        {
            var save = Create(); var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            var root = Dashboard(); var controller = ScoutController(root);
            var owner = new GameObject("Scouting UI Flow Test");
            var document = owner.AddComponent<UIDocument>();
            document.panelSettings = Resources.Load<PanelSettings>("PWManagerRuntime/PWManagerPanelSettings");
            document.rootVisualElement.Add(root);
            try
            {
                DashboardSession.SetActiveSave(save, true); Render(controller, save);
                root.Q<DropdownField>("scouting-gender").index = 2;
                Click(root.Q<Button>("scouting-start")); Click(root.Q<Button>("scouting-start"));
                Assert.That(save.ScoutAssignments, Has.Count.EqualTo(1));
                Assert.That(root.Q<Button>("scouting-start").enabledInHierarchy, Is.False);
                var assignment = save.ScoutAssignments.Single();
                Assert.That(assignment.SearchConditions.Gender, Is.EqualTo(WrestlerGender.Female));
                var process = (Action<GameSave>)Delegate.CreateDelegate(typeof(Action<GameSave>), controller.GetType().GetMethod("ProcessDay", BindingFlags.NonPublic | BindingFlags.Static));
                new TimeFlowService(processScouting: process).AdvanceTo(save, assignment.CompleteDate); Render(controller, save);
                Assert.That(root.Q<ScrollView>("scouting-results").Query<Button>(className: "wrestler-link").ToList(), Has.Count.EqualTo(6));
                var before = JsonUtility.ToJson(save); Render(controller, save); Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
                Assert.That(root.Q("scouting-reports-panel").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                using (var select = ClickEvent.GetPooled()) { select.target = root.Q("scout-candidate-" + save.ScoutCandidates[0].WrestlerId); select.target.SendEvent(select); }
                Click(root.Q<Button>("scout-negotiate-" + save.ScoutCandidates[0].WrestlerId));
                Assert.That(root.Q("contract-negotiation-overlay"), Is.Not.Null);
                Assert.That(root.Q<LongField>("contract-signing-bonus").value, Is.GreaterThan(0));
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); DashboardSession.SetActiveSave(previous, transient); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CandidateProfile_UsesSavedAssessmentAndColorsThenShowsActualGradesAfterRecruitment(bool revealed)
        {
            var save = Create(); var service = Service(); var assignment = service.Start(save, new ScoutSearchConditions());
            save.CurrentDate = assignment.CompleteDate; service.ProcessDay(save);
            var wrestler = save.Wrestlers[0]; var report = save.ScoutCandidates[0];
            wrestler.Attributes.Power = 20;
            var estimate = report.ValueEstimates.Single(x => x.Type == ScoutValueType.MatchOverall);
            estimate.Minimum = estimate.Maximum = 10; estimate.IsFullyRevealed = true;
            var powerEstimate = report.ValueEstimates.Single(x => x.Type == ScoutValueType.Power);
            powerEstimate.Minimum = powerEstimate.Maximum = 10; powerEstimate.IsFullyRevealed = revealed; powerEstimate.Assessment = ScoutAssessment.Low;
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            var go = new GameObject("Scout profile test"); go.SetActive(false); var controller = go.AddComponent<WrestlerProfileController>();
            var root = Resources.Load<VisualTreeAsset>("PWManagerUI/WrestlerProfile").CloneTree();
            try
            {
                DashboardSession.SetActiveSave(save, true);
                typeof(WrestlerProfileController).GetMethod("BindInteractiveRoot", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, new object[] { root });
                typeof(WrestlerProfileController).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, new object[] { wrestler });
                Assert.That(root.Q<Label>("wp-match-overall").text, Is.EqualTo("D+"));
                var rows = root.Q("wp-match-abilities").Children().Where(x => x.Q<Label>(className: "wp-ability-label") != null).ToList();
                var power = rows.Single(x => x.Q<Label>(className: "wp-ability-label").text == "파워").Q<Label>(className: "wp-ability-value");
                Assert.That(power.text, Is.EqualTo(revealed ? "D+" : "낮음"));
                Assert.That(power.ClassListContains("grade-ss"), Is.False);
                Assert.That(power.ClassListContains(revealed ? "grade-dplus" : "grade-eplus"), Is.True);
                Assert.That(root.Query<Label>(className: "wp-ability-value").ToList().All(x => x.text != "미확인" && !x.text.Contains("~")), Is.True);
                Assert.That(root.Q<Label>("wp-match-potential").text, Is.Not.EqualTo("미확인"));
                Assert.That(root.Q<Button>("wp-open-growth").enabledSelf, Is.False);
                save.ScoutCandidates.Remove(report);
                typeof(WrestlerProfileController).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, new object[] { wrestler });
                power = root.Q("wp-match-abilities").Children().Single(x => x.Q<Label>(className: "wp-ability-label")?.text == "파워").Q<Label>(className: "wp-ability-value");
                Assert.That(power.text, Is.EqualTo("SS")); Assert.That(power.ClassListContains("grade-ss"), Is.True);
                Assert.That(power.ClassListContains("scout-unconfirmed"), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); DashboardSession.SetActiveSave(previous, transient); }
        }

        [Test]
        public void UncontractedCandidate_IsNotInRosterAndDoesNotReceiveQuarterlyGrowth()
        {
            var save = Create(); var service = Service(); var assignment = service.Start(save, new ScoutSearchConditions());
            save.CurrentDate = assignment.CompleteDate; service.ProcessDay(save);
            var root = Dashboard(); var rosterType = typeof(DashboardController).Assembly.GetType("PWManager.Presentation.DashboardRosterController");
            var roster = Activator.CreateInstance(rosterType, new object[] { root });
            rosterType.GetMethod("Render").Invoke(roster, new object[] { save });
            Assert.That(root.Q("roster-rows").Query<Button>(className: "wrestler-link").ToList(), Is.Empty);
            save.LastGrowthQuarterEnd = new(new GameDate(2026, 8, 31)); save.CurrentDate = new GameDate(2026, 11, 30);
            new WrestlerGrowthService().PrepareDue(save).Apply();
            Assert.That(save.GrowthQuarterResults, Is.Empty);
            var wrestler = save.Wrestlers[0]; var demand = ContractNegotiationService.Demand(save, wrestler.Id, ContractType.Wrestler);
            new ContractNegotiationService(random: new FixedRandom(0)).Submit(save, wrestler.Id, ContractType.Wrestler, demand.SigningBonus, demand.MonthlySalary);
            rosterType.GetMethod("Render").Invoke(roster, new object[] { save });
            Assert.That(root.Q("roster-rows").Query<Button>(className: "wrestler-link").ToList(), Has.Count.EqualTo(1));
        }

        [Test]
        public void InitialRecruitmentInformation_IsListedAndCanBeContracted()
        {
            var save = InitialMarket(); var root = Dashboard(); var controller = ScoutController(root);
            Render(controller, save);
            var report = save.ScoutCandidates.First(); var person = save.Wrestlers.Single(x => x.Id == report.WrestlerId);
            Assert.That(report.Source, Is.EqualTo(ScoutReportSource.InitialRecruitment));
            Assert.That(root.Q("scout-candidate-" + person.Id), Is.Not.Null);
            Assert.That(root.Q("scout-candidate-" + person.Id).Query<Button>(className: "wrestler-link").ToList(), Has.Count.EqualTo(1));
            var rosterType = typeof(DashboardController).Assembly.GetType("PWManager.Presentation.DashboardRosterController");
            var roster = Activator.CreateInstance(rosterType, new object[] { root });
            rosterType.GetMethod("Render").Invoke(roster, new object[] { save });
            Assert.That(root.Q("roster-rows").Query<Button>(className: "wrestler-link").ToList(), Has.Count.EqualTo(8));

            var demand = ContractNegotiationService.Demand(save, person.Id, ContractType.Wrestler);
            var result = new ContractNegotiationService(random: new FixedRandom(0))
                .Submit(save, person.Id, ContractType.Wrestler, demand.SigningBonus, demand.MonthlySalary);
            Assert.That(result.Accepted, Is.True);
            Assert.That(person.PromotionId, Is.EqualTo(save.Promotion.Id));
            Assert.That(save.ScoutCandidates.Any(x => x.WrestlerId == person.Id), Is.False);
            Render(controller, save);
            Assert.That(root.Q("scout-candidate-" + person.Id), Is.Null);
            Assert.That(GameSaveValidator.Validate(save), Is.Empty);
        }

        [Test]
        public void InitialRecruitmentInformation_ExpiresAndRediscoveryPreservesKnowledge()
        {
            var save = InitialMarket();
            var expected = save.Wrestlers.Where(x => string.IsNullOrEmpty(x.PromotionId) && x.Identity.Gender == WrestlerGender.Female)
                .OrderBy(x => x.Id, StringComparer.Ordinal).Take(6).ToList();
            Assert.That(expected, Has.Count.EqualTo(6));
            foreach (var person in expected)
            {
                foreach (var field in typeof(WrestlerAttributesState).GetFields()) field.SetValue(person.Attributes, 10f);
                person.Growth.MatchPotentialCap = person.Growth.PromoPotentialCap = 20;
            }
            var first = expected[0]; var expiry = save.ScoutCandidates.Single(x => x.WrestlerId == first.Id).ExpiryDate.Value;
            save.CurrentDate = expiry;
            Assert.That(ContractNegotiationService.HasCurrentRecruitmentInformation(save, first), Is.True);
            save.CurrentDate = expiry.AddDays(1);
            Assert.Throws<InvalidOperationException>(() => ContractNegotiationService.Demand(save, first.Id, ContractType.Wrestler));
            var root = Dashboard(); Render(ScoutController(root), save);
            Assert.That(root.Q("scouting-results").Query<Button>(className: "wrestler-link").ToList(), Is.Empty);
            var before = expected.ToDictionary(x => x.Id, x => JsonUtility.ToJson(x));
            var previousTraits = save.ScoutCandidates.ToDictionary(x => x.WrestlerId, x => x.RevealedTraitIds.ToArray());
            var count = save.Wrestlers.Count;
            var service = Service(); var assignment = service.Start(save, new ScoutSearchConditions { HasGender = true, Gender = WrestlerGender.Female });
            save.CurrentDate = assignment.CompleteDate; var reports = service.CompleteDue(save);
            Assert.That(reports.Select(x => x.WrestlerId), Is.EquivalentTo(expected.Select(x => x.Id)));
            Assert.That(save.Wrestlers, Has.Count.EqualTo(count));
            foreach (var report in reports)
            {
                Assert.That(report.Source, Is.EqualTo(ScoutReportSource.Scouting));
                Assert.That(report.KnowledgeLevel, Is.EqualTo(4));
                Assert.That(report.ValueEstimates.All(x => x.IsFullyRevealed), Is.True);
                Assert.That(report.RevealedTraitIds, Is.EquivalentTo(previousTraits[report.WrestlerId]));
                Assert.That(JsonUtility.ToJson(save.Wrestlers.Single(x => x.Id == report.WrestlerId)), Is.EqualTo(before[report.WrestlerId]));
            }
            Assert.That(GameSaveValidator.Validate(save), Is.Empty);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void RecruitmentReport_RejectsInvalidSourceOrAssignmentReference(int invalidCase)
        {
            var save = InitialMarket(); var report = save.ScoutCandidates.First();
            if (invalidCase == 0) report.Source = (ScoutReportSource)99;
            if (invalidCase == 1) report.Source = ScoutReportSource.Scouting;
            if (invalidCase == 2)
            {
                var assignment = Service().Start(save, new ScoutSearchConditions());
                report.AssignmentId = assignment.Id;
            }
            Assert.That(GameSaveValidator.Validate(save), Is.Not.Empty);
        }

        private static GameSave InitialMarket()
        {
            var catalog = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog");
            var candidates = new WrestlerGenerator(new StaticContentRegistry(catalog), 195)
                .GenerateInitialCandidates(new GameDate(2026, 6, 1)).Take(20).ToList();
            var save = new GameStartService().CreateInitialSave(new GameStartRequest
            {
                PromotionName = "Initial Market", InitialCash = 10000000, WorldSeed = 195, UtcNow = DateTime.UtcNow,
                InitialCandidates = candidates,
                WrestlerContracts = candidates.Take(8).Select(x => new InitialWrestlerContractInput { Wrestler = x }).ToList(),
                RegularVenueId = "venue_001", RegularShowFrequency = RegularShowFrequency.Monthly, PpvFrequency = PpvFrequency.EveryFourMonths
            });
            var scout = save.StaffDepartments.Single(x => x.DepartmentType == StaffDepartmentType.Scout);
            scout.CurrentLevel = scout.UnlockedLevel = 1;
            save.WorldPopulationInitialized = true;
            return save;
        }

        private static VisualElement Dashboard()
        {
            var root = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard").CloneTree(); DashboardViewHost.MountMissingViews(root);
            root.Q("scouting-content").style.display = DisplayStyle.Flex; return root;
        }
        private static object ScoutController(VisualElement root) => Activator.CreateInstance(typeof(DashboardController).Assembly.GetType("PWManager.Presentation.DashboardScoutingController"), new object[] { root });
        private static void Render(object controller, GameSave save) => controller.GetType().GetMethod("Render").Invoke(controller, new object[] { save });
        private static void Click(Button button) => typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button.clickable, new object[] { null });
        private sealed class FixedRandom : System.Random
        {
            private readonly double value;
            public FixedRandom(double value) => this.value = value;
            public override double NextDouble() => value;
        }
    }
}
