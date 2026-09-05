using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Domain.Validation;
using PWManager.Presentation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace PWManager.Tests
{
    public sealed class MatchSpotTests
    {
        [Test]
        public void NormalSpots_AreSeparateAutomaticContent()
        {
            Assert.That(NormalMatchSpotCatalog.All.Count, Is.EqualTo(16));
            Assert.That(NormalMatchSpotCatalog.All.All(x => x.Id.StartsWith("normal_")), Is.True);
            Assert.That(NormalMatchSpotCatalog.Available(Plan("spot_001"), MatchSpotPhase.Middle, false).Any(), Is.True);
            Assert.That(NormalMatchSpotCatalog.Available(Plan("spot_001"), MatchSpotPhase.Middle, false).Any(x => x.TeamOnly), Is.False);
        }

        [Test]
        public void Catalog_AllSixteenSpotsExecuteWithBothVariantsAndStableRoles()
        {
            Assert.That(SpecialMatchSpotCatalog.All.Count, Is.EqualTo(16));
            Assert.That(SpecialMatchSpotCatalog.All.Select(x => x.Id).Distinct().Count(), Is.EqualTo(16));
            var save = Save();
            foreach (var definition in SpecialMatchSpotCatalog.All)
            {
                Assert.That(definition.Difficulty, Is.InRange(1, 20));
                var match = Plan(definition.Id);
                match.Spots[0].Phase = definition.Phases[0];
                match.Spots[0].ActorId = definition.ActorScope == SpotActorScope.Outsider ? "e" : "a";
                match.Spots[0].TargetId = definition.Relationship == SpotRelationship.Teammates ? "c" : "b";
                Assert.That(MatchSpotEvaluator.Validate(save, match), Is.Empty, definition.Name);
                var variants = new HashSet<int>();
                for (var seed = 0; seed < 30; seed++)
                {
                    var result = MatchSpotEvaluator.Evaluate(save, match, save.CurrentDate, "event", seed).Single();
                    variants.Add(result.VariantIndex);
                    Assert.That(result.ScriptedMoment, Does.Not.Contain("{"));
                    save.Wrestlers.Reverse(); match.Sides.Reverse();
                    Assert.That(JsonUtility.ToJson(MatchSpotEvaluator.Evaluate(save, match, save.CurrentDate, "event", seed).Single()), Is.EqualTo(JsonUtility.ToJson(result)));
                }
                Assert.That(variants.Count, Is.EqualTo(2), definition.Name);
            }
        }

        [Test]
        public void ProbabilitiesAndRepetition_FollowDocumentAndRejectInvalidValues()
        {
            foreach (var execution in new[] { 0f, 1f, 10f, 20f })
                foreach (var difficulty in new[] { 1f, 10f, 20f })
                {
                    var p = MatchSpotEvaluator.Probabilities(execution, difficulty);
                    Assert.That(p.Sum(), Is.EqualTo(1).Within(1e-12));
                    Assert.That(p.All(x => x >= 0 && x <= 1), Is.True);
                }
            Assert.That(MatchSpotEvaluator.Probabilities(20, 10)[3], Is.GreaterThan(MatchSpotEvaluator.Probabilities(1, 10)[3]));
            Assert.That(MatchSpotEvaluator.Score(1, SpotExecutionResult.GreatSuccess, 0), Is.EqualTo(1.65f).Within(.0001));
            Assert.That(MatchSpotEvaluator.Score(1, SpotExecutionResult.Success, 6), Is.LessThan(0));
            Assert.That(MatchSpotEvaluator.Score(1, SpotExecutionResult.Failure, 8), Is.EqualTo(-.75f));
            Assert.That(MatchSpotEvaluator.Score(1, SpotExecutionResult.Disaster, 8), Is.EqualTo(-2.25f));
            Assert.Throws<ArgumentOutOfRangeException>(() => MatchSpotEvaluator.Probabilities(float.NaN, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => MatchSpotEvaluator.Score(1, SpotExecutionResult.Success, -1));
        }

        [Test]
        public void Conditions_RejectInvalidRolesAndGimmicks()
        {
            var save = Save(); var match = Plan("spot_004");
            match.Spots[0].ActorId = "e";
            Assert.That(MatchSpotEvaluator.Validate(save, match), Is.Empty);
            match.Spots[0].ActorId = "a";
            Assert.That(MatchSpotEvaluator.Validate(save, match), Is.Not.Empty);
            match.Spots[0].ActorId = "e"; match.Spots[0].Phase = MatchSpotPhase.PostMatch;
            Assert.That(MatchSpotEvaluator.Validate(save, match), Is.Not.Empty);
            match = Plan("spot_009"); match.Spots[0].TargetId = "c";
            Assert.That(MatchSpotEvaluator.Validate(save, match), Is.Empty);
            match.Spots[0].TargetId = "b";
            Assert.That(MatchSpotEvaluator.Validate(save, match), Is.Not.Empty);
        }

        [Test]
        public void Execution_OnlyConditionAndInjuryAdjustPerformanceAndScriptSurvivesFailure()
        {
            var save = Save(); var match = Plan("spot_001");
            float Execution() => MatchSpotEvaluator.Evaluate(save, match, save.CurrentDate, "event", 7)[0].EffectiveExecution;
            var normal = Execution();
            save.Wrestlers[0].Condition.Satisfaction = 0;
            save.Wrestlers[0].Momentum.Momentum = 100;
            Assert.That(Execution(), Is.EqualTo(normal));
            save.Wrestlers[0].Condition.Condition = 20;
            Assert.That(Execution(), Is.LessThan(normal));
            var tired = Execution();
            save.Wrestlers[0].Condition.InjuryStatus = InjuryStatus.Moderate;
            Assert.That(Execution(), Is.LessThan(tired));
            var failure = Enumerable.Range(0, 100).Select(seed => MatchSpotEvaluator.Evaluate(save, match, save.CurrentDate, "event", seed)[0])
                .First(x => x.Result < SpotExecutionResult.Success);
            Assert.That(failure.ScriptedMoment, Does.Contain("가람"));
            Assert.That(failure.ScriptedMoment, Does.Contain("나래"));
            Assert.That(failure.FinalSpotScore, Is.LessThan(0));
        }

        [Test]
        public void History_CountsEightWeeksPendingMatchesAndSameMatchRepeats()
        {
            var save = Save(); var match = Plan("spot_001");
            var history = new MatchResultState { ShowEventId = "previous" };
            foreach (var days in new[] { -57, -56, -1, 1 })
                history.SpotResults.Add(new SpotResultState { SpotId = "spot_001", Date = save.CurrentDate.AddDays(days) });
            save.MatchResults.Add(history);
            var pending = new MatchResultState { ShowEventId = "earlier-today", SpotResults = { new SpotResultState { SpotId = "spot_001", Date = save.CurrentDate } } };
            match.Spots.Add(new PlannedSpotState { SpotId = "spot_001", ActorId = "a", TargetId = "b", Phase = MatchSpotPhase.Entrance });
            var results = MatchSpotEvaluator.Evaluate(save, match, save.CurrentDate, "current", 7, new[] { pending });
            Assert.That(results.Select(x => x.RepeatCount), Is.EqualTo(new[] { 3, 4 }));
            Assert.That(MatchSpotEvaluator.RecentUses(save, "spot_001", save.CurrentDate, "previous"), Is.Zero);
        }

        [Test]
        public void ShowExecution_PersistsSpotQualityAndNarratesInPhaseOrder()
        {
            var catalog = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog");
            var save = ShowTestSaveFactory.Create(catalog, 73);
            var content = new StaticContentRegistry(catalog);
            var planning = new ShowPlanningService(content);
            var match = new MatchPlanState { MatchTypeId = "matchtype_001", MatchGimmickId = "gimmick_000", FinishType = MatchFinishType.Draw,
                ParticipantIds = save.Wrestlers.Take(2).Select(x => x.Id).ToList(), OpeningSpot = "직접 입력한 경기 전 사건", ClosingSpot = "직접 입력한 경기 후 사건" };
            var outsider = save.Wrestlers.First(x => !match.ParticipantIds.Contains(x.Id)).Id;
            match.Spots.Add(new PlannedSpotState { SpotId = "spot_015", Phase = MatchSpotPhase.PostMatch, ActorId = outsider, TargetId = match.ParticipantIds[0] });
            match.Spots.Add(new PlannedSpotState { SpotId = "spot_002", Phase = MatchSpotPhase.Entrance, ActorId = outsider, TargetId = match.ParticipantIds[1] });
            match.Spots.Add(new PlannedSpotState { SpotId = "spot_006", Phase = MatchSpotPhase.Middle, ActorId = outsider, TargetId = match.ParticipantIds[0] });
            var show = save.Shows[0]; show.DurationLimit = 20;
            var showEvent = planning.AddMatch(save, show.Id, match, 20);
            planning.Confirm(save, show.Id); show.Status = ShowStatus.InProgress;
            var evaluator = new MatchEvaluator(content, content);
            new ShowExecutionService(planning, evaluator, new PromoEvaluator()).Execute(save, show.Id, 7, 100, 20);
            var result = save.MatchResults.Single();
            Assert.That(result.TechnicalEvaluation.Spot, Is.EqualTo(result.SpotResults.Sum(x => x.FinalSpotScore)));
            Assert.That(result.SpotResults.Select(x => x.Phase), Is.Ordered);
            Assert.That(result.SpotResults.Select(x => x.RepeatCount), Is.EqualTo(new[] { 0, 0, 0 }));
            Assert.That(result.FinishType, Is.EqualTo(MatchFinishType.Draw));
            Assert.That(result.SimulationBeats.First().Detail, Is.EqualTo(match.OpeningSpot));
            Assert.That(result.SimulationBeats.Any(x => x.Detail == match.ClosingSpot), Is.True);
            foreach (var spot in result.SpotResults)
                Assert.That(result.SimulationBeats.Any(x => x.Phase == (int)spot.Phase && x.Detail?.StartsWith(spot.ScriptedMoment) == true), Is.True);
            Assert.That(result.NarrativeLines.Count, Is.EqualTo(result.SimulationBeats.Count));
            var restored = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            Assert.That(GameSaveValidator.Validate(restored), Is.Empty);
            Assert.That(JsonUtility.ToJson(restored.MatchResults[0]), Is.EqualTo(JsonUtility.ToJson(result)));
            Assert.That(restored.MatchPlans.Single().Spots.Count, Is.EqualTo(3));
        }

        [Test]
        public void RunInStop_EndsMatchAtTheSpotAndShortensDuration()
        {
            var catalog = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog");
            var save = ShowTestSaveFactory.Create(catalog, 74);
            var content = new StaticContentRegistry(catalog);
            var planning = new ShowPlanningService(content);
            var participants = save.Wrestlers.Take(2).Select(x => x.Id).ToList();
            var match = new MatchPlanState { MatchTypeId = "matchtype_001", MatchGimmickId = "gimmick_000",
                FinishType = MatchFinishType.Draw, ParticipantIds = participants };
            match.Spots.Add(new PlannedSpotState { SpotId = "spot_004", Phase = MatchSpotPhase.Middle,
                ActorId = save.Wrestlers.First(x => !participants.Contains(x.Id)).Id, TargetId = participants[0] });
            var show = save.Shows[0]; show.DurationLimit = 20;
            planning.AddMatch(save, show.Id, match, 20);
            planning.Confirm(save, show.Id); show.Status = ShowStatus.InProgress;
            new ShowExecutionService(planning, new MatchEvaluator(content, content), new PromoEvaluator()).Execute(save, show.Id, 7, 100, 20);
            var result = save.MatchResults.Single();
            Assert.That(result.WasStoppedBySpot, Is.True);
            Assert.That(result.ActualMatchDuration, Is.LessThan(result.PlannedDuration));
            Assert.That(result.SimulationBeats.Last().Detail, Does.Contain("중단"));
            Assert.That(result.SimulationBeats.Any(x => x.BeatType == MatchBeatType.Finish), Is.False);
        }

        [Test]
        public void Editor_ChangesAreIsolatedUntilSaveAndCanBeReopened()
        {
            var save = Save(); var match = Plan("spot_001");
            var ui = new GameObject("Spot Editor Test");
            try
            {
                var document = ui.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("PWManagerRuntime/PWManagerPanelSettings");
                var host = document.rootVisualElement;
                var editor = new MatchSpotEditor(host, save, match, save.CurrentDate);
                Assert.That(host.Q<DropdownField>("spot-type-0").label, Is.EqualTo("스팟 종류"));
                Assert.That(host.Q<Label>("spot-preview-0").text, Does.Contain("가람").And.Contain("나래").And.Not.Contain("{"));
                host.Q<DropdownField>("spot-type-0").index = 10;
                Assert.That(host.Q<Label>("spot-preview-0").text, Does.Contain("마루").And.Contain("가람"));
                Assert.That(match.Spots[0].SpotId, Is.EqualTo("spot_001"));
                match.Spots = editor.Read();
                Assert.That(match.Spots[0].SpotId, Is.EqualTo("spot_011"));
                Assert.That(match.Spots[0].ActorId, Is.EqualTo("e"));
                Assert.That(match.Spots[0].Phase, Is.EqualTo(MatchSpotPhase.Middle));
                editor = new MatchSpotEditor(host, save, match, save.CurrentDate);
                Assert.That(host.Q<DropdownField>("spot-type-0").index, Is.EqualTo(10));
                host.Q<DropdownField>("spot-actor-0").index = 0;
                Assert.Throws<InvalidOperationException>(() => editor.Read());
            }
            finally { UnityEngine.Object.DestroyImmediate(ui); }
        }

        [UnityTest]
        public IEnumerator Editor_SpotTypeAndPreviewHaveVisibleNonOverlappingLayout()
        {
            var window = ScriptableObject.CreateInstance<UnityEditor.EditorWindow>();
            try
            {
                window.position = new Rect(0, 0, 700, 700);
                window.Show();
                var host = window.rootVisualElement;
                host.style.width = 700;
                host.style.height = 700;
                host.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/Dashboard"));
                host.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/PWManagerTheme"));
                _ = new MatchSpotEditor(host, Save(), Plan("spot_001"), new GameDate(2026, 9, 3));
                yield return null;
                yield return null;
                var choice = host.Q<DropdownField>("spot-type-0");
                var preview = host.Q<Label>("spot-preview-0");
                var roles = host.Q(className: "spot-roles");
                Assert.That(choice.resolvedStyle.height, Is.GreaterThanOrEqualTo(50));
                Assert.That(preview.resolvedStyle.height, Is.GreaterThan(0));
                Assert.That(choice.worldBound.yMax, Is.LessThanOrEqualTo(preview.worldBound.yMin));
                Assert.That(preview.worldBound.yMax, Is.LessThanOrEqualTo(roles.worldBound.yMin));
            }
            finally { window.Close(); }
        }

        private static MatchPlanState Plan(string id) => new()
        {
            MatchTypeId = "matchtype_002", MatchGimmickId = "gimmick_000",
            Sides = { new MatchSideState { Id = "left", MemberIds = { "a", "c" } }, new MatchSideState { Id = "right", MemberIds = { "b", "d" } } },
            Spots = { new PlannedSpotState { SpotId = id, ActorId = "a", TargetId = "b", Phase = SpecialMatchSpotCatalog.Find(id)?.Phases[0] ?? MatchSpotPhase.Middle } }
        };

        private static GameSave Save()
        {
            var save = new GameSave { CurrentDate = new GameDate(2026, 9, 3) };
            var names = new[] { "가람", "나래", "다온", "라온", "마루" };
            for (var i = 0; i < names.Length; i++)
            {
                var wrestler = new WrestlerState { Id = ((char)('a' + i)).ToString() };
                wrestler.Identity.RingName = names[i];
                foreach (var field in typeof(WrestlerAttributesState).GetFields()) field.SetValue(wrestler.Attributes, 12f);
                save.Wrestlers.Add(wrestler);
            }
            return save;
        }
    }
}
