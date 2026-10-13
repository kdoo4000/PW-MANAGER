using System;
using System.Collections.Generic;
using PWManager.Domain.Services;

namespace PWManager.Domain.Models
{
    public enum MatchOpeningStyle { Automatic, Probe, Clash, Dominance, Ambush }
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
        public string OpeningSpot;
        public string MiddleSpot;
        public string ClosingSpot;
        public List<PlannedSpotState> Spots = new();
        public List<PlannedStoryInjuryState> StoryInjuries = new();
        public int FallsToWin = 1;
        public bool IronMan, Elimination, Tornado;
        public string DominantSideId;
        public MatchOpeningStyle OpeningStyle;
        public string StorylineId;
        public string ChampionshipId;
        public StoryAction StoryAction;

        // Legacy save compatibility. New match plans use Sides and the fields above.
        public List<string> ParticipantIds = new();
        public int MembersPerTeam;
        public int TeamCount;
        public string WinnerId;
        public MatchFinishType FinishType;
    }

    [Serializable]
    public sealed class PlannedStoryInjuryState
    {
        public string WrestlerId;
        public BodyRegion Region = BodyRegion.Body;
        public InjuryStatus Severity = InjuryStatus.Minor;
        public int Days = 7;
        public bool Absent;
    }
}
