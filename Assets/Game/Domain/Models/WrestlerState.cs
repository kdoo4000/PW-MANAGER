using System;

namespace PWManager.Domain.Models
{
    [Serializable]
    public sealed class WrestlerState
    {
        public WrestlerIdentityState Identity = new();
        public WrestlerAttributesState Attributes = new();
        public WrestlerHiddenState Hidden;
        public WrestlerAdaptationState Adaptation = new();
        public WrestlerConditionState Condition = new();
        public KayfabeInjuryState KayfabeInjury = new();
        public WrestlerGrowthState Growth = new();
        public WrestlerStatusState Status = new();
        public WrestlerFameState Fame = new();
        public WrestlerMomentumState Momentum = new();
        public WrestlerFanReactionState FanReaction = new();
        public WrestlerRosterState Roster = new();
        public WrestlerPresentationState Presentation = new();

        public string Id
        {
            get => Identity.Id;
            set => Identity.Id = value;
        }

        public WrestlerState CopyForGroupSimulation()
        {
            var copy = (WrestlerState)MemberwiseClone();
            copy.Hidden = Hidden?.Copy();
            copy.Adaptation = Adaptation?.Copy() ?? new();
            copy.Condition = Condition.Copy();
            copy.KayfabeInjury = KayfabeInjury?.Copy() ?? new();
            copy.Roster = new WrestlerRosterState
            {
                PromotionId = Roster.PromotionId, Alignment = Roster.Alignment, ActivityState = Roster.ActivityState,
                ActiveTagTeamId = Roster.ActiveTagTeamId, ActiveStableId = Roster.ActiveStableId,
                LastMatchDate = Roster.LastMatchDate, LastAppearanceDate = Roster.LastAppearanceDate
            };
            return copy;
        }

        public string PromotionId
        {
            get => Roster.PromotionId;
            set => Roster.PromotionId = value;
        }
    }
}
