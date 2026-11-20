using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    public enum ChampionshipType { Individual, TagTeam }
    public enum ChampionshipDivision { Open, Men, Women }

    [Serializable]
    public sealed class ChampionshipMatchState
    {
        public string SourceResultId;
        public string ShowId;
        public GameDate Date;
        public string ChampionBeforeId;
        public string ChampionAfterId;
        public List<string> ParticipantIds = new();
        public List<string> ChallengerIds = new();
        public List<string> WinnerMemberIds = new();
        public MatchFinishType FinishType;
        public bool WasStopped;
        public bool SuccessfulDefense;
        public float MatchQuality;
        public float FanSatisfaction;
        public float ChampionStanding;
        public float ChallengerStanding;
        public float PrestigeBefore;
        public float PrestigeAfter;
    }

    [Serializable]
    public sealed class ChampionshipReignState
    {
        public string HolderId;
        public List<string> MemberIds = new();
        public GameDate WonDate;
        public OptionalGameDate LostDate;
        public string SourceResultId;
        public int Defenses;
    }

    [Serializable]
    public sealed class ChampionshipState
    {
        public string Id;
        public string PromotionId;
        public string Name;
        public ChampionshipType Type;
        public ChampionshipDivision Division;
        public bool IsWorldChampionship;
        public BeltDesignState BeltDesign;
        public OptionalGameDate CreatedDate;
        public string HolderId;
        public List<ChampionshipReignState> Reigns = new();
        public bool HasPrestige;
        public float Prestige;
        public List<ChampionshipMatchState> MatchHistory = new();
    }
}
