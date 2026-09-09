using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEditor;
using UnityEngine;

namespace PWManager.Editor
{
    public sealed class ScoutingPreviewWindow : EditorWindow
    {
        private const string CatalogPath = "Assets/Game/Data/Static/GameStaticContentCatalog.asset";

        private StaticContentCatalog catalog;
        private GameSave save;
        private ScoutingService service;
        private Vector2 scroll;
        private int seed = 12345;
        private int scoutLevel = 1;
        private bool filterGender;
        private WrestlerGender gender;
        private bool filterBackground;
        private WrestlerBackground background;
        private readonly HashSet<string> expandedCandidates = new();

        [MenuItem("PW Manager/Scouting Preview")]
        public static void Open() => GetWindow<ScoutingPreviewWindow>("Scouting Preview");

        private void OnEnable()
        {
            catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath);
            minSize = new Vector2(760, 520);
            ResetSimulation();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Scouting Settings", EditorStyles.boldLabel);
            seed = EditorGUILayout.IntField("Seed", seed);
            scoutLevel = EditorGUILayout.IntSlider("Scout Level", scoutLevel, 1, 5);
            filterGender = EditorGUILayout.Toggle("Filter Gender", filterGender);
            if (filterGender) gender = (WrestlerGender)EditorGUILayout.EnumPopup("Gender", gender);
            filterBackground = EditorGUILayout.Toggle("Filter Background", filterBackground);
            if (filterBackground) background = (WrestlerBackground)EditorGUILayout.EnumPopup("Background", background);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reset")) ResetSimulation();
                EditorGUI.BeginDisabledGroup(catalog == null || HasActiveAssignment());
                if (GUILayout.Button("Start Scouting")) Run(StartScouting);
                EditorGUI.EndDisabledGroup();
                EditorGUI.BeginDisabledGroup(!HasActiveAssignment());
                if (GUILayout.Button("Advance to Completion")) Run(CompleteScouting);
                EditorGUI.EndDisabledGroup();
                EditorGUI.BeginDisabledGroup(save?.ScoutCandidates?.Count == 0);
                if (GUILayout.Button("Expire Candidates")) Run(ExpireCandidates);
                EditorGUI.EndDisabledGroup();
            }

