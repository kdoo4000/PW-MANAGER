using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Identifiers;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public sealed class WrestlerCareerService
    {
        private readonly Func<string> createId;
        public WrestlerCareerService(Func<string> createId = null) => this.createId = createId ?? EntityId.CreateRuntimeId;

        public sealed class CareerPlan
        {
            private readonly GameSave target, prepared;
            private bool applied;
            public IReadOnlyList<string> NewRecordIds { get; }
            internal IReadOnlyDictionary<string, WrestlerMomentumState> MomentumForGrowth => prepared.Wrestlers.ToDictionary(x => x.Id, x => x.Momentum);
            internal long ProjectedPrestige => prepared.Promotion.PromotionPrestige;
            internal CareerPlan(GameSave target, GameSave prepared)
            {
                this.target = target; this.prepared = prepared;
                NewRecordIds = prepared.CareerEvents.Skip(target.CareerEvents?.Count ?? 0).Select(x => x.Id).ToList();
            }
            public void Apply()
            {
                if (applied) return;
                foreach (var w in target.Wrestlers)
                {
                    var next = prepared.Wrestlers.Single(x => x.Id == w.Id);
                    w.Status = next.Status; w.Momentum = next.Momentum; w.FanReaction = next.FanReaction; w.Fame = next.Fame;
                }
                target.Promotion.Audience = prepared.Promotion.Audience;
                target.Promotion.PromotionPrestige = prepared.Promotion.PromotionPrestige;
                target.Promotion.AppliedPrestigeReasonIds = prepared.Promotion.AppliedPrestigeReasonIds;
                target.Promotion.QuarterlyGrowth = prepared.Promotion.QuarterlyGrowth;
                target.CareerEvents = prepared.CareerEvents;
                target.CareerObservationStartDate = prepared.CareerObservationStartDate;
                target.LastCareerQuarterEnd = prepared.LastCareerQuarterEnd; applied = true;
            }
        }

        public CareerPlan PrepareShow(ResultApplicationContext context, IReadOnlyList<ChampionshipState> preparedTitles = null)
        {
            context.EnsureNotApplied(); var original = context.Save;
            ValidateState(original);
            if (original.IsCancelledShow(context.Show)) throw new InvalidOperationException("취소된 쇼에는 선수 변화를 반영할 수 없습니다.");
            var save = original.CopyForCareerSimulation();
            if (save.CareerObservationStartDate.Year == 0) save.CareerObservationStartDate = context.Show.Date;
            preparedTitles ??= ChampionshipService.PrepareResults(original, context.MatchResults.OrderBy(x => context.Show.TimelineEventIds.IndexOf(x.ShowEventId)), context.Show.Date);
            FanAudienceService.ValidateResult(original, context.ShowResult); FanAudienceService.ValidateAudience(original);
            FanAudienceService.Apply(save, context.Show, context.ShowResult);
            var matches = context.MatchResults.Where(x => string.IsNullOrEmpty(x.CancellationReason)).ToList();
            var card = new Dictionary<string, (string Source, int Reference)>();
            foreach (var match in matches)
            foreach (var id in Members(match))
            {
                var reference = CardReference(context.Show, match.ShowEventId);
                if (!card.TryGetValue(id, out var existing) || reference > existing.Reference) card[id] = (match.Id, reference);
            }
            var initialStatus = original.Wrestlers.ToDictionary(x => x.Id, x => x.Status.StatusValue);
            foreach (var eventId in context.Show.TimelineEventIds)
            {
                var match = matches.SingleOrDefault(x => x.ShowEventId == eventId);
                if (match != null) ApplyMatch(save, context, match, preparedTitles, card, initialStatus);
                var promo = context.PromoResults.SingleOrDefault(x => x.ShowEventId == eventId && string.IsNullOrEmpty(x.CancellationReason));
                if (promo != null)
                {
                    CheckScore(promo.PromoScore, 100);
                    var plan = save.PromoPlans.Single(x => x.Id == promo.PromoPlanId);
                    foreach (var id in plan.ParticipantIds.Distinct())
                    {
                        var w = save.Wrestlers.Single(x => x.Id == id);
                        var record = Begin(save, w, CareerEventKind.Promo, context.Show.Date, promo.Id, context.Show.Id);
                        var gain = Math.Min(10, (promo.PromoScore >= 90 ? 6 : promo.PromoScore >= 75 ? 3 : promo.PromoScore >= 60 ? 1 : 0) + FanGain(FanScore(save, promo.FanReaction)));
                        Accumulate(w, record, gain); w.Momentum.LastMomentumActivityDate = new(context.Show.Date);
                        End(save, w, record);
                    }
                }
            }
            ApplyFame(original, save, context, preparedTitles);
            UpdateRookies(save, context.Show.Date, preparedTitles);
            if (context.Show.Date.Equals(save.CurrentDate)) SettleQuarters(save, new HashSet<string> { context.Show.Id }, true, context.Show.Id);
            return Finish(original, save);
        }

        private void ApplyFame(GameSave original, GameSave save, ResultApplicationContext context, IReadOnlyList<ChampionshipState> titles)
        {
            if (!context.ShowResult.HasFameSnapshot) return;
            var contributions = new Dictionary<string, (double Activity, double Title)>();
            void Include(string id, double activity, double title)
            {
                contributions.TryGetValue(id, out var prior);
                contributions[id] = (Math.Max(prior.Activity, activity), Math.Max(prior.Title, title));
            }
            foreach (var match in context.MatchResults.Where(x => string.IsNullOrEmpty(x.CancellationReason)))
            {
                var score = Math.Max(match.CriticReview?.FinalScore ?? match.FinalMatchQuality * 5, FanScore(original, match.FanReaction));
                var activity = WrestlerFameRules.Activity(score, match.ActualMatchDuration);
                foreach (var id in Members(match))
                {
                    var titleBonus = 0d;
                    foreach (var title in titles)
                    foreach (var history in title.MatchHistory.Where(x => x.SourceResultId == match.Id && x.WinnerMemberIds.Contains(id)))
                    {
                        var world = title.Type == ChampionshipType.Individual && title.IsWorldChampionship;
                        var acquired = !string.IsNullOrEmpty(history.ChampionAfterId) && history.ChampionBeforeId != history.ChampionAfterId;
                        titleBonus = Math.Max(titleBonus, acquired ? world ? 4 : 2 : history.SuccessfulDefense ? world ? 2 : 1 : 0);
                    }
                    Include(id, activity, titleBonus);
                }
            }
            foreach (var promo in context.PromoResults.Where(x => string.IsNullOrEmpty(x.CancellationReason)))
            {
                var minutes = original.ShowEvents.Single(x => x.Id == promo.ShowEventId).PlannedDuration;
                var activity = WrestlerFameRules.Activity(Math.Max(promo.PromoScore, FanScore(original, promo.FanReaction)), minutes);
                foreach (var id in original.PromoPlans.Single(x => x.Id == promo.PromoPlanId).ParticipantIds.Distinct()) Include(id, activity, 0);
            }
            foreach (var pair in contributions)
            {
                var w = save.Wrestlers.Single(x => x.Id == pair.Key);
                var gain = WrestlerFameRules.CalculateGain(w.Fame.Value, context.ShowResult.FameReach, pair.Value.Activity, pair.Value.Title);
                if (gain <= 0) continue;
                var e = Begin(save, w, CareerEventKind.Fame, context.Show.Date, context.ShowResult.Id, context.Show.Id);
                e.FameBefore = w.Fame.Value; e.FameRemainderBefore = w.Fame.Remainder; e.FameMilestoneBefore = w.Fame.HighestMilestone;
                e.FameGain = gain; e.FameActivity = pair.Value.Activity; e.FameTitle = pair.Value.Title;
                e.FameWasRookie = original.Wrestlers.Single(x => x.Id == w.Id).Status.IsRookie; e.FamePromotionId = original.Promotion.Id;
                WrestlerFameRules.Accumulate(w.Fame, gain);
                e.FameAfter = w.Fame.Value; e.FameRemainderAfter = w.Fame.Remainder; e.FameMilestoneAfter = w.Fame.HighestMilestone;
                e.HasPromotionFameBonus = w.PromotionId == original.Promotion.Id;
                if (e.HasPromotionFameBonus)
                {
                    e.FameRookieDevelopment = e.FameMilestoneBefore < 2 && e.FameMilestoneAfter >= 2 &&
                        WasRookieHire(original, w.Id, context.Show.Date);
                    e.FamePromotionBonus = PromotionMilestoneBonus(e);
                }
                e.StatusAfter = e.StatusBefore; e.MomentumAfter = e.MomentumBefore; e.FailureAfter = e.FailureBefore; e.IsRookie = e.WasRookie;
                save.CareerEvents.Add(e);
            }
        }

        private static bool WasRookieHire(GameSave save, string wrestlerId, GameDate date) => save.OwnContracts.Any(x =>
            x.Type == ContractType.Wrestler && x.PersonId == wrestlerId && x.WasRookieWhenHired && x.StartDate.CompareTo(date) <= 0);

        private static int PromotionMilestoneBonus(WrestlerCareerEventState e) =>
            (e.FameMilestoneBefore < 1 && e.FameMilestoneAfter >= 1 ? 5 : 0) +
            (e.FameMilestoneBefore < 2 && e.FameMilestoneAfter >= 2 ? 10 : 0) + (e.FameRookieDevelopment ? 10 : 0);

        public CareerPlan PrepareDue(GameSave original, IEnumerable<string> ignoredShowIds = null, bool includeCurrentQuarter = true, GameDate? rookieDate = null)
        {
            ValidateState(original); var save = original.CopyForCareerSimulation();
            if (save.CareerObservationStartDate.Year == 0) save.CareerObservationStartDate = save.CurrentDate;
            UpdateRookies(save, rookieDate ?? save.CurrentDate, save.Championships);
            SettleQuarters(save, new HashSet<string>(ignoredShowIds ?? Array.Empty<string>()), includeCurrentQuarter);
            return Finish(original, save);
        }

        private CareerPlan Finish(GameSave original, GameSave prepared)
        {
            var plan = new CareerPlan(original, prepared);
            WrestlerGrowthService.ValidateNewIds(original, plan.NewRecordIds); ValidateState(prepared); return plan;
        }

        private void ApplyMatch(GameSave save, ResultApplicationContext context, MatchResultState match, IReadOnlyList<ChampionshipState> titles,
            Dictionary<string, (string Source, int Reference)> card, Dictionary<string, int> initialStatus)
        {
            var ids = Members(match); var states = ids.ToDictionary(id => id, id => save.Wrestlers.Single(x => x.Id == id));
            var status = states.ToDictionary(x => x.Key, x => x.Value.Status.StatusValue);
            var winners = Winners(match); var decisive = winners.Count > 0;
            var performer = match.FinishPerformerId ?? match.WinnerId;
            performer ??= winners.Count == 1 ? winners.Single() : null;
            var loser = match.LoserTargetId;
            if (string.IsNullOrEmpty(loser) && ids.Count == 2 && winners.Count == 1) loser = ids.First(x => !winners.Contains(x));
            var quality = match.CriticReview?.FinalScore ?? match.FinalMatchQuality * 5;
            CheckScore(quality, 105); var fans = FanScore(save, match.FanReaction);
            var title = titles.FirstOrDefault(x => x.MatchHistory.Any(h => h.SourceResultId == match.Id));
            var titleMatch = title?.MatchHistory.Single(x => x.SourceResultId == match.Id);
            var main = MainEvent(context.Show) == match.ShowEventId;
            foreach (var id in ids)
            {
                var w = states[id]; var record = Begin(save, w, CareerEventKind.Match, context.Show.Date, match.Id, context.Show.Id);
                var opponents = Opponents(match, id); var highest = opponents.Count == 0 ? status[id] : opponents.Max(x => status[x]);
                if (card[id].Source == match.Id) Reason(record, StatusChangeReason.Card, CardGain(card[id].Reference - initialStatus[id]));
                if (titleMatch?.ChallengerIds.Contains(id) == true)
                    Reason(record, StatusChangeReason.TitleChallenge, CardGain((title.Type == ChampionshipType.Individual && title.IsWorldChampionship ? 80 : 60) - status[id]));
                var champion = titleMatch != null && !string.IsNullOrEmpty(titleMatch.ChampionAfterId) &&
                    titleMatch.ChampionBeforeId != titleMatch.ChampionAfterId && titleMatch.WinnerMemberIds.Contains(id);
                if (champion) Reason(record, StatusChangeReason.Championship,
                    ChampionshipGain((title.Type == ChampionshipType.Individual && title.IsWorldChampionship ? 90 : 70) - status[id]));
                var won = winners.Contains(id);
                if (decisive && won)
                {
                    var direct = id == performer && !string.IsNullOrEmpty(loser) && status.ContainsKey(loser) ? WinGain(status[loser] - status[id]) : 0;
                    var indirect = WinGain(highest - status[id]) * .4f;
                    Reason(record, StatusChangeReason.Victory, Round(Math.Max(direct, indirect) * WinMultiplier(match.FinishType)));
                }
                else if (decisive)
                {
                    var winningStatus = winners.Max(x => status[x]);
                    var loss = Loss(status[id] - winningStatus);
                    Reason(record, StatusChangeReason.Defeat, -Round(loss * (id == loser ? LossMultiplier(match.FinishType) : .25f)));
                }
                var gain = 0f;
                if (won) gain += 4 + (quality >= 75 && match.FinishType is MatchFinishType.Pinfall or MatchFinishType.Submission ? 2 : 0);
                gain += quality >= 95 ? 5 : quality >= 85 ? 3 : quality >= 75 ? 2 : 0;
                var goodLoss = decisive && !won && (quality >= 75 || fans >= 75);
                if (goodLoss) gain += Math.Max(quality, fans) >= 95 ? 4 : Math.Max(quality, fans) >= 85 ? 2 : 1;
                var gap = highest - status[id];
                if (won || goodLoss) gain += GapMomentum(gap, won);
                gain += Math.Min(3, (match.ExecutionEvents ?? new()).Count(x => x.EventType == MatchExecutionEventType.GreatMoment && x.ParticipantIds.Contains(id)));
                gain += FanGain(fans); Accumulate(w, record, Math.Min(15, gain));
                var titleOpportunity = titleMatch?.ChallengerIds.Contains(id) == true;
                if (w.Momentum.Momentum >= 50 && (main || titleOpportunity))
                {
                    if (champion || main && won)
                        Release(save, w, record, false, champion ? MomentumReleaseReason.Title : MomentumReleaseReason.MainEvent);
                    else if (goodLoss && gap >= 20)
                        Release(save, w, record, true, titleOpportunity ? MomentumReleaseReason.Title : MomentumReleaseReason.MainEvent);
                    else if (decisive)
                    {
                        var protectedFailure = match.FinishType is MatchFinishType.Disqualification or MatchFinishType.CountOut || id != loser;
                        record.FailureAdded = protectedFailure ? .5f : 1;
                        w.Momentum.MomentumFailure += record.FailureAdded;
                        if (w.Momentum.MomentumFailure >= 3) Collapse(save, w, record);
                    }
                }
                w.Status.OfficialMatchCount++; w.Momentum.LastMomentumActivityDate = new(context.Show.Date);
                if (w.Status.IsRookie && (w.Status.OfficialMatchCount >= 10 || champion || DebutedOneYear(w, context.Show.Date))) w.Status.IsRookie = false;
                End(save, w, record);
            }
        }

        private WrestlerCareerEventState Begin(GameSave save, WrestlerState w, CareerEventKind kind, GameDate date, string source = null, string show = null)
        {
            if (save.CareerEvents.Any(x => x.WrestlerId == w.Id && x.Kind == kind &&
                (source != null ? x.SourceResultId == source : x.Date.Equals(date)))) throw new InvalidOperationException("선수 변화는 이미 반영되었습니다.");
            return new() { Id = createId(), WrestlerId = w.Id, Kind = kind, Date = date, ShowId = show, SourceResultId = source,
                StatusBefore = w.Status.StatusValue, WasRookie = w.Status.IsRookie, MomentumBefore = w.Momentum.Momentum, FailureBefore = w.Momentum.MomentumFailure };
        }

        private static void End(GameSave save, WrestlerState w, WrestlerCareerEventState e)
        {
            w.Status.StatusValue = Math.Clamp(e.StatusBefore + e.StatusReasons.Sum(x => x.Amount), 0, 100);
            e.StatusAfter = w.Status.StatusValue; w.Status.LastStatusChange = e.StatusAfter - e.StatusBefore;
            e.IsRookie = w.Status.IsRookie; e.MomentumAfter = w.Momentum.Momentum; e.FailureAfter = w.Momentum.MomentumFailure;
            if (e.StatusReasons.Count > 0 || e.WasRookie != e.IsRookie) w.Status.StatusChangeRecordIds.Add(e.Id);
            save.CareerEvents.Add(e);
        }
        private static void Reason(WrestlerCareerEventState e, StatusChangeReason reason, int amount)
        { if (amount != 0) e.StatusReasons.Add(new() { Reason = reason, Amount = amount }); }
        private static void Accumulate(WrestlerState w, WrestlerCareerEventState e, float amount)
        {
            w.Momentum.Momentum = Math.Clamp(e.MomentumBefore + amount, 0, 100);
            e.MomentumGain = w.Momentum.Momentum - e.MomentumBefore; w.Momentum.LastMomentumGain = e.MomentumGain;
        }
        private static void Release(GameSave save, WrestlerState w, WrestlerCareerEventState e, bool partial, MomentumReleaseReason reason)
        {
            var amount = w.Momentum.Momentum * (partial ? .5f : 1);
            e.ReleasedMomentum = amount; e.ReleaseType = partial ? MomentumReleaseType.Partial : MomentumReleaseType.Full; e.ReleaseReason = reason;
            Reason(e, StatusChangeReason.MomentumRelease, amount >= 80 ? 8 : amount >= 60 ? 5 : amount >= 40 ? 3 : amount >= 20 ? 1 : 0);
            var fans = amount >= 80 ? 5 : amount >= 60 ? 3 : amount >= 40 ? 2 : amount >= 20 ? 1 : 0;
            ChangeFans(save, w, e, fans, fans);
            w.Momentum.Momentum -= amount; w.Momentum.MomentumFailure = 0; w.Momentum.LastMomentumRelease = amount;
            w.Momentum.ReleaseType = e.ReleaseType; w.Momentum.ReleaseReason = reason;
        }
        private static void Collapse(GameSave save, WrestlerState w, WrestlerCareerEventState e)
        {
            var high = w.Momentum.Momentum >= 80; var middle = w.Momentum.Momentum >= 60;
            Reason(e, StatusChangeReason.MomentumCollapse, high ? -3 : middle ? -2 : -1);
            ChangeFans(save, w, e, high ? -4 : middle ? -2 : -1, high ? -10 : middle ? -6 : -3);
            e.Collapsed = true; ResetMomentum(w);
        }
        private static void ResetMomentum(WrestlerState w)
        {
            w.Momentum.Momentum = w.Momentum.MomentumFailure = w.Momentum.LastMomentumGain = w.Momentum.LastMomentumRelease = 0;
            w.Momentum.ReleaseType = MomentumReleaseType.None; w.Momentum.ReleaseReason = MomentumReleaseReason.None;
        }
        private static void ChangeFans(GameSave save, WrestlerState w, WrestlerCareerEventState e, float interest, float satisfaction)
        {
            if (interest == 0 && satisfaction == 0) return;
            if (save.Promotion.Audience?.IsInitialized != true) save.Promotion.Audience = FanAudienceService.CreateInitial(e.Date);
            for (var group = 0; group < 3; group++)
            {
                var response = group == 0 ? w.FanReaction.Mark : group == 1 ? w.FanReaction.Casual : w.FanReaction.Hardcore;
                var before = response.Interest; response.Interest = Math.Clamp(before + interest, 0, 100);
                if (group == 0) w.FanReaction.Mark = response; else if (group == 1) w.FanReaction.Casual = response; else w.FanReaction.Hardcore = response;
                Set(e.InterestChange, group, response.Interest - before);
                var audience = FanAudienceService.Group(save.Promotion.Audience, group); before = audience.Satisfaction;
                audience.Satisfaction = Math.Clamp(before + satisfaction, 0, 100); Set(e.PromotionSatisfactionChange, group, audience.Satisfaction - before);
            }
            w.FanReaction.LastUpdatedAt = new(e.Date);
        }

        private void UpdateRookies(GameSave save, GameDate date, IReadOnlyList<ChampionshipState> titles)
        {
            foreach (var w in save.Wrestlers.Where(x => x.Status.IsRookie &&
                (x.Status.OfficialMatchCount >= 10 || DebutedOneYear(x, date) || titles.Any(t => t.Reigns.Any(r => r.MemberIds.Contains(x.Id))))))
            {
                var e = Begin(save, w, CareerEventKind.RookieGraduation, date); w.Status.IsRookie = false; End(save, w, e);
            }
        }
        private void SettleQuarters(GameSave save, HashSet<string> ignored, bool inclusive, string applyingShowId = null)
        {
            var latest = WrestlerGrowthService.LatestQuarterEnd(save.CurrentDate, inclusive);
            if (!save.LastCareerQuarterEnd.HasValue) save.LastCareerQuarterEnd = WrestlerGrowthService.LatestQuarterEnd(save.CurrentDate, false);
            if (!latest.HasValue) return;
            var end = save.LastCareerQuarterEnd.HasValue ? NextQuarter(save.LastCareerQuarterEnd.Value) : latest.Value;
            while (end.CompareTo(latest.Value) <= 0)
            {
                if (end.Equals(save.CurrentDate) && save.Shows.Any(x => x.Date.Equals(end) && x.Status != ShowStatus.Completed && !save.IsCancelledShow(x) && !ignored.Contains(x.Id))) return;
                var prestigeBefore = save.Promotion.PromotionPrestige; var ppvBonus = 0L;
                var startValue = new DateTime(end.Year, end.Month, 1).AddMonths(-2); var start = new GameDate(startValue.Year, startValue.Month, 1);
                if (save.CareerObservationStartDate.CompareTo(start) <= 0)
                foreach (var w in save.Wrestlers.Where(x => x.Identity.CreatedDate.CompareTo(start) <= 0 &&
                    (!x.Momentum.LastMomentumActivityDate.HasValue || x.Momentum.LastMomentumActivityDate.Value.CompareTo(start) < 0) &&
                    (x.Momentum.Momentum > 0 || x.Momentum.MomentumFailure > 0)))
                {
                    var e = Begin(save, w, CareerEventKind.Inactivity, end); ResetMomentum(w); End(save, w, e);
                }
                var paid = new HashSet<string>(save.Promotion.AppliedPrestigeReasonIds);
                var rewards = save.CareerEvents.Where(x => x.Kind == CareerEventKind.Fame && x.FamePromotionBonus > 0 &&
                    x.Date.CompareTo(end) <= 0 && !paid.Contains(x.Id)).ToList();
                save.Promotion.PromotionPrestige = checked(save.Promotion.PromotionPrestige + rewards.Sum(x => (long)x.FamePromotionBonus));
                save.Promotion.AppliedPrestigeReasonIds.AddRange(rewards.Select(x => x.Id));
                foreach (var show in save.Shows.Where(x => x.Date.CompareTo(end) <= 0 && !save.IsCancelledShow(x) &&
                    (x.Status == ShowStatus.Completed || x.Id == applyingShowId)))
                {
                    var result = save.ShowResults.SingleOrDefault(x => x.ShowId == show.Id && x.ShowVersion == show.ShowVersion && x.HasPrestigeSnapshot);
                    if (result?.PpvPrestigeBonus is not > 0) continue;
                    var reason = PromotionPrestigeRules.PpvReason(show.Id);
                    if (save.Promotion.AppliedPrestigeReasonIds.Contains(reason)) continue;
                    save.Promotion.PromotionPrestige = checked(save.Promotion.PromotionPrestige + result.PpvPrestigeBonus);
                    ppvBonus = checked(ppvBonus + result.PpvPrestigeBonus);
                    save.Promotion.AppliedPrestigeReasonIds.Add(reason);
                }
                PromotionQuarterService.Settle(save, end, applyingShowId, prestigeBefore, rewards.Sum(x => (long)x.FamePromotionBonus), ppvBonus);
                save.LastCareerQuarterEnd = new(end);
                if (end.Year == 9999 && end.Month == 11) break;
                end = NextQuarter(end);
            }
        }

        public static bool HasMomentumRisk(GameSave save, ShowState show, MatchPlanState plan, string wrestlerId)
        {
            var w = save.Wrestlers.FirstOrDefault(x => x.Id == wrestlerId);
            if (w?.Momentum == null || show.Status == ShowStatus.Completed || save.IsCancelledShow(show) ||
                w.Momentum.Momentum + 15 < 50 || w.Momentum.MomentumFailure < 2 || plan.FinishType == MatchFinishType.Draw) return false;
            var members = (plan.Sides?.Count > 0 ? plan.Sides.SelectMany(x => x.MemberIds) : plan.ParticipantIds).ToHashSet();
            if (!members.Contains(wrestlerId)) return false;
            var eventId = save.ShowEvents.FirstOrDefault(x => x.ShowId == show.Id && x.DetailId == plan.Id)?.Id;
            var title = save.Championships.FirstOrDefault(x => x.Id == plan.ChampionshipId);
            var defender = title != null && (title.Type == ChampionshipType.Individual ? title.HolderId == wrestlerId :
                save.TagTeams.Any(x => x.Id == title.HolderId && x.MemberIds.Contains(wrestlerId)));
            var main = eventId != null && MainEvent(show) == eventId;
            if (!main && (title == null || defender)) return false;
            var winningSide = plan.Sides?.FirstOrDefault(x => x.Id == plan.WinningSideId || x.MemberIds.Contains(plan.FinishPerformerId ?? plan.WinnerId));
            var won = winningSide?.MemberIds.Contains(wrestlerId) == true || plan.WinnerId == wrestlerId;
            var protectedFinish = plan.FinishType is MatchFinishType.Disqualification or MatchFinishType.CountOut;
            if (won && (main || !protectedFinish)) return false;
            var direct = plan.LoserTargetId == wrestlerId || members.Count == 2;
            var failure = protectedFinish || !direct ? .5f : 1;
            return w.Momentum.MomentumFailure + failure >= 3;
        }
        private static string MainEvent(ShowState show) => string.IsNullOrEmpty(show.MainEventId) ? show.TimelineEventIds.LastOrDefault() : show.MainEventId;
        private static int CardReference(ShowState show, string eventId) => MainEvent(show) == eventId ? 75 :
            (string.IsNullOrEmpty(show.OpeningEventId) ? show.TimelineEventIds.FirstOrDefault() : show.OpeningEventId) == eventId ||
            show.TimelineEventIds.IndexOf(eventId) == show.TimelineEventIds.Count - 2 ? 60 : 40;
        private static List<string> Members(MatchResultState r) => r.ParticipantProfiles.SelectMany(x => x.MemberIds).Distinct().ToList();
        private static List<string> Opponents(MatchResultState r, string id)
        {
            if (r.ParticipantProfiles.Count == 1) return Members(r).Where(x => x != id).ToList();
            var side = r.ParticipantProfiles.First(x => x.MemberIds.Contains(id));
            return r.ParticipantProfiles.Where(x => x != side).SelectMany(x => x.MemberIds).Distinct().ToList();
        }
        private static HashSet<string> Winners(MatchResultState r)
        {
            if (r.FinishType == MatchFinishType.Draw || r.WasStoppedByInjury || r.WasStoppedByExecution || r.WasStoppedBySpot) return new();
            var side = !string.IsNullOrEmpty(r.WinningSideId) ? r.ParticipantProfiles.FirstOrDefault(x => x.SideId == r.WinningSideId) : null;
            var id = r.FinishPerformerId ?? r.WinnerId;
            if (r.ParticipantProfiles.Count == 1 && string.IsNullOrEmpty(r.WinningSideId))
                return !string.IsNullOrEmpty(id) && Members(r).Contains(id) ? new() { id } : new();
            side ??= r.ParticipantProfiles.FirstOrDefault(x => x.MemberIds.Contains(id));
            return side != null ? new(side.MemberIds) : new();
        }
        private static bool DebutedOneYear(WrestlerState w, GameDate date) => w.Status.DebutDate.Year > 0 &&
            new DateTime(w.Status.DebutDate.Year, w.Status.DebutDate.Month, w.Status.DebutDate.Day).AddYears(1) <= new DateTime(date.Year, date.Month, date.Day);
        private static GameDate NextQuarter(GameDate end)
        { var next = new DateTime(end.Year, end.Month, 1).AddMonths(3); return new(next.Year, next.Month, DateTime.DaysInMonth(next.Year, next.Month)); }
        private static int CardGain(int gap) => gap >= 50 ? 6 : gap >= 35 ? 5 : gap >= 20 ? 3 : gap >= 10 ? 1 : 0;
        private static int WinGain(int gap) => gap >= 50 ? 12 : gap >= 35 ? 9 : gap >= 20 ? 6 : gap >= 10 ? 3 : gap >= 0 ? 1 : 0;
        private static int ChampionshipGain(int gap) => gap < 0 ? 0 : WinGain(gap);
        private static int Loss(int gap) => gap >= 50 ? 6 : gap >= 35 ? 5 : gap >= 20 ? 3 : gap >= 10 ? 1 : 0;
        private static float WinMultiplier(MatchFinishType f) => f switch { MatchFinishType.Submission => 1.1f, MatchFinishType.RollUp => .65f,
            MatchFinishType.Disqualification => .2f, MatchFinishType.CountOut => .4f, MatchFinishType.Draw => 0, _ => 1 };
        private static float LossMultiplier(MatchFinishType f) => f switch { MatchFinishType.Submission => 1.1f, MatchFinishType.RollUp => .35f,
            MatchFinishType.Disqualification => .1f, MatchFinishType.CountOut => .2f, MatchFinishType.Draw => 0, _ => 1 };
        private static int GapMomentum(int gap, bool win) => win ? gap >= 50 ? 6 : gap >= 35 ? 4 : gap >= 20 ? 3 : gap >= 10 ? 1 : 0 :
            gap >= 50 ? 4 : gap >= 35 ? 3 : gap >= 20 ? 2 : gap >= 10 ? 1 : 0;
        private static int FanGain(float score) => score >= 90 ? 5 : score >= 75 ? 3 : score >= 60 ? 1 : 0;
        private static float FanScore(GameSave save, FanReactionResultState fans)
        {
            if (fans?.IsCalculated != true) return 0;
            for (var i = 0; i < 3; i++) CheckScore(FanAudienceService.Get(fans, i), 100);
            var audience = save.Promotion.Audience;
            var total = audience?.TotalFollowers ?? 0;
            return total > 0 ? (float)Enumerable.Range(0, 3).Sum(i => FanAudienceService.Get(fans, i) * (double)FanAudienceService.Group(audience, i).Followers / total) :
                (fans.Family + fans.Light + fans.Mania) / 3;
        }
        private static int Round(float value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
        private static void Set(FanReactionResultState v, int group, float value)
        { if (group == 0) v.Family = value; else if (group == 1) v.Light = value; else v.Mania = value; }
        private static void CheckScore(float value, float max)
        { if (!float.IsFinite(value) || value < 0 || value > max) throw new InvalidOperationException("선수 변화를 계산할 평가가 올바르지 않습니다."); }

        public static void ValidateState(GameSave save)
        {
            var ids = new HashSet<string>(); var sources = new HashSet<string>();
            PromotionQuarterService.Validate(save);
            var prestigeReasons = save.Promotion.AppliedPrestigeReasonIds;
            if (prestigeReasons == null || prestigeReasons.Any(string.IsNullOrWhiteSpace) || prestigeReasons.Distinct().Count() != prestigeReasons.Count)
                throw new InvalidOperationException("단체 명성 지급 기록이 중복되거나 올바르지 않습니다.");
            if (save.LastCareerQuarterEnd.HasValue)
            {
                var end = save.LastCareerQuarterEnd.Value;
                if (!ValidDate(end) || end.Month is not (2 or 5 or 8 or 11) || end.Day != DateTime.DaysInMonth(end.Year, end.Month) || end.CompareTo(save.CurrentDate) > 0)
                    throw new InvalidOperationException("선수 변화 분기 체크포인트가 올바르지 않습니다.");
            }
            foreach (var result in save.ShowResults ?? new())
            {
                PromotionPrestigeRules.Validate(result);
                if (result?.HasFameSnapshot == true && result.FameReach < 0) throw new InvalidOperationException("명성 노출 규모가 올바르지 않습니다.");
            }
            foreach (var w in save.Wrestlers)
            {
                WrestlerFameRules.Validate(w.Fame);
                if (w.Status == null || w.Momentum == null || w.Status.StatusValue < 0 || w.Status.StatusValue > 100 || w.Status.OfficialMatchCount < 0 ||
                    !FiniteRange(w.Momentum.Momentum, 0, 100) || !float.IsFinite(w.Momentum.MomentumFailure) || w.Momentum.MomentumFailure < 0)
                    throw new InvalidOperationException("선수 위상·모멘텀 상태가 올바르지 않습니다.");
            }
            foreach (var e in save.CareerEvents ?? new())
            {
                if (e == null || !EntityId.IsValidRuntimeId(e.Id) || !ids.Add(e.Id) || !save.Wrestlers.Any(x => x.Id == e.WrestlerId) || !ValidDate(e.Date) ||
                    !Enum.IsDefined(typeof(CareerEventKind), e.Kind) || !Enum.IsDefined(typeof(MomentumReleaseType), e.ReleaseType) ||
                    !Enum.IsDefined(typeof(MomentumReleaseReason), e.ReleaseReason) || e.StatusReasons == null || e.StatusReasons.Any(x => x == null || !Enum.IsDefined(typeof(StatusChangeReason), x.Reason)) ||
                    e.StatusBefore < 0 || e.StatusBefore > 100 || e.StatusAfter != Math.Clamp(e.StatusBefore + e.StatusReasons.Sum(x => (long)x.Amount), 0, 100) ||
                    !FiniteRange(e.MomentumBefore, 0, 100) || !FiniteRange(e.MomentumAfter, 0, 100) || !FiniteRange(e.MomentumGain, 0, e.Kind == CareerEventKind.Match ? 15 : 10) ||
                    !FiniteRange(e.ReleasedMomentum, 0, 100) || !float.IsFinite(e.FailureBefore) || !float.IsFinite(e.FailureAfter) || e.FailureBefore < 0 || e.FailureAfter < 0 ||
                    !FiniteRange(e.FailureAdded, 0, 1) || !sources.Add($"{e.WrestlerId}:{e.Kind}:{e.SourceResultId}:{e.Date.Year}:{e.Date.Month}:{e.Date.Day}") ||
                    e.Kind == CareerEventKind.Match && !save.MatchResults.Any(x => x.Id == e.SourceResultId && x.ShowId == e.ShowId) ||
                    e.Kind == CareerEventKind.Promo && !save.PromoResults.Any(x => x.Id == e.SourceResultId && x.ShowId == e.ShowId))
                    throw new InvalidOperationException("선수 변화 기록이 올바르지 않습니다.");
                if (e.ReleaseType != MomentumReleaseType.None &&
                    (Math.Abs(e.ReleasedMomentum - (e.MomentumBefore + e.MomentumGain) * (e.ReleaseType == MomentumReleaseType.Partial ? .5f : 1)) > .001f || e.FailureAfter != 0 || e.ReleaseReason == MomentumReleaseReason.None))
                    throw new InvalidOperationException("모멘텀 해소 기록이 올바르지 않습니다.");
                var expected = e.Collapsed || e.Kind == CareerEventKind.Inactivity ? 0 : e.MomentumBefore + e.MomentumGain - e.ReleasedMomentum;
                if (Math.Abs(e.MomentumAfter - expected) > .001f) throw new InvalidOperationException("모멘텀 변화 합계가 일치하지 않습니다.");
                if (e.Kind == CareerEventKind.Fame)
                {
                    var source = save.ShowResults.SingleOrDefault(x => x.Id == e.SourceResultId && x.ShowId == e.ShowId && x.HasFameSnapshot);
                    if (source == null || e.FamePromotionId != save.Promotion.Id || e.FameGain <= 0)
                        throw new InvalidOperationException("명성 기록 출처가 올바르지 않습니다.");
                    var fame = new WrestlerFameState { Value = e.FameBefore, Remainder = e.FameRemainderBefore, HighestMilestone = e.FameMilestoneBefore };
                    var gain = WrestlerFameRules.CalculateGain(e.FameBefore, source.FameReach, e.FameActivity, e.FameTitle);
                    if (!double.IsFinite(e.FameGain) || Math.Abs(gain - e.FameGain) > .000001) throw new InvalidOperationException("명성 증가 계산이 일치하지 않습니다.");
                    WrestlerFameRules.Accumulate(fame, gain);
                    if (e.FameAfter != fame.Value || !double.IsFinite(e.FameRemainderAfter) || Math.Abs(e.FameRemainderAfter - fame.Remainder) > .000001 ||
                        e.FameMilestoneAfter != fame.HighestMilestone) throw new InvalidOperationException("명성 변화 합계가 일치하지 않습니다.");
                    if (e.HasPromotionFameBonus)
                    {
                        var rookie = e.FameMilestoneBefore < 2 && e.FameMilestoneAfter >= 2 && WasRookieHire(save, e.WrestlerId, e.Date);
                        if (e.FameRookieDevelopment != rookie || e.FamePromotionBonus != PromotionMilestoneBonus(e))
                            throw new InvalidOperationException("선수 명성의 단체 보너스가 올바르지 않습니다.");
                    }
                    else if (e.FamePromotionBonus != 0 || e.FameRookieDevelopment)
                        throw new InvalidOperationException("자격이 없는 단체 명성 보너스입니다.");
                    if (prestigeReasons.Contains(e.Id) && e.FamePromotionBonus <= 0)
                        throw new InvalidOperationException("지급할 단체 명성 보너스가 없습니다.");
                }
                foreach (var delta in new[] { e.InterestChange, e.PromotionSatisfactionChange })
                    if (delta == null || Enumerable.Range(0, 3).Any(i => !FiniteRange(FanAudienceService.Get(delta, i), -100, 100)))
                        throw new InvalidOperationException("팬 반응 변화 기록이 올바르지 않습니다.");
            }
        }
        private static bool FiniteRange(float v, float min, float max) => float.IsFinite(v) && v >= min && v <= max;
        private static bool ValidDate(GameDate date)
        { try { _ = new DateTime(date.Year, date.Month, date.Day); return true; } catch (ArgumentOutOfRangeException) { return false; } }
    }
}
