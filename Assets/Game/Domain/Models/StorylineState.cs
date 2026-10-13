using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    public enum StorylineStatus { Active, Completed }
    public enum StoryPartyType { Wrestler, TagTeam, Stable }
    public enum StoryCandidateStatus { Pending, Adopted, Expired }

    [Serializable]
    public sealed class StoryCandidateState
    {
        public string Id;
        public string ShowId;
        public string StorylineId;
        public StoryCandidateStatus Status;
        public float Quality;
        public StoryEventState Event;
    }
    public enum StoryAction { None, Progress, End }
    public enum StoryEventType { Match, Promo }
    public enum StoryIncident { None, Competition, Challenge, Taunt, Attack, Betrayal, Rescue, Respect, Reconciliation }

    [Serializable]
    public sealed class StoryEventState
    {
        public string SourceResultId;
        public StoryEventType EventType;
        public StoryIncident Incident;
        public string ActorId;
        public string TargetId;
        public GameDate Date;
        public float TensionChange;
        public float InterestChange;
        public float FatigueChange;
    }

    [Serializable]
    public sealed class StorylineState
    {
        public string Id;
        public string PromotionId;
        public string Name;
        public string Context;
        public StorylineStatus Status;
        public StoryPartyType PartyType;
        public string FirstGroupId;
        public string SecondGroupId;
        public string FirstWrestlerId;
        public string SecondWrestlerId;
        public float Tension;
        public float Interest;
        public float Fatigue;
        public GameDate StartedDate;
        public GameDate CompletedDate;
        public List<StoryEventState> History = new();

        public StorylineState CopyForSimulation()
        {
            var copy = (StorylineState)MemberwiseClone();
            copy.History = new List<StoryEventState>(History ?? new());
            return copy;
        }
    }

    [Serializable]
    public sealed class StorylineResultChangeState
    {
        public string StorylineId;
        public string SourceResultId;
        public StoryEventType EventType;
        public StoryAction Action;
        public StoryIncident Incident;
        public string ActorId;
        public string TargetId;
        public float TensionChange;
        public float InterestChange;
        public float FatigueChange;
    }

    [Serializable]
    public sealed class StoryRelationshipChangeState
    {
        public string FirstWrestlerId;
        public string SecondWrestlerId;
        public float HostilityChange;
        public float ValueChange;
        public string SourceResultId;
    }
}
