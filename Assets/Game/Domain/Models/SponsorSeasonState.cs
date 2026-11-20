using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    public enum SponsorGoalType { Fans, Prestige, Attendance, ShowQuality, WrestlerAppearance, Advertisement, StyleChampion, GiantMatch, WrestlerFame }

    [Serializable]
    public sealed class SponsorCompany
    {
        public string Id, Name, Industry, Description, LogoResource;
        public SponsorGoalType PreferredGoal;
    }

    [Serializable]
    public sealed class SponsorGoalState
    {
        public SponsorGoalType Type;
        public long Target, Progress;
        public string WrestlerId, StyleId;
        public float MinimumQuality;
        public List<string> CountedShowIds = new();
    }

    [Serializable]
    public sealed class SponsorOfferState
    {
        public string Id, CompanyId, CompanyName, Industry, Description, LogoResource;
        public int Difficulty;
        public long BasePayment, CompletionPayment;
        public bool Selected, RewardPaid, Failed;
        public List<SponsorGoalState> Goals = new();
    }

    [Serializable]
    public sealed class SponsorSeasonState
    {
        public string PolicyId;
        public GameDate StartDate, EndDate;
        public bool SelectionConfirmed;
        public List<SponsorOfferState> Offers = new();
    }
}
