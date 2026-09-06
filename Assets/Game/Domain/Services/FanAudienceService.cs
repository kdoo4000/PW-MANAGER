using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public static class FanAudienceService
    {
        public const long MaximumFollowersPerGroup = 1000000000;

        public static PromotionAudienceState CreateInitial(GameDate date, long followers = 2500) => new()
        {
            IsInitialized = true,
            Mark = new AudienceGroupState { Followers = followers * 35 / 100 },
            Casual = new AudienceGroupState { Followers = followers * 40 / 100 },
            Hardcore = new AudienceGroupState { Followers = followers - followers * 35 / 100 - followers * 40 / 100 },
            LastWeeklyUpdate = date
        };

        public static void Initialize(GameSave save)
        {
            if (save.Promotion.Audience?.IsInitialized != true) save.Promotion.Audience = CreateInitial(save.CurrentDate);
        }

        public static FanReactionResultState Expectation(GameSave save, ShowState show)
        {
            var events = Events(save, show).ToList();
            var result = new FanReactionResultState { IsCalculated = true };
            for (var group = 0; group < 3; group++)
            {
                var weight = events.Sum(x => x.PlannedDuration);
                Set(result, group, weight == 0 ? 42f : events.Sum(x =>
                    Expected(Participants(save, x), show.ShowType, group) * x.PlannedDuration) / weight);
            }
            return result;
        }

        public static long BaseDemand(GameSave save, ShowState show, long referencePrice)
        {
            if (referencePrice <= 0) throw new ArgumentOutOfRangeException(nameof(referencePrice));
            var audience = save.Promotion.Audience?.IsInitialized == true ? save.Promotion.Audience : CreateInitial(save.CurrentDate);
            var expectation = Expectation(save, show);
            var draw = 0d;
            for (var group = 0; group < 3; group++)
            {
                var fans = Group(audience, group);
                draw += fans.Followers * (.8 + (Get(expectation, group) - 42) / 60d) * (.4 + fans.Satisfaction * .012);
            }
            var premium = show.ShowType == ScheduledShowType.Regular ? 1d : 1.2d;
            var fairPrice = 30d * (double)TicketPricingRules.GetShowTypeMultiplier(show.ShowType);
            // ponytail: one local market; introduce regions when venues have regional audience data.
            var affordability = Math.Max(.1, Math.Min(1.5, Math.Pow(fairPrice / referencePrice, 1.2)));
            var recent = save.Shows.Count(x => x != null && x.Status == ShowStatus.Completed &&
                x.Date.DaysUntil(show.Date) >= 0 && x.Date.DaysUntil(show.Date) < 7);
            var programLength = Math.Min(1d, Events(save, show).Sum(x => x.PlannedDuration) / 90d);
            return (long)Math.Floor(draw * .16 * premium * affordability * programLength / (1 + .35 * recent));
        }

        public static void Evaluate(GameSave save, ShowState show, ShowResultState result,
            IReadOnlyList<MatchResultState> matches, IReadOnlyList<PromoResultState> promos)
        {
            result.FanExpectation = Expectation(save, show);
            result.FanSatisfaction = new FanReactionResultState { IsCalculated = true };
            var changes = new Dictionary<string, WrestlerFanChangeState>(StringComparer.Ordinal);
            var events = Events(save, show).ToList();
            var duration = events.Sum(x => x.PlannedDuration);
            result.FanExposure = Math.Min(1f, duration / 90f);
            foreach (var showEvent in events)
            {
                var wrestlers = Participants(save, showEvent);
                var match = matches.FirstOrDefault(x => x.ShowEventId == showEvent.Id);
                var promo = promos.FirstOrDefault(x => x.ShowEventId == showEvent.Id);
                var quality = Clamp(match != null ? match.FinalMatchQuality * 5f : promo.PromoScore, 0, 100);
                var reaction = match != null ? match.FanReaction : promo.FanReaction;
                reaction.IsCalculated = true;
                for (var group = 0; group < 3; group++)
                {
                    var performance = wrestlers.Count == 0 ? quality : wrestlers.Average(w =>
                        (group == 0 ? (w.Roster.Alignment == KayfabeAlignment.Heel ? w.Attributes.HeelWork : w.Attributes.FaceWork) :
                         group == 1 ? w.Attributes.Charisma : w.Attributes.RingPsychology) * 5f);
                    var qualityWeight = group == 0 ? .7f : group == 1 ? .8f : .95f;
                    var delivered = quality * qualityWeight + performance * (1 - qualityWeight);
                    var satisfaction = Clamp(50 + (delivered - Expected(wrestlers, show.ShowType, group)) * 1.2f, 0, 100);
                    Set(reaction, group, satisfaction);
                    Set(result.FanSatisfaction, group, Get(result.FanSatisfaction, group) + satisfaction * showEvent.PlannedDuration / duration);
                    foreach (var wrestler in wrestlers)
                    {
                        if (!changes.TryGetValue(wrestler.Id, out var change))
                            changes.Add(wrestler.Id, change = new WrestlerFanChangeState { WrestlerId = wrestler.Id });
                        var response = Response(wrestler, group);
                        var exposure = Math.Min(1f, showEvent.PlannedDuration / 20f);
                        var preference = (satisfaction - 50) * .045f;
                        if (group == 0 && wrestler.Roster.Alignment == KayfabeAlignment.Heel)
                            preference = -Math.Max(0, satisfaction - 40) * .035f;
                        var interest = (.7f + (satisfaction - 50) * .045f);
                        interest *= interest > 0 ? 1 - response.Interest / 100f : response.Interest / 100f;
                        var delta = Change(change, group);
                        delta.Preference += preference * exposure;
                        delta.Interest += interest * exposure;
                        SetChange(change, group, delta);
                    }
                }
            }
            foreach (var change in changes.Values)
            {
                var wrestler = save.Wrestlers.Single(x => x.Id == change.WrestlerId);
                var days = wrestler.FanReaction.LastUpdatedAt.HasValue
                    ? wrestler.FanReaction.LastUpdatedAt.Value.DaysUntil(show.Date) : 7;
                var frequency = days <= 0 ? 0f : days < 7 ? .35f : 1f;
                for (var group = 0; group < 3; group++)
                {
                    var delta = Change(change, group);
                    delta.Preference = Clamp(delta.Preference, -3, 3) * frequency;
                    delta.Interest = Clamp(delta.Interest, -3, 3) * frequency;
                    SetChange(change, group, delta);
                }
            }
            result.FanResponseChanges = changes.Values.ToList();
        }

        public static void ValidateAudience(GameSave save)
        {
            var audience = save.Promotion.Audience;
            if (audience?.IsInitialized != true) return;
            try
            {
                if (audience.LastWeeklyUpdate.DaysUntil(save.CurrentDate) < 0)
                    throw new InvalidOperationException("Audience update date cannot be in the future.");
            }
            catch (ArgumentOutOfRangeException) { throw new InvalidOperationException("Audience update date is invalid."); }
            for (var group = 0; group < 3; group++)
            {
                var fans = Group(audience, group);
                if (fans == null || !InRange(fans.Followers, 0, MaximumFollowersPerGroup) ||
                    !InRange(fans.Satisfaction, 0, 100) || !InRange(fans.WeeklyExposure, 0, 1.5) ||
                    !InRange(fans.FollowerRemainder, -1, 1) || Math.Abs(fans.FollowerRemainder) >= 1)
                    throw new InvalidOperationException("Audience values are missing, non-finite or outside their limits.");
            }
        }

        public static void ValidateResult(GameSave save, ShowResultState result)
        {
            if (result.FanSatisfaction?.IsCalculated != true) return; // Old results do not acquire retroactive rewards.
            if (result.FanExpectation?.IsCalculated != true ||
                !InRange(result.FanExposure, 0, 1)) throw new InvalidOperationException("Invalid fan result.");
            for (var group = 0; group < 3; group++)
                if (!InRange(Get(result.FanSatisfaction, group), 0, 100) || !InRange(Get(result.FanExpectation, group), 0, 100))
                    throw new InvalidOperationException("Fan scores must be finite and between 0 and 100.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var change in result.FanResponseChanges ?? throw new InvalidOperationException("Fan changes are required."))
            {
                if (change == null || !ids.Add(change.WrestlerId) || !save.Wrestlers.Any(x => x?.Id == change.WrestlerId))
                    throw new InvalidOperationException("Fan change references an invalid or duplicate wrestler.");
                for (var group = 0; group < 3; group++)
                {
                    var delta = Change(change, group);
                    if (!InRange(delta.Preference, -3, 3) || !InRange(delta.Interest, -3, 3))
                        throw new InvalidOperationException("Fan change is outside its per-show limit.");
                }
            }
        }

        public static void Apply(GameSave save, ShowState show, ShowResultState result)
        {
            if (result.FanSatisfaction?.IsCalculated != true) return;
            Initialize(save);
            foreach (var change in result.FanResponseChanges)
            {
                var wrestler = save.Wrestlers.Single(x => x.Id == change.WrestlerId);
                wrestler.FanReaction.Upgrade();
                for (var group = 0; group < 3; group++)
                {
                    var response = Response(wrestler, group);
                    var delta = Change(change, group);
                    response.Preference = Clamp(response.Preference + delta.Preference, 0, 100);
                    response.Interest = Clamp(response.Interest + delta.Interest, 0, 100);
                    if (group == 0) wrestler.FanReaction.Mark = response;
                    else if (group == 1) wrestler.FanReaction.Casual = response;
                    else wrestler.FanReaction.Hardcore = response;
                }
                wrestler.FanReaction.LastUpdatedAt = new OptionalGameDate(show.Date);
            }
            for (var group = 0; group < 3; group++)
            {
                var audience = Group(save.Promotion.Audience, group);
                var exposure = Math.Min(result.FanExposure, 1.5f - audience.WeeklyExposure);
                audience.Satisfaction += (Get(result.FanSatisfaction, group) - audience.Satisfaction) * .4f * exposure;
                audience.WeeklyExposure += exposure;
            }
        }

        public static void AdvanceWeek(GameSave save, GameDate date)
        {
            ValidateAudience(save);
            Initialize(save);
            var audience = save.Promotion.Audience;
            while (audience.LastWeeklyUpdate.DaysUntil(date) >= 7)
            {
                var before = audience.TotalFollowers;
                for (var group = 0; group < 3; group++)
                {
                    var fans = Group(audience, group);
                    var acquisition = fans.WeeklyExposure * (.003 + Math.Max(0, fans.Satisfaction - 50) * .0006);
                    var churn = fans.WeeklyExposure == 0 ? .004 : .003 + Math.Max(0, 50 - fans.Satisfaction) * .0006;
                    var rate = Math.Max(-.02, Math.Min(.02, acquisition - churn));
                    var exact = fans.Followers * rate + fans.FollowerRemainder;
                    var change = (long)Math.Truncate(exact);
                    fans.Followers = Math.Max(0, Math.Min(MaximumFollowersPerGroup, fans.Followers + change));
                    fans.FollowerRemainder = fans.Followers == 0 || fans.Followers == MaximumFollowersPerGroup ? 0 : exact - change;
                    fans.Satisfaction += (50 - fans.Satisfaction) * .1f;
                    fans.WeeklyExposure = 0;
                }
                audience.LastWeeklyChange = audience.TotalFollowers - before;
                audience.LastWeeklyUpdate = audience.LastWeeklyUpdate.AddDays(7);
            }
        }

        public static AudienceGroupState Group(PromotionAudienceState audience, int group) =>
            group == 0 ? audience.Mark : group == 1 ? audience.Casual : audience.Hardcore;
        public static float Get(FanReactionResultState value, int group) => group == 0 ? value.Family : group == 1 ? value.Light : value.Mania;
        public static bool InRange(double value, double min, double max) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= min && value <= max;
        private static void Set(FanReactionResultState value, int group, float score)
        { if (group == 0) value.Family = score; else if (group == 1) value.Light = score; else value.Mania = score; }
        private static FanResponseState Response(WrestlerState w, int group) =>
            group == 0 ? w.FanReaction.MarkResponse : group == 1 ? w.FanReaction.CasualResponse : w.FanReaction.HardcoreResponse;
        private static FanResponseState Change(WrestlerFanChangeState change, int group) => group == 0 ? change.Mark : group == 1 ? change.Casual : change.Hardcore;
        private static void SetChange(WrestlerFanChangeState change, int group, FanResponseState value)
        { if (group == 0) change.Mark = value; else if (group == 1) change.Casual = value; else change.Hardcore = value; }
        private static float Expected(List<WrestlerState> wrestlers, ScheduledShowType type, int group) =>
            42f + (wrestlers.Count == 0 ? 0 : wrestlers.Average(w => Response(w, group).Interest * .28f + Math.Abs(Response(w, group).Preference - 50) * .06f)) +
            (type == ScheduledShowType.Regular ? 0 : type == ScheduledShowType.PpvRegular ? 4 : type == ScheduledShowType.PpvMajor ? 6 : 8);
        private static IEnumerable<ShowEventState> Events(GameSave save, ShowState show) =>
            show.TimelineEventIds.Select(id => save.ShowEvents.Single(x => x.Id == id));
        private static List<WrestlerState> Participants(GameSave save, ShowEventState showEvent)
        {
            IEnumerable<string> ids;
            if (showEvent.EventType == ShowEventType.Match)
            {
                var plan = save.MatchPlans.Single(x => x.Id == showEvent.DetailId);
                ids = plan.Sides?.Any(x => x.MemberIds.Count > 0) == true ? plan.Sides.SelectMany(x => x.MemberIds) : plan.ParticipantIds;
            }
            else ids = save.PromoPlans.Single(x => x.Id == showEvent.DetailId).ParticipantIds;
            return ids.Distinct(StringComparer.Ordinal).Select(id => save.Wrestlers.Single(x => x.Id == id)).ToList();
        }
        private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
    }
}
