using System;

namespace PWManager.Domain.Models
{
    [Serializable]
    public sealed class WrestlerIdentityState
    {
        public string Id;
        public string LegalName;
        public string RingName;
        public string Nickname;
        public WrestlerGender Gender;
        public WrestlerBackground Background;
        public GameDate BirthDate;
        public GameDate CreatedDate;
        public OptionalGameDate ExpiryDate;
        public int HeightCm;
        public int WeightKg;
        public WrestlerBodyType BodyType;
        public int CareerYears;
    }
}
