using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    public enum WrestlerAttribute
    {
        RingPsychology, RingImprovisation, Technical, Brawling, Power, HighFlying,
        SpotWork, SpecialtyMatches, Selling, Stamina,
        Charisma, MicWork, Improvisation, Acting, FaceWork, HeelWork, Comedy
    }
    public enum ExperienceActivity { Match, Promo, Training }
    public enum AttributeChangeCause { Growth, Aging }

    [Serializable]
    public sealed class WrestlerExperienceState
    {
        public string Id, WrestlerId, SourceResultId;
        public GameDate Date;
        public ExperienceActivity Activity;
        public float BaseExperience;
        public AttributeProgressState Progress = new();
        public OptionalGameDate SettledQuarter;
        public WrestlerExperienceState Copy()
        {
            var copy = (WrestlerExperienceState)MemberwiseClone();
            copy.Progress = Progress.Copy(); return copy;
        }
    }

    [Serializable]
    public sealed class AttributeChangeState
    {
        public WrestlerAttribute Attribute;
        public float Before, After;
        public AttributeChangeCause Cause;
    }

    [Serializable]
    public sealed class GrowthQuarterResultState
    {
        public string Id, WrestlerId;
        public GameDate StartDate, EndDate;
        public AgingState Stage;
        public AttributeProgressState ConsumedProgress = new();
        public List<AttributeChangeState> Changes = new();
    }
}
