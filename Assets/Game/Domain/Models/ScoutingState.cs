using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    public enum ScoutAssignmentStatus { InProgress, Completed }

    public enum ScoutValueType
    {
        RingPsychology, RingImprovisation, Technical, Brawling, Power, HighFlying, SpotWork, SpecialtyMatches, Selling, Stamina,
        Charisma, MicWork, Improvisation, Acting, FaceWork, HeelWork, Comedy,
        MatchOverall, PromoOverall, MatchPotential, PromoPotential
    }

    [Serializable]
    public sealed class ScoutValueEstimate
    {
        public ScoutValueType Type;
        public float Minimum;
        public float Maximum;
    }

    [Serializable]
    public sealed class ScoutSearchConditions
    {
        public bool HasGender;
        public WrestlerGender Gender;
        public bool HasBackground;
        public WrestlerBackground Background;
    }

    [Serializable]
    public sealed class ScoutAssignmentState
    {
        public string Id;
        public ScoutSearchConditions SearchConditions = new();
        public GameDate StartDate;
        public GameDate CompleteDate;
        public ScoutAssignmentStatus Status;
    }

    [Serializable]
    public sealed class ScoutCandidateState
    {
        public string WrestlerId;
        public string AssignmentId;
        public int KnowledgeLevel;
        public List<ScoutValueEstimate> ValueEstimates = new();
        public List<string> RevealedTraitIds = new();
    }
}
