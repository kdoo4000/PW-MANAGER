using System;

namespace PWManager.Domain.Models
{
    [Serializable]
    public sealed class SeasonPolicyState
    {
        public string Id;
        public GameDate StartDate;
        public GameDate EndDate;
        public RegularShowFrequency RegularShowFrequency;
        public PpvFrequency PpvFrequency;
        public string SignaturePpvScheduleId;
        public string RegularVenueContractId;
    }
}
