using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    public enum MatchExecutionEventType { Mistake, Injury, GreatMoment }

    [Serializable]
    public sealed class MoveResultState
    {
        public string MoveId;
        public string MoveName;
        public string ActorId;
        public string TargetId;
        public bool IsFinisher;
        public float ExecutionScore;
        public float SellingScore;
        public SpotExecutionResult Result;
    }

    [Serializable]
    public sealed class TechnicalEvaluationBreakdownState
    {
        public float Performance;
        public float Structure;
        public float Duration;
        public float Spot;
        public float RandomVariance;
    }

    [Serializable]
    public sealed class WrestlerMatchPerformanceState
    {
        public string WrestlerId;
        public float BaseRoutine;
        public float PerformanceVariance;
        public float StaminaPenalty;
        public float EffectiveRoutine;
    }

    [Serializable]
    public sealed class MatchParticipantProfileState
    {
        public string SideId;
        public List<string> MemberIds = new();
        public string RepresentedTagTeamId;
        public float AppliedChemistry = 50f;
        public float RoutineExecution;
        public float RingPsychology;
        public float RingImprovisation;
        public float Technical;
        public float Brawling;
        public float Power;
        public float HighFlying;
        public float SpotWork;
        public float SpecialtyMatches;
        public float Selling;
        public float Stamina;
    }

    [Serializable]
    public sealed class MatchExecutionEventState
    {
        public MatchExecutionEventType EventType;
        public List<string> ParticipantIds = new();
        public string Reason;
    }

    [Serializable]
    public sealed class MatchResultState
    {
        public string Id;
        public string ShowId;
        public int ShowVersion;
        public string ShowEventId;
        public string MatchPlanId;
        public int PlannedDuration;
        public int ActualMatchDuration;
        public string WinnerId;
        public string WinningSideId;
        public string FinishPerformerId;
        public string LoserTargetId;
        public List<string> IndirectWinnerIds = new();
        public List<string> IndirectLoserIds = new();
        public MatchFinishType FinishType;
        public bool WasStoppedBySpot;
        public float FinalMatchQuality;
        public CriticReviewState CriticReview = new();
        public TechnicalEvaluationBreakdownState TechnicalEvaluation = new();
        public List<WrestlerMatchPerformanceState> WrestlerPerformances = new();
        public List<MatchParticipantProfileState> ParticipantProfiles = new();
        public List<ResultChangeState> MatchStoryEvaluationInput = new();
        public FanReactionResultState FanReaction = new();
        public List<ResultChangeState> AfterEffects = new();
        public List<WrestlerConditionChangeData> WrestlerConditionChanges = new();
        public List<MatchExecutionEventState> ExecutionEvents = new();
        public List<EvaluationReasonState> EvaluationReasons = new();
        public List<MatchSimulationBeatState> SimulationBeats = new();
        public PWManager.Domain.Services.MatchEngineState EngineState;
        public List<SpotResultState> SpotResults = new();
        public List<MoveResultState> MoveResults = new();
        public List<NarrativeLineState> NarrativeLines = new();
        public int ResultSeed;
    }
}
