using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Identifiers;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Domain.Validation
{
    public static class GameSaveValidator
    {
        private const float AttributeTotalTolerance = .001f;

        public static List<string> Validate(GameSave save)
        {
            var errors = new List<string>();
            if (save == null)
            {
                errors.Add("GameSave is required.");
                return errors;
            }

            if (save.Promotion == null)
            {
                errors.Add("Promotion is required.");
                return errors;
            }

            ValidatePromotion(save.Promotion, errors);
            ValidateWrestlers(save, errors);
            ValidateTagTeams(save, errors);
            ValidateManagers(save, errors);
            ValidateContracts(save, errors);
            ValidateOperations(save, errors);
            ValidateShows(save, errors);
            ValidateTransactions(save, errors);
            return errors;
        }

        private static void ValidateShows(GameSave save, List<string> errors)
        {
            var scheduleIds = new HashSet<string>((save.Schedules ?? new List<ScheduleState>()).Where(x => x != null).Select(x => x.Id), StringComparer.Ordinal);
            var venueContractIds = new HashSet<string>((save.VenueContracts ?? new List<VenueContractState>()).Where(x => x != null).Select(x => x.Id), StringComparer.Ordinal);
            var showIds = new HashSet<string>(StringComparer.Ordinal);
            var eventIds = new HashSet<string>(StringComparer.Ordinal);
            var matchIds = new HashSet<string>(StringComparer.Ordinal);
            var promoIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var match in save.MatchPlans ?? new List<MatchPlanState>())
            {
                if (match == null) { errors.Add("Match plan cannot be null."); continue; }
                ValidateUniqueRuntimeId(match.Id, "MatchPlan", matchIds, errors);
                ValidateStaticId(match.MatchTypeId, "matchtype_", "MatchTypeId", errors);
                if (!string.IsNullOrEmpty(match.MatchGimmickId))
                    ValidateStaticId(match.MatchGimmickId, "gimmick_", "MatchGimmickId", errors);
            }
            foreach (var promo in save.PromoPlans ?? new List<PromoPlanState>())
            {
                if (promo == null) { errors.Add("Promo plan cannot be null."); continue; }
                ValidateUniqueRuntimeId(promo.Id, "PromoPlan", promoIds, errors);
            }
            foreach (var showEvent in save.ShowEvents ?? new List<ShowEventState>())
            {
                if (showEvent == null) { errors.Add("Show event cannot be null."); continue; }
                ValidateUniqueRuntimeId(showEvent.Id, "ShowEvent", eventIds, errors);
                if (showEvent.PlannedDuration <= 0 || showEvent.PlannedDuration % 5 != 0)
                    errors.Add($"Show event {showEvent.Id} duration must be a positive multiple of five.");
                if (showEvent.EventType == ShowEventType.Match && !matchIds.Contains(showEvent.DetailId))
                    errors.Add($"Show event {showEvent.Id} references a missing match plan.");
                if (showEvent.EventType == ShowEventType.Promo && !promoIds.Contains(showEvent.DetailId))
                    errors.Add($"Show event {showEvent.Id} references a missing promo plan.");
            }
            foreach (var show in save.Shows ?? new List<ShowState>())
            {
                if (show == null) { errors.Add("Show cannot be null."); continue; }
                ValidateUniqueRuntimeId(show.Id, "Show", showIds, errors);
                if (!scheduleIds.Contains(show.ScheduleId)) errors.Add($"Show {show.Id} references a missing schedule.");
                if (!venueContractIds.Contains(show.VenueContractId)) errors.Add($"Show {show.Id} references a missing venue contract.");
                if (show.DurationLimit <= 0 || show.DurationLimit % 5 != 0) errors.Add($"Show {show.Id} duration limit must be a positive multiple of five.");
                var timeline = show.TimelineEventIds ?? new List<string>();
                if (timeline.Distinct(StringComparer.Ordinal).Count() != timeline.Count) errors.Add($"Show {show.Id} contains duplicate timeline events.");
                foreach (var id in timeline)
                {
                    if (!eventIds.Contains(id)) errors.Add($"Show {show.Id} references a missing timeline event.");
                    else
                    {
                        var owner = save.ShowEvents.First(x => x.Id == id);
                        if (owner.ShowId != show.Id) errors.Add($"Show event {id} belongs to another show.");
                    }
                }
                if (timeline.Count > 0 && (show.OpeningEventId != timeline[0] || show.MainEventId != timeline[timeline.Count - 1]))
                    errors.Add($"Show {show.Id} opening or main event does not match its timeline.");
                if (show.Status == ShowStatus.Confirmed)
                {
                    if (show.CalculatePlannedDuration(save.ShowEvents) != show.DurationLimit) errors.Add($"Confirmed show {show.Id} does not use its full duration.");
                    if (show.ShowVersion < 1) errors.Add($"Confirmed show {show.Id} must have a version.");
                    if (show.AttendanceVarianceBasisPoints < 9500 || show.AttendanceVarianceBasisPoints > 10500)
                        errors.Add($"Show {show.Id} attendance variance must be between -5% and +5%.");
                }
            }
            foreach (var showEvent in save.ShowEvents ?? new List<ShowEventState>())
                if (showEvent != null && !showIds.Contains(showEvent.ShowId)) errors.Add($"Show event {showEvent.Id} references a missing show.");
        }

        private static void ValidateOperations(GameSave save, List<string> errors)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var departmentTypes = new HashSet<StaffDepartmentType>();
            foreach (var department in save.StaffDepartments ?? new List<StaffDepartmentState>())
            {
                if (department == null) { errors.Add("Staff department cannot be null."); continue; }
                ValidateUniqueRuntimeId(department.Id, "StaffDepartment", ids, errors);
                if (!departmentTypes.Add(department.DepartmentType)) errors.Add($"Duplicate staff department: {department.DepartmentType}");
                if (department.CurrentLevel < 0 || department.CurrentLevel > 5) errors.Add("Staff CurrentLevel must be between 0 and 5.");
                if (department.UnlockedLevel < department.CurrentLevel || department.UnlockedLevel > 5)
                    errors.Add("Staff UnlockedLevel must be between CurrentLevel and 5.");
            }

            var venueIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var contract in save.VenueContracts ?? new List<VenueContractState>())
            {
                if (contract == null) { errors.Add("Venue contract cannot be null."); continue; }
                ValidateUniqueRuntimeId(contract.Id, "VenueContract", ids, errors);
                venueIds.Add(contract.Id);
                ValidateStaticId(contract.VenueId, "venue_", "VenueId", errors);
                if (contract.StartDate.CompareTo(contract.EndDate) > 0) errors.Add($"Venue contract {contract.Id} starts after it ends.");
                if (contract.ProductionCost < 0) errors.Add($"Venue contract {contract.Id} contains a negative production cost.");
            }

            var scheduleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var schedule in save.Schedules ?? new List<ScheduleState>())
            {
                if (schedule == null) { errors.Add("Schedule cannot be null."); continue; }
                ValidateUniqueRuntimeId(schedule.Id, "Schedule", ids, errors);
                scheduleIds.Add(schedule.Id);
                if (schedule.AttendanceVarianceBasisPoints < TicketPricingRules.MinimumAttendanceVarianceBasisPoints ||
                    schedule.AttendanceVarianceBasisPoints > TicketPricingRules.MaximumAttendanceVarianceBasisPoints)
                    errors.Add($"Schedule {schedule.Id} attendance variance must be between -5% and +5%.");
            }

            if (save.SeasonPolicy == null)
            {
                if (!string.IsNullOrEmpty(save.Promotion.SeasonPolicyId)) errors.Add("Promotion references a missing season policy.");
                return;
            }

            ValidateUniqueRuntimeId(save.SeasonPolicy.Id, "SeasonPolicy", ids, errors);
            if (save.Promotion.SeasonPolicyId != save.SeasonPolicy.Id) errors.Add("Promotion.SeasonPolicyId does not match SeasonPolicy.Id.");
            if (save.SeasonPolicy.StartDate.Month != 6 || save.SeasonPolicy.StartDate.Day != 1 ||
                save.SeasonPolicy.EndDate.Month != 5 || save.SeasonPolicy.EndDate.Day != 31 ||
                save.SeasonPolicy.EndDate.Year != save.SeasonPolicy.StartDate.Year + 1)
                errors.Add("Season policy must run from June 1 through May 31 of the next year.");
            if (!venueIds.Contains(save.SeasonPolicy.RegularVenueContractId)) errors.Add("Season policy references a missing regular venue contract.");
            if (!string.IsNullOrEmpty(save.SeasonPolicy.SignaturePpvScheduleId) && !scheduleIds.Contains(save.SeasonPolicy.SignaturePpvScheduleId))
                errors.Add("Season policy references a missing signature PPV schedule.");
        }

        private static void ValidateUniqueRuntimeId(string id, string name, HashSet<string> ids, List<string> errors)
        {
            if (!EntityId.IsValidRuntimeId(id)) errors.Add($"{name}.Id must be a runtime GUID.");
            else if (!ids.Add(id)) errors.Add($"Duplicate {name}.Id: {id}");
        }

        private static void ValidateManagers(GameSave save, List<string> errors)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var manager in save.Managers ?? new List<ManagerState>())
            {
                if (manager == null) { errors.Add("Manager cannot be null."); continue; }
                if (!EntityId.IsValidRuntimeId(manager.Id)) errors.Add("Manager.Id must be a runtime GUID.");
                else if (!ids.Add(manager.Id)) errors.Add($"Duplicate Manager.Id: {manager.Id}");
                if (!string.IsNullOrEmpty(manager.StaticDataId) &&
                    (!EntityId.IsValidStaticId(manager.StaticDataId) || !manager.StaticDataId.StartsWith("manager_", StringComparison.Ordinal)))
                    errors.Add($"Invalid manager static ID: {manager.StaticDataId}");
                if (manager.PromotionId != save.Promotion.Id) errors.Add($"Manager {manager.Id} references another promotion.");
                var values = new[] { manager.Attributes.Charisma, manager.Attributes.MicWork,
                    manager.Attributes.Improvisation, manager.Attributes.Acting, manager.Attributes.FaceWork,
                    manager.Attributes.HeelWork, manager.Attributes.Comedy, manager.Attributes.RingImprovisation,
                    manager.Attributes.SpotWork, manager.Attributes.Selling };
                foreach (var value in values) ValidateRange(value, 1, 20, "Manager attribute", errors);
                ValidateRange(manager.PromoPotentialCap, 1, 20, "Manager PromoPotentialCap", errors);
            }
        }

        private static void ValidatePromotion(PromotionState promotion, List<string> errors)
        {
            if (!EntityId.IsValidRuntimeId(promotion.Id)) errors.Add("Promotion.Id must be a runtime GUID.");
            if (string.IsNullOrWhiteSpace(promotion.Name)) errors.Add("Promotion.Name is required.");
            if (promotion.InitialCash < 0) errors.Add("Promotion.InitialCash must be non-negative.");
            if (promotion.PromotionPrestige < 0) errors.Add("Promotion.PromotionPrestige must be non-negative.");
        }

        private static void ValidateWrestlers(GameSave save, List<string> errors)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var wrestler in save.Wrestlers ?? new List<WrestlerState>())
            {
                if (wrestler == null) { errors.Add("Wrestler cannot be null."); continue; }
                if (!EntityId.IsValidRuntimeId(wrestler.Id)) errors.Add("Wrestler.Id must be a runtime GUID.");
                else if (!ids.Add(wrestler.Id)) errors.Add($"Duplicate Wrestler.Id: {wrestler.Id}");
                ValidateIdentity(wrestler, errors);
                if (wrestler.PromotionId != save.Promotion.Id) errors.Add($"Wrestler {wrestler.Id} references another promotion.");
                ValidateAttributes(wrestler, errors);
                ValidateRange(wrestler.Condition.Condition, 0, 100, "Condition", errors);
                ValidateRange(wrestler.Condition.Satisfaction, 0, 100, "Satisfaction", errors);
                ValidateRange(wrestler.Status.StatusValue, 0, 100, "StatusValue", errors);
                ValidateRange(wrestler.Momentum.Momentum, 0, 100, "Momentum", errors);
                ValidateRange(wrestler.FanReaction.ManiaFanReaction, -100, 100, "ManiaFanReaction", errors);
                ValidateRange(wrestler.FanReaction.LightFanReaction, -100, 100, "LightFanReaction", errors);
                ValidateRange(wrestler.FanReaction.FamilyFanReaction, -100, 100, "FamilyFanReaction", errors);
                ValidatePresentation(wrestler, errors);
            }
        }

        private static void ValidateIdentity(WrestlerState wrestler, List<string> errors)
        {
            if (wrestler.Identity == null) { errors.Add("Wrestler.Identity is required."); return; }
            if (string.IsNullOrWhiteSpace(wrestler.Identity.LegalName)) errors.Add("Wrestler.LegalName is required.");
            if (string.IsNullOrWhiteSpace(wrestler.Identity.RingName)) errors.Add("Wrestler.RingName is required.");
            if (wrestler.Identity.HeightCm <= 0) errors.Add("Wrestler.HeightCm must be positive.");
            if (wrestler.Identity.WeightKg <= 0) errors.Add("Wrestler.WeightKg must be positive.");
            if (wrestler.Identity.CareerYears < 0) errors.Add("Wrestler.CareerYears must be non-negative.");
        }

        private static void ValidateTagTeams(GameSave save, List<string> errors)
        {
            var wrestlerIds = new HashSet<string>((save.Wrestlers ?? new List<WrestlerState>())
                .Where(x => x != null).Select(x => x.Id), StringComparer.Ordinal);
            var teamIds = new HashSet<string>(StringComparer.Ordinal);
            var activeMembers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var team in save.TagTeams ?? new List<TagTeamState>())
            {
                if (team == null) { errors.Add("Tag team cannot be null."); continue; }
                ValidateUniqueRuntimeId(team.Id, "TagTeam", teamIds, errors);
                if (team.PromotionId != save.Promotion.Id) errors.Add($"Tag team {team.Id} references another promotion.");
                var members = team.MemberIds ?? new List<string>();
                if (members.Count != 2 || members.Distinct(StringComparer.Ordinal).Count() != 2)
                    errors.Add($"Tag team {team.Id} must contain exactly two unique members.");
                foreach (var memberId in members)
                    if (!wrestlerIds.Contains(memberId)) errors.Add($"Tag team {team.Id} references a missing wrestler.");
                ValidateRange(team.Chemistry, 0, 100, "TagTeam Chemistry", errors);
                if (team.Status == TagTeamStatus.Active)
                    foreach (var memberId in members)
                        if (!activeMembers.Add(memberId)) errors.Add($"Wrestler {memberId} belongs to multiple active tag teams.");
            }

            foreach (var wrestler in save.Wrestlers ?? new List<WrestlerState>())
            {
                var activeTagTeamId = wrestler?.Roster?.ActiveTagTeamId;
                if (string.IsNullOrEmpty(activeTagTeamId)) continue;
                var team = (save.TagTeams ?? new List<TagTeamState>()).SingleOrDefault(x => x?.Id == activeTagTeamId);
                if (team == null || team.Status != TagTeamStatus.Active || !team.MemberIds.Contains(wrestler.Id))
                    errors.Add($"Wrestler {wrestler.Id} references an invalid active tag team.");
            }
        }

        private static void ValidatePresentation(WrestlerState wrestler, List<string> errors)
        {
            if (wrestler.Presentation == null) { errors.Add("Wrestler.Presentation is required."); return; }
            ValidateStaticId(wrestler.Presentation.WrestlingStyleId, "style_", "WrestlingStyleId", errors);
            foreach (var id in wrestler.Presentation.TraitIds ?? new List<string>()) ValidateStaticId(id, "trait_", "TraitId", errors);
            foreach (var id in wrestler.Presentation.SignatureMoveIds ?? new List<string>()) ValidateStaticId(id, "move_", "SignatureMoveId", errors);
            foreach (var id in wrestler.Presentation.FinisherMoveIds ?? new List<string>()) ValidateStaticId(id, "move_", "FinisherMoveId", errors);
            if ((wrestler.Presentation.SignatureMoveIds?.Count ?? 0) > 2) errors.Add("A wrestler can have at most two signature moves.");
            if ((wrestler.Presentation.FinisherMoveIds?.Count ?? 0) > 2) errors.Add("A wrestler can have at most two finishers.");
        }

        private static void ValidateStaticId(string id, string prefix, string name, List<string> errors)
        {
            if (!EntityId.IsValidStaticId(id) || !id.StartsWith(prefix, StringComparison.Ordinal))
                errors.Add($"Invalid {name}: {id}");
        }

        private static void ValidateAttributes(WrestlerState wrestler, List<string> errors)
        {
            var values = new[] { wrestler.Attributes.RingPsychology, wrestler.Attributes.RingImprovisation,
                wrestler.Attributes.Technical, wrestler.Attributes.Brawling, wrestler.Attributes.Power,
                wrestler.Attributes.HighFlying, wrestler.Attributes.SpotWork, wrestler.Attributes.SpecialtyMatches,
                wrestler.Attributes.Selling, wrestler.Attributes.Stamina, wrestler.Attributes.Charisma,
                wrestler.Attributes.MicWork, wrestler.Attributes.Improvisation, wrestler.Attributes.Acting,
                wrestler.Attributes.FaceWork, wrestler.Attributes.HeelWork, wrestler.Attributes.Comedy };
            foreach (var value in values) ValidateRange(value, 1, 20, "Wrestler attribute", errors);
            ValidateRange(wrestler.Growth.MatchPotentialCap, 1, 20, "MatchPotentialCap", errors);
            ValidateRange(wrestler.Growth.PromoPotentialCap, 1, 20, "PromoPotentialCap", errors);
            if (wrestler.Attributes.MatchTotal > wrestler.Growth.MatchPotentialCap * 10f + AttributeTotalTolerance)
                errors.Add("Match attributes exceed the match potential cap.");
            if (wrestler.Attributes.PromoTotal > wrestler.Growth.PromoPotentialCap * 7f + AttributeTotalTolerance)
                errors.Add("Promo attributes exceed the promo potential cap.");
        }

        private static void ValidateContracts(GameSave save, List<string> errors)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var wrestlerIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var wrestler in save.Wrestlers ?? new List<WrestlerState>()) if (wrestler != null) wrestlerIds.Add(wrestler.Id);
            var managerIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var manager in save.Managers ?? new List<ManagerState>()) if (manager != null) managerIds.Add(manager.Id);
            foreach (var contract in save.Contracts ?? new List<ContractState>())
            {
                if (contract == null) { errors.Add("Contract cannot be null."); continue; }
                if (!EntityId.IsValidRuntimeId(contract.Id)) errors.Add("Contract.Id must be a runtime GUID.");
                else if (!ids.Add(contract.Id)) errors.Add($"Duplicate Contract.Id: {contract.Id}");
                if (contract.Type == ContractType.Wrestler && !wrestlerIds.Contains(contract.PersonId))
                    errors.Add($"Contract {contract.Id} references a missing wrestler.");
                if (contract.Type == ContractType.Manager && !managerIds.Contains(contract.PersonId))
                    errors.Add($"Contract {contract.Id} references a missing manager.");
                if (contract.StartDate.CompareTo(contract.EndDate) > 0) errors.Add($"Contract {contract.Id} starts after it ends.");
                if (contract.EndDate.Month != 5 || contract.EndDate.Day != 31) errors.Add($"Contract {contract.Id} must end on May 31.");
                if (contract.SigningBonus < 0 || contract.MonthlySalary < 0 || contract.TerminationCost < 0)
                    errors.Add($"Contract {contract.Id} contains a negative amount.");
            }

            var contracts = save.Contracts ?? new List<ContractState>();
            for (var i = 0; i < contracts.Count; i++)
            for (var j = i + 1; j < contracts.Count; j++)
            {
                var first = contracts[i];
                var second = contracts[j];
                if (first == null || second == null || first.PersonId != second.PersonId) continue;
                if (!IsActive(first.Status) || !IsActive(second.Status)) continue;
                if (first.StartDate.CompareTo(second.EndDate) <= 0 && second.StartDate.CompareTo(first.EndDate) <= 0)
                    errors.Add($"Active contracts overlap for person {first.PersonId}.");
            }
        }

        private static void ValidateTransactions(GameSave save, List<string> errors)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var transaction in save.Transactions ?? new List<TransactionRecord>())
            {
                if (transaction == null) { errors.Add("Transaction cannot be null."); continue; }
                if (!EntityId.IsValidRuntimeId(transaction.Id)) errors.Add("Transaction.Id must be a runtime GUID.");
                else if (!ids.Add(transaction.Id)) errors.Add($"Duplicate Transaction.Id: {transaction.Id}");
                if (transaction.PromotionId != save.Promotion.Id) errors.Add($"Transaction {transaction.Id} references another promotion.");
                if (transaction.Amount == 0) errors.Add($"Transaction {transaction.Id} cannot have a zero amount.");
            }
        }

        private static bool IsActive(ContractStatus status) => status is ContractStatus.Active or ContractStatus.Expiring or ContractStatus.Overdue;

        private static void ValidateRange(float value, float min, float max, string name, List<string> errors)
        {
            if (value < min || value > max) errors.Add($"{name} must be between {min} and {max}.");
        }
    }
}
