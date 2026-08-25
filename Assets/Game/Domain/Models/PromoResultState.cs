using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    [Serializable]
    public sealed class PromoResultState
    {
        public string Id;
        public string ShowId;
        public int ShowVersion;
        public string ShowEventId;
        public string PromoPlanId;
        public float PromoScore;
        public FanReactionResultState FanReaction = new();
        public List<ResultChangeState> StoryChanges = new();
        public List<ResultChangeState> StoryCandidateInput = new();
        public List<ResultChangeState> FanReactionChanges = new();
        public List<ResultChangeState> RelationshipChanges = new();
        public List<ResultChangeState> SponsorProgressChanges = new();
        public List<EvaluationReasonState> EvaluationReasons = new();
        public int ResultSeed;
    }
}
