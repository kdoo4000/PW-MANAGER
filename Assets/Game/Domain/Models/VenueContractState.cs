using System;

namespace PWManager.Domain.Models
{
    [Serializable]
    public sealed class VenueContractState
    {
        public string Id;
        public string VenueId;
        public VenueContractType ContractType;
        public GameDate StartDate;
        public GameDate EndDate;
        public long ProductionCost;
        public VenueContractStatus Status;
    }
}
