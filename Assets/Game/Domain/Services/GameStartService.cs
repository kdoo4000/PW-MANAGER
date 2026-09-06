using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Identifiers;
using PWManager.Domain.Models;
using PWManager.Domain.Validation;

namespace PWManager.Domain.Services
{
    public sealed class InitialWrestlerContractInput
    {
        public WrestlerState Wrestler;
        public int ContractEndYear = 2027;
        public long SigningBonus;
        public long MonthlySalary;
        public long TerminationCost;
        public bool HasExpectedRole;
        public ExpectedRole ExpectedRole;
    }

    public sealed class GameStartRequest
    {
        public bool HasPlayerCharacter;
        public string PlayerName;
        public PlayerCareerRole PlayerRole;
        public PlayerReputation PlayerReputation;
        public PlayerWrestlingType PlayerWrestlingType;
        public string PlayerWrestlingStyleId;
        public WrestlerGender PlayerGender;
        public GameDate PlayerBirthDate = new(1994, 6, 1);
        public int PlayerHeightCm = 180;
        public int PlayerWeightKg = 90;
        public string PromotionName;
        public string PromotionAbbreviation;
        public long InitialCash;
        public long InitialPrestige;
        public int WorldSeed;
        public DateTime UtcNow;
        public IReadOnlyList<InitialWrestlerContractInput> WrestlerContracts;
        public string RegularVenueId;
        public long RegularVenueProductionCost;
        public RegularShowFrequency RegularShowFrequency;
        public PpvFrequency PpvFrequency;
        public IReadOnlyList<ScheduledShowType> PpvTypes;
    }

    public sealed class GameStartService
    {
        private static readonly GameDate StartDate = new(2026, 6, 1);
        private static readonly GameDate SeasonEndDate = new(2027, 5, 31);
        private readonly Func<string> createId;

        public GameStartService(Func<string> createId = null)
        {
            this.createId = createId ?? EntityId.CreateRuntimeId;
        }

        public GameSave CreateInitialSave(GameStartRequest request)
        {
            ValidateRequest(request);

            var save = GameSave.CreateNew(request.WorldSeed, request.UtcNow);
            save.Promotion = new PromotionState
            {
                Id = createId(),
                Name = request.PromotionName.Trim(),
                Abbreviation = string.IsNullOrWhiteSpace(request.PromotionAbbreviation)
                    ? PromotionState.CreateAbbreviation(request.PromotionName)
                    : request.PromotionAbbreviation.Trim().ToUpperInvariant(),
                InitialCash = request.InitialCash,
                PromotionPrestige = request.InitialPrestige
            };

            if (request.HasPlayerCharacter) AddPlayer(save, request);

            foreach (var input in request.WrestlerContracts)
            {
                var wrestler = CloneWrestler(input.Wrestler);
                wrestler.PromotionId = save.Promotion.Id;
                wrestler.Roster.ActivityState = RosterActivityState.Active;
                wrestler.Identity.ExpiryDate = default;
                save.Wrestlers.Add(wrestler);

                var contract = new ContractState
                {
                    Id = createId(),
                    PersonId = wrestler.Id,
                    Type = ContractType.Wrestler,
                    StartDate = StartDate,
                    EndDate = new GameDate(input.ContractEndYear, 5, 31),
                    SigningBonus = input.SigningBonus,
                    MonthlySalary = input.MonthlySalary,
                    TerminationCost = input.TerminationCost,
                    HasExpectedRole = input.HasExpectedRole,
                    ExpectedRole = input.ExpectedRole,
                    Status = ContractStatus.Active
                };
                save.Contracts.Add(contract);
                if (input.SigningBonus > 0)
                {
                    save.Transactions.Add(new TransactionRecord
                    {
                        Id = createId(), PromotionId = save.Promotion.Id, Date = StartDate,
                        Type = TransactionType.SigningFee, Amount = -input.SigningBonus,
                        ReasonId = contract.Id
                    });
                }
            }

            AddInitialStaff(save);
            var venueContract = new VenueContractState
            {
                Id = createId(), VenueId = request.RegularVenueId,
                ContractType = VenueContractType.RegularSeason,
                StartDate = StartDate, EndDate = SeasonEndDate,
                ProductionCost = request.RegularVenueProductionCost,
                Status = VenueContractStatus.Reserved
            };
            save.VenueContracts.Add(venueContract);

            var scheduleResult = new SeasonScheduleGenerator(createId).Generate(new SeasonScheduleGenerationRequest
            {
                SeasonStartYear = 2026,
                RegularShowFrequency = request.RegularShowFrequency,
                PpvFrequency = request.PpvFrequency,
                PpvTypes = request.PpvTypes,
                RegularVenueContractId = venueContract.Id,
                InitialStatus = ScheduleStatus.Confirmed
            });
            save.SeasonPolicy = scheduleResult.Policy;
            save.SeasonPolicy.RegularShowName = "정규 쇼";
            save.SeasonPolicy.PpvShowTitles = scheduleResult.Schedules
                .Where(x => x.ShowType != ScheduledShowType.Regular)
                .Select(x => new PpvTitlePolicyState { Month = x.Date.Month, Title = $"{x.Date.Month}월 PPV" }).ToList();
            save.Schedules.AddRange(scheduleResult.Schedules);
            save.Promotion.SeasonPolicyId = save.SeasonPolicy.Id;
            AddInitialShowDrafts(save, venueContract.Id);
            save.Promotion.Audience = request.HasPlayerCharacter
                ? FanAudienceService.CreateInitial(save.CurrentDate, PlayerCharacterRules.StartingFans(request.PlayerReputation))
                : FanAudienceService.CreateInitial(save.CurrentDate);

            var errors = GameSaveValidator.Validate(save);
            if (errors.Count > 0)
                throw new InvalidOperationException("Initial save validation failed: " + string.Join(" | ", errors));
            return save;
        }

