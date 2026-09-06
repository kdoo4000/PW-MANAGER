using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public sealed class DashboardViewHost : MonoBehaviour
    {
        [SerializeField] private VisualTreeAsset viewAsset;
        private VisualElement instance;

        public static void MountMissingViews(VisualElement root)
        {
            var workspace = root.Q("dashboard-workspace");
            foreach (var (asset, element) in new[] {
                ("DashboardOverview", "overview-content"), ("DashboardRoster", "roster-content"),
                ("DashboardSchedule", "show-schedule-content"), ("DashboardInbox", "message-content"),
                ("ShowPlanning", "show-planning-content"), ("ShowEditor", "show-editor-overlay") })
                if (root.Q(element) == null)
                    Resources.Load<VisualTreeAsset>("PWManagerUI/" + asset).CloneTree(workspace);
        }

        public void Mount(VisualElement root)
        {
            instance?.RemoveFromHierarchy();
            var workspace = root?.Q<VisualElement>("dashboard-workspace");
            if (workspace != null && viewAsset != null)
            {
                viewAsset.CloneTree(workspace);
                instance = workspace.ElementAt(workspace.childCount - 1);
            }
        }

        public void Unmount()
        {
            instance?.RemoveFromHierarchy();
            instance = null;
        }
    }
}
