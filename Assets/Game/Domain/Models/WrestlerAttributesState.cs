using System;

namespace PWManager.Domain.Models
{
    [Serializable]
    public sealed class WrestlerAttributesState
    {
        public float RingPsychology;
        public float RingImprovisation;
        public float Technical;
        public float Brawling;
        public float Power;
        public float HighFlying;
        public float SpotWork;
        public float SpecialtyMatches;
        public float Selling;
        public float Stamina;
        public float Charisma;
        public float MicWork;
        public float Improvisation;
        public float Acting;
        public float FaceWork;
        public float HeelWork;
        public float Comedy;

        public float MatchTotal => RingPsychology + RingImprovisation + Technical + Brawling + Power
            + HighFlying + SpotWork + SpecialtyMatches + Selling + Stamina;

        public float PromoTotal => Charisma + MicWork + Improvisation + Acting + FaceWork + HeelWork + Comedy;

        public float MatchOverall => MatchTotal / 10f;
        public float PromoOverall => PromoTotal / 7f;
    }

    [Serializable]
    public sealed class AttributeProgressState
    {
        public float RingPsychology;
        public float RingImprovisation;
        public float Technical;
        public float Brawling;
        public float Power;
        public float HighFlying;
        public float SpotWork;
        public float SpecialtyMatches;
        public float Selling;
        public float Stamina;
        public float Charisma;
        public float MicWork;
        public float Improvisation;
        public float Acting;
        public float FaceWork;
        public float HeelWork;
        public float Comedy;
    }
}
