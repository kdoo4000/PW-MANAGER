using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Identifiers;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public enum ShowValidationSeverity { Error, Warning, Info }

    public sealed class ShowValidationIssue
    {
        public ShowValidationSeverity Severity;
        public string Code;
        public string Message;
        public string EntityId;
    }

    public sealed class ShowPlanningService
    {
        private readonly Func<string> createId;
        private readonly Random random;
        private readonly IMatchTypeRules matchTypeRules;

        public ShowPlanningService(IMatchTypeRules matchTypeRules, Func<string> createId = null, int? randomSeed = null)
        {
            this.matchTypeRules = matchTypeRules ?? throw new ArgumentNullException(nameof(matchTypeRules));
            this.createId = createId ?? EntityId.CreateRuntimeId;
            random = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
        }

        public ShowState CreateDraft(GameSave save, string scheduleId, string name, string venueContractId, int durationLimit)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            var schedule = save.Schedules.SingleOrDefault(x => x?.Id == scheduleId)
                ?? throw new ArgumentException("Schedule was not found.", nameof(scheduleId));
            if (save.Shows.Any(x => x?.ScheduleId == scheduleId))
                throw new InvalidOperationException("A show already exists for this schedule.");
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Show name is required.", nameof(name));
            if (durationLimit <= 0 || durationLimit % 5 != 0)
                throw new ArgumentException("Duration limit must be a positive multiple of five.", nameof(durationLimit));
            var venue = save.VenueContracts.SingleOrDefault(x => x?.Id == venueContractId)
                ?? throw new ArgumentException("Venue contract was not found.", nameof(venueContractId));
            if (venue.StartDate.CompareTo(schedule.Date) > 0 || venue.EndDate.CompareTo(schedule.Date) < 0)
                throw new ArgumentException("Venue contract does not cover the show date.", nameof(venueContractId));

            var show = new ShowState
            {
                Id = createId(), ScheduleId = schedule.Id, Name = name.Trim(), ShowType = schedule.ShowType,
                Date = schedule.Date, VenueContractId = venue.Id, DurationLimit = durationLimit,
                EstimatedCost = venue.ProductionCost, Status = ShowStatus.Draft
            };
            save.Shows.Add(show);
            return show;
        }

        public ShowEventState AddMatch(GameSave save, string showId, MatchPlanState match, int plannedDuration)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            ValidateEventTarget(save, showId, plannedDuration);
            match.Id = string.IsNullOrEmpty(match.Id) ? createId() : match.Id;
            if (string.IsNullOrEmpty(match.MatchGimmickId)) match.MatchGimmickId = "gimmick_000";
            ExpandLegacyMatchPlan(match);
            var participantIds = MatchParticipants(match);
            if (participantIds.Count > 0 && participantIds.Distinct(StringComparer.Ordinal).Count() != participantIds.Count)
                throw new ArgumentException("Match participants must be unique.", nameof(match));
            if (save.MatchPlans.Any(x => x?.Id == match.Id)) throw new ArgumentException("Match ID is duplicated.", nameof(match));
            save.MatchPlans.Add(match);
            return AddEvent(save, showId, ShowEventType.Match, match.Id, plannedDuration);
        }

        public ShowEventState AddPromo(GameSave save, string showId, PromoPlanState promo, int plannedDuration)
        {
            if (promo == null) throw new ArgumentNullException(nameof(promo));
            ValidateEventTarget(save, showId, plannedDuration);
            promo.Id = string.IsNullOrEmpty(promo.Id) ? createId() : promo.Id;
            if (save.PromoPlans.Any(x => x?.Id == promo.Id)) throw new ArgumentException("Promo ID is duplicated.", nameof(promo));
            save.PromoPlans.Add(promo);
            return AddEvent(save, showId, ShowEventType.Promo, promo.Id, plannedDuration);
        }

        public void UpdateMatch(GameSave save, string showId, string eventId, MatchPlanState replacement, int plannedDuration)
        {
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));
            ValidateEventTarget(save, showId, plannedDuration);
            var showEvent = save.ShowEvents.SingleOrDefault(x => x?.Id == eventId && x.ShowId == showId && x.EventType == ShowEventType.Match)
                ?? throw new ArgumentException("Match event was not found.", nameof(eventId));
            var index = save.MatchPlans.FindIndex(x => x?.Id == showEvent.DetailId);
            if (index < 0) throw new InvalidOperationException("Match details are missing.");

            replacement.Id = showEvent.DetailId;
            if (string.IsNullOrEmpty(replacement.MatchGimmickId)) replacement.MatchGimmickId = "gimmick_000";
            ExpandLegacyMatchPlan(replacement);
            var participantIds = MatchParticipants(replacement);
            if (participantIds.Count == 0 || participantIds.Distinct(StringComparer.Ordinal).Count() != participantIds.Count)
                throw new ArgumentException("A match requires unique participants.", nameof(replacement));

            save.MatchPlans[index] = replacement;
            showEvent.PlannedDuration = plannedDuration;
            MarkModified(GetShow(save, showId));
        }

        public void MoveEvent(GameSave save, string showId, int oldIndex, int newIndex)
        {
            var show = GetShow(save, showId);
            if (oldIndex < 0 || oldIndex >= show.TimelineEventIds.Count || newIndex < 0 || newIndex >= show.TimelineEventIds.Count)
                throw new ArgumentOutOfRangeException(nameof(oldIndex));
            var id = show.TimelineEventIds[oldIndex];
            show.TimelineEventIds.RemoveAt(oldIndex);
            show.TimelineEventIds.Insert(newIndex, id);
            MarkModified(show);
            UpdateCardPositions(show);
        }

        public void SetEventDuration(GameSave save, string showId, string eventId, int plannedDuration)
        {
            ValidateEventTarget(save, showId, plannedDuration);
            var showEvent = save.ShowEvents.SingleOrDefault(x => x?.Id == eventId && x.ShowId == showId)
                ?? throw new ArgumentException("Show event was not found.", nameof(eventId));
            showEvent.PlannedDuration = plannedDuration;
            MarkModified(GetShow(save, showId));
        }

        public void RemoveEvent(GameSave save, string showId, string eventId)
        {
            var show = GetShow(save, showId);
            var showEvent = save.ShowEvents.SingleOrDefault(x => x?.Id == eventId && x.ShowId == showId)
                ?? throw new ArgumentException("Show event was not found.", nameof(eventId));
            save.ShowEvents.Remove(showEvent);
            show.TimelineEventIds.Remove(eventId);
            if (showEvent.EventType == ShowEventType.Match) save.MatchPlans.RemoveAll(x => x?.Id == showEvent.DetailId);
            else save.PromoPlans.RemoveAll(x => x?.Id == showEvent.DetailId);
            MarkModified(show);
            UpdateCardPositions(show);
        }

        public IReadOnlyList<ShowValidationIssue> Validate(GameSave save, string showId)
        {
            var show = GetShow(save, showId);
            var issues = new List<ShowValidationIssue>();
            if (string.IsNullOrWhiteSpace(show.Name)) Error(issues, "show.name", "Show name is required.", show.Id);
            if (show.DurationLimit <= 0 || show.DurationLimit % 5 != 0) Error(issues, "show.duration-limit", "Duration limit must be a positive multiple of five.", show.Id);
            var events = save.ShowEvents.Where(x => x?.ShowId == show.Id).ToDictionary(x => x.Id, StringComparer.Ordinal);
            foreach (var eventId in show.TimelineEventIds)
            {
                if (!events.TryGetValue(eventId, out var showEvent)) { Error(issues, "event.missing", "Timeline references a missing event.", eventId); continue; }
                if (showEvent.PlannedDuration <= 0 || showEvent.PlannedDuration % 5 != 0)
                    Error(issues, "event.duration", "Event duration must be a positive multiple of five.", eventId);
                if (showEvent.EventType == ShowEventType.Match) ValidateMatch(save, show, showEvent, issues);
                else ValidatePromo(save, show, showEvent, issues);
            }
            var used = show.CalculatePlannedDuration(events.Values);
            if (used != show.DurationLimit)
                Error(issues, "show.duration-total", $"Planned duration {used} must equal limit {show.DurationLimit}.", show.Id);
            AddAppearanceWarnings(save, show, events.Values, issues);
            return issues;
        }

        public void Confirm(GameSave save, string showId)
        {
            var show = GetShow(save, showId);
            show.Status = ShowStatus.Review;
            var errors = Validate(save, showId).Where(x => x.Severity == ShowValidationSeverity.Error).ToList();
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(" | ", errors.Select(x => x.Message)));
            show.ShowVersion++;
            if (show.ShowVersion == 1) show.AttendanceVarianceBasisPoints = NextTriangularVariance();
            show.Status = ShowStatus.Confirmed;
            UpdateCardPositions(show);
        }

        private ShowEventState AddEvent(GameSave save, string showId, ShowEventType type, string detailId, int duration)
        {
            if (duration <= 0 || duration % 5 != 0) throw new ArgumentException("Planned duration must be a positive multiple of five.", nameof(duration));
            var show = GetShow(save, showId);
            var showEvent = new ShowEventState { Id = createId(), ShowId = show.Id, EventType = type, DetailId = detailId, PlannedDuration = duration };
            save.ShowEvents.Add(showEvent);
            show.TimelineEventIds.Add(showEvent.Id);
            MarkModified(show);
            UpdateCardPositions(show);
            return showEvent;
        }

        private static void ValidateEventTarget(GameSave save, string showId, int duration)
        {
            GetShow(save, showId);
            if (duration <= 0 || duration % 5 != 0)
                throw new ArgumentException("Planned duration must be a positive multiple of five.", nameof(duration));
        }

        private void ValidateMatch(GameSave save, ShowState show, ShowEventState showEvent, List<ShowValidationIssue> issues)
        {
            var match = save.MatchPlans.SingleOrDefault(x => x?.Id == showEvent.DetailId);
            if (match == null) { Error(issues, "match.missing", "Match details are missing.", showEvent.Id); return; }
            ExpandLegacyMatchPlan(match);
            if (!matchTypeRules.TryGetParticipantRange(match.MatchTypeId, out var minimum, out var maximum))
                Error(issues, "match.type", "Match type does not exist in static content.", match.Id);
            var sides = match.Sides ?? new List<MatchSideState>();
            var participants = sides.Where(x => x != null).SelectMany(x => x.MemberIds ?? new List<string>()).ToList();
            var participantCount = participants.Count;
            if (sides.Any(x => x == null || string.IsNullOrWhiteSpace(x.Id)) ||
                sides.Where(x => x != null).Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != sides.Count)
                Error(issues, "match.side-ids", "Match sides require unique IDs.", match.Id);
            if (participantCount < minimum || participantCount > maximum || participants.Distinct().Count() != participantCount)
                Error(issues, "match.participants", $"Match type requires {minimum} to {maximum} unique participants.", match.Id);
            var resolvedTeamCount = 0;
            if (matchTypeRules.TryGetTeamRules(match.MatchTypeId, out var minimumTeamCount, out var maximumTeamCount,
                out var minimumMembersPerTeam, out var maximumMembersPerTeam) && minimumTeamCount > 0)
            {
                var teamCount = sides.Count;
                var membersPerTeam = sides.Count == 0 ? 0 : sides[0]?.MemberIds?.Count ?? 0;
                resolvedTeamCount = teamCount;
                if (teamCount < minimumTeamCount || teamCount > maximumTeamCount ||
                    membersPerTeam < minimumMembersPerTeam || membersPerTeam > maximumMembersPerTeam ||
                    sides.Any(x => x?.MemberIds == null || x.MemberIds.Count != membersPerTeam) ||
                    teamCount * membersPerTeam != participantCount)
                    Error(issues, "match.teams", "Team count and members per team must form a supported equal-team match.", match.Id);
            }
            else if (sides.Count != participantCount || sides.Any(x => x?.MemberIds?.Count != 1))
                Error(issues, "match.sides", "An individual match requires one participant per side.", match.Id);
            MatchGimmickRules? gimmickRules = null;
            if (!string.IsNullOrEmpty(match.MatchGimmickId))
            {
                if (!matchTypeRules.TryGetGimmickRules(match.MatchGimmickId, out var rules) ||
                    !rules.IsCompatible(match.MatchTypeId, participantCount))
                    Error(issues, "match.gimmick", "Match gimmick is missing or incompatible with the format and participant count.", match.Id);
                else gimmickRules = rules;
            }
            if (!MatchFinishRules.IsAllowed(match.FinishType, minimumTeamCount > 0, participantCount, resolvedTeamCount, gimmickRules))
                Error(issues, "match.finish-type", "The selected finish type is not allowed by the match composition or gimmick rules.", match.Id);
            var winningSide = sides.SingleOrDefault(x => x?.Id == match.WinningSideId);
            if (match.FinishType != MatchFinishType.Draw && winningSide == null)
                Error(issues, "match.winner-side", "Winning side must be one of the match sides.", match.Id);
            if (match.FinishType != MatchFinishType.Draw && (string.IsNullOrEmpty(match.FinishPerformerId) ||
                winningSide?.MemberIds == null || !winningSide.MemberIds.Contains(match.FinishPerformerId)))
                Error(issues, "match.finish-performer", "Finish performer must belong to the winning side.", match.Id);
            if (!string.IsNullOrEmpty(match.LoserTargetId) && !participants.Contains(match.LoserTargetId))
                Error(issues, "match.loser", "Loser target must be a participant.", match.Id);
            if (!string.IsNullOrEmpty(match.LoserTargetId) && winningSide?.MemberIds?.Contains(match.LoserTargetId) == true)
                Error(issues, "match.loser-side", "Loser target cannot belong to the winning side.", match.Id);
            foreach (var participantId in participants) ValidateParticipant(save, show, participantId, true, issues, match.Id);
            foreach (var message in MatchSpotEvaluator.Validate(save, match)) Error(issues, "match.spot", message, match.Id);
        }

        private static void ValidatePromo(GameSave save, ShowState show, ShowEventState showEvent, List<ShowValidationIssue> issues)
        {
            var promo = save.PromoPlans.SingleOrDefault(x => x?.Id == showEvent.DetailId);
            if (promo == null) { Error(issues, "promo.missing", "Promo details are missing.", showEvent.Id); return; }
            if (promo.ParticipantIds == null || promo.ParticipantIds.Count == 0 || promo.ParticipantIds.Distinct().Count() != promo.ParticipantIds.Count)
                Error(issues, "promo.participants", "A promo requires unique participants.", promo.Id);
            if (promo.Purpose == PromoPurpose.SponsorAdvertisement && string.IsNullOrWhiteSpace(promo.SponsorRequirementId))
                Error(issues, "promo.sponsor", "Sponsor advertisement requires a sponsor requirement.", promo.Id);
            foreach (var participantId in promo.ParticipantIds ?? new List<string>()) ValidateParticipant(save, show, participantId, false, issues, promo.Id);
        }

        private static void ValidateParticipant(GameSave save, ShowState show, string participantId, bool match, List<ShowValidationIssue> issues, string entityId)
        {
            var wrestler = save.Wrestlers.SingleOrDefault(x => x?.Id == participantId);
            if (wrestler == null) { Error(issues, "participant.missing", "Participant wrestler was not found.", entityId); return; }
            var contracted = save.Contracts.Any(x => x != null && x.PersonId == participantId &&
                (x.Status is ContractStatus.Active or ContractStatus.Expiring) &&
                x.StartDate.CompareTo(show.Date) <= 0 && x.EndDate.CompareTo(show.Date) >= 0);
            if (!contracted) Error(issues, "participant.contract", "Participant does not have an active contract on the show date.", entityId);
            if (wrestler.Roster.ActivityState != RosterActivityState.Active)
                Error(issues, "participant.roster", "Participant is not on the active roster.", entityId);
            if (wrestler.Condition.Availability == WrestlerAvailability.Unavailable || match && wrestler.Condition.Availability == WrestlerAvailability.MatchUnavailable)
                Error(issues, "participant.availability", "Participant is unavailable for this event.", entityId);
        }

        private static void AddAppearanceWarnings(GameSave save, ShowState show, IEnumerable<ShowEventState> events, List<ShowValidationIssue> issues)
        {
            var matchParticipants = new List<string>();
            foreach (var showEvent in events.Where(x => x.EventType == ShowEventType.Match))
            {
                var match = save.MatchPlans.SingleOrDefault(x => x?.Id == showEvent.DetailId);
                if (match != null) matchParticipants.AddRange(MatchParticipants(match));
            }
            foreach (var id in matchParticipants.GroupBy(x => x).Where(x => x.Count() >= 2).Select(x => x.Key))
                issues.Add(new ShowValidationIssue { Severity = ShowValidationSeverity.Warning, Code = "participant.multiple-matches", Message = "Wrestler is booked in two or more matches.", EntityId = id });
        }

        private int NextTriangularVariance()
        {
            var sample = (random.NextDouble() + random.NextDouble()) / 2d;
            return 9500 + (int)Math.Round(sample * 1000d);
        }

        private void ExpandLegacyMatchPlan(MatchPlanState match)
        {
            match.Sides ??= new List<MatchSideState>();
            match.ParticipantIds ??= new List<string>();
            if (match.Sides.Count == 0 && match.ParticipantIds.Count > 0)
            {
                var teamCount = match.TeamCount;
                var membersPerTeam = match.MembersPerTeam;
                var isTeamMatch = matchTypeRules.TryGetTeamRules(match.MatchTypeId, out var minimumTeams, out _, out _, out _) && minimumTeams > 0;
                if (isTeamMatch && teamCount == 0 && membersPerTeam == 0)
                {
                    teamCount = minimumTeams;
                    membersPerTeam = match.ParticipantIds.Count / teamCount;
                }
                if (!isTeamMatch) { teamCount = match.ParticipantIds.Count; membersPerTeam = 1; }
                if (teamCount > 0 && membersPerTeam > 0)
                {
                    var remainder = match.ParticipantIds.Count % teamCount;
                    var participantIndex = 0;
                    for (var sideIndex = 0; sideIndex < teamCount; sideIndex++)
                    {
                        var sideMemberCount = membersPerTeam + (sideIndex < remainder ? 1 : 0);
                        match.Sides.Add(new MatchSideState
                        {
                            Id = createId(),
                            MemberIds = match.ParticipantIds.Skip(participantIndex).Take(sideMemberCount).ToList()
                        });
                        participantIndex += sideMemberCount;
                    }
                }
            }

            var participants = MatchParticipants(match);
            match.ParticipantIds = participants;
            match.TeamCount = match.Sides.Count;
            match.MembersPerTeam = match.Sides.Count == 0 ? 0 : match.Sides[0]?.MemberIds?.Count ?? 0;
            if (string.IsNullOrEmpty(match.FinishPerformerId)) match.FinishPerformerId = match.WinnerId;
            if (string.IsNullOrEmpty(match.WinnerId)) match.WinnerId = match.FinishPerformerId;
            if (string.IsNullOrEmpty(match.WinningSideId) && !string.IsNullOrEmpty(match.FinishPerformerId))
                match.WinningSideId = match.Sides.FirstOrDefault(x => x?.MemberIds?.Contains(match.FinishPerformerId) == true)?.Id;
        }

        private static List<string> MatchParticipants(MatchPlanState match) =>
            match?.Sides?.Where(x => x != null).SelectMany(x => x.MemberIds ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
            ?? new List<string>();

        private static ShowState GetShow(GameSave save, string showId)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            return save.Shows.SingleOrDefault(x => x?.Id == showId) ?? throw new ArgumentException("Show was not found.", nameof(showId));
        }

        private static void MarkModified(ShowState show)
        {
            if (show.Status == ShowStatus.Confirmed) show.Status = ShowStatus.Preparing;
            else if (show.Status == ShowStatus.Draft) show.Status = ShowStatus.Preparing;
        }

        private static void UpdateCardPositions(ShowState show)
        {
            show.OpeningEventId = show.TimelineEventIds.Count == 0 ? null : show.TimelineEventIds[0];
            show.MainEventId = show.TimelineEventIds.Count == 0 ? null : show.TimelineEventIds[^1];
        }

        private static void Error(List<ShowValidationIssue> issues, string code, string message, string entityId) =>
            issues.Add(new ShowValidationIssue { Severity = ShowValidationSeverity.Error, Code = code, Message = message, EntityId = entityId });
    }
}
