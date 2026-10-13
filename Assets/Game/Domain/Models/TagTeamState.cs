using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    public enum TagTeamStatus { Active, Inactive, Disbanded }

    [Serializable]
    public sealed class GroupResultChangeState
    {
        public string SourceResultId;
        public string GroupId;
        public StoryPartyType GroupType;
        public bool Disband;
        public string Name;
        public string LeaderId;
        public List<string> MemberIds = new();
    }

    [Serializable]
    public sealed class TagTeamState
    {
        public string Id;
        public string PromotionId;
        public string Name;
        public List<string> MemberIds = new();
        public TagTeamStatus Status;
        public float Chemistry = 50f;
    }

    [Serializable]
    public sealed class StableState
    {
        public string Id;
        public string PromotionId;
        public string Name;
        public string LeaderId;
        public List<string> MemberIds = new();
        public TagTeamStatus Status;
        public float Chemistry = 50f;
    }
}