        private void AddInitialShowDrafts(GameSave save, string venueContractId)
        {
            var regularSequence = 0;
            foreach (var schedule in save.Schedules.OrderBy(x => x.Date))
            {
                if (schedule.ShowType == ScheduledShowType.Regular) regularSequence++;
                save.Shows.Add(new ShowState
                {
                    Id = createId(),
                    ScheduleId = schedule.Id,
                    Name = ShowNamingRules.Compose(save.Promotion.Abbreviation,
                        schedule.ShowType == ScheduledShowType.Regular
                            ? save.SeasonPolicy.RegularShowName
                            : save.SeasonPolicy.PpvShowTitles.Single(x => x.Month == schedule.Date.Month).Title,
                        schedule.ShowType, regularSequence, schedule.Date.Year),
                    ShowType = schedule.ShowType,
                    Date = schedule.Date,
                    VenueContractId = venueContractId,
                    DurationLimit = 120,
                    EstimatedCost = save.VenueContracts.Single(x => x.Id == venueContractId).ProductionCost,
                    Status = ShowStatus.Draft
                });
            }
        }

        private static void ValidateRequest(GameStartRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var playerAge = request.PlayerBirthDate.AgeOn(StartDate);
            if (request.HasPlayerCharacter && (playerAge < 18 || playerAge > 70 || !PlayerCharacterRules.IsPhysicalProfileValid(request.PlayerGender, request.PlayerHeightCm, request.PlayerWeightKg)))
                throw new ArgumentException("Player age, height, or weight is outside the allowed range.", nameof(request));
            if (request.HasPlayerCharacter && request.PlayerRole == PlayerCareerRole.WrestlerManager &&
                !PlayerCharacterRules.IsStyleAvailable(request.PlayerWrestlingStyleId, request.PlayerHeightCm, request.PlayerGender))
                throw new ArgumentException("The selected wrestling style is not available for the player's physical profile.", nameof(request));
            if (string.IsNullOrWhiteSpace(request.PromotionName)) throw new ArgumentException("Promotion name is required.", nameof(request));
            if (!string.IsNullOrWhiteSpace(request.PromotionAbbreviation) &&
                (request.PromotionAbbreviation.Trim().Length < 2 || request.PromotionAbbreviation.Trim().Length > 8))
                throw new ArgumentException("Promotion abbreviation must contain between 2 and 8 characters.", nameof(request));
            if (request.UtcNow.Kind != DateTimeKind.Utc) throw new ArgumentException("UtcNow must be UTC.", nameof(request));
            if (request.InitialCash < 0 || request.InitialPrestige < 0) throw new ArgumentException("Initial money and prestige cannot be negative.", nameof(request));
            if (!EntityId.IsValidStaticId(request.RegularVenueId) || !request.RegularVenueId.StartsWith("venue_", StringComparison.Ordinal))
                throw new ArgumentException("A valid regular venue ID is required.", nameof(request));
            if (request.RegularVenueProductionCost < 0) throw new ArgumentException("Venue production cost cannot be negative.", nameof(request));

            var contracts = request.WrestlerContracts;
            if (contracts == null || contracts.Count < 8 || contracts.Count > 20)
                throw new ArgumentException("The initial roster must contain between 8 and 20 wrestlers.", nameof(request));
            if (contracts.Any(x => x?.Wrestler == null)) throw new ArgumentException("Every contract requires a wrestler.", nameof(request));
            if (contracts.Select(x => x.Wrestler.Id).Distinct(StringComparer.Ordinal).Count() != contracts.Count)
                throw new ArgumentException("The initial roster contains duplicate wrestlers.", nameof(request));
            if (contracts.Any(x => x.ContractEndYear < 2027)) throw new ArgumentException("Initial contracts must end on or after May 31, 2027.", nameof(request));
            if (contracts.Any(x => x.SigningBonus < 0 || x.MonthlySalary < 0 || x.TerminationCost < 0))
                throw new ArgumentException("Contract amounts cannot be negative.", nameof(request));
            if (contracts.Sum(x => x.SigningBonus) > request.InitialCash)
                throw new ArgumentException("Signing bonuses exceed the initial cash.", nameof(request));
        }

