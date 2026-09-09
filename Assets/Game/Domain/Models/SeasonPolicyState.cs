using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    [Serializable]
    public sealed class PpvTitlePolicyState
    {
        public int Month;
        public string Title;
        public int DurationMinutes = 120;
    }

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
        public string RegularShowName;
        public int RegularShowDurationMinutes = 120;
        public List<PpvTitlePolicyState> PpvShowTitles = new();
    }
}
