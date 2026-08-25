using System;

namespace PWManager.Domain.Models
{
    [Serializable]
    public sealed class ResultChangeState
    {
        public string TargetId;
        public string ValueKey;
        public float Amount;
        public string Reason;
    }

    [Serializable]
    public sealed class EvaluationReasonState
    {
        public string Code;
        public float Contribution;
    }

    [Serializable]
    public sealed class FanReactionResultState
    {
        public float Mania;
        public float Light;
        public float Family;
    }
}
