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
    public sealed class WrestlerGeneratorPreviewWindow : EditorWindow
    {
        private const string CatalogPath = "Assets/Game/Data/Static/GameStaticContentCatalog.asset";

        private StaticContentCatalog catalog;
        private StaticContentRegistry content;
        private IReadOnlyList<WrestlerState> candidates = Array.Empty<WrestlerState>();
        private Vector2 listScroll;
        private Vector2 detailScroll;
        private int selectedIndex = -1;
        private int seed = 12345;
        private int year = 2026;
        private int month = 1;
        private int day = 1;

        [MenuItem("PW Manager/Wrestler Generator Preview")]
        public static void Open()
        {
            GetWindow<WrestlerGeneratorPreviewWindow>("Wrestler Generator");
        }

        private void OnEnable()
        {
            catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath);
            if (catalog != null) content = new StaticContentRegistry(catalog);
            minSize = new Vector2(900, 500);
        }

        private void OnGUI()
        {
            DrawControls();
            if (catalog == null)
            {
                EditorGUILayout.HelpBox($"Static content catalog was not found at {CatalogPath}.", MessageType.Error);
                return;
            }

            if (candidates.Count == 0)
            {
                EditorGUILayout.HelpBox("Generate candidates to preview the current wrestler generation rules.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawCandidateList();
                DrawCandidateDetails();
            }
        }

        private void DrawControls()
        {
            EditorGUILayout.LabelField("Generation Settings", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                seed = EditorGUILayout.IntField("Seed", seed, GUILayout.Width(220));
                year = EditorGUILayout.IntField("Year", year, GUILayout.Width(170));
                month = EditorGUILayout.IntField("Month", month, GUILayout.Width(150));
                day = EditorGUILayout.IntField("Day", day, GUILayout.Width(150));
                if (GUILayout.Button("Generate 24 Candidates", GUILayout.Height(22))) Generate();
            }
        }

        private void Generate()
        {
            try
            {
                var date = new GameDate(year, month, day);
                var generator = new WrestlerGenerator(content, seed);
                candidates = generator.GenerateInitialCandidates(date);
                selectedIndex = candidates.Count > 0 ? 0 : -1;
                Debug.Log($"Generated {candidates.Count} wrestler candidates with seed {seed}.");
            }
            catch (Exception exception)
            {
                candidates = Array.Empty<WrestlerState>();
                selectedIndex = -1;
                Debug.LogException(exception);
                ShowNotification(new GUIContent("Generation failed. Check the Console."));
            }
        }

        private void DrawCandidateList()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(position.width * .62f)))
            {
                EditorGUILayout.LabelField($"Candidates ({candidates.Count})", EditorStyles.boldLabel);
                DrawRow("#", "Name", "Gender", "Age", "Match", "Promo", "Style", true);
                listScroll = EditorGUILayout.BeginScrollView(listScroll);
                for (var i = 0; i < candidates.Count; i++)
                {
                    var wrestler = candidates[i];
                    var selected = i == selectedIndex;
                    var previousColor = GUI.backgroundColor;
                    if (selected) GUI.backgroundColor = new Color(.55f, .75f, 1f);
                    if (GUILayout.Button(GUIContent.none, GUILayout.Height(22), GUILayout.ExpandWidth(true))) selectedIndex = i;
                    var row = GUILayoutUtility.GetLastRect();
                    GUI.backgroundColor = previousColor;
                    DrawRowAt(row, (i + 1).ToString(), wrestler.Identity.RingName, wrestler.Identity.Gender.ToString(),
                        GetAge(wrestler.Identity.BirthDate).ToString(), WrestlerOverallCalculator.Match(wrestler).ToString("F1"),
                        WrestlerOverallCalculator.Promo(wrestler).ToString("F1"), GetStyleName(wrestler.Presentation.WrestlingStyleId));
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawCandidateDetails()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true)))
            {
                EditorGUILayout.LabelField("Selected Candidate", EditorStyles.boldLabel);
                if (selectedIndex < 0 || selectedIndex >= candidates.Count) return;
                var wrestler = candidates[selectedIndex];
                detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
                Field("Name", wrestler.Identity.RingName);
                Field("ID", wrestler.Identity.Id);
                Field("Gender", wrestler.Identity.Gender.ToString());
                Field("Background", wrestler.Identity.Background.ToString());
                Field("Birth date", FormatDate(wrestler.Identity.BirthDate));
                Field("Height / Weight", $"{wrestler.Identity.HeightCm} cm / {wrestler.Identity.WeightKg} kg");
                Field("Body type", wrestler.Identity.BodyType.ToString());
                Field("Career", $"{wrestler.Identity.CareerYears} years");
                Field("Match archetype", wrestler.Presentation.MatchArchetype.ToString());
                Field("Promo archetype", wrestler.Presentation.PromoArchetype.ToString());
                Field("Match", $"{WrestlerOverallCalculator.Match(wrestler):F2} / potential {wrestler.Growth.MatchPotentialCap:F1}");
                Field("Promo", $"{WrestlerOverallCalculator.Promo(wrestler):F2} / potential {wrestler.Growth.PromoPotentialCap:F1}");
                Field("Style", GetStyleName(wrestler.Presentation.WrestlingStyleId));
                Field("Traits", Names(wrestler.Presentation.TraitIds, id => content.Traits[id].DisplayName));
                Field("Signature", Names(wrestler.Presentation.SignatureMoveIds, id => content.Moves[id].DisplayName));
                Field("Finisher", Names(wrestler.Presentation.FinisherMoveIds, id => content.Moves[id].DisplayName));
                Field("Expires", FormatDate(wrestler.Identity.ExpiryDate.Value));
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Match Attributes", EditorStyles.boldLabel);
                Field("Psychology / Improvisation", $"{wrestler.Attributes.RingPsychology:F2} / {wrestler.Attributes.RingImprovisation:F2}");
                Field("Technical / Brawling", $"{wrestler.Attributes.Technical:F2} / {wrestler.Attributes.Brawling:F2}");
                Field("Power / High Flying", $"{wrestler.Attributes.Power:F2} / {wrestler.Attributes.HighFlying:F2}");
                Field("Spot / Specialty", $"{wrestler.Attributes.SpotWork:F2} / {wrestler.Attributes.SpecialtyMatches:F2}");
                Field("Selling / Stamina", $"{wrestler.Attributes.Selling:F2} / {wrestler.Attributes.Stamina:F2}");
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Promo Attributes", EditorStyles.boldLabel);
                Field("Charisma / Mic Work", $"{wrestler.Attributes.Charisma:F2} / {wrestler.Attributes.MicWork:F2}");
                Field("Improvisation / Acting", $"{wrestler.Attributes.Improvisation:F2} / {wrestler.Attributes.Acting:F2}");
                Field("Face / Heel Work", $"{wrestler.Attributes.FaceWork:F2} / {wrestler.Attributes.HeelWork:F2}");
                Field("Comedy", wrestler.Attributes.Comedy.ToString("F2"));
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawRow(string number, string name, string gender, string age, string match, string promo, string style, bool header)
        {
            var rect = EditorGUILayout.GetControlRect(false, 20);
            if (header) EditorGUI.DrawRect(rect, new Color(.18f, .18f, .18f));
            DrawRowAt(rect, number, name, gender, age, match, promo, style, header ? EditorStyles.boldLabel : EditorStyles.label);
        }

        private static void DrawRowAt(Rect rect, string number, string name, string gender, string age, string match, string promo, string style, GUIStyle guiStyle = null)
        {
            guiStyle ??= EditorStyles.label;
            var widths = new[] { .05f, .29f, .11f, .08f, .10f, .10f, .27f };
            var values = new[] { number, name, gender, age, match, promo, style };
            var x = rect.x + 4;
            for (var i = 0; i < values.Length; i++)
            {
                var width = (rect.width - 8) * widths[i];
                GUI.Label(new Rect(x, rect.y + 1, width, rect.height - 2), values[i], guiStyle);
                x += width;
            }
        }

        private string GetStyleName(string id) => content.Styles.TryGetValue(id, out var style) ? style.DisplayName : id;
        private static string Names(IEnumerable<string> ids, Func<string, string> getName) => string.Join(", ", ids.Select(getName));
        private static string FormatDate(GameDate date) => $"{date.Year:D4}-{date.Month:D2}-{date.Day:D2}";
        private int GetAge(GameDate birthDate) => birthDate.AgeOn(new GameDate(year, month, day));
        private static void Field(string label, string value) => EditorGUILayout.LabelField(label, value);
    }
}
