using PWManager.Domain.Data;
using UnityEngine;

namespace PWManager.Data.Definitions
{
    public abstract class StaticDefinition : ScriptableObject, IIdentifiable
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;

        public string Id => id;
        public string DisplayName => displayName;

#if UNITY_EDITOR
        public void SetEditorIdentity(string value, string label)
        {
            id = value;
            displayName = label;
        }
#endif
    }
}
