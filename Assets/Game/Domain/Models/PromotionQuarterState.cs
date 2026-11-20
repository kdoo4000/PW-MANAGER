using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    [Serializable]
    public sealed class PromotionQuarterState
    {
        public bool IsInitialized;
        public GameDate OpeningDate;
        public long OpeningFollowers;
        public List<string> BaselineResultIds = new();
        public List<PromotionQuarterRecord> History = new();
        public PromotionQuarterState Copy() => new()
        {
            IsInitialized = IsInitialized, OpeningDate = OpeningDate, OpeningFollowers = OpeningFollowers,
            BaselineResultIds = new(BaselineResultIds), History = new(History)
        };
    }

    [Serializable]
    public sealed class PromotionQuarterRecord
    {
        public GameDate StartDate, EndDate;
        public long OpeningFollowers, ClosingFollowers, TotalAttendance;
        public int CompletedShows;
        public double WeightedAttendance, WeightedOccupancy, StarPower;
        public long BaseScore, FanScore, AttendanceScore, RosterScore, MilestoneBonus, PpvBonus;
        public long PrestigeBefore, PrestigeAfter;
        public long TotalGain => checked(BaseScore + FanScore + AttendanceScore + RosterScore + MilestoneBonus + PpvBonus);
    }
}
