using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    public enum ScoutAssignmentStatus { InProgress, Completed }
    public enum ScoutReportSource { Scouting, InitialRecruitment }

    public enum ScoutValueType
    {
        RingPsychology, RingImprovisation, Technical, Brawling, Power, HighFlying, SpotWork, SpecialtyMatches, Selling, Stamina,
        Charisma, MicWork, Improvisation, Acting, FaceWork, HeelWork, Comedy,
        MatchOverall, PromoOverall, MatchPotential, PromoPotential
    }

    public enum ScoutAssessment { VeryLow, Low, Average, High, VeryHigh }

    [Serializable]
    public sealed class ScoutValueEstimate
    {
        public ScoutValueType Type;
        public float Minimum;
        public float Maximum;
        public bool HasEvaluation;
        public bool IsFullyRevealed;
        public ScoutAssessment Assessment;
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
        public const int DurationDays = 7;
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
        public ScoutReportSource Source;
        public int KnowledgeLevel;
        public OptionalGameDate ExpiryDate;
        public List<ScoutValueEstimate> ValueEstimates = new();
        // Legacy report input only; hidden attributes are never disclosed here.
        public List<string> RevealedTraitIds = new();

        public bool IsExpired(GameDate date) => ExpiryDate.HasValue && ExpiryDate.Value.CompareTo(date) < 0;
    }
}
