using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Identifiers;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public sealed class WrestlerBehaviorService
    {
        private readonly Func<string> createId;
        private readonly Random random;
        public WrestlerBehaviorService(Func<string> createId = null, Random random = null)
        { this.createId = createId ?? EntityId.CreateRuntimeId; this.random = random; }

        public static List<string> AvailableForShow(GameSave save, GameDate date) => save.Wrestlers
            .Where(w => w != null && w.Id != save.Player?.WrestlerId && w.PromotionId == save.Promotion.Id &&
                w.Roster.ActivityState == RosterActivityState.Active && w.Condition.InjuryStatus == InjuryStatus.None &&
                w.Condition.Availability == WrestlerAvailability.Available &&
                !(w.KayfabeInjury?.Absent == true && (!w.KayfabeInjury.ReturnDate.HasValue || w.KayfabeInjury.ReturnDate.Value.CompareTo(date) > 0)) &&
                save.OwnContracts.Any(c => c.PersonId == w.Id && c.Type == ContractType.Wrestler && c.Covers(date)))
            .Select(w => w.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();

        public static ExpectedRole ExpectedRoleAt(GameSave save, WrestlerState person, GameDate date)
        {
            var contract = save.OwnContracts.SingleOrDefault(c => c.PersonId == person.Id && c.Type == ContractType.Wrestler && c.Covers(date));
            return contract?.HasExpectedRole == true ? contract.ExpectedRole : person.Status.StatusValue >= 75
                ? ExpectedRole.MainEventer : person.Status.StatusValue >= 40 ? ExpectedRole.Midcarder : ExpectedRole.Jobber;
        }

        public static void CaptureCardPlacements(GameSave save, ShowResultState result, IReadOnlyList<MatchResultState> matches,
            IReadOnlyDictionary<string, ExpectedRole> expectedRoles)
        {
            var show = save.Shows.Single(s => s.Id == result.ShowId);
            var ordered = matches.OrderBy(m => show.TimelineEventIds.IndexOf(m.ShowEventId)).ToList();
            result.CardPlacements = CardPlacements(ordered, result.AvailableWrestlerIds, id => expectedRoles[id], out var upper, out var main);
            result.HasCardPlacementSnapshot = true; result.HasUpperCardOpportunity = upper; result.HasMainEventOpportunity = main;
        }

        private static List<WrestlerCardPlacement> CardPlacements(IReadOnlyList<MatchResultState> matches, IEnumerable<string> available,
            Func<string, ExpectedRole> role, out bool upper, out bool main)
        {
            var upperStart = matches.Count / 2;
            bool Performed(MatchResultState m) => string.IsNullOrEmpty(m.CancellationReason) && m.ActualMatchDuration > 0;
            upper = matches.Skip(upperStart).Any(Performed); main = matches.Count > 0 && Performed(matches.Last());
            var result = new List<WrestlerCardPlacement>();
            foreach (var id in available.OrderBy(x => x, StringComparer.Ordinal))
            {
                var booked = Enumerable.Range(0, matches.Count).Where(i => matches[i].ParticipantProfiles.Any(p => p.MemberIds.Contains(id))).ToList();
                if (booked.Count == 0) continue;
                var performed = booked.Where(i => Performed(matches[i])).ToList();
                var best = performed.Count > 0 ? performed.Max() : -1;
                result.Add(new() { WrestlerId = id, ExpectedRole = role(id), HasMatchAppearance = best >= 0,
                    UpperCard = best >= upperStart, MainEvent = best >= 0 && best == matches.Count - 1,
                    CancelledUpperCard = booked.Any(i => i >= upperStart && !Performed(matches[i])),
                    CancelledMainEvent = booked.Any(i => i == matches.Count - 1 && !Performed(matches[i])) });
            }
            return result;
        }

        public BehaviorPlan PrepareShow(GameSave save, ShowResultState result, IReadOnlyCollection<string> appeared)
        {
            ValidateState(save);
            var copy = save.CopyForBehaviorSimulation(); var plan = new BehaviorPlan(save, copy);
            var key = $"behavior-show:{result.ShowId}:{result.ShowVersion}";
            if (!result.HasAvailabilitySnapshot || save.ProcessedIds.Contains(key)) return plan;
            var show = save.Shows.Single(x => x.Id == result.ShowId);
            if (show.ShowVersion != result.ShowVersion || show.Status != ShowStatus.ResultsReviewed || save.IsCancelledShow(show))
                throw new InvalidOperationException("확정할 쇼의 출연 관찰만 반영할 수 있습니다.");
            var cancelledBookings = save.MatchResults.Where(r => result.MatchResultIds.Contains(r.Id) && !string.IsNullOrEmpty(r.CancellationReason))
                .SelectMany(r => r.ParticipantProfiles.SelectMany(p => p.MemberIds))
                .Concat(save.PromoResults.Where(r => result.PromoResultIds.Contains(r.Id) && !string.IsNullOrEmpty(r.CancellationReason))
                    .SelectMany(r => save.PromoPlans.Single(p => p.Id == r.PromoPlanId).ParticipantIds)).ToHashSet(StringComparer.Ordinal);
            foreach (var id in result.AvailableWrestlerIds.OrderBy(x => x, StringComparer.Ordinal))
            {
                var person = save.Wrestlers.Single(x => x.Id == id);
                if (!Employed(save, person, save.CurrentDate) || id == save.Player?.WrestlerId) continue;
                var state = copy.WrestlerBehaviors.SingleOrDefault(x => x.PromotionId == save.Promotion.Id && x.WrestlerId == id);
                var didAppear = appeared.Contains(id); var whollyCancelled = !didAppear && cancelledBookings.Contains(id);
                if (whollyCancelled && state?.Concerns.Any(c => c.Kind == WrestlerConcernKind.CardPlacement && c.Status == WrestlerConcernStatus.PromiseActive) != true) continue;
                if (state == null) { state = new() { PromotionId = save.Promotion.Id, WrestlerId = id }; copy.WrestlerBehaviors.Add(state); }
                if (state.Observations.Count > 0 && state.Observations.Last().Date.CompareTo(show.Date) > 0)
                    throw new InvalidOperationException("출연 관찰은 날짜 순서대로 반영해야 합니다.");
                if (!whollyCancelled)
                {
                    state.Observations.Add(new() { ShowId = show.Id, ShowVersion = show.ShowVersion, Date = show.Date, Appeared = didAppear });
                    while (state.Observations.Count > 4) state.Observations.RemoveAt(0);
                    state.MissedStreak = didAppear ? 0 : state.MissedStreak + 1;
                }
                var placement = result.HasCardPlacementSnapshot ? result.CardPlacements.SingleOrDefault(x => x.WrestlerId == id) : null;
                foreach (var active in state.Concerns.Where(c => c.Status == WrestlerConcernStatus.PromiseActive).ToList())
                {
                    var promise = active.Promises.Single(x => x.Status == WrestlerPromiseStatus.Active);
                    if (show.Date.CompareTo(promise.StartDate) < 0 || show.Date.CompareTo(promise.DueDate) > 0) continue;
                    var card = active.Kind == WrestlerConcernKind.CardPlacement;
                    var met = card ? placement?.HasMatchAppearance == true && MatchesRole(placement, promise.RequiredCardRole) : didAppear;
                    if (card && (!result.HasCardPlacementSnapshot || !met &&
                        (CancelledRole(placement, promise.RequiredCardRole) ||
                        !(promise.RequiredCardRole == ExpectedRole.MainEventer ? result.HasMainEventOpportunity : result.HasUpperCardOpportunity)))) continue;
                    if (!card && whollyCancelled) continue;
                    promise.EligibleShows++;
                    if (met) SettlePromise(copy, state, active, promise, WrestlerPromiseStatus.Kept, plan);
                    else if (promise.EligibleShows >= 2) SettlePromise(copy, state, active, promise, WrestlerPromiseStatus.Broken, plan);
                }
                UpdateDeadlines(copy, state, save.CurrentDate, plan, show.Id);
                if (!whollyCancelled && state.Observations.Count == 4)
                {
                    var role = ExpectedRoleAt(save, person, show.Date);
                    var target = Math.Min(1, .25f * ((int)role + 1) + .25f * Normalized(person.Hidden?.Ambition ?? 10));
                    var count = state.Observations.Count(x => x.Appeared);
                    var severity = Math.Clamp((target - count / 4f) * 100 + Math.Max(0, state.MissedStreak - 4) * 5, 0, 100);
                    EvaluateConcern(copy, person, state, WrestlerConcernKind.AppearanceOpportunity, severity, show, key,
                        $"최근 정상 출전 가능했던 4회 쇼 중 {count}회 출연했습니다.", ExpectedRole.Jobber, plan);
                }
                if (placement?.HasMatchAppearance != true || !MatchesRole(placement, placement.ExpectedRole) && CancelledRole(placement, placement.ExpectedRole)) continue;
                state.CardObservations.Add(new() { ShowId = show.Id, ShowVersion = show.ShowVersion, Date = show.Date,
                    ExpectedRole = placement.ExpectedRole, UpperCard = placement.UpperCard, MainEvent = placement.MainEvent });
                while (state.CardObservations.Count > 4) state.CardObservations.RemoveAt(0);
                if (state.CardObservations.Count < 4) continue;
                var cardSeverity = Math.Clamp(state.CardObservations.Average(o =>
                    (o.ExpectedRole switch { ExpectedRole.MainEventer => .75f, ExpectedRole.Midcarder => .5f, _ => 0f }) +
                    .25f * Normalized(person.Hidden?.Ambition ?? 10) -
                    ((o.ExpectedRole == ExpectedRole.MainEventer ? o.MainEvent : o.UpperCard) ? 1f : 0f)) * 100, 0, 100);
                EvaluateConcern(copy, person, state, WrestlerConcernKind.CardPlacement, cardSeverity, show, key,
                    $"최근 실제 경기 출연 4회 쇼 중 상위 카드 {state.CardObservations.Count(o => o.UpperCard)}회, 메인이벤트 경기 {state.CardObservations.Count(o => o.MainEvent)}회에 출전했습니다.",
                    placement.ExpectedRole == ExpectedRole.MainEventer ? ExpectedRole.MainEventer : ExpectedRole.Midcarder, plan);
            }
            // A deferred deadline must also settle when this result has no evidence for the promise holder.
            foreach (var state in copy.WrestlerBehaviors.Where(s => s.PromotionId == save.Promotion.Id &&
                Employed(save, save.Wrestlers.Single(w => w.Id == s.WrestlerId), save.CurrentDate)))
                UpdateDeadlines(copy, state, save.CurrentDate, plan, show.Id);
            new LockerRoomService(createId, random).ProcessShow(copy, result, plan.ChangeSatisfaction);
            copy.ProcessedIds.Add(key); plan.Validate(); return plan;
        }

        private void EvaluateConcern(GameSave save, WrestlerState person, WrestlerBehaviorState state, WrestlerConcernKind kind, float severity,
            ShowState show, string key, string observation, ExpectedRole requestedRole, BehaviorPlan plan)
        {
            var concern = Active(state, kind);
            if (concern == null && severity > 0) { concern = new() { Id = createId(), Kind = kind, StartedDate = save.CurrentDate }; state.Concerns.Add(concern); }
            if (concern == null) return;
            var previous = concern.Severity; concern.Severity = severity; plan.ChangeSatisfaction(person.Id, (previous - severity) * .15f);
            if (Math.Abs(previous - severity) >= 1) Record(save, state, concern, WrestlerBehaviorEventKind.Observation, observation, show.Id);
            var card = kind == WrestlerConcernKind.CardPlacement;
            if (severity == 0 && !concern.Promises.Any(x => x.Status == WrestlerPromiseStatus.Active))
            {
                concern.Status = WrestlerConcernStatus.Resolved; ResolveRequest(save, concern);
                Record(save, state, concern, WrestlerBehaviorEventKind.Resolved, card ? "실제 카드 배치가 개선되어 카드 대우 문제의 관찰을 마쳤습니다." :
                    "최근 출연 기회가 개선되어 출연 부족 문제의 관찰을 마쳤습니다.", show.Id); return;
            }
            if (concern.Status != WrestlerConcernStatus.Watching || state.NextActionDate.HasValue && save.CurrentDate.CompareTo(state.NextActionDate.Value) < 0) return;
            var assertiveness = Normalized(person.Hidden?.Assertiveness ?? 10);
            var chance = Math.Clamp(.15 + .5 * assertiveness + .25 * severity / 100 + .05 * state.IgnoredRequests + .05 * state.BrokenPromises, .1, .95);
            if (severity < 60 - 30 * assertiveness || Roll(save, person.Id, key, card ? "card-request" : "request") >= chance) return;
            concern.RequestedCardRole = requestedRole; concern.Status = WrestlerConcernStatus.AwaitingResponse; concern.ResponseDueDate = new(save.CurrentDate.AddDays(7));
            var subject = card ? "카드 대우" : "출연 기회";
            var request = Record(save, state, concern, WrestlerBehaviorEventKind.Request,
                $"{subject}를 개선해 달라며 {(severity >= 75 ? "분명한 입장으로" : "차분하게")} 비공개 면담을 요청했습니다.",
                show.Id, WrestlerInteractionStance.Reserve, severity >= 75 ? WrestlerBehaviorTone.Firm : WrestlerBehaviorTone.Calm);
            concern.RequestId = request.Id; state.NextActionDate = new(save.CurrentDate.AddDays(14));
            new InboxService(createId).Publish(save, "wrestler-request:" + request.Id, InboxMessageType.Decision, InboxPriority.Important,
                "선수 면담", $"{Name(person)}의 {subject} 면담 요청", observation + $" {Date(concern.ResponseDueDate.Value)}까지 면담에 응답할 수 있습니다.",
                InboxTargetType.WrestlerReport, person.Id);
        }

        public BehaviorPlan PrepareDay(GameSave save, GameDate date)
        {
            ValidateState(save);
            var copy = save.CopyForBehaviorSimulation(); copy.CurrentDate = date;
            var plan = new BehaviorPlan(save, copy); var key = "behavior-day:" + DateKey(date);
            if (save.ProcessedIds.Contains(key)) return plan;
            plan.PrepareAdaptation(date);
            new LockerRoomService(createId, random).ProcessTraining(copy, date, plan.ChangeSatisfaction);
            var roomUnreported = copy.LockerRoomEvents.Where(e => !e.IncludedInWeeklyReport && e.PromotionId == save.Promotion.Id).ToList();
            var states = copy.WrestlerBehaviors.Where(x => x.PromotionId == save.Promotion.Id).ToList();
            if (states.Count == 0 && !plan.HasAdaptationChanges) return plan;
            foreach (var state in states)
            {
                var person = save.Wrestlers.Single(x => x.Id == state.WrestlerId);
                if (!Employed(save, person, date))
                {
                    state.Observations.Clear(); state.CardObservations.Clear(); state.MissedStreak = 0;
                    foreach (var concern in state.Concerns.Where(c => c.Status != WrestlerConcernStatus.Resolved).ToList())
                    {
                        ResolveRequest(copy, concern);
                        foreach (var promise in concern.Promises.Where(x => x.Status == WrestlerPromiseStatus.Active)) promise.Status = WrestlerPromiseStatus.Unassessable;
                        concern.Status = WrestlerConcernStatus.Resolved;
                        Record(copy, state, concern, WrestlerBehaviorEventKind.EmploymentClosed, concern.Kind == WrestlerConcernKind.CardPlacement ?
                            "소속 또는 계약이 바뀌어 우리 단체의 카드 대우 관찰을 종료했습니다." : "소속 또는 계약이 바뀌어 우리 단체의 출연 기회 관찰을 종료했습니다.");
                    }
                }
                else
                {
                    UpdateDeadlines(copy, state, date, plan);
                    TryPublicComplaint(copy, person, state, date, plan);
                }
                var unreported = state.Events.Where(x => !x.IncludedInWeeklyReport).ToList();
                var room = roomUnreported.Where(e => e.Significant && (e.ActorId == state.WrestlerId || e.PartnerId == state.WrestlerId || e.MediatorId == state.WrestlerId)).ToList();
                if (new DateTime(date.Year, date.Month, date.Day).DayOfWeek != DayOfWeek.Monday || unreported.Count + room.Count == 0) continue;
                var descriptions = room.Select(e => e.Description).Concat(unreported.Select(x => x.Description)).ToList();
                new InboxService(createId).Publish(copy, $"wrestler-weekly:{state.WrestlerId}:{DateKey(date)}",
                    InboxMessageType.Navigation, InboxPriority.Normal, "선수 관리팀", $"{Name(person)}의 주간 개인 보고서",
                    string.Join("\n", descriptions.TakeLast(4)) +
                    (descriptions.Count > 4 ? "\n이전 관찰은 개인 보고서에서 확인할 수 있습니다." : ""),
                    InboxTargetType.WrestlerReport, state.WrestlerId);
                foreach (var entry in unreported) entry.IncludedInWeeklyReport = true;
            }
            if (new DateTime(date.Year, date.Month, date.Day).DayOfWeek == DayOfWeek.Monday)
                foreach (var entry in roomUnreported) entry.IncludedInWeeklyReport = true;
            copy.ProcessedIds.Add(key); plan.Validate(); return plan;
        }
        public void ProcessDay(GameSave save) => PrepareDay(save, save.CurrentDate).Apply();

        private void TryPublicComplaint(GameSave save, WrestlerState person, WrestlerBehaviorState state, GameDate date, BehaviorPlan plan)
        {
            if (save.HasRunningShow || person.Id == save.Player?.WrestlerId ||
                state.NextActionDate.HasValue && date.CompareTo(state.NextActionDate.Value) < 0) return;
            var threshold = 60 + 30 * Normalized(person.Hidden?.Professionalism ?? 10);
            foreach (var concern in state.Concerns.Where(c => c.Status == WrestlerConcernStatus.Watching && c.Severity >= threshold).OrderByDescending(c => c.Severity))
            {
                var cause = state.Events.LastOrDefault(e => e.ConcernId == concern.Id && e.Kind is WrestlerBehaviorEventKind.Request or
                    WrestlerBehaviorEventKind.Ignored or WrestlerBehaviorEventKind.PromiseBroken or WrestlerBehaviorEventKind.PromiseAgreed or
                    WrestlerBehaviorEventKind.PromiseKept or WrestlerBehaviorEventKind.PromiseUnassessable or WrestlerBehaviorEventKind.Declined or WrestlerBehaviorEventKind.PromiseRejected);
                if (cause == null || cause.Kind is not (WrestlerBehaviorEventKind.Ignored or WrestlerBehaviorEventKind.PromiseBroken) || concern.LastPublicCauseId == cause.Id) continue;
                var request = state.Events.LastOrDefault(e => e.ConcernId == concern.Id && e.Kind == WrestlerBehaviorEventKind.Request && e.Date.CompareTo(cause.Date) <= 0);
                if (request == null) continue;
                var source = request.ShowId + ":" + save.Shows.Single(s => s.Id == request.ShowId).ShowVersion + ":" + cause.Kind + ":" + DateKey(cause.Date);
                var chance = Math.Clamp(.1 + .7 * Normalized(person.Hidden?.MediaExpression ?? 10) + .15 * concern.Severity / 100 - .1 * state.Trust / 100, .05, .95);
                if (Roll(save, person.Id, source, "public-channel-choice") >= chance) continue;
                var channel = Roll(save, person.Id, source, "public-channel") < .5 ? WrestlerPublicChannel.Press : WrestlerPublicChannel.SocialMedia;
                var subject = concern.Kind == WrestlerConcernKind.CardPlacement ? "카드 대우" : "출연 기회";
                var description = $"{(channel == WrestlerPublicChannel.Press ? "언론" : "SNS")}을 통해 {subject} 문제와 " +
                    (cause.Kind == WrestlerBehaviorEventKind.PromiseBroken ? "지켜지지 않은 약속" : "면담 요청에 답을 받지 못한 사실") + "에 대해 공개적으로 불만을 밝혔습니다.";
                var entry = Record(save, state, concern, WrestlerBehaviorEventKind.PublicComplaint, description,
                    stance: WrestlerInteractionStance.Disagree, tone: WrestlerBehaviorTone.Firm);
                entry.PublicChannel = channel; entry.OriginBehaviorEventId = cause.Id;
                concern.LastPublicCauseId = cause.Id; state.NextActionDate = new(date.AddDays(14));
                plan.ChangePublicAudience(person, concern.Severity);
                new InboxService(createId).Publish(save, "wrestler-public:" + entry.Id, InboxMessageType.Navigation, InboxPriority.Important,
                    channel == WrestlerPublicChannel.Press ? "언론 소식" : "SNS 소식", $"{Name(person)}의 공개 발언", description, InboxTargetType.WrestlerReport, person.Id);
                return;
            }
        }

        public void Respond(GameSave save, string wrestlerId, string concernId, WrestlerConcernResponse response)
        {
            ValidateState(save);
            if (save.HasRunningShow) throw new InvalidOperationException("쇼 결과 반영을 마친 뒤 면담에 응답하세요.");
            if (!Enum.IsDefined(typeof(WrestlerConcernResponse), response)) throw new InvalidOperationException("면담 응답을 확인하세요.");
            var person = save.Wrestlers.Single(x => x.Id == wrestlerId);
            if (!Employed(save, person, save.CurrentDate) || !save.IsWrestlerDiscovered(wrestlerId)) throw new InvalidOperationException("현재 우리 단체 선수의 면담에만 응답할 수 있습니다.");
            var copy = save.CopyForBehaviorSimulation(); var plan = new BehaviorPlan(save, copy);
            var state = copy.WrestlerBehaviors.Single(x => x.PromotionId == save.Promotion.Id && x.WrestlerId == wrestlerId);
            var concern = state.Concerns.Single(x => x.Id == concernId);
            if (concern.Status != WrestlerConcernStatus.AwaitingResponse || !concern.ResponseDueDate.HasValue || save.CurrentDate.CompareTo(concern.ResponseDueDate.Value) > 0)
                throw new InvalidOperationException("현재 응답할 수 있는 면담 요청이 아닙니다.");
            ResolveRequest(copy, concern); concern.Status = WrestlerConcernStatus.Watching; state.NextActionDate = new(save.CurrentDate.AddDays(14));
            var card = concern.Kind == WrestlerConcernKind.CardPlacement;
            if (response == WrestlerConcernResponse.Decline)
            {
                state.Trust = Math.Max(0, state.Trust - 2);
                Record(copy, state, concern, WrestlerBehaviorEventKind.Declined, card ? "카드 대우 개선 요구를 거절했습니다. 실제 카드 배치는 계속 관찰합니다." :
                    "출연 기회 확대 요구를 거절했습니다. 실제 출연 상황은 계속 관찰합니다.", stance: WrestlerInteractionStance.Disagree);
            }
            else
            {
                var chance = Math.Clamp((state.Trust + 30 - 15 * Normalized(person.Hidden?.Ambition ?? 10) - 10 * state.BrokenPromises - 5 * state.IgnoredRequests) / 100f, .05f, .95f);
                var request = state.Events.Single(x => x.Id == concern.RequestId);
                var source = request.ShowId + ":" + save.Shows.Single(s => s.Id == request.ShowId).ShowVersion;
                if (Roll(save, wrestlerId, source, card ? "card-promise" : "promise") >= chance)
                    Record(copy, state, concern, WrestlerBehaviorEventKind.PromiseRejected, card ? "제안한 카드 배치 약속이 충분하지 않다며 받아들이지 않았습니다." :
                        "제안한 출연 약속이 충분하지 않다며 받아들이지 않았습니다.",
                        stance: WrestlerInteractionStance.Disagree, tone: WrestlerBehaviorTone.Firm);
                else
                {
                    concern.Promises.Add(new() { Id = createId(), StartDate = save.CurrentDate, DueDate = save.CurrentDate.AddDays(28), RequiredCardRole = concern.RequestedCardRole });
                    concern.Status = WrestlerConcernStatus.PromiseActive;
                    Record(copy, state, concern, WrestlerBehaviorEventKind.PromiseAgreed,
                        card ? $"앞으로 28일 안에 해당 카드 경기를 제공할 수 있는 정상 출전 가능 최대 2회 쇼 중 최소 1회 {CardRoleName(concern.RequestedCardRole)} 출전 기회를 제공하기로 합의했습니다. 실제 경기 출전으로 이행 여부를 판단합니다." :
                        "앞으로 28일 안에 정상 출전 가능한 최대 2회 쇼 중 최소 1회 출연 기회를 제공하기로 합의했습니다. 실제 출연 후 이행 여부를 판단합니다.", stance: WrestlerInteractionStance.Agree);
                }
            }
            plan.Validate(); plan.Apply();
        }

        private void UpdateDeadlines(GameSave save, WrestlerBehaviorState state, GameDate date, BehaviorPlan plan, string processingShowId = null)
        {
            foreach (var concern in state.Concerns.Where(c => c.Status != WrestlerConcernStatus.Resolved).ToList())
                UpdateDeadline(save, state, concern, date, plan, processingShowId);
        }
        private void UpdateDeadline(GameSave save, WrestlerBehaviorState state, WrestlerConcernState concern, GameDate date, BehaviorPlan plan, string processingShowId)
        {
            if (concern.Status == WrestlerConcernStatus.AwaitingResponse && concern.ResponseDueDate.HasValue && date.CompareTo(concern.ResponseDueDate.Value) > 0)
            {
                concern.Status = WrestlerConcernStatus.Watching; ResolveRequest(save, concern);
                state.IgnoredRequests++; state.Trust = Math.Max(0, state.Trust - 5); state.NextActionDate = new(date.AddDays(14));
                Record(save, state, concern, WrestlerBehaviorEventKind.Ignored, "응답 기한까지 면담에 답하지 않아 요청이 종료되었습니다.");
            }
            var promise = concern.Promises.SingleOrDefault(x => x.Status == WrestlerPromiseStatus.Active);
            if (promise == null || date.CompareTo(promise.DueDate) <= 0) return;
            var pending = save.Shows.Any(s => s.Id != processingShowId && !save.IsCancelledShow(s) &&
                s.Date.CompareTo(promise.StartDate) >= 0 && s.Date.CompareTo(promise.DueDate) <= 0 &&
                s.Status is ShowStatus.InProgress or ShowStatus.ResultReview or ShowStatus.ResultsReviewed);
            if (!pending) SettlePromise(save, state, concern, promise, promise.EligibleShows == 0 ? WrestlerPromiseStatus.Unassessable : WrestlerPromiseStatus.Broken, plan);
        }
        private void SettlePromise(GameSave save, WrestlerBehaviorState state, WrestlerConcernState concern, WrestlerAppearancePromise promise, WrestlerPromiseStatus status, BehaviorPlan plan)
        {
            promise.Status = status; concern.Status = WrestlerConcernStatus.Watching; state.NextActionDate = new(save.CurrentDate.AddDays(14));
            var card = concern.Kind == WrestlerConcernKind.CardPlacement;
            if (status == WrestlerPromiseStatus.Kept)
            {
                state.Trust = Math.Min(100, state.Trust + 5);
                Record(save, state, concern, WrestlerBehaviorEventKind.PromiseKept, card ? $"합의한 기간과 기회 안에 실제 {CardRoleName(promise.RequiredCardRole)}에 출전해 카드 배치 약속을 이행했습니다. 대우 문제는 최근 관찰로 별도로 판단합니다." :
                    "합의한 기간과 출연 가능 쇼 안에 실제 출연해 약속을 이행했습니다. 출연 비중 문제는 최근 관찰에 따라 별도로 판단합니다.");
            }
            else if (status == WrestlerPromiseStatus.Broken)
            {
                state.BrokenPromises++; state.Trust = Math.Max(0, state.Trust - 12); plan.ChangeSatisfaction(state.WrestlerId, -5);
                Record(save, state, concern, WrestlerBehaviorEventKind.PromiseBroken, card ? $"합의한 기회가 있었지만 실제 {CardRoleName(promise.RequiredCardRole)}에 출전하지 못해 카드 배치 약속이 지켜지지 않았습니다." :
                    "합의한 기회가 있었지만 실제 출연하지 못해 출연 약속이 지켜지지 않았습니다.");
            }
            else Record(save, state, concern, WrestlerBehaviorEventKind.PromiseUnassessable, card ? "약속 기간에 해당 카드 경기를 제공할 수 있는 정상 출전 가능 완료 쇼가 없어 카드 약속 이행 여부를 판단하지 않았습니다." :
                "약속 기간에 정상 출전 가능한 완료 쇼가 없어 이행 여부를 판단하지 않았습니다.");
        }
        private WrestlerBehaviorEventState Record(GameSave save, WrestlerBehaviorState state, WrestlerConcernState concern,
            WrestlerBehaviorEventKind kind, string description, string showId = null, WrestlerInteractionStance stance = WrestlerInteractionStance.None, WrestlerBehaviorTone tone = WrestlerBehaviorTone.Calm)
        {
            var entry = new WrestlerBehaviorEventState { Id = createId(), ConcernId = concern.Id, ShowId = showId, Date = save.CurrentDate,
                Kind = kind, Description = description, Stance = stance, Tone = tone }; state.Events.Add(entry); return entry;
        }
        private static void ResolveRequest(GameSave save, WrestlerConcernState concern)
        { if (!string.IsNullOrEmpty(concern.RequestId)) new InboxService().ResolveBySource(save, "wrestler-request:" + concern.RequestId); }
        public static string CardRoleName(ExpectedRole role) => role == ExpectedRole.MainEventer ? "메인이벤트 경기" : "상위 카드 경기";
        private static bool MatchesRole(WrestlerCardPlacement placement, ExpectedRole role) => role == ExpectedRole.MainEventer ? placement.MainEvent : placement.UpperCard;
        private static bool CancelledRole(WrestlerCardPlacement placement, ExpectedRole role) => placement != null &&
            (role == ExpectedRole.MainEventer ? placement.CancelledMainEvent : placement.CancelledUpperCard);
        private static WrestlerConcernState Active(WrestlerBehaviorState state, WrestlerConcernKind kind) => state.Concerns.SingleOrDefault(x => x.Kind == kind && x.Status != WrestlerConcernStatus.Resolved);
        private static bool Employed(GameSave save, WrestlerState person, GameDate date) => person.PromotionId == save.Promotion.Id &&
            person.Roster.ActivityState != RosterActivityState.Released && save.OwnContracts.Any(c => c.PersonId == person.Id && c.Type == ContractType.Wrestler && c.Covers(date));
        private static float Normalized(int value) => (value - 1) / 19f;
        private static string Name(WrestlerState person) => string.IsNullOrWhiteSpace(person.Identity.RingName) ? person.Identity.LegalName : person.Identity.RingName;
        private static string Date(GameDate date) => $"{date.Year}.{date.Month:D2}.{date.Day:D2}";
        private static string DateKey(GameDate date) => $"{date.Year:D4}{date.Month:D2}{date.Day:D2}";
        private double Roll(GameSave save, string wrestlerId, string source, string kind)
        {
            if (random != null) return random.NextDouble();
            unchecked { var hash = 2166136261u ^ (uint)save.WorldSeed; foreach (var c in wrestlerId + ":" + source + ":" + kind) { hash ^= c; hash *= 16777619u; }
                return new Random((int)(hash & 0x7fffffffu)).NextDouble(); }
        }
        public static IEnumerable<string> RecordIds(GameSave save) => (save.WrestlerBehaviors ?? new()).SelectMany(s =>
            s.Concerns.Select(c => c.Id).Concat(s.Concerns.SelectMany(c => c.Promises.Select(p => p.Id))).Concat(s.Events.Select(e => e.Id)))
            .Concat((save.LockerRoomEvents ?? new()).Select(e => e.Id));

        public static void ValidateState(GameSave save)
        {
            if (save?.Promotion == null) throw new InvalidOperationException("선수 행동에는 활성 단체가 필요합니다.");
            WrestlerAdaptationService.Validate(save);
            LockerRoomService.Validate(save);
            var states = new HashSet<string>(); var ids = (save.LockerRoomEvents ?? new()).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var state in save.WrestlerBehaviors ?? new())
            {
                if (state == null || save.FindPromotion(state.PromotionId) == null || !save.Wrestlers.Any(w => w.Id == state.WrestlerId) ||
                    !states.Add(state.PromotionId + ":" + state.WrestlerId) || !float.IsFinite(state.Trust) || state.Trust is < 0 or > 100 ||
                    state.MissedStreak < 0 || state.IgnoredRequests < 0 || state.BrokenPromises < 0 || state.Observations == null ||
                    state.Concerns == null || state.Events == null || state.Observations.Count > 4 || state.CardObservations == null || state.CardObservations.Count > 4)
                    throw new InvalidOperationException("선수 행동 상태가 올바르지 않습니다.");
                if (state.NextActionDate.HasValue) CheckDate(state.NextActionDate.Value);
                var observed = new HashSet<string>(); GameDate? previous = null;
                foreach (var observation in state.Observations)
                {
                    if (observation == null || !save.Shows.Any(s => s.Id == observation.ShowId && s.ShowVersion == observation.ShowVersion) ||
                        !observed.Add(observation.ShowId + ":" + observation.ShowVersion) || previous.HasValue && previous.Value.CompareTo(observation.Date) > 0)
                        throw new InvalidOperationException("출연 관찰 근거가 올바르지 않습니다.");
                    CheckDate(observation.Date); previous = observation.Date;
                }
                previous = null; observed.Clear();
                foreach (var observation in state.CardObservations)
                {
                    if (observation == null) throw new InvalidOperationException("카드 관찰이 비어 있습니다.");
                    var source = save.ShowResults.SingleOrDefault(r => r.ShowId == observation.ShowId && r.ShowVersion == observation.ShowVersion);
                    var placement = source?.CardPlacements?.SingleOrDefault(p => p?.WrestlerId == state.WrestlerId);
                    if (source?.HasCardPlacementSnapshot != true || placement?.HasMatchAppearance != true ||
                        !MatchesRole(placement, placement.ExpectedRole) && CancelledRole(placement, placement.ExpectedRole) ||
                        !Enum.IsDefined(typeof(ExpectedRole), observation.ExpectedRole) ||
                        placement.ExpectedRole != observation.ExpectedRole || placement.UpperCard != observation.UpperCard || placement.MainEvent != observation.MainEvent ||
                        !save.Shows.Any(s => s.Id == observation.ShowId && s.ShowVersion == observation.ShowVersion && s.Date.Equals(observation.Date)) ||
                        !observed.Add(observation.ShowId + ":" + observation.ShowVersion) || previous.HasValue && previous.Value.CompareTo(observation.Date) > 0)
                        throw new InvalidOperationException("카드 관찰 근거가 올바르지 않습니다.");
                    CheckDate(observation.Date); previous = observation.Date;
                }
                if (state.Concerns.Any(c => c == null) || state.Concerns.Where(c => c.Status != WrestlerConcernStatus.Resolved).GroupBy(c => c.Kind).Any(g => g.Count() > 1))
                    throw new InvalidOperationException("출연 문제 회차가 중복되거나 비어 있습니다.");
                foreach (var concern in state.Concerns)
                {
                    CheckId(concern.Id, ids); CheckDate(concern.StartedDate);
                    if (!Enum.IsDefined(typeof(WrestlerConcernStatus), concern.Status) || !Enum.IsDefined(typeof(WrestlerConcernKind), concern.Kind) ||
                        !Enum.IsDefined(typeof(ExpectedRole), concern.RequestedCardRole) ||
                        concern.Kind == WrestlerConcernKind.CardPlacement && (concern.Status is WrestlerConcernStatus.AwaitingResponse or WrestlerConcernStatus.PromiseActive) && concern.RequestedCardRole == ExpectedRole.Jobber ||
                        !float.IsFinite(concern.Severity) ||
                        concern.Severity is < 0 or > 100 || concern.Promises == null) throw new InvalidOperationException("선수 문제 상태가 올바르지 않습니다.");
                    if (concern.ResponseDueDate.HasValue) CheckDate(concern.ResponseDueDate.Value);
                    if (concern.Status == WrestlerConcernStatus.AwaitingResponse && (string.IsNullOrEmpty(concern.RequestId) || !concern.ResponseDueDate.HasValue))
                        throw new InvalidOperationException("면담 요청의 응답 기한이 없습니다.");
                    foreach (var promise in concern.Promises)
                    {
                        if (promise == null) throw new InvalidOperationException("출연 약속이 비어 있습니다.");
                        CheckId(promise.Id, ids); CheckDate(promise.StartDate); CheckDate(promise.DueDate);
                        if (!Enum.IsDefined(typeof(WrestlerPromiseStatus), promise.Status) || !Enum.IsDefined(typeof(ExpectedRole), promise.RequiredCardRole) ||
                            concern.Kind == WrestlerConcernKind.CardPlacement && (promise.RequiredCardRole == ExpectedRole.Jobber ||
                                promise.Status == WrestlerPromiseStatus.Active && promise.RequiredCardRole != concern.RequestedCardRole) || promise.StartDate.CompareTo(promise.DueDate) > 0 ||
                            promise.EligibleShows is < 0 or > 2) throw new InvalidOperationException("출연 약속 조건이 올바르지 않습니다.");
                    }
                    if (concern.Promises.Count(p => p.Status == WrestlerPromiseStatus.Active) != (concern.Status == WrestlerConcernStatus.PromiseActive ? 1 : 0))
                        throw new InvalidOperationException("현재 출연 약속과 문제 상태가 다릅니다.");
                }
                foreach (var entry in state.Events)
                {
                    if (entry == null) throw new InvalidOperationException("선수 관찰 기록이 비어 있습니다.");
                    CheckId(entry.Id, ids); CheckDate(entry.Date);
                    if (!state.Concerns.Any(c => c.Id == entry.ConcernId) || string.IsNullOrWhiteSpace(entry.Description) ||
                        !Enum.IsDefined(typeof(WrestlerBehaviorEventKind), entry.Kind) || !Enum.IsDefined(typeof(WrestlerInteractionStance), entry.Stance) ||
                        !Enum.IsDefined(typeof(WrestlerBehaviorTone), entry.Tone) || !string.IsNullOrEmpty(entry.ShowId) && !save.Shows.Any(s => s.Id == entry.ShowId))
                        throw new InvalidOperationException("선수 관찰 기록의 근거가 올바르지 않습니다.");
                }
                foreach (var concern in state.Concerns.Where(c => !string.IsNullOrEmpty(c.RequestId)))
                    if (!state.Events.Any(e => e.Id == concern.RequestId && e.ConcernId == concern.Id && e.Kind == WrestlerBehaviorEventKind.Request && !string.IsNullOrEmpty(e.ShowId)))
                        throw new InvalidOperationException("선수 문제의 요청 기록이 없습니다.");
                foreach (var concern in state.Concerns.Where(c => !string.IsNullOrEmpty(c.LastPublicCauseId)))
                    if (!state.Events.Any(e => e.ConcernId == concern.Id && e.Kind == WrestlerBehaviorEventKind.PublicComplaint && e.OriginBehaviorEventId == concern.LastPublicCauseId))
                        throw new InvalidOperationException("선수 공개 발언의 원래 사건이 없습니다.");
                foreach (var entry in state.Events)
                {
                    if (!Enum.IsDefined(typeof(WrestlerPublicChannel), entry.PublicChannel) ||
                        entry.Kind == WrestlerBehaviorEventKind.PublicComplaint && (entry.PublicChannel == WrestlerPublicChannel.Private ||
                            !state.Events.Any(e => e.Id == entry.OriginBehaviorEventId && e.ConcernId == entry.ConcernId && e.Date.CompareTo(entry.Date) <= 0 &&
                                e.Kind is WrestlerBehaviorEventKind.Ignored or WrestlerBehaviorEventKind.PromiseBroken)) ||
                        entry.Kind != WrestlerBehaviorEventKind.PublicComplaint && (entry.PublicChannel != WrestlerPublicChannel.Private || !string.IsNullOrEmpty(entry.OriginBehaviorEventId)))
                        throw new InvalidOperationException("선수 발언의 공개 범위가 올바르지 않습니다.");
                }
            }
            foreach (var result in save.ShowResults ?? new())
            {
                if (result == null) continue; var available = result.AvailableWrestlerIds ?? new();
                if (result.HasAvailabilitySnapshot && result.AvailableWrestlerIds == null || !result.HasAvailabilitySnapshot && available.Count > 0 ||
                    available.Count != available.Distinct().Count() || available.Any(id => !save.Wrestlers.Any(w => w.Id == id)))
                    throw new InvalidOperationException("쇼의 출전 가능 스냅샷이 올바르지 않습니다.");
                ValidateCardSnapshot(save, result);
            }
        }

        private static void ValidateCardSnapshot(GameSave save, ShowResultState result)
        {
            if (!result.HasCardPlacementSnapshot)
            {
                if (result.CardPlacements?.Count > 0 || result.HasUpperCardOpportunity || result.HasMainEventOpportunity)
                    throw new InvalidOperationException("과거 쇼에 카드 관찰 근거가 추가되어 있습니다.");
                return;
            }
            if (!result.HasAvailabilitySnapshot || result.CardPlacements == null || result.CardPlacements.Any(p => p == null || string.IsNullOrWhiteSpace(p.WrestlerId) ||
                    !result.AvailableWrestlerIds.Contains(p.WrestlerId) || !Enum.IsDefined(typeof(ExpectedRole), p.ExpectedRole)) ||
                result.CardPlacements.Select(p => p.WrestlerId).Distinct().Count() != result.CardPlacements.Count)
                throw new InvalidOperationException("카드 스냅샷이 올바르지 않습니다.");
            var show = save.Shows.SingleOrDefault(s => s?.Id == result.ShowId && s.ShowVersion == result.ShowVersion);
            if (show?.TimelineEventIds == null || show.TimelineEventIds.Distinct().Count() != show.TimelineEventIds.Count ||
                save.MatchResults == null || save.MatchResults.Any(m => m == null) || result.MatchResultIds == null ||
                result.MatchResultIds.Distinct().Count() != result.MatchResultIds.Count)
                throw new InvalidOperationException("카드 스냅샷의 경기 근거가 올바르지 않습니다.");
            var events = show.TimelineEventIds.Select(id => save.ShowEvents.SingleOrDefault(e => e?.Id == id && e.ShowId == show.Id)).ToList();
            if (events.Any(e => e == null)) throw new InvalidOperationException("카드 스냅샷의 편성 근거가 올바르지 않습니다.");
            var planned = events.Where(e => e.EventType == ShowEventType.Match).ToDictionary(e => e.Id, StringComparer.Ordinal);
            var matches = result.MatchResultIds.Select(id => save.MatchResults.SingleOrDefault(m => m.Id == id)).ToList();
            if (matches.Count != planned.Count || matches.Any(m => m == null || m.ShowId != show.Id || m.ShowVersion != show.ShowVersion ||
                !planned.TryGetValue(m.ShowEventId ?? "", out var entry) || entry.DetailId != m.MatchPlanId ||
                m.ParticipantProfiles == null || m.ParticipantProfiles.Any(p => p?.MemberIds == null ||
                    p.MemberIds.Any(id => !save.Wrestlers.Any(w => w.Id == id)))) || matches.Select(m => m.ShowEventId).Distinct().Count() != matches.Count)
                throw new InvalidOperationException("카드 스냅샷의 경기 근거가 올바르지 않습니다.");
            var placements = result.CardPlacements.ToDictionary(p => p.WrestlerId, StringComparer.Ordinal);
            var expected = CardPlacements(matches.OrderBy(m => show.TimelineEventIds.IndexOf(m.ShowEventId)).ToList(), result.AvailableWrestlerIds,
                id => placements.TryGetValue(id, out var p) ? p.ExpectedRole : ExpectedRole.Jobber, out var upper, out var main);
            if (upper != result.HasUpperCardOpportunity || main != result.HasMainEventOpportunity || expected.Count != placements.Count ||
                expected.Any(p => !placements.TryGetValue(p.WrestlerId, out var actual) || actual.UpperCard != p.UpperCard || actual.MainEvent != p.MainEvent ||
                    actual.HasMatchAppearance != p.HasMatchAppearance || actual.CancelledUpperCard != p.CancelledUpperCard || actual.CancelledMainEvent != p.CancelledMainEvent))
                throw new InvalidOperationException("카드 스냅샷이 실제 경기 위치와 다릅니다.");
        }
        private static void CheckId(string id, HashSet<string> ids)
        { if (!EntityId.IsValidRuntimeId(id) || !ids.Add(id)) throw new InvalidOperationException("선수 행동 기록 ID가 중복되거나 올바르지 않습니다."); }
        private static void CheckDate(GameDate date)
        { try { _ = new DateTime(date.Year, date.Month, date.Day); } catch (ArgumentOutOfRangeException) { throw new InvalidOperationException("선수 행동 날짜가 올바르지 않습니다."); } }

        public sealed class BehaviorPlan
        {
            private readonly GameSave target, prepared;
            private readonly Dictionary<string, float> mood = new(StringComparer.Ordinal);
            private readonly Dictionary<string, InboxMessageStatus> priorStatuses;
            private readonly HashSet<string> priorRecords, priorKeys;
            private readonly int priorMessageCount;
            private bool applied;
            private Dictionary<string, WrestlerAdaptationState> adaptations = new(StringComparer.Ordinal);
            private readonly float[] audienceChanges = new float[3];
            internal bool HasAdaptationChanges => adaptations.Count > 0;
            internal void PrepareAdaptation(GameDate date) => adaptations = WrestlerAdaptationService.PrepareDay(target, date);
            internal void ChangePublicAudience(WrestlerState person, float severity)
            {
                if (target.Promotion.Audience?.IsInitialized != true) return;
                FanAudienceService.ValidateAudience(target);
                var reach = Math.Clamp((person.Fame?.Value ?? 0) / 500f, .05f, 1);
                var effect = (.5f + 1.5f * severity / 100) * reach;
                var groups = new[] { target.Promotion.Audience.Mark, target.Promotion.Audience.Casual, target.Promotion.Audience.Hardcore };
                var weights = new[] { .8f, .5f, 1f };
                for (var i = 0; i < 3; i++) if (groups[i].Followers > 0) audienceChanges[i] -= effect * weights[i];
            }
            internal BehaviorPlan(GameSave target, GameSave prepared)
            {
                this.target = target; this.prepared = prepared;
                priorRecords = RecordIds(target).ToHashSet(StringComparer.Ordinal); priorMessageCount = target.InboxMessages.Count;
                priorStatuses = target.InboxMessages.ToDictionary(x => x.Id, x => x.Status); priorKeys = target.ProcessedIds.ToHashSet(StringComparer.Ordinal);
            }
            public IReadOnlyList<string> NewRecordIds => RecordIds(prepared).Where(id => !priorRecords.Contains(id))
                .Concat(prepared.InboxMessages.Skip(priorMessageCount).Select(x => x.Id)).ToList();
            internal void ChangeSatisfaction(string id, float delta) => mood[id] = mood.GetValueOrDefault(id) + delta;
            internal void Validate()
            {
                ValidateState(prepared); WrestlerGrowthService.ValidateNewIds(target, NewRecordIds);
                if (mood.Values.Any(x => !float.IsFinite(x))) throw new InvalidOperationException("감정 변화가 올바르지 않습니다.");
                if (audienceChanges.Any(x => x != 0))
                {
                    if (target.Promotion.Audience?.IsInitialized != true) throw new InvalidOperationException("공개 발언에 대한 팬 반응을 반영할 수 없습니다.");
                    FanAudienceService.ValidateAudience(target);
                }
            }
            public void Apply()
            {
                if (applied) return;
                // Other day services may have allocated records after this plan was prepared.
                Validate();
                target.WrestlerBehaviors = prepared.WrestlerBehaviors;
                target.LockerRoomRelationships = prepared.LockerRoomRelationships; target.LockerRoomEvents = prepared.LockerRoomEvents;
                foreach (var adaptation in adaptations) target.Wrestlers.Single(w => w.Id == adaptation.Key).Adaptation = adaptation.Value;
                if (audienceChanges.Any(x => x != 0))
                {
                    var groups = new[] { target.Promotion.Audience.Mark, target.Promotion.Audience.Casual, target.Promotion.Audience.Hardcore };
                    for (var i = 0; i < 3; i++) groups[i].Satisfaction = Math.Clamp(groups[i].Satisfaction + audienceChanges[i], 0, 100);
                }
                foreach (var entry in mood)
                {
                    var person = target.Wrestlers.Single(w => w.Id == entry.Key);
                    person.Condition.Satisfaction = Math.Clamp(person.Condition.Satisfaction + entry.Value, 0, 100);
                }
                foreach (var message in prepared.InboxMessages.Take(priorMessageCount))
                {
                    if (priorStatuses[message.Id] != message.Status) target.InboxMessages.Single(x => x.Id == message.Id).Status = message.Status;
                }
                target.InboxMessages.AddRange(prepared.InboxMessages.Skip(priorMessageCount));
                foreach (var key in prepared.ProcessedIds.Where(x => !priorKeys.Contains(x))) target.ProcessedIds.Add(key);
                applied = true;
            }
        }
    }
}
