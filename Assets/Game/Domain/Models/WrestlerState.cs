using System;

namespace PWManager.Domain.Models
{
    [Serializable]
    public sealed class WrestlerState
    {
        public WrestlerIdentityState Identity = new();
        public WrestlerAttributesState Attributes = new();
        public WrestlerConditionState Condition = new();
        public WrestlerGrowthState Growth = new();
        public WrestlerStatusState Status = new();
        public WrestlerMomentumState Momentum = new();
        public WrestlerFanReactionState FanReaction = new();
        public WrestlerRosterState Roster = new();
        public WrestlerPresentationState Presentation = new();

        public string Id
        {
            get => Identity.Id;
            set => Identity.Id = value;
        }

        public string PromotionId
        {
            get => Roster.PromotionId;
            set => Roster.PromotionId = value;
        }
    }
}
