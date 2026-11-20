using System;
using System.Collections.Generic;
using System.Linq;

namespace PWManager.Domain.Models
{
    public enum TrainingMode { Normal, Focused, Rest }

    [Serializable]
    public sealed class TrainingPlanState
    {
        public string WrestlerId;
        public TrainingMode Mode;
        public WrestlerAttribute Focus;
        public TrainingPlanState Copy() => (TrainingPlanState)MemberwiseClone();
    }

    [Serializable]
    public sealed class TrainingContributionState
    {
        public string WrestlerId;
        public int TrainingDays, RestDays;
        public float Experience, ConditionChange;
        public bool Injured;
        public AttributeProgressState Progress = new();
        public TrainingContributionState Copy()
        {
            var copy = (TrainingContributionState)MemberwiseClone(); copy.Progress = Progress.Copy(); return copy;
        }
    }

    [Serializable]
    public sealed class TrainingWeekState
    {
        public string Id;
        public GameDate StartDate, EndDate;
        public List<TrainingContributionState> Results = new();
    }

    [Serializable]
    public sealed class TrainingState
    {
        public bool IsInitialized;
        public int FacilityLevel = 1;
        public int TrainerLevel = 1;
        public GameDate WeekStartDate, LastProcessedDate;
        public List<TrainingPlanState> Plans = new();
        public List<TrainingContributionState> Pending = new();
        public List<TrainingWeekState> History = new();
        public TrainingState Copy() => new() { IsInitialized = IsInitialized, FacilityLevel = FacilityLevel, TrainerLevel = TrainerLevel, WeekStartDate = WeekStartDate,
            LastProcessedDate = LastProcessedDate, Plans = Plans.Select(x => x.Copy()).ToList(),
            Pending = Pending.Select(x => x.Copy()).ToList(), History = new(History) };
    }
}
