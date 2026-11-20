using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    public enum CareerEventKind { Match, Promo, Inactivity, RookieGraduation, Fame }
    public enum StatusChangeReason { Card, Victory, Defeat, TitleChallenge, Championship, MomentumRelease, MomentumCollapse }

    [Serializable]
    public sealed class StatusChangeReasonState
    {
        public StatusChangeReason Reason;
        public int Amount;
    }

    [Serializable]
    public sealed class WrestlerCareerEventState
    {
        public string Id, WrestlerId, ShowId, SourceResultId;
        public GameDate Date;
        public CareerEventKind Kind;
        public int StatusBefore, StatusAfter;
        public bool WasRookie, IsRookie;
        public List<StatusChangeReasonState> StatusReasons = new();
        public float MomentumBefore, MomentumGain, ReleasedMomentum, MomentumAfter;
        public float FailureBefore, FailureAdded, FailureAfter;
        public MomentumReleaseType ReleaseType;
        public MomentumReleaseReason ReleaseReason;
        public bool Collapsed;
        public long FameBefore, FameAfter;
        public double FameRemainderBefore, FameRemainderAfter, FameGain, FameActivity, FameTitle;
        public int FameMilestoneBefore, FameMilestoneAfter;
        public bool FameWasRookie;
        public string FamePromotionId;
        public bool HasPromotionFameBonus, FameRookieDevelopment;
        public int FamePromotionBonus;
        public FanReactionResultState InterestChange = new();
        public FanReactionResultState PromotionSatisfactionChange = new();
    }
}