        private void AddPlayer(GameSave save, GameStartRequest request)
        {
            var build = PlayerCharacterRules.Build(request.PlayerRole, request.PlayerReputation, request.PlayerWrestlingType, request.PlayerWrestlingStyleId);
            var playerName = string.IsNullOrWhiteSpace(request.PlayerName) ? "플레이어" : request.PlayerName.Trim();
            save.Player = new PlayerCharacterState
            {
                Name = playerName, Role = request.PlayerRole, Reputation = request.PlayerReputation,
                WrestlingType = request.PlayerWrestlingType, WrestlingStyleId = build.WrestlingStyleId,
                Gender = request.PlayerGender, BirthDate = request.PlayerBirthDate, HeightCm = request.PlayerHeightCm,
                WeightKg = request.PlayerWeightKg, MatchAbility = build.MatchAbility, PromoAbility = build.PromoAbility
            };

            if (request.PlayerRole == PlayerCareerRole.ProfessionalManager)
            {
                var manager = new ManagerState
                {
                    Id = createId(), PromotionId = save.Promotion.Id, PromoPotentialCap = build.PromoAbility,
                    ActivityState = RosterActivityState.Active,
                    Attributes = new ManagerAttributesState
                    {
                        Charisma = build.Attributes.Charisma, MicWork = build.Attributes.MicWork, Improvisation = build.Attributes.Improvisation, Acting = build.Attributes.Acting,
                        FaceWork = build.Attributes.FaceWork, HeelWork = build.Attributes.HeelWork, Comedy = build.Attributes.Comedy,
                        RingImprovisation = build.Attributes.Improvisation, SpotWork = build.Attributes.Acting, Selling = build.Attributes.Charisma
                    }
                };
                save.Managers.Add(manager);
                save.Player.ManagerId = manager.Id;
                return;
            }

            var wrestler = CreatePlayerWrestler(save, request, playerName, build);
            save.Wrestlers.Add(wrestler);
            save.Player.WrestlerId = wrestler.Id;
        }

        private WrestlerState CreatePlayerWrestler(GameSave save, GameStartRequest request, string name, PlayerCharacterBuild build)
        {
            var type = request.PlayerWrestlingType;
            return new WrestlerState
            {
                Identity = new WrestlerIdentityState
                {
                    Id = createId(), LegalName = name, RingName = name, Background = WrestlerBackground.OtherPromotion,
                    Gender = request.PlayerGender, BirthDate = request.PlayerBirthDate, CreatedDate = StartDate,
                    HeightCm = request.PlayerHeightCm, WeightKg = request.PlayerWeightKg,
                    BodyType = WrestlerBodyType.Balanced, CareerYears = 10
                },
                Attributes = build.Attributes,
                Growth = new WrestlerGrowthState { MatchPotentialCap = 20, PromoPotentialCap = 20 },
                Condition = new WrestlerConditionState { Condition = 100, Satisfaction = 50, Availability = WrestlerAvailability.Available },
                Roster = new WrestlerRosterState { PromotionId = save.Promotion.Id, ActivityState = RosterActivityState.Active },
                Presentation = new WrestlerPresentationState
                {
                    MatchArchetype = type == PlayerWrestlingType.Worker ? MatchArchetype.Technical : type == PlayerWrestlingType.Showman ? MatchArchetype.Spot : MatchArchetype.Balanced,
                    PromoArchetype = type == PlayerWrestlingType.Showman ? PromoArchetype.Charisma : PromoArchetype.Balanced,
                    PromoDisposition = PromoDisposition.Balanced, WrestlingStyleId = build.WrestlingStyleId
                }
            };
        }

