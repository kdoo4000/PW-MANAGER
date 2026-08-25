using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    public enum MatchFinishType
    {
        Pinfall, Submission, RollUp, Disqualification, CountOut, Draw,
        Escape, ObjectRetrieval, TableBreak
    }

    [Serializable]
    public sealed class MatchSideState
    {
        public string Id;
        public List<string> MemberIds = new();
        public string RepresentedTagTeamId;
    }

    [Serializable]
    public sealed class MatchPlanState
    {
        public string Id;
        public string MatchTypeId;
        public string MatchGimmickId;
        public List<MatchSideState> Sides = new();
        public string WinningSideId;
        public string FinishPerformerId;
        public string LoserTargetId;

        // Legacy save compatibility. New match plans use Sides and the fields above.
        public List<string> ParticipantIds = new();
        public int MembersPerTeam;
        public int TeamCount;
        public string WinnerId;
        public MatchFinishType FinishType;
    }
}
