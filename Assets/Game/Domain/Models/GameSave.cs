using System;
using System.Collections.Generic;
using PWManager.Domain.Identifiers;

namespace PWManager.Domain.Models
{
    public enum InboxMessageType { Information, Navigation, Decision }
    public enum InboxPriority { Normal, Important, Required }
    public enum InboxMessageStatus { Unread, Read, Resolved, Expired }
    public enum InboxTargetType { None, Wrestler, Show, Contract, Finance, Promotion }

    [Serializable]
    public sealed class InboxMessageState
    {
        public string Id;
        public string SourceEventId;
        public GameDate CreatedDate;
        public GameDate DueDate;
        public bool HasDueDate;
        public InboxMessageType Type;
        public InboxPriority Priority;
        public InboxMessageStatus Status;
        public string Sender;
        public string Subject;
        public string Body;
        public InboxTargetType TargetType;
        public string TargetId;
    }

    [Serializable]
    public sealed class GameSave
    {
        public const int CurrentSaveVersion = 1;

        public int SaveVersion;
        public string SaveId;
        public string CreatedAtUtc;
        public string UpdatedAtUtc;
        public int WorldSeed;
        public GameDate CurrentDate;
        public PromotionState Promotion;
        public PlayerCharacterState Player;
        public List<LocalizedNameState> SystemNames;
        public List<WrestlerState> Wrestlers;
        public List<TagTeamState> TagTeams;
        public List<ManagerState> Managers;
        public List<ContractState> Contracts;
        public List<StaffDepartmentState> StaffDepartments;
        public List<VenueContractState> VenueContracts;
        public SeasonPolicyState SeasonPolicy;
        public List<ScheduleState> Schedules;
        public List<ShowState> Shows;
        public List<ShowEventState> ShowEvents;
        public List<MatchPlanState> MatchPlans;
        public List<PromoPlanState> PromoPlans;
        public List<ShowResultState> ShowResults;
        public List<MatchResultState> MatchResults;
        public List<PromoResultState> PromoResults;
        public List<TransactionRecord> Transactions;
        public List<InboxMessageState> InboxMessages;
        public List<string> ProcessedIds;

        public GameSave()
        {
            Wrestlers = new List<WrestlerState>();
            SystemNames = PWManager.Domain.Models.SystemNames.CreateDefaults();
            TagTeams = new List<TagTeamState>();
            Managers = new List<ManagerState>();
            Contracts = new List<ContractState>();
            StaffDepartments = new List<StaffDepartmentState>();
            VenueContracts = new List<VenueContractState>();
            Schedules = new List<ScheduleState>();
            Shows = new List<ShowState>();
            ShowEvents = new List<ShowEventState>();
            MatchPlans = new List<MatchPlanState>();
            PromoPlans = new List<PromoPlanState>();
            ShowResults = new List<ShowResultState>();
            MatchResults = new List<MatchResultState>();
            PromoResults = new List<PromoResultState>();
            Transactions = new List<TransactionRecord>();
            InboxMessages = new List<InboxMessageState>();
            ProcessedIds = new List<string>();
        }

        public static GameSave CreateNew(int worldSeed, DateTime utcNow)
        {
            if (utcNow.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("Timestamp must be UTC.", nameof(utcNow));
            }

            var timestamp = utcNow.ToString("O");

            return new GameSave
            {
                SaveVersion = CurrentSaveVersion,
                SaveId = EntityId.CreateRuntimeId(),
                CreatedAtUtc = timestamp,
                UpdatedAtUtc = timestamp,
                WorldSeed = worldSeed,
                CurrentDate = new GameDate(2026, 6, 1)
            };
        }
    }
}
