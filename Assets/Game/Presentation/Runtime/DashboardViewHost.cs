using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public sealed class DashboardViewHost : MonoBehaviour
    {
        [SerializeField] private VisualTreeAsset viewAsset;
        private VisualElement instance;

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
