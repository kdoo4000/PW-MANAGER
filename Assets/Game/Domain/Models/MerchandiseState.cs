using System;
using System.Collections.Generic;
using System.Linq;
namespace PWManager.Domain.Models
{
    public enum MerchandiseChannel { Online, Venue }
    [Serializable] public sealed class MerchandiseRemainder
    {
        public string WrestlerId;
        public double Online, Venue;
        public MerchandiseRemainder Copy() => (MerchandiseRemainder)MemberwiseClone();
    }
    [Serializable] public sealed class MerchandiseSale
    {
        public string Key, WrestlerId;
        public GameDate Date;
        public MerchandiseChannel Channel;
        public long Units;
        public long Gross, Production, Selling, Royalty;
        public long Net => Gross - Production - Selling - Royalty;
    }
    [Serializable] public sealed class MerchandiseState
    {
        public bool IsInitialized;
        public GameDate LastOnlineDate;
        public List<MerchandiseRemainder> Remainders = new();
        public List<MerchandiseSale> Sales = new();
        public List<string> ProcessedKeys = new();
        public MerchandiseState Copy() => new() { IsInitialized = IsInitialized, LastOnlineDate = LastOnlineDate,
            Remainders = Remainders.Select(x => x.Copy()).ToList(), Sales = new(Sales), ProcessedKeys = new(ProcessedKeys) };
    }
}
