using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Identifiers;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public sealed class LockerRoomService
    {
        private readonly Func<string> createId;
        private readonly Random random;
        public LockerRoomService(Func<string> createId = null, Random random = null)
        { this.createId = createId ?? EntityId.CreateRuntimeId; this.random = random; }

        public void ProcessShow(GameSave save, ShowResultState result, Action<string, float> changeMood)
        {
            var show = save.Shows.Single(s => s.Id == result.ShowId);
            var groups = new List<(List<string> Ids, bool Tension)>();
            foreach (var match in save.MatchResults.Where(m => result.MatchResultIds.Contains(m.Id) && string.IsNullOrEmpty(m.CancellationReason) && m.ActualMatchDuration > 0))
                groups.Add((match.ParticipantProfiles.SelectMany(p => p.MemberIds).Distinct().ToList(),
                    match.EngineState?.Events.Any(e => e.PhysicalExecutionFailed) == true || match.ExecutionEvents.Any(e => e.EventType == MatchExecutionEventType.Mistake)));
            foreach (var promo in save.PromoResults.Where(p => result.PromoResultIds.Contains(p.Id) && string.IsNullOrEmpty(p.CancellationReason)))
                groups.Add((promo.ParticipantUsage.Select(p => p.WrestlerId).Distinct().ToList(), false));
            Process(save, show.Date, LockerRoomWorkKind.Show, $"show:{show.Id}:{show.ShowVersion}", show.Id, groups, changeMood);
        }

        public void ProcessTraining(GameSave save, GameDate date, Action<string, float> changeMood)
        {
            if (save.Training?.IsInitialized != true) return;
            var people = save.Training.Plans.Where(p => p.Mode != TrainingMode.Rest &&
                save.Wrestlers.Any(w => w.Id == p.WrestlerId && TrainingService.Eligible(save, w) && w.Condition.InjuryStatus != InjuryStatus.Severe))
                .Select(p => p.WrestlerId).Distinct().ToList();
            Process(save, date, LockerRoomWorkKind.Training, "training:" + DateKey(date), null,
                new() { (people, people.Count > TrainingService.Facility(save.Training.FacilityLevel).Capacity) }, changeMood);
        }

        private void Process(GameSave save, GameDate date, LockerRoomWorkKind work, string source, string showId,
            List<(List<string> Ids, bool Tension)> groups, Action<string, float> changeMood)
        {
            var people = save.Wrestlers.Where(w => w.Id != save.Player?.WrestlerId && save.IsWrestlerDiscovered(w.Id) &&
                w.PromotionId == save.Promotion.Id && w.Roster.ActivityState == RosterActivityState.Active &&
                save.OwnContracts.Any(c => c.PersonId == w.Id && c.Type == ContractType.Wrestler && c.Covers(date))).ToDictionary(w => w.Id, StringComparer.Ordinal);
            foreach (var group in groups) group.Ids.RemoveAll(id => !people.ContainsKey(id));
            var used = save.LockerRoomEvents.Where(e => e.PromotionId == save.Promotion.Id && e.SourceKey == source &&
                e.Kind is LockerRoomEventKind.Contact or LockerRoomEventKind.Conflict).Select(e => Pair(e.ActorId, e.PartnerId)).ToHashSet(StringComparer.Ordinal);
            foreach (var actor in people.Values.Where(w => groups.Any(g => g.Ids.Contains(w.Id))).OrderBy(w => w.Id, StringComparer.Ordinal))
            {
                var peers = groups.Where(g => g.Ids.Contains(actor.Id)).SelectMany(g => g.Ids).Where(id => id != actor.Id && !used.Contains(Pair(actor.Id, id)))
                    .Distinct().OrderBy(id => id, StringComparer.Ordinal).ToList();
                if (peers.Count == 0) continue;
                var rng = random ?? new Random(Seed(save.WorldSeed, actor.Id + ":" + source));
                var sociability = H(actor.Hidden?.Sociability ?? 10);
                if (rng.NextDouble() >= .1 + .65 * sociability) continue;
                var weights = peers.Select(id => { var known = Find(save, actor.Id, id);
                    return known == null ? .5 + 1.5 * sociability : known.Affinity >= 0 ? 1 + known.Affinity / 100d : .3; }).ToArray();
                var peer = people[peers[PerformerSelector.WeightedIndex(weights, rng)]];
                var key = Pair(actor.Id, peer.Id); used.Add(key);
                var relation = Find(save, actor.Id, peer.Id);
                if (relation == null)
                {
                    var first = string.CompareOrdinal(actor.Id, peer.Id) < 0 ? actor.Id : peer.Id;
                    relation = new() { FirstWrestlerId = first, SecondWrestlerId = first == actor.Id ? peer.Id : actor.Id };
                    save.LockerRoomRelationships.Add(relation);
                }
                if (relation.LastInteractionDate.HasValue && relation.LastInteractionDate.Value.CompareTo(date) > 0)
                    throw new InvalidOperationException("라커룸 교류는 날짜 순서대로 반영해야 합니다.");
                var shared = groups.Where(g => g.Ids.Contains(actor.Id) && g.Ids.Contains(peer.Id)).ToList();
                var tension = shared.Any(g => g.Tension) || relation.Affinity < -10;
                var conflict = tension && rng.NextDouble() < .02 + .18 * (H(actor.Hidden?.ConflictProneness ?? 10) + H(peer.Hidden?.ConflictProneness ?? 10)) / 2 +
                    .08 * Math.Clamp(-relation.Affinity / 100, 0, 1);
                var previousAffinity = relation.Affinity;
                relation.Affinity = Math.Clamp(relation.Affinity + (conflict ? -4 : work == LockerRoomWorkKind.Training ? .1f : 1), -100, 100);
                relation.Interactions++; relation.LastInteractionDate = new(date);
                EnsureBehavior(save, actor.Id); EnsureBehavior(save, peer.Id);
                Record(save, date, work, source, showId, actor.Id, peer.Id, conflict ? LockerRoomEventKind.Conflict : LockerRoomEventKind.Contact,
                    conflict ? work == LockerRoomWorkKind.Training ? "함께 훈련하며 자원 배분과 기존 마찰을 논의하는 과정에서 의견 충돌이 생겼습니다." :
                        "함께 수행한 경기의 진행 문제와 기존 마찰을 논의하다 의견 충돌이 생겼습니다." :
                        work == LockerRoomWorkKind.Training ? "함께 훈련하며 대화를 나누고 친분을 쌓았습니다." : "공동 출연 후 대화를 나누고 친분을 쌓았습니다.",
                    significant: conflict || relation.Interactions == 1 || previousAffinity < 15 && relation.Affinity >= 15);
                if (conflict)
                {
                    changeMood(actor.Id, -1); changeMood(peer.Id, -1);
                    var leader = shared.SelectMany(g => g.Ids).Distinct().Where(id => id != actor.Id && id != peer.Id).Select(id => people[id])
                        .OrderByDescending(w => w.Hidden?.Leadership ?? 10).ThenBy(w => w.Id, StringComparer.Ordinal).FirstOrDefault();
                    if (leader != null && rng.NextDouble() < .1 + .55 * H(leader.Hidden?.Leadership ?? 10))
                    {
                        var influence = .1f + .9f * H(leader.Hidden?.Leadership ?? 10);
                        EnsureBehavior(save, leader.Id);
                        relation.Affinity = Math.Min(100, relation.Affinity + 2 * influence);
                        changeMood(actor.Id, .5f * influence); changeMood(peer.Id, .5f * influence);
                        var entry = Record(save, date, work, source, showId, actor.Id, peer.Id, LockerRoomEventKind.Mediation,
                            "같은 자리에 있던 동료가 중재해 마찰이 일부 진정됐습니다.", significant: true);
                        entry.MediatorId = leader.Id;
                    }
                }
                if (relation.Affinity >= 15) ShareOpinion(save, date, work, source, showId, actor, peer, relation.Affinity, rng);
            }
        }

        private void ShareOpinion(GameSave save, GameDate date, LockerRoomWorkKind work, string source, string showId,
            WrestlerState actor, WrestlerState peer, float affinity, Random rng)
        {
            var sender = EnsureBehavior(save, actor.Id); var receiver = EnsureBehavior(save, peer.Id);
            var concern = sender.Concerns.Where(c => c.Status != WrestlerConcernStatus.Resolved && c.Severity >= 60)
                .OrderByDescending(c => c.Severity).FirstOrDefault();
            if (concern == null) return;
            var origin = sender.Events.LastOrDefault(e => e.ConcernId == concern.Id && e.Kind == WrestlerBehaviorEventKind.Request);
            if (origin == null || save.LockerRoomEvents.Any(e => e.Kind == LockerRoomEventKind.OpinionShared && e.ActorId == actor.Id &&
                e.PartnerId == peer.Id && e.OriginBehaviorEventId == origin.Id)) return;
            if (rng.NextDouble() >= .1 + .5 * H(actor.Hidden?.Assertiveness ?? 10) + .15 * concern.Severity / 100) return;
            var matching = receiver.Concerns.SingleOrDefault(c => c.Kind == concern.Kind && c.Status != WrestlerConcernStatus.Resolved && c.Severity > 0);
            var influence = matching == null ? 0 : 2 * (.1f + .9f * H(actor.Hidden?.Leadership ?? 10)) * affinity / 100 * matching.Severity / 100;
            var before = receiver.Trust;
            receiver.Trust = Math.Max(0, receiver.Trust - influence);
            var entry = Record(save, date, work, source, showId, actor.Id, peer.Id, LockerRoomEventKind.OpinionShared,
                matching == null ? "친한 동료에게 대우 문제를 직접 이야기했습니다. 상대는 같은 문제를 겪고 있지 않아 입장을 따르지 않았습니다." :
                    receiver.Trust < before ? "친한 동료에게 대우 문제를 직접 이야기했습니다. 상대도 자신의 경험과 함께 검토하며 단체에 대한 신뢰가 낮아졌습니다." :
                    "친한 동료에게 대우 문제를 직접 이야기했습니다. 상대도 자신의 경험과 함께 검토했으나 단체에 대한 신뢰는 달라지지 않았습니다.", significant: true);
            entry.OriginBehaviorEventId = origin.Id;
        }
        private LockerRoomEventState Record(GameSave save, GameDate date, LockerRoomWorkKind work, string source, string showId,
            string actor, string peer, LockerRoomEventKind kind, string description, bool significant)
        {
            var entry = new LockerRoomEventState { Id = createId(), PromotionId = save.Promotion.Id, SourceKey = source, ShowId = showId,
                ActorId = actor, PartnerId = peer, Date = date, WorkKind = work, Kind = kind, Description = description, Significant = significant };
            save.LockerRoomEvents.Add(entry); return entry;
        }
        private static WrestlerBehaviorState EnsureBehavior(GameSave save, string id)
        {
            var state = save.WrestlerBehaviors.SingleOrDefault(s => s.PromotionId == save.Promotion.Id && s.WrestlerId == id);
            if (state == null) { state = new() { PromotionId = save.Promotion.Id, WrestlerId = id }; save.WrestlerBehaviors.Add(state); }
            return state;
        }
        private static LockerRoomRelationshipState Find(GameSave save, string a, string b) => save.LockerRoomRelationships.SingleOrDefault(r => Pair(r.FirstWrestlerId, r.SecondWrestlerId) == Pair(a, b));
        private static string Pair(string a, string b) => string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;
        private static float H(int value) => (value - 1) / 19f;
        private static string DateKey(GameDate date) => $"{date.Year:D4}{date.Month:D2}{date.Day:D2}";
        private static int Seed(int seed, string text)
        { unchecked { var value = 2166136261u ^ (uint)seed; foreach (var c in text) { value ^= c; value *= 16777619; } return (int)(value & 0x7fffffff); } }

        public static void Validate(GameSave save)
        {
            var people = (save.Wrestlers ?? new()).Where(w => w != null).Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
            var pairs = new HashSet<string>(StringComparer.Ordinal); var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var relation in save.LockerRoomRelationships ?? new())
            {
                if (relation == null || !people.Contains(relation.FirstWrestlerId) || !people.Contains(relation.SecondWrestlerId) ||
                    string.CompareOrdinal(relation.FirstWrestlerId, relation.SecondWrestlerId) >= 0 || !pairs.Add(Pair(relation.FirstWrestlerId, relation.SecondWrestlerId)) ||
                    !float.IsFinite(relation.Affinity) || relation.Affinity is < -100 or > 100 || relation.Interactions < 1 || !relation.LastInteractionDate.HasValue)
                    throw new InvalidOperationException("실제 선수 관계가 올바르지 않습니다.");
                CheckDate(relation.LastInteractionDate.Value, save.CurrentDate);
            }
            var contacts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in save.LockerRoomEvents ?? new())
            {
                if (entry == null || !EntityId.IsValidRuntimeId(entry.Id) || !ids.Add(entry.Id) || save.FindPromotion(entry.PromotionId) == null ||
                    !people.Contains(entry.ActorId) || !people.Contains(entry.PartnerId) || entry.ActorId == entry.PartnerId ||
                    !Enum.IsDefined(typeof(LockerRoomEventKind), entry.Kind) || !Enum.IsDefined(typeof(LockerRoomWorkKind), entry.WorkKind) ||
                    string.IsNullOrWhiteSpace(entry.Description) || string.IsNullOrEmpty(entry.SourceKey) || !pairs.Contains(Pair(entry.ActorId, entry.PartnerId)))
                    throw new InvalidOperationException("라커룸 관찰 근거가 올바르지 않습니다.");
                CheckDate(entry.Date, save.CurrentDate);
                if (entry.WorkKind == LockerRoomWorkKind.Show)
                {
                    var show = save.Shows.SingleOrDefault(s => s?.Id == entry.ShowId);
                    if (show == null || !show.Date.Equals(entry.Date) || entry.SourceKey != $"show:{show.Id}:{show.ShowVersion}")
                        throw new InvalidOperationException("라커룸 공동 출연 근거가 없습니다.");
                }
                else if (!string.IsNullOrEmpty(entry.ShowId) || entry.SourceKey != "training:" + DateKey(entry.Date))
                    throw new InvalidOperationException("라커룸 공동 훈련 근거가 없습니다.");
                if (entry.Kind is LockerRoomEventKind.Contact or LockerRoomEventKind.Conflict &&
                    !contacts.Add(entry.PromotionId + ":" + entry.SourceKey + ":" + Pair(entry.ActorId, entry.PartnerId)))
                    throw new InvalidOperationException("같은 공동 작업의 교류가 중복되었습니다.");
                if (entry.Kind == LockerRoomEventKind.Mediation && (!people.Contains(entry.MediatorId) || entry.MediatorId == entry.ActorId || entry.MediatorId == entry.PartnerId) ||
                    entry.Kind != LockerRoomEventKind.Mediation && !string.IsNullOrEmpty(entry.MediatorId))
                    throw new InvalidOperationException("라커룸 중재자가 올바르지 않습니다.");
                if (entry.Kind == LockerRoomEventKind.OpinionShared && !(save.WrestlerBehaviors ?? new()).Any(s => s.PromotionId == entry.PromotionId &&
                    s.WrestlerId == entry.ActorId && s.Events.Any(e => e.Id == entry.OriginBehaviorEventId && e.Kind == WrestlerBehaviorEventKind.Request && e.Date.CompareTo(entry.Date) <= 0)) ||
                    entry.Kind != LockerRoomEventKind.OpinionShared && !string.IsNullOrEmpty(entry.OriginBehaviorEventId))
                    throw new InvalidOperationException("공유된 의견의 원래 사건이 없습니다.");
            }
        }
        private static void CheckDate(GameDate date, GameDate current)
        {
            try { _ = new DateTime(date.Year, date.Month, date.Day); }
            catch (ArgumentOutOfRangeException) { throw new InvalidOperationException("라커룸 날짜가 올바르지 않습니다."); }
            if (date.CompareTo(current) > 0) throw new InvalidOperationException("라커룸 날짜가 미래입니다.");
        }
    }
}
