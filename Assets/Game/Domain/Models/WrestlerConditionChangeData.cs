using System;

namespace PWManager.Domain.Models
{
    public enum WrestlerConditionChangeReason { MatchParticipation }

    [Serializable]
    public sealed class WrestlerConditionChangeData
    {
        public string SourceResultId;
        public string WrestlerId;
        public float ConditionDelta;
        public WrestlerConditionChangeReason ReasonCode;
    }
}
