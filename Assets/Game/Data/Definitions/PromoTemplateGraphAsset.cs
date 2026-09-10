using System;
using System.Collections.Generic;
using SaintsField;
using SaintsField.Playa;
using UnityEngine;
using UnityEngine.Serialization;

namespace PWManager.Data.Definitions
{
    public enum PromoGraphNodeType
    {
        Start,
        Beat,
        Choice,
        Condition,
        Spot,
        Outcome,
        Change
    }

    [Serializable]
    public sealed class PromoGraphNodeData
    {
        [Required] public string Id;
        [EnumToggleButtons]
        public PromoGraphNodeType Type;
        [Required] public string Title;
        [ResizableTextArea] public string Description;
        public Vector2 Position;
    }

    [Serializable]
    public sealed class PromoGraphEdgeData
    {
        [Required] public string Id;
        [Required] public string FromNodeId;
        [Required] public string ToNodeId;
        public string Label;
    }

    [CreateAssetMenu(menuName = "PW Manager/Promo/Template Graph")]
    public sealed class PromoTemplateGraphAsset : ScriptableObject
    {
        [Required] public string TemplateId;
        [FormerlySerializedAs("DisplayName")] public string KoreanName;
        [Required] public string EnglishName;
        public string DisplayName
        {
            get => string.IsNullOrWhiteSpace(KoreanName) ? EnglishName : KoreanName;
            set => KoreanName = value;
        }
        [ListDrawerSettings(searchable: true)] public List<PromoGraphNodeData> Nodes = new();
        [ListDrawerSettings(searchable: true)] public List<PromoGraphEdgeData> Edges = new();
    }
}
