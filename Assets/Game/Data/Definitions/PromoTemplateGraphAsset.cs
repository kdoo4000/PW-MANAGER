using System;
using System.Collections.Generic;
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
        public string Id;
        public PromoGraphNodeType Type;
        public string Title;
        [TextArea] public string Description;
        public Vector2 Position;
    }

    [Serializable]
    public sealed class PromoGraphEdgeData
    {
        public string Id;
        public string FromNodeId;
        public string ToNodeId;
        public string Label;
    }

    [CreateAssetMenu(menuName = "PW Manager/Promo/Template Graph")]
    public sealed class PromoTemplateGraphAsset : ScriptableObject
    {
        public string TemplateId;
        [FormerlySerializedAs("DisplayName")] public string KoreanName;
        public string EnglishName;
        public string DisplayName
        {
            get => string.IsNullOrWhiteSpace(KoreanName) ? EnglishName : KoreanName;
            set => KoreanName = value;
        }
        public List<PromoGraphNodeData> Nodes = new();
        public List<PromoGraphEdgeData> Edges = new();
    }
}