        private void AddInitialStaff(GameSave save)
        {
            foreach (var type in new[] { StaffDepartmentType.Medical, StaffDepartmentType.Scout, StaffDepartmentType.Promotion })
                save.StaffDepartments.Add(new StaffDepartmentState { Id = createId(), DepartmentType = type, CurrentLevel = 1, UnlockedLevel = 1 });
            save.StaffDepartments.Add(new StaffDepartmentState
            {
                Id = createId(), DepartmentType = StaffDepartmentType.Commentary, CurrentLevel = 0, UnlockedLevel = 0
            });
        }

        private static WrestlerState CloneWrestler(WrestlerState source)
        {
            return new WrestlerState
            {
                Identity = new WrestlerIdentityState
                {
                    Id = source.Identity.Id, LegalName = source.Identity.LegalName, RingName = source.Identity.RingName,
                    Nickname = source.Identity.Nickname, Gender = source.Identity.Gender, Background = source.Identity.Background,
                    BirthDate = source.Identity.BirthDate, CreatedDate = source.Identity.CreatedDate, ExpiryDate = source.Identity.ExpiryDate,
                    HeightCm = source.Identity.HeightCm, WeightKg = source.Identity.WeightKg,
                    BodyType = source.Identity.BodyType, CareerYears = source.Identity.CareerYears
                },
                Attributes = CloneAttributes(source.Attributes),
                Condition = new WrestlerConditionState
                {
                    Condition = source.Condition.Condition, InjuryStatus = source.Condition.InjuryStatus,
                    RecoveryDate = source.Condition.RecoveryDate, Satisfaction = source.Condition.Satisfaction,
                    Availability = source.Condition.Availability
                },
                Growth = new WrestlerGrowthState
                {
                    MatchPotentialCap = source.Growth.MatchPotentialCap, PromoPotentialCap = source.Growth.PromoPotentialCap,
                    GrowthProgress = source.Growth.GrowthProgress, AgingState = source.Growth.AgingState,
                    ExperienceLogIds = new List<string>(source.Growth.ExperienceLogIds),
                    AttributeHistoryIds = new List<string>(source.Growth.AttributeHistoryIds)
                },
                Status = new WrestlerStatusState
                {
                    StatusValue = source.Status.StatusValue, IsRookie = source.Status.IsRookie, DebutDate = source.Status.DebutDate,
                    OfficialMatchCount = source.Status.OfficialMatchCount, LastStatusChange = source.Status.LastStatusChange,
                    StatusChangeRecordIds = new List<string>(source.Status.StatusChangeRecordIds)
                },
                Momentum = new WrestlerMomentumState
                {
                    Momentum = source.Momentum.Momentum, MomentumFailure = source.Momentum.MomentumFailure,
                    LastMomentumActivityDate = source.Momentum.LastMomentumActivityDate, LastMomentumGain = source.Momentum.LastMomentumGain,
                    LastMomentumRelease = source.Momentum.LastMomentumRelease, ReleaseType = source.Momentum.ReleaseType,
                    ReleaseReason = source.Momentum.ReleaseReason
                },
                FanReaction = source.FanReaction.Copy(),
                Roster = new WrestlerRosterState
                {
                    PromotionId = source.Roster.PromotionId, Alignment = source.Roster.Alignment,
                    ActivityState = source.Roster.ActivityState, ActiveTagTeamId = source.Roster.ActiveTagTeamId,
                    ActiveStableId = source.Roster.ActiveStableId, LastMatchDate = source.Roster.LastMatchDate,
                    LastAppearanceDate = source.Roster.LastAppearanceDate
                },
                Presentation = new WrestlerPresentationState
                {
                    MatchArchetype = source.Presentation.MatchArchetype, PromoArchetype = source.Presentation.PromoArchetype, PromoDisposition = source.Presentation.PromoDisposition,
                    WrestlingStyleId = source.Presentation.WrestlingStyleId,
                    TraitIds = new List<string>(source.Presentation.TraitIds),
                    SignatureMoveIds = new List<string>(source.Presentation.SignatureMoveIds),
                    FinisherMoveIds = new List<string>(source.Presentation.FinisherMoveIds)
                }
            };
        }

        private static WrestlerAttributesState CloneAttributes(WrestlerAttributesState x) => new()
        {
            RingPsychology=x.RingPsychology, RingImprovisation=x.RingImprovisation, Technical=x.Technical,
            Brawling=x.Brawling, Power=x.Power, HighFlying=x.HighFlying, SpotWork=x.SpotWork,
            SpecialtyMatches=x.SpecialtyMatches, Selling=x.Selling, Stamina=x.Stamina,
            Charisma=x.Charisma, MicWork=x.MicWork, Improvisation=x.Improvisation, Acting=x.Acting,
            FaceWork=x.FaceWork, HeelWork=x.HeelWork, Comedy=x.Comedy
        };
    }
}
