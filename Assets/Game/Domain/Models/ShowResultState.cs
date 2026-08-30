using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    [Serializable]
    public sealed class ShowEvaluationState
    {
        public float Score;
        public List<EvaluationReasonState> EvaluationReasons = new();
    }

    [Serializable]
    public sealed class FinancialSettlementState
    {
        public long Revenue;
        public long Cost;
        public long NetIncome;
    }

    [Serializable]
    public sealed class ShowResultState
    {
        public string Id;
        public string ShowId;
        public int ShowVersion;
        public List<string> TimelineResultIds = new();
        public List<string> MatchResultIds = new();
        public List<string> PromoResultIds = new();
        public ShowEvaluationState ShowEvaluation = new();
        public FinancialSettlementState FinancialSettlement = new();
        public List<ResultChangeState> WrestlerChanges = new();
        public List<ResultChangeState> FanChanges = new();
        public List<ResultChangeState> StoryChanges = new();
        public List<ResultChangeState> TitleChanges = new();
        public List<ResultChangeState> TournamentChanges = new();
    }
}
