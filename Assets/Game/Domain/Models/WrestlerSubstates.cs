using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    [Serializable]
    public sealed class WrestlerConditionState
    {
        public float Condition = 100f;
        public InjuryStatus InjuryStatus;
        public OptionalGameDate RecoveryDate;
        public float Satisfaction;
        public WrestlerAvailability Availability;
    }

    [Serializable]
    public sealed class WrestlerGrowthState
    {
        public float MatchPotentialCap;
        public float PromoPotentialCap;
        public AttributeProgressState GrowthProgress = new();
        public List<string> ExperienceLogIds = new();
        public List<string> AttributeHistoryIds = new();
        public AgingState AgingState;
    }

    [Serializable]
    public sealed class WrestlerStatusState
    {
        public int StatusValue;
        public bool IsRookie;
        public GameDate DebutDate;
        public int OfficialMatchCount;
        public int LastStatusChange;
        public List<string> StatusChangeRecordIds = new();
    }

    [Serializable]
    public sealed class WrestlerMomentumState
    {
        public float Momentum;
        public float MomentumFailure;
        public OptionalGameDate LastMomentumActivityDate;
        public float LastMomentumGain;
        public float LastMomentumRelease;
        public MomentumReleaseType ReleaseType;
        public MomentumReleaseReason ReleaseReason;
    }

    [Serializable]
    public sealed class WrestlerFanReactionState
    {
        public float ManiaFanReaction;
        public float LightFanReaction;
        public float FamilyFanReaction;
        public OptionalGameDate LastUpdatedAt;
    }

    [Serializable]
    public sealed class WrestlerRosterState
    {
        public string PromotionId;
        public KayfabeAlignment Alignment;
        public RosterActivityState ActivityState;
        public string ActiveTagTeamId;
        public string ActiveStableId;
        public OptionalGameDate LastMatchDate;
        public OptionalGameDate LastAppearanceDate;
    }
}
