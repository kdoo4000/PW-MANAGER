using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Data.Catalogs;
using PWManager.Data.Definitions;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEditor;
using UnityEngine;

namespace PWManager.Editor
{
    public sealed class ShowPlanningPreviewWindow : EditorWindow
    {
        private const string CatalogPath = "Assets/Game/Data/Static/GameStaticContentCatalog.asset";
        private StaticContentRegistry content;
        private GameSave save;
        private ShowState show;
        private IReadOnlyList<ShowValidationIssue> issues = Array.Empty<ShowValidationIssue>();
        private string selectedEventId;
        private string loadedEventId;
        private string draggedEventId;
        private int durationLimit = 120;
        private int rosterSeed = 9090;
        private int rosterSize = 12;
        private int matchTypeIndex;
        private int matchGimmickIndex;
        private int tagCompositionIndex;
        private int matchParticipants = 2;
        private int matchMembersPerTeam;
        private int matchTeamCount;
        private int matchDuration = 20;
        private MatchFinishType matchFinishType = MatchFinishType.Pinfall;
        private int winningSideIndex;
        private int finishPerformerIndex;
        private int loserTargetIndex;
        private readonly List<int> assignedWrestlerIndices = new();
        private MatchResultState technicalResult;
        private float evaluationSpotScore;
        private int evaluationSeed = 1234;
        private int promoDuration = 10;
        private PromoPurpose promoPurpose;
        private PromoPresentation promoPresentation;
        private Vector2 setupScroll;
        private Vector2 timelineScroll;
        private Vector2 issueScroll;

        [MenuItem("PW Manager/Show Planning Preview")]
        public static void Open() => GetWindow<ShowPlanningPreviewWindow>("Show Planning Preview");

