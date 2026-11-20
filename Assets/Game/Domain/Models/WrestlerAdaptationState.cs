using System;

namespace PWManager.Domain.Models
{
    [Serializable]
    public sealed class WrestlerAdaptationState
    {
        public bool IsInitialized;
        public string StyleId, PromotionId;
        public ExpectedRole Role;
        public KayfabeAlignment Alignment;
        public float StyleReadiness = 1, RoleReadiness = 1, EnvironmentReadiness = 1;
        public OptionalGameDate LastProcessedDate;
        public WrestlerAdaptationState Copy() => (WrestlerAdaptationState)MemberwiseClone();
    }
}