            if (catalog == null)
            {
                EditorGUILayout.HelpBox($"Static content catalog was not found at {CatalogPath}.", MessageType.Error);
                return;
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Current State", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Date", Format(save.CurrentDate));
            var assignment = save.ScoutAssignments.LastOrDefault();
            EditorGUILayout.LabelField("Assignment", assignment == null ? "None" : $"{assignment.Status} · {Format(assignment.StartDate)} - {Format(assignment.CompleteDate)}");
            EditorGUILayout.LabelField("Candidates", save.ScoutCandidates.Count.ToString());

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var candidateState in save.ScoutCandidates)
            {
                var wrestler = save.Wrestlers.First(x => x.Id == candidateState.WrestlerId);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(wrestler.Identity.RingName, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Profile", $"{wrestler.Identity.Gender} · {wrestler.Identity.Background} · {wrestler.Identity.HeightCm} cm / {wrestler.Identity.WeightKg} kg");
                    EditorGUILayout.LabelField("Match Style", catalog.WrestlingStyles.FirstOrDefault(x => x?.Id == wrestler.Presentation.WrestlingStyleId)?.DisplayName ?? wrestler.Presentation.WrestlingStyleId);
                    EditorGUILayout.LabelField("Ability", $"Match {Report(candidateState, ScoutValueType.MatchOverall, WrestlerOverallCalculator.Match(wrestler))} · Promo {Report(candidateState, ScoutValueType.PromoOverall, WrestlerOverallCalculator.Promo(wrestler))}");
                    EditorGUILayout.LabelField("Potential", $"Match {Report(candidateState, ScoutValueType.MatchPotential, wrestler.Growth.MatchPotentialCap)} · Promo {Report(candidateState, ScoutValueType.PromoPotential, wrestler.Growth.PromoPotentialCap)}");
                    EditorGUILayout.LabelField("Knowledge / Expires", $"Lv.{candidateState.KnowledgeLevel} · {Format(wrestler.Identity.ExpiryDate.Value)}");
                    var expanded = EditorGUILayout.Foldout(expandedCandidates.Contains(wrestler.Id), "All Attributes", true);
                    if (expanded) expandedCandidates.Add(wrestler.Id); else expandedCandidates.Remove(wrestler.Id);
                    if (expanded) DrawAttributes(wrestler.Attributes, candidateState);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void ResetSimulation()
        {
            save = GameSave.CreateNew(seed, DateTime.UtcNow);
            expandedCandidates.Clear();
            save.Promotion = new PromotionState { Id = Guid.NewGuid().ToString("D"), Name = "Preview Promotion" };
            save.StaffDepartments.Add(new StaffDepartmentState
            {
                Id = Guid.NewGuid().ToString("D"), DepartmentType = StaffDepartmentType.Scout,
                CurrentLevel = scoutLevel, UnlockedLevel = scoutLevel
            });
            if (catalog != null)
                service = new ScoutingService(new WrestlerGenerator(new StaticContentRegistry(catalog), seed), catalog.ScoutTeamLevels);
            Repaint();
        }

        private void StartScouting()
        {
            save.StaffDepartments[0].CurrentLevel = scoutLevel;
            save.StaffDepartments[0].UnlockedLevel = scoutLevel;
            service.Start(save, new ScoutSearchConditions
            {
                HasGender = filterGender, Gender = gender,
                HasBackground = filterBackground, Background = background
            });
        }

        private void CompleteScouting()
        {
            save.CurrentDate = save.ScoutAssignments.Last(x => x.Status == ScoutAssignmentStatus.InProgress).CompleteDate;
            service.CompleteDue(save);
        }

        private void ExpireCandidates()
        {
            save.CurrentDate = save.Wrestlers.Where(x => x.Identity.ExpiryDate.HasValue)
                .Select(x => x.Identity.ExpiryDate.Value).OrderBy(x => x).Last().AddDays(1);
            service.RemoveExpiredCandidates(save);
        }

        private bool HasActiveAssignment() => save?.ScoutAssignments?.Any(x => x?.Status == ScoutAssignmentStatus.InProgress) == true;

        private void Run(Action action)
        {
            try { action(); }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowNotification(new GUIContent(exception.Message));
            }
            Repaint();
        }

        private static string Format(GameDate date) => $"{date.Year:D4}-{date.Month:D2}-{date.Day:D2}";

        private static string Rating(float value) => $"{value:F2} ({WrestlerOverallCalculator.Grade(value)})";

        private static void DrawAttributes(WrestlerAttributesState attributes, ScoutCandidateState candidate)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Match", EditorStyles.boldLabel);
            Attribute("Ring Psychology", ScoutValueType.RingPsychology, attributes.RingPsychology, candidate);
            Attribute("Ring Improvisation", ScoutValueType.RingImprovisation, attributes.RingImprovisation, candidate);
            Attribute("Technical", ScoutValueType.Technical, attributes.Technical, candidate);
            Attribute("Brawling", ScoutValueType.Brawling, attributes.Brawling, candidate);
            Attribute("Power", ScoutValueType.Power, attributes.Power, candidate);
            Attribute("High Flying", ScoutValueType.HighFlying, attributes.HighFlying, candidate);
            Attribute("Spot Work", ScoutValueType.SpotWork, attributes.SpotWork, candidate);
            Attribute("Specialty Matches", ScoutValueType.SpecialtyMatches, attributes.SpecialtyMatches, candidate);
            Attribute("Selling", ScoutValueType.Selling, attributes.Selling, candidate);
            Attribute("Stamina", ScoutValueType.Stamina, attributes.Stamina, candidate);
            EditorGUILayout.LabelField("Promo", EditorStyles.boldLabel);
            Attribute("Charisma", ScoutValueType.Charisma, attributes.Charisma, candidate);
            Attribute("Mic Work", ScoutValueType.MicWork, attributes.MicWork, candidate);
            Attribute("Improvisation", ScoutValueType.Improvisation, attributes.Improvisation, candidate);
            Attribute("Acting", ScoutValueType.Acting, attributes.Acting, candidate);
            Attribute("Face Work", ScoutValueType.FaceWork, attributes.FaceWork, candidate);
            Attribute("Heel Work", ScoutValueType.HeelWork, attributes.HeelWork, candidate);
            Attribute("Comedy", ScoutValueType.Comedy, attributes.Comedy, candidate);
            EditorGUI.indentLevel--;
        }

        private static void Attribute(string name, ScoutValueType type, float value, ScoutCandidateState candidate) =>
            EditorGUILayout.LabelField(name, Report(candidate, type, value));

        private static string Report(ScoutCandidateState candidate, ScoutValueType type, float actual)
        {
            var estimate = candidate.ValueEstimates.FirstOrDefault(x => x?.Type == type);
            if (estimate == null) return $"Actual {Rating(actual)} · Report —";
            var report = Math.Abs(estimate.Maximum - estimate.Minimum) < .01f
                ? Rating(estimate.Minimum)
                : $"{estimate.Minimum:F1}~{estimate.Maximum:F1} ({WrestlerOverallCalculator.Grade(estimate.Minimum)}~{WrestlerOverallCalculator.Grade(estimate.Maximum)})";
            return $"Actual {Rating(actual)} · Report {report}";
        }

    }
}