        private void OnEnable()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath);
            if (catalog != null) content = new StaticContentRegistry(catalog);
            minSize = new Vector2(880, 600);
        }

        private void OnGUI()
        {
            if (content == null) { EditorGUILayout.HelpBox("Static content catalog is missing or invalid.", MessageType.Error); return; }
            DrawShowControls();
            if (show == null) { EditorGUILayout.HelpBox("Create an empty show, then add matches and promos independently.", MessageType.Info); return; }
            EditorGUILayout.Space(8);
            var contentHeight = Mathf.Max(300f, position.height - 245f);
            using (new EditorGUILayout.HorizontalScope(GUILayout.Height(contentHeight)))
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox,
                    GUILayout.Width(position.width * .58f), GUILayout.ExpandHeight(true)))
                {
                    setupScroll = EditorGUILayout.BeginScrollView(setupScroll, EditorStyles.helpBox,
                        GUILayout.ExpandHeight(true));
                    DrawSelectedEvent();
                    EditorGUILayout.EndScrollView();
                }
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox,
                    GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true))) DrawTimeline(contentHeight);
            }
            DrawValidation();
        }

        private void DrawShowControls()
        {
            EditorGUILayout.LabelField("Show", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                durationLimit = EditorGUILayout.IntField("Duration Limit", durationLimit, GUILayout.Width(260));
                rosterSeed = EditorGUILayout.IntField("Roster Seed", rosterSeed, GUILayout.Width(210));
                rosterSize = EditorGUILayout.IntSlider("Roster", rosterSize, 8, 20, GUILayout.Width(250));
                if (GUILayout.Button(show == null ? "Create Test Show" : "Regenerate Roster & Show", GUILayout.Width(180))) CreateShow();
                using (new EditorGUI.DisabledScope(show == null))
                {
                    if (GUILayout.Button("Validate")) ValidateCard();
                    if (GUILayout.Button("Confirm")) ConfirmCard();
                }
            }
            if (show == null) return;
            var used = show.CalculatePlannedDuration(save.ShowEvents);
            EditorGUILayout.LabelField($"Status: {show.Status}    Version: {show.ShowVersion}    Used: {used} / {show.DurationLimit} min    Remaining: {show.DurationLimit - used} min");
        }

        private void DrawMatchEditorFields(ShowEventState showEvent)
        {
            var types = content.MatchTypes.Values.OrderBy(x => x.Id).ToArray();
            matchTypeIndex = Mathf.Clamp(matchTypeIndex, 0, types.Length - 1);
            matchTypeIndex = EditorGUILayout.Popup("Format", matchTypeIndex, types.Select(x => x.DisplayName).ToArray());
            var type = types[matchTypeIndex];
            if (type.MinimumTeamCount > 0)
            {
                var compositions = SupportedTeamCompositions(type, save.Wrestlers.Count);
                tagCompositionIndex = Mathf.Clamp(tagCompositionIndex, 0, compositions.Count - 1);
                var composition = compositions[tagCompositionIndex];
                tagCompositionIndex = EditorGUILayout.Popup("Teams", tagCompositionIndex,
                    compositions.Select(x => MatchDisplayName(type, x.MembersPerTeam, x.TeamCount)).ToArray());
                composition = compositions[tagCompositionIndex];
                matchMembersPerTeam = composition.MembersPerTeam;
                matchTeamCount = composition.TeamCount;
                matchParticipants = matchMembersPerTeam * matchTeamCount;
            }
            else
            {
                matchMembersPerTeam = 0;
                matchTeamCount = 0;
                matchParticipants = EditorGUILayout.IntSlider("Wrestlers", matchParticipants,
                    type.MinimumParticipants, Math.Min(type.MaximumParticipants, save.Wrestlers.Count));
            }
            EnsureAssignmentSlots(matchParticipants);
            DrawSideAssignments(type);

            var gimmicks = content.MatchGimmicks.Values
                .Where(x => matchParticipants >= x.MinimumParticipants && matchParticipants <= x.MaximumParticipants &&
                    x.CompatibleMatchTypeIds.Contains(type.Id))
                .OrderBy(x => x.Id).ToArray();
            var gimmickNames = gimmicks.Select(x => x.DisplayName).ToArray();
            matchGimmickIndex = Mathf.Clamp(matchGimmickIndex, 0, gimmickNames.Length - 1);
            matchGimmickIndex = EditorGUILayout.Popup("Gimmick", matchGimmickIndex, gimmickNames);
            var gimmick = gimmicks[matchGimmickIndex];
            DrawFinishControls(type, gimmick);

            using (new EditorGUILayout.HorizontalScope())
            {
                matchDuration = EditorGUILayout.IntField("Duration", matchDuration);
                var hasDuplicateAssignments = assignedWrestlerIndices.Distinct().Count() != assignedWrestlerIndices.Count;
                using (new EditorGUI.DisabledScope(hasDuplicateAssignments))
                    if (GUILayout.Button("Apply Changes", GUILayout.Width(120))) UpdateMatch(showEvent, type, gimmick);
            }
            DrawTechnicalEvaluation(showEvent, type);
        }

        private void DrawTimeline(float panelHeight)
        {
            EditorGUILayout.LabelField($"Timeline ({show.TimelineEventIds.Count})", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ Match")) AddDefaultMatch();
                if (GUILayout.Button("+ Promo")) AddPromo();
            }
            EditorGUILayout.LabelField("Drag an event onto another event to reorder it.", EditorStyles.miniLabel);
            timelineScroll = EditorGUILayout.BeginScrollView(timelineScroll, EditorStyles.helpBox,
                GUILayout.ExpandWidth(true), GUILayout.Height(Mathf.Max(100f, panelHeight - 72f)));
            for (var index = 0; index < show.TimelineEventIds.Count; index++)
            {
                var showEvent = save.ShowEvents.Single(x => x.Id == show.TimelineEventIds[index]);
                var previous = GUI.backgroundColor;
                if (showEvent.Id == selectedEventId) GUI.backgroundColor = new Color(.55f, .75f, 1f);
                var row = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.button, GUILayout.Height(32), GUILayout.ExpandWidth(true));
                if (GUI.Button(row, $"{index + 1}. {EventName(showEvent)}   {showEvent.PlannedDuration} min")) SelectEvent(showEvent.Id);
                GUI.backgroundColor = previous;
                HandleTimelineDrag(row, showEvent.Id, index);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawTechnicalEvaluation(ShowEventState showEvent, MatchTypeDefinition type)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Technical Evaluation", EditorStyles.boldLabel);
            evaluationSpotScore = EditorGUILayout.FloatField("Spot Score", evaluationSpotScore);
            evaluationSeed = EditorGUILayout.IntField("Result Seed", evaluationSeed);
            var canEvaluate = show.ShowVersion > 0;
            using (new EditorGUI.DisabledScope(!canEvaluate))
                if (GUILayout.Button("Evaluate Technical Quality")) Execute(() =>
                {
                    technicalResult = new MatchEvaluator(content, content, findMove: content.GetMoveRules)
                        .EvaluateTechnical(save, showEvent.Id, evaluationSpotScore, evaluationSeed);
                });
            if (show.ShowVersion < 1)
                EditorGUILayout.HelpBox("Confirm the show before evaluating the match.", MessageType.Info);

            if (technicalResult?.ShowEventId != showEvent.Id) return;
            var breakdown = technicalResult.TechnicalEvaluation;
            EditorGUILayout.LabelField($"Final Match Quality: {technicalResult.FinalMatchQuality:F2} / 20", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Time: {technicalResult.ActualMatchDuration} min (planned {technicalResult.PlannedDuration} min)");
            EditorGUILayout.LabelField($"Performance {breakdown.Performance:F2}    Structure {breakdown.Structure:F2}    Duration {breakdown.Duration:+0.00;-0.00;0.00}    Spot {breakdown.Spot:+0.00;-0.00;0.00}");
            foreach (var performance in technicalResult.WrestlerPerformances)
                EditorGUILayout.LabelField(
                    $"{WrestlerName(performance.WrestlerId)}: base {performance.BaseRoutine:F2}, variance {performance.PerformanceVariance:+0.00;-0.00;0.00}, stamina -{performance.StaminaPenalty:F2}, effective {performance.EffectiveRoutine:F2}");
            foreach (var profile in technicalResult.ParticipantProfiles.Where(x => x.MemberIds.Count > 1))
                EditorGUILayout.LabelField(
                    $"Side: {string.Join(" & ", profile.MemberIds.Select(WrestlerName))} — routine {profile.RoutineExecution:F2}, psychology {profile.RingPsychology:F2}, chemistry {profile.AppliedChemistry:F0}");
        }

        private void DrawSelectedEvent()
        {
            EditorGUILayout.LabelField("Selected Event", EditorStyles.boldLabel);
            var showEvent = save.ShowEvents.SingleOrDefault(x => x.Id == selectedEventId);
            if (showEvent == null) { EditorGUILayout.LabelField("Select an event from the timeline."); return; }
            if (loadedEventId != showEvent.Id) LoadSelectedEvent(showEvent);
            EditorGUILayout.LabelField("Type", showEvent.EventType.ToString());
            if (showEvent.EventType == ShowEventType.Match) DrawMatchEditorFields(showEvent);
            else DrawPromoEditor(showEvent);
            var index = show.TimelineEventIds.IndexOf(showEvent.Id);
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(index == 0)) if (GUILayout.Button("Move Up")) Execute(() => Service().MoveEvent(save, show.Id, index, index - 1));
                using (new EditorGUI.DisabledScope(index == show.TimelineEventIds.Count - 1)) if (GUILayout.Button("Move Down")) Execute(() => Service().MoveEvent(save, show.Id, index, index + 1));
            }
            if (GUILayout.Button("Delete Event"))
            {
                Execute(() => Service().RemoveEvent(save, show.Id, showEvent.Id));
                selectedEventId = null;
                loadedEventId = null;
            }
        }

        private void DrawValidation()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField($"Validation ({issues.Count})", EditorStyles.boldLabel);
            issueScroll = EditorGUILayout.BeginScrollView(issueScroll, EditorStyles.helpBox, GUILayout.Height(120));
            if (issues.Count == 0) EditorGUILayout.LabelField("No validation issues.");
            foreach (var issue in issues) EditorGUILayout.HelpBox($"[{issue.Code}] {issue.Message}", issue.Severity == ShowValidationSeverity.Error ? MessageType.Error : MessageType.Warning);
            EditorGUILayout.EndScrollView();
        }

        private void CreateShow() => Execute(() =>
        {
            save = CreateInitialSave();
            show = Service().CreateDraft(save, save.Schedules[0].Id, "Preview Show", save.VenueContracts[0].Id, durationLimit);
            assignedWrestlerIndices.Clear();
            selectedEventId = null;
            technicalResult = null;
            issues = Array.Empty<ShowValidationIssue>();
        });

        private void AddMatch(MatchTypeDefinition type, MatchGimmickDefinition gimmick) => Execute(() =>
        {
            var sides = BuildSides(type);
            var winningSide = matchFinishType == MatchFinishType.Draw ? null : sides[Mathf.Clamp(winningSideIndex, 0, sides.Count - 1)];
            var finishPerformer = winningSide?.MemberIds[Mathf.Clamp(finishPerformerIndex, 0, winningSide.MemberIds.Count - 1)];
            var loserCandidates = winningSide == null ? new List<string>() : sides.Where(x => x.Id != winningSide.Id).SelectMany(x => x.MemberIds).ToList();
            selectedEventId = Service().AddMatch(save, show.Id, new MatchPlanState
            {
                MatchTypeId = type.Id, MatchGimmickId = gimmick?.Id, Sides = sides,
                WinningSideId = winningSide?.Id, FinishPerformerId = finishPerformer,
                LoserTargetId = matchFinishType == MatchFinishType.Draw ? null : loserCandidates[Mathf.Clamp(loserTargetIndex, 0, loserCandidates.Count - 1)],
                FinishType = matchFinishType
            }, matchDuration).Id;
            issues = Service().Validate(save, show.Id);
            loadedEventId = null;
            technicalResult = null;
        });

        private void AddDefaultMatch()
        {
            var type = content.MatchTypes.Values.OrderBy(x => x.Id).First(x => x.MinimumTeamCount == 0);
            var gimmick = content.MatchGimmicks.Values.Single(x => x.Id == "gimmick_000");
            matchTypeIndex = Array.IndexOf(content.MatchTypes.Values.OrderBy(x => x.Id).ToArray(), type);
            matchParticipants = 2;
            matchMembersPerTeam = 0;
            matchTeamCount = 0;
            matchFinishType = MatchFinishType.Pinfall;
            winningSideIndex = 0;
            finishPerformerIndex = 0;
            loserTargetIndex = 0;
            matchDuration = 20;
            assignedWrestlerIndices.Clear();
            var start = save.MatchPlans.Count % save.Wrestlers.Count;
            assignedWrestlerIndices.Add(start);
            assignedWrestlerIndices.Add((start + 1) % save.Wrestlers.Count);
            AddMatch(type, gimmick);
        }

        private void UpdateMatch(ShowEventState showEvent, MatchTypeDefinition type, MatchGimmickDefinition gimmick) => Execute(() =>
        {
            var sides = BuildSides(type);
            var winningSide = matchFinishType == MatchFinishType.Draw ? null : sides[Mathf.Clamp(winningSideIndex, 0, sides.Count - 1)];
            var finishPerformer = winningSide?.MemberIds[Mathf.Clamp(finishPerformerIndex, 0, winningSide.MemberIds.Count - 1)];
            var loserCandidates = winningSide == null ? new List<string>() : sides.Where(x => x.Id != winningSide.Id).SelectMany(x => x.MemberIds).ToList();
            Service().UpdateMatch(save, show.Id, showEvent.Id, new MatchPlanState
            {
                MatchTypeId = type.Id,
                MatchGimmickId = gimmick.Id,
                Sides = sides,
                WinningSideId = winningSide?.Id,
                FinishPerformerId = finishPerformer,
                LoserTargetId = winningSide == null ? null : loserCandidates[Mathf.Clamp(loserTargetIndex, 0, loserCandidates.Count - 1)],
                FinishType = matchFinishType
            }, matchDuration);
            issues = Service().Validate(save, show.Id);
            loadedEventId = null;
            technicalResult = null;
        });

        private void AddPromo() => Execute(() =>
        {
            var promo = new PromoPlanState
            {
                Purpose = promoPurpose, Presentation = promoPresentation,
                ParticipantIds = { save.Wrestlers[(save.PromoPlans.Count + 6) % save.Wrestlers.Count].Id }
            };
            if (promoPurpose == PromoPurpose.SponsorAdvertisement) promo.SponsorRequirementId = "sponsor_requirement_preview";
            selectedEventId = Service().AddPromo(save, show.Id, promo, promoDuration).Id;
            issues = Service().Validate(save, show.Id);
            loadedEventId = null;
        });

        private void DrawPromoEditor(ShowEventState showEvent)
        {
            var promo = save.PromoPlans.Single(x => x.Id == showEvent.DetailId);
            promoPurpose = (PromoPurpose)EditorGUILayout.EnumPopup("Purpose", promoPurpose);
            promoPresentation = (PromoPresentation)EditorGUILayout.EnumPopup("Presentation", promoPresentation);
            promoDuration = EditorGUILayout.IntField("Duration", promoDuration);
            var participant = save.Wrestlers.FindIndex(x => x.Id == promo.ParticipantIds.FirstOrDefault());
            participant = EditorGUILayout.Popup("Participant", Mathf.Max(0, participant), save.Wrestlers.Select(x => x.Identity.RingName).ToArray());
            if (GUILayout.Button("Apply Changes")) Execute(() =>
            {
                promo.Purpose = promoPurpose;
                promo.Presentation = promoPresentation;
                promo.ParticipantIds = new List<string> { save.Wrestlers[participant].Id };
                promo.SponsorRequirementId = promoPurpose == PromoPurpose.SponsorAdvertisement ? "sponsor_requirement_preview" : null;
                Service().SetEventDuration(save, show.Id, showEvent.Id, promoDuration);
                issues = Service().Validate(save, show.Id);
                loadedEventId = null;
            });
        }

        private void SelectEvent(string eventId)
        {
            selectedEventId = eventId;
            loadedEventId = null;
        }

        private void LoadSelectedEvent(ShowEventState showEvent)
        {
            loadedEventId = showEvent.Id;
            if (showEvent.EventType == ShowEventType.Promo)
            {
                var promo = save.PromoPlans.Single(x => x.Id == showEvent.DetailId);
                promoPurpose = promo.Purpose;
                promoPresentation = promo.Presentation;
                promoDuration = showEvent.PlannedDuration;
                return;
            }

            var match = save.MatchPlans.Single(x => x.Id == showEvent.DetailId);
            var types = content.MatchTypes.Values.OrderBy(x => x.Id).ToArray();
            matchTypeIndex = Math.Max(0, Array.FindIndex(types, x => x.Id == match.MatchTypeId));
            var type = types[matchTypeIndex];
            matchParticipants = match.Sides.SelectMany(x => x.MemberIds).Count();
            matchTeamCount = type.MinimumTeamCount > 0 ? match.Sides.Count : 0;
            matchMembersPerTeam = type.MinimumTeamCount > 0 ? match.Sides.FirstOrDefault()?.MemberIds.Count ?? 0 : 0;
            if (type.MinimumTeamCount > 0)
            {
                var compositions = SupportedTeamCompositions(type, save.Wrestlers.Count);
                tagCompositionIndex = Math.Max(0, compositions.FindIndex(x => x.TeamCount == matchTeamCount && x.MembersPerTeam == matchMembersPerTeam));
            }
            assignedWrestlerIndices.Clear();
            assignedWrestlerIndices.AddRange(match.Sides.SelectMany(x => x.MemberIds)
                .Select(id => Math.Max(0, save.Wrestlers.FindIndex(x => x.Id == id))));
            var gimmicks = content.MatchGimmicks.Values.Where(x => matchParticipants >= x.MinimumParticipants &&
                matchParticipants <= x.MaximumParticipants && x.CompatibleMatchTypeIds.Contains(type.Id)).OrderBy(x => x.Id).ToArray();
            matchGimmickIndex = Math.Max(0, Array.FindIndex(gimmicks, x => x.Id == match.MatchGimmickId));
            matchFinishType = match.FinishType;
            winningSideIndex = Math.Max(0, match.Sides.FindIndex(x => x.Id == match.WinningSideId));
            var winningSide = match.Sides.ElementAtOrDefault(winningSideIndex);
            finishPerformerIndex = Math.Max(0, winningSide?.MemberIds.FindIndex(x => x == match.FinishPerformerId) ?? 0);
            var loserCandidates = match.Sides.Where(x => x.Id != winningSide?.Id).SelectMany(x => x.MemberIds).ToList();
            loserTargetIndex = Math.Max(0, loserCandidates.FindIndex(x => x == match.LoserTargetId));
            matchDuration = showEvent.PlannedDuration;
        }

        private void HandleTimelineDrag(Rect row, string eventId, int targetIndex)
        {
            var current = Event.current;
            if (current.type == EventType.MouseDrag && row.Contains(current.mousePosition))
            {
                draggedEventId = eventId;
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData("PWManager.ShowEvent", eventId);
                DragAndDrop.StartDrag(EventName(save.ShowEvents.Single(x => x.Id == eventId)));
                current.Use();
                return;
            }
            if (!row.Contains(current.mousePosition) || DragAndDrop.GetGenericData("PWManager.ShowEvent") is not string draggedId) return;
            if (current.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                current.Use();
            }
            else if (current.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                var oldIndex = show.TimelineEventIds.IndexOf(draggedId);
                if (oldIndex >= 0 && oldIndex != targetIndex) Execute(() => Service().MoveEvent(save, show.Id, oldIndex, targetIndex));
                draggedEventId = null;
                DragAndDrop.SetGenericData("PWManager.ShowEvent", null);
                current.Use();
            }
        }

        private void ValidateCard() => Execute(() => issues = Service().Validate(save, show.Id));
        private void ConfirmCard() => Execute(() => { Service(1234).Confirm(save, show.Id); issues = Service().Validate(save, show.Id); ShowNotification(new GUIContent("Show confirmed.")); });
        private ShowPlanningService Service(int? seed = null) => new(content, randomSeed: seed);

        private void EnsureAssignmentSlots(int count)
        {
            while (assignedWrestlerIndices.Count < count) assignedWrestlerIndices.Add(assignedWrestlerIndices.Count % save.Wrestlers.Count);
            if (assignedWrestlerIndices.Count > count) assignedWrestlerIndices.RemoveRange(count, assignedWrestlerIndices.Count - count);
            for (var i = 0; i < assignedWrestlerIndices.Count; i++)
                assignedWrestlerIndices[i] = Mathf.Clamp(assignedWrestlerIndices[i], 0, save.Wrestlers.Count - 1);
        }

        private void DrawSideAssignments(MatchTypeDefinition type)
        {
            var teamMatch = type.MinimumTeamCount > 0;
            var sideCount = teamMatch ? matchTeamCount : matchParticipants;
            var membersPerSide = teamMatch ? matchMembersPerTeam : 1;
            var wrestlerNames = save.Wrestlers.Select(x => $"{x.Identity.RingName}  (Match {WrestlerOverallCalculator.Match(x):F1})").ToArray();
            EditorGUILayout.LabelField("Side Assignments", EditorStyles.boldLabel);
            for (var side = 0; side < sideCount; side++)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField($"Side {side + 1}", EditorStyles.miniBoldLabel);
                    for (var member = 0; member < membersPerSide; member++)
                    {
                        var slot = side * membersPerSide + member;
                        assignedWrestlerIndices[slot] = EditorGUILayout.Popup($"Member {member + 1}", assignedWrestlerIndices[slot], wrestlerNames);
                    }
                }
            }
            if (assignedWrestlerIndices.Distinct().Count() != assignedWrestlerIndices.Count)
                EditorGUILayout.HelpBox("The same wrestler is assigned more than once.", MessageType.Error);
        }

        private void DrawFinishControls(MatchTypeDefinition type, MatchGimmickDefinition gimmick)
        {
            content.TryGetGimmickRules(gimmick.Id, out var gimmickRules);
            var sideCount = type.MinimumTeamCount > 0 ? matchTeamCount : matchParticipants;
            var allowedFinishes = Enum.GetValues(typeof(MatchFinishType)).Cast<MatchFinishType>()
                .Where(x => MatchFinishRules.IsAllowed(x, type.MinimumTeamCount > 0, matchParticipants, sideCount, gimmickRules)).ToArray();
            var finishIndex = Math.Max(0, Array.IndexOf(allowedFinishes, matchFinishType));
            finishIndex = EditorGUILayout.Popup("Finish", finishIndex, allowedFinishes.Select(x => x.ToString()).ToArray());
            matchFinishType = allowedFinishes[finishIndex];
            if (matchFinishType == MatchFinishType.Draw) return;

            winningSideIndex = EditorGUILayout.Popup("Winning Side", Mathf.Clamp(winningSideIndex, 0, sideCount - 1),
                Enumerable.Range(1, sideCount).Select(x => $"Side {x}").ToArray());
            var sides = BuildSides(type);
            var winningSide = sides[winningSideIndex];
            var performerNames = winningSide.MemberIds.Select(WrestlerName).ToArray();
            finishPerformerIndex = EditorGUILayout.Popup("Finish Performer", Mathf.Clamp(finishPerformerIndex, 0, performerNames.Length - 1), performerNames);
            var loserNames = sides.Where(x => x.Id != winningSide.Id).SelectMany(x => x.MemberIds).Select(WrestlerName).ToArray();
            loserTargetIndex = EditorGUILayout.Popup("Loser Target", Mathf.Clamp(loserTargetIndex, 0, loserNames.Length - 1), loserNames);
        }

        private List<MatchSideState> BuildSides(MatchTypeDefinition type)
        {
            var teamMatch = type.MinimumTeamCount > 0;
            var sideCount = teamMatch ? matchTeamCount : matchParticipants;
            var membersPerSide = teamMatch ? matchMembersPerTeam : 1;
            var sides = new List<MatchSideState>(sideCount);
            for (var side = 0; side < sideCount; side++)
                sides.Add(new MatchSideState
                {
                    Id = $"preview-side-{Guid.NewGuid():N}",
                    MemberIds = Enumerable.Range(0, membersPerSide)
                        .Select(member => save.Wrestlers[assignedWrestlerIndices[side * membersPerSide + member]].Id).ToList()
                });
            return sides;
        }

        private string WrestlerName(string wrestlerId) => save.Wrestlers.Single(x => x.Id == wrestlerId).Identity.RingName;

        private string EventName(ShowEventState showEvent)
        {
            if (showEvent.EventType == ShowEventType.Promo)
            {
                var promo = save.PromoPlans.Single(x => x.Id == showEvent.DetailId);
                var names = string.Join(", ", promo.ParticipantIds.Select(WrestlerName));
                return $"Promo: {names} — {promo.Purpose}";
            }
            var match = save.MatchPlans.Single(x => x.Id == showEvent.DetailId);
            var gimmick = string.IsNullOrEmpty(match.MatchGimmickId) ? null : content.MatchGimmicks[match.MatchGimmickId];
            var gimmickSuffix = gimmick == null || gimmick.Id == "gimmick_000" ? "" : $" [{gimmick.DisplayName}]";
            var sides = match.Sides?.Where(x => x?.MemberIds != null).Select(x =>
                string.Join(" & ", x.MemberIds.Select(WrestlerName))).ToList();
            var matchup = sides != null && sides.Count > 0
                ? string.Join(" vs ", sides)
                : string.Join(" vs ", match.ParticipantIds.Select(WrestlerName));
            return $"Match: {matchup}{gimmickSuffix}";
        }

        private static string MatchDisplayName(MatchTypeDefinition type, int membersPerTeam, int teamCount, int participantCount = 0)
        {
            if (type.Id == "matchtype_001") return participantCount == 2 ? "Singles" : $"{participantCount}-Way";
            if (teamCount == 2) return $"{membersPerTeam}-on-{membersPerTeam} Tag Team";
            if (teamCount > 2) return $"{teamCount}-Way Tag Team ({membersPerTeam} per team)";
            return type.DisplayName;
        }

        private static List<(int MembersPerTeam, int TeamCount)> SupportedTeamCompositions(MatchTypeDefinition type, int availableWrestlers)
        {
            var result = new List<(int MembersPerTeam, int TeamCount)>();
            for (var teamCount = type.MinimumTeamCount; teamCount <= type.MaximumTeamCount; teamCount++)
            for (var members = type.MinimumMembersPerTeam; members <= type.MaximumMembersPerTeam; members++)
            {
                var total = teamCount * members;
                if (total >= type.MinimumParticipants && total <= type.MaximumParticipants && total <= availableWrestlers)
                    result.Add((members, teamCount));
            }
            return result.OrderBy(x => x.MembersPerTeam * x.TeamCount).ThenBy(x => x.TeamCount).ToList();
        }

        private GameSave CreateInitialSave()
        {
            var candidates = new WrestlerGenerator(content, rosterSeed).GenerateInitialCandidates(new GameDate(2026, 6, 1));
            return new GameStartService().CreateInitialSave(new GameStartRequest
            {
                PromotionName = "Preview Promotion", InitialCash = 100000, WorldSeed = rosterSeed, UtcNow = DateTime.UtcNow,
                WrestlerContracts = candidates.Take(rosterSize).Select(x => new InitialWrestlerContractInput { Wrestler = x, ContractEndYear = 2027, MonthlySalary = 500 }).ToList(),
                RegularVenueId = "venue_001", RegularVenueProductionCost = 500,
                RegularShowFrequency = RegularShowFrequency.Monthly, PpvFrequency = PpvFrequency.EveryFourMonths
            });
        }

        private void Execute(Action action)
        {
            try { action(); }
            catch (Exception exception) { Debug.LogException(exception); ShowNotification(new GUIContent(exception.Message)); }
            Repaint();
        }
    }
}
