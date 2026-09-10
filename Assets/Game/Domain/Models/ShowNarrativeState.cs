using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    public enum NarrativeLineType { Commentary, Dialogue, Action, Crowd }
    public enum MatchBeatType { Opening, Control, Counter, Comeback, PlannedSpot, NearFall, FinisherAttempt, Finish, PostMatch, Context, Execution, EngineAction }

    [Serializable]
    public sealed class NarrativeLineState
    {
        public NarrativeLineType Type;
        public string SpeakerId;
        public string Text;
        public int Intensity;
    }

    [Serializable]
    public sealed class MatchSimulationBeatState
    {
        public MatchBeatType BeatType;
        public string ActorId;
        public string TargetId;
        public string Detail;
        public bool Succeeded = true;
        public int Phase;
        public int Importance;
        public float CrowdReaction;
        public float DurationSeconds;
        public float MatchProgress;
        public int EngineEventIndex;
    }

    [Serializable]
    public sealed class PromoScriptState
    {
        public string Summary;
        public bool UsedFallback;
        public List<string> FulfilledBeatIds = new();
        public List<NarrativeLineState> Lines = new();
    }
}
