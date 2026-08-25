using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    public enum TagTeamStatus { Active, Inactive, Disbanded }

    [Serializable]
    public sealed class TagTeamState
    {
        public string Id;
        public string PromotionId;
        public List<string> MemberIds = new();
        public TagTeamStatus Status;
        public float Chemistry = 50f;
    }
}
