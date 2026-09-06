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
        // Retained for old saves. Family maps to Mark, Light to Casual, Mania to Hardcore.
        public float ManiaFanReaction;
        public float LightFanReaction;
        public float FamilyFanReaction;
        public OptionalGameDate LastUpdatedAt;
        public bool UsesTwoAxes;
        public FanResponseState Mark;
        public FanResponseState Casual;
        public FanResponseState Hardcore;

        public FanResponseState MarkResponse => UsesTwoAxes ? Mark : ConvertLegacy(FamilyFanReaction);
        public FanResponseState CasualResponse => UsesTwoAxes ? Casual : ConvertLegacy(LightFanReaction);
        public FanResponseState HardcoreResponse => UsesTwoAxes ? Hardcore : ConvertLegacy(ManiaFanReaction);

        public void Upgrade()
        {
            if (UsesTwoAxes) return;
            Mark = MarkResponse;
            Casual = CasualResponse;
            Hardcore = HardcoreResponse;
            UsesTwoAxes = true;
        }

        public WrestlerFanReactionState Copy()
        {
            var copy = (WrestlerFanReactionState)MemberwiseClone();
            copy.Upgrade();
            return copy;
        }

        private static FanResponseState ConvertLegacy(float reaction) => new()
        {
            Preference = (reaction + 100f) * .5f,
            Interest = Math.Abs(reaction)
        };
    }

    [Serializable]
    public struct FanResponseState
    {
        public float Preference;
        public float Interest;
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
