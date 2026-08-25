using System;

namespace PWManager.Domain.Models
{
    public enum TransactionType
    {
        SigningFee,
        Salary,
        StaffDepartmentUpgrade,
        VenueUnlock,
        FacilityRent,
        VenueCost,
        TicketRevenue,
        BroadcastRevenue,
        SponsorRevenue,
        MerchandiseRevenue,
        MarketingCost,
        MedicalCost,
        PerformanceBonus,
        TerminationFee,
        Refund,
        LatePayment
    }

    [Serializable]
    public sealed class TransactionRecord
    {
        public string Id;
        public string PromotionId;
        public GameDate Date;
        public TransactionType Type;
        public long Amount;
        public string ReasonId;
        public bool IsVoided;
    }
}
