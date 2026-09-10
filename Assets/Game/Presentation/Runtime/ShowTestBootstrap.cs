using System;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Domain.Models;
using SaintsField.Playa;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public sealed class ShowTestBootstrap : MonoBehaviour
    {
        [SerializeField] private int rosterSeed = 20260903;
        private GameObject dashboardObject;
        private GameSave previousSave;
        private GameSave testSave;
        private bool previousTransient;
        private bool initialized;

        private void Start()
        {
            previousSave = DashboardSession.ActiveSave;
            previousTransient = DashboardSession.IsTransient;
            initialized = true;
            StartShow();
        }

        [Button("테스트 쇼 다시 시작")]
        private void StartShow()
        {
            if (!Application.isPlaying) return;
            try
            {
                var catalog = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog");
                var save = ShowTestSaveFactory.Create(catalog, rosterSeed);
                var tree = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard");
                var panel = Resources.Load<PanelSettings>("PWManagerRuntime/PWManagerPanelSettings");
                if (tree == null || panel == null) throw new InvalidOperationException("대시보드 화면 리소스가 없습니다.");
                if (dashboardObject != null)
                {
                    dashboardObject.SetActive(false);
                    dashboardObject.name = "Previous Show Test UI";
                    Destroy(dashboardObject);
                }
                testSave = save;
                DashboardSession.SetActiveSave(testSave, transient: true);
                dashboardObject = new GameObject("Dashboard UI");
                dashboardObject.SetActive(false);
                dashboardObject.transform.SetParent(transform, false);
                var document = dashboardObject.AddComponent<UIDocument>();
                document.panelSettings = panel;
                document.visualTreeAsset = tree;
                var controller = dashboardObject.AddComponent<DashboardController>();
                dashboardObject.AddComponent<WrestlerProfileController>();
                dashboardObject.SetActive(true);
                controller.OpenShowTest(save.Shows[0].Id, StartShow);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                var root = dashboardObject?.GetComponent<UIDocument>()?.rootVisualElement;
                var notice = root?.Q<Label>("show-planning-notice");
                if (notice != null) notice.text = exception.Message;
            }
        }

        private void OnDestroy()
        {
            if (dashboardObject != null) dashboardObject.SetActive(false);
            if (initialized && DashboardSession.ActiveSave == testSave)
                DashboardSession.SetActiveSave(previousSave, previousTransient);
        }
    }
}
