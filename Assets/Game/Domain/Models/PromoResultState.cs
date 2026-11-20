using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    [Serializable]
    public sealed class PromoParticipantUsageState
    {
        public string WrestlerId;
        public KayfabeAlignment Alignment;
        public PromoDisposition Disposition;
    }

    [Serializable]
    public sealed class PromoResultState
    {
        public string Id;
        public string ShowId;
        public int ShowVersion;
        public string ShowEventId;
        public string PromoPlanId;
        public string CancellationReason;
        public List<PromoParticipantUsageState> ParticipantUsage = new();
        public float PromoScore;
        public FanReactionResultState FanReaction = new();
        public List<ResultChangeState> StoryChanges = new();
        public List<ResultChangeState> StoryCandidateInput = new();
        public List<ResultChangeState> FanReactionChanges = new();
        public List<ResultChangeState> RelationshipChanges = new();
        public List<ResultChangeState> SponsorProgressChanges = new();
        public List<EvaluationReasonState> EvaluationReasons = new();
        public PromoScriptState Narrative;
        public int ResultSeed;
    }
}
