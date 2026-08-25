using System;

namespace PWManager.Domain.Models
{
    [Serializable]
    public sealed class ManagerAttributesState
    {
        public float Charisma;
        public float MicWork;
        public float Improvisation;
        public float Acting;
        public float FaceWork;
        public float HeelWork;
        public float Comedy;
        public float RingImprovisation;
        public float SpotWork;
        public float Selling;

        public float ManagerOverall => (Charisma + MicWork + Improvisation + Acting + FaceWork
            + HeelWork + Comedy + RingImprovisation + SpotWork + Selling) / 10f;
    }

    [Serializable]
    public sealed class ManagerState
    {
        public string Id;
        public string StaticDataId;
        public string PromotionId;
        public ManagerAttributesState Attributes = new();
        public float PromoPotentialCap;
        public KayfabeAlignment Alignment;
        public RosterActivityState ActivityState;
    }
}
