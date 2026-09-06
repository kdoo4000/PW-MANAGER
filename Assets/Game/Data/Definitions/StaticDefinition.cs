using PWManager.Domain.Data;
using UnityEngine;
using UnityEngine.Serialization;

namespace PWManager.Data.Definitions
{
    public abstract class StaticDefinition : ScriptableObject, IIdentifiable
    {
        [SerializeField] private string id;
        [FormerlySerializedAs("displayName")]
        [SerializeField] private string koreanName;
        [SerializeField] private string englishName;

        public string Id => id;
        public string KoreanName => koreanName;
        public string EnglishName => englishName;
        public string DisplayName => string.IsNullOrWhiteSpace(koreanName) ? englishName : koreanName;

#if UNITY_EDITOR
        public void SetEditorIdentity(string value, string korean, string english)
        {
            id = value;
            koreanName = korean;
            englishName = english;
        }
#endif
    }
}
