using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Identifiers;

namespace PWManager.Domain.Models
{
    public enum InboxMessageType { Information, Navigation, Decision }
    public enum InboxPriority { Normal, Important, Required }
    public enum InboxMessageStatus { Unread, Read, Resolved, Expired }
    public enum InboxTargetType { None, Wrestler, Show, Contract, Finance, Promotion, Season, WrestlerReport }

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
        public InboxMessageState Copy() => (InboxMessageState)MemberwiseClone();
    }

    [Serializable]
    public sealed class GameSave
    {
        public const int CurrentSaveVersion = 30;

        public int SaveVersion;
        public string SaveId;
        public string CreatedAtUtc;
        public string UpdatedAtUtc;
        public int WorldSeed;
        public GameDate CurrentDate;
        public PromotionState Promotion;
        public List<PromotionState> OtherPromotions = new();
        public PlayerCharacterState Player;
        public List<LocalizedNameState> SystemNames;
        public List<WrestlerState> Wrestlers;
        public bool WorldPopulationInitialized;
        public List<string> UndiscoveredWrestlerIds = new();
        public List<StoryRelationshipState> StoryRelationships = new();
        public List<StorylineState> Storylines = new();
        public List<StoryCandidateState> StoryCandidates = new();
        public int RelationshipSchemaVersion;
        public List<TagTeamState> TagTeams;
        public List<ChampionshipState> Championships = new();
        public List<StableState> Stables = new();
        public List<ManagerState> Managers;
        public List<ContractState> Contracts;
        public List<SalaryPaymentState> SalaryPayments = new();
        public List<ContractNegotiationState> ContractNegotiations = new();
        public List<ContractTerminationState> ContractTerminations = new();
        public List<SeasonResultState> SeasonResults = new();
        public List<SponsorSeasonState> SponsorSeasons = new();
        public MerchandiseState Merchandise = new();
        public List<WrestlerExperienceState> ExperienceRecords = new();
        public List<GrowthQuarterResultState> GrowthQuarterResults = new();
        public OptionalGameDate LastGrowthQuarterEnd;
        public TrainingState Training;
        public List<WrestlerCareerEventState> CareerEvents = new();
        public GameDate CareerObservationStartDate;
        public OptionalGameDate LastCareerQuarterEnd;
        public List<StaffDepartmentState> StaffDepartments;
        public List<ScoutAssignmentState> ScoutAssignments;
        public List<ScoutCandidateState> ScoutCandidates;
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
        public List<WrestlerBehaviorState> WrestlerBehaviors = new();
        public List<LockerRoomRelationshipState> LockerRoomRelationships = new();
        public List<LockerRoomEventState> LockerRoomEvents = new();

        public bool IsWrestlerDiscovered(string wrestlerId) => UndiscoveredWrestlerIds?.Contains(wrestlerId) != true;

        public IEnumerable<ContractState> OwnContracts => (Contracts ?? Enumerable.Empty<ContractState>())
            .Where(x => x != null && (string.IsNullOrEmpty(x.PromotionId) || x.PromotionId == Promotion?.Id));

        public PromotionState FindPromotion(string promotionId) => Promotion?.Id == promotionId ? Promotion :
            OtherPromotions?.FirstOrDefault(x => x?.Id == promotionId);

        public bool IsCancelledShow(ShowState show) => show != null &&
            Schedules?.Any(x => x?.Id == show.ScheduleId && x.Status == ScheduleStatus.Cancelled) == true;
        public bool IsRunningShow(ShowState show) => show != null &&
            (show.Status is ShowStatus.InProgress or ShowStatus.ResultReview or ShowStatus.ResultsReviewed) && !IsCancelledShow(show);
        public bool HasRunningShow => Shows?.Any(IsRunningShow) == true;

        public GameSave()
        {
            Wrestlers = new List<WrestlerState>();
            SystemNames = PWManager.Domain.Models.SystemNames.CreateDefaults();
            TagTeams = new List<TagTeamState>();
            Managers = new List<ManagerState>();
            Contracts = new List<ContractState>();
            StaffDepartments = new List<StaffDepartmentState>();
            ScoutAssignments = new List<ScoutAssignmentState>();
            ScoutCandidates = new List<ScoutCandidateState>();
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
                LastGrowthQuarterEnd = new OptionalGameDate(new GameDate(2026, 5, 31)),
                LastCareerQuarterEnd = new OptionalGameDate(new GameDate(2026, 5, 31)),
                CareerObservationStartDate = new GameDate(2026, 6, 1),
                CurrentDate = new GameDate(2026, 6, 1)
            };
        }

        public GameSave CopyForStorySimulation()
        {
            var copy = (GameSave)MemberwiseClone();
            copy.ShowResults = new(ShowResults ?? new());
            copy.MatchResults = new(MatchResults ?? new());
            copy.PromoResults = new(PromoResults ?? new());
            copy.TagTeams = new();
            foreach (var team in TagTeams ?? new())
                copy.TagTeams.Add(new TagTeamState { Id = team.Id, PromotionId = team.PromotionId, Name = team.Name,
                    MemberIds = new(team.MemberIds), Status = team.Status, Chemistry = team.Chemistry });
            copy.Stables = new();
            foreach (var stable in Stables ?? new())
                copy.Stables.Add(new StableState { Id = stable.Id, PromotionId = stable.PromotionId, Name = stable.Name,
                    MemberIds = new(stable.MemberIds), LeaderId = stable.LeaderId, Status = stable.Status, Chemistry = stable.Chemistry });
            copy.Wrestlers = new();
            foreach (var wrestler in Wrestlers) copy.Wrestlers.Add(wrestler.CopyForGroupSimulation());
            copy.StoryRelationships = new();
            foreach (var pair in StoryRelationships ?? new())
                copy.StoryRelationships.Add(pair == null ? null : new StoryRelationshipState
                {
                    FirstWrestlerId = pair.FirstWrestlerId, SecondWrestlerId = pair.SecondWrestlerId,
                    Value = pair.Value, Hostility = pair.Hostility
                });
            copy.Storylines = new();
            foreach (var story in Storylines ?? new())
                copy.Storylines.Add(story?.CopyForSimulation());
            return copy;
        }

        public GameSave CopyForGrowthSimulation()
        {
            var copy = (GameSave)MemberwiseClone();
            copy.Wrestlers = Wrestlers.Select(w =>
            {
                var actor = w.CopyForGroupSimulation();
                actor.Attributes = w.Attributes.Copy(); actor.Growth = w.Growth.Copy();
                return actor;
            }).ToList();
            return copy;
        }

        public GameSave CopyForBehaviorSimulation()
        {
            var copy = (GameSave)MemberwiseClone();
            copy.WrestlerBehaviors = (WrestlerBehaviors ?? new()).Select(x => x.Copy()).ToList();
            copy.LockerRoomRelationships = (LockerRoomRelationships ?? new()).Select(x => x.Copy()).ToList();
            copy.LockerRoomEvents = (LockerRoomEvents ?? new()).Select(x => x.Copy()).ToList();
            copy.InboxMessages = (InboxMessages ?? new()).Select(x => x.Copy()).ToList();
            copy.ProcessedIds = new(ProcessedIds ?? new());
            return copy;
        }

        public GameSave CopyForCareerSimulation()
        {
            var copy = (GameSave)MemberwiseClone();
            copy.Promotion = Promotion.CopyForFanSimulation();
            copy.Promotion.AppliedPrestigeReasonIds = new(Promotion.AppliedPrestigeReasonIds);
            copy.Promotion.QuarterlyGrowth = Promotion.QuarterlyGrowth?.Copy();
            copy.Wrestlers = Wrestlers.Select(w =>
            {
                var actor = w.CopyForGroupSimulation();
                actor.Status = w.Status.Copy(); actor.Momentum = w.Momentum.Copy(); actor.FanReaction = w.FanReaction.Copy(); actor.Fame = w.Fame.Copy();
                return actor;
            }).ToList();
            copy.CareerEvents = new(CareerEvents ?? new());
            return copy;
        }
    }
}
