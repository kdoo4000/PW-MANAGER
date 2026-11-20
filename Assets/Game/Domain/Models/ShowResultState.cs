using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    [Serializable]
    public sealed class WrestlerCardPlacement
    {
        public string WrestlerId;
        public ExpectedRole ExpectedRole;
        public bool UpperCard, MainEvent;
        public bool HasMatchAppearance, CancelledUpperCard, CancelledMainEvent;
    }

    [Serializable]
    public sealed class ShowEvaluationState
    {
        public float Score;
        public CriticReviewState CriticReview;
        public List<EvaluationReasonState> EvaluationReasons = new();
    }

    [Serializable]
    public sealed class FinancialSettlementState
    {
        public long Revenue;
        public long Cost;
        public long NetIncome;
        public long Attendance;
        public long TicketPrice;
    }

    [Serializable]
    public sealed class ShowResultState
    {
        public string Id;
        public string ShowId;
        public int ShowVersion;
        public List<string> TimelineResultIds = new();
        public List<string> MatchResultIds = new();
        public List<string> PromoResultIds = new();
        public ShowEvaluationState ShowEvaluation = new();
        public FinancialSettlementState FinancialSettlement = new();
        public List<ResultChangeState> WrestlerChanges = new();
        public List<ResultChangeState> FanChanges = new();
        public List<ResultChangeState> StoryChanges = new();
        public List<StorylineResultChangeState> StorylineChanges = new();
        public List<StorylineState> CreatedStorylines = new();
        public List<StoryEventState> StoryEvents = new();
        [NonSerialized] public bool StoryHistoryApplied;
        public List<StoryCandidateState> StoryCandidates = new();
        public List<GroupResultChangeState> GroupChanges = new();
        public List<StoryRelationshipChangeState> RelationshipChanges = new();
        public List<ResultChangeState> TitleChanges = new();
        public List<ResultChangeState> TournamentChanges = new();
        public FanReactionResultState FanExpectation;
        public FanReactionResultState FanSatisfaction;
        public float FanExposure;
        public bool HasFameSnapshot;
        public bool HasAvailabilitySnapshot;
        public List<string> AvailableWrestlerIds = new();
        public bool HasCardPlacementSnapshot, HasUpperCardOpportunity, HasMainEventOpportunity;
        public List<WrestlerCardPlacement> CardPlacements = new();
        public long FameReach;
        public bool HasPrestigeSnapshot;
        public int PrestigeVenueCapacity;
        public ScheduledShowType PrestigeShowType;
        public double PpvSuccessScore;
        public int PpvPrestigeBonus;
        public List<WrestlerFanChangeState> FanResponseChanges = new();
    }

    [Serializable]
    public sealed class WrestlerFanChangeState
    {
        public string WrestlerId;
        public FanResponseState Mark;
        public FanResponseState Casual;
        public FanResponseState Hardcore;
    }
}
