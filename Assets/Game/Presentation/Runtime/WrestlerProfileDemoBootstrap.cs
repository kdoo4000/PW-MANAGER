using System;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Domain.Models;
using UnityEngine;
using UnityEngine.UIElements;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace PWManager.Presentation
{
    public sealed class WrestlerProfileDemoBootstrap : MonoBehaviour
    {
        private GameSave previousSave;
        private GameSave demoSave;

        private void Start()
        {
            previousSave = DashboardSession.ActiveSave;
            var catalog = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog");
            demoSave = ShowTestSaveFactory.Create(catalog, 20260906);
            demoSave.Promotion.Name = "PWM";
            demoSave.Promotion.Abbreviation = "PWM";
            var wrestler = demoSave.Wrestlers[0];
            ConfigureDemo(wrestler);
            DashboardSession.SetActiveSave(demoSave, transient: true);

            var ui = new GameObject("Dashboard UI");
            ui.SetActive(false);
            ui.transform.SetParent(transform, false);
            var document = ui.AddComponent<UIDocument>();
            document.panelSettings = Resources.Load<PanelSettings>("PWManagerRuntime/PWManagerPanelSettings");
            document.visualTreeAsset = Resources.Load<VisualTreeAsset>("PWManagerUI/WrestlerProfile");
            ui.AddComponent<WrestlerProfileController>();
            ui.SetActive(true);
            WrestlerProfileController.Open(wrestler);

            var root = document.rootVisualElement;
            root.schedule.Execute(() =>
            {
                Set(root, "wp-matches", "64");
                Set(root, "wp-wins", "41");
                Set(root, "wp-draws", "3");
                Set(root, "wp-losses", "20");
                Set(root, "wp-championships", "4");
                Set(root, "wp-world-championships", "1");
                Set(root, "wp-win-streak", "6");
                Set(root, "wp-recent-result", "승리 · 메인 이벤트");
            });
        }

        private static void ConfigureDemo(WrestlerState wrestler)
        {
            wrestler.Identity.BirthDate = new GameDate(1994, 3, 18);
            wrestler.Identity.HeightCm = 188;
            wrestler.Identity.WeightKg = 104;
            wrestler.Identity.CareerYears = 11;
            wrestler.Identity.Background = WrestlerBackground.Athlete;
            wrestler.Status.StatusValue = 88;
            wrestler.Status.IsRookie = false;
            wrestler.Status.OfficialMatchCount = 64;
            wrestler.Condition.Condition = 92;
            wrestler.Condition.Satisfaction = 84;
            wrestler.Condition.Availability = WrestlerAvailability.Available;
            wrestler.Condition.InjuryStatus = InjuryStatus.None;
            wrestler.Momentum.Momentum = 86;
            wrestler.Roster.Alignment = KayfabeAlignment.Face;
            wrestler.Presentation.WrestlingStyleId = "style_007";
            wrestler.Presentation.PromoDisposition = PromoDisposition.Serious;
            wrestler.Attributes.RingPsychology = 17.8f;
            wrestler.Attributes.RingImprovisation = 16.7f;
            wrestler.Attributes.Technical = 18.1f;
            wrestler.Attributes.Brawling = 15.2f;
            wrestler.Attributes.Power = 17f;
            wrestler.Attributes.HighFlying = 14.6f;
            wrestler.Attributes.SpotWork = 16.4f;
            wrestler.Attributes.SpecialtyMatches = 15.6f;
            wrestler.Attributes.Selling = 18.3f;
            wrestler.Attributes.Stamina = 17.2f;
            wrestler.Attributes.Charisma = 18.4f;
            wrestler.Attributes.MicWork = 18.1f;
            wrestler.Attributes.Improvisation = 17.2f;
            wrestler.Attributes.Acting = 16.5f;
            wrestler.Attributes.FaceWork = 18.6f;
            wrestler.Attributes.HeelWork = 14.7f;
            wrestler.Attributes.Comedy = 14.2f;
            wrestler.Growth.MatchPotentialCap = 18.8f;
            wrestler.Growth.PromoPotentialCap = 18.5f;
            wrestler.FanReaction.UsesTwoAxes = true;
            wrestler.FanReaction.Hardcore = new FanResponseState { Preference = 93, Interest = 96 };
            wrestler.FanReaction.Casual = new FanResponseState { Preference = 87, Interest = 91 };
            wrestler.FanReaction.Mark = new FanResponseState { Preference = 90, Interest = 94 };
        }

        private static void Set(VisualElement root, string name, string value)
        {
            var label = root.Q<Label>(name);
            if (label != null) label.text = value;
        }

        private void OnDestroy()
        {
            if (DashboardSession.ActiveSave == demoSave)
                DashboardSession.SetActiveSave(previousSave, transient: false);
        }

#if UNITY_EDITOR
        [MenuItem("PW Manager/Demo/Create Wrestler Profile Scene")]
        public static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Wrestler Profile Demo").AddComponent<WrestlerProfileDemoBootstrap>();
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Wrestler Profile Demo.unity");
            AssetDatabase.SaveAssets();
        }
#endif
    }
}
