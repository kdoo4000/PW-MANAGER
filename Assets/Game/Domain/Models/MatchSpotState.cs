using System;

namespace PWManager.Domain.Models
{
    public enum MatchSpotPhase { Entrance = 0, Early = 1, Middle = 2, Late = 3, PostMatch = 4 }
    public enum SpotExecutionResult { Disaster, Failure, Success, GreatSuccess }
    public enum SpotScriptedEffect { None, MatchStopped, Disqualification }

    [Serializable]
    public sealed class PlannedSpotState
    {
        public string SpotId;
        public MatchSpotPhase Phase = MatchSpotPhase.Middle;
        public string ActorId;
        public string TargetId;
        public string PartnerId;
    }

    [Serializable]
    public sealed class SpotResultState
    {
        public string SpotId;
        public MatchSpotPhase Phase;
        public string ActorId;
        public string TargetId;
        public string PartnerId;
        public GameDate Date;
        public int VariantIndex;
        public string ScriptedMoment;
        public SpotExecutionResult InitialResult;
        public SpotExecutionResult Result;
        public bool Recovered;
        public float EffectiveExecution;
        public int RepeatCount;
        public float FinalSpotScore;
        public SpotScriptedEffect ScriptedEffect;
    }
}
