using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PWManager.Domain.Identifiers;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public sealed class WrestlerGrowthService
    {
        private static readonly FieldInfo[] Attributes = Enum.GetNames(typeof(WrestlerAttribute))
            .Select(x => typeof(WrestlerAttributesState).GetField(x)).ToArray();
        private static readonly FieldInfo[] Progress = Enum.GetNames(typeof(WrestlerAttribute))
            .Select(x => typeof(AttributeProgressState).GetField(x)).ToArray();
        private readonly Func<string> createId;
        private readonly Random random;

        public WrestlerGrowthService(Func<string> createId = null, Random random = null)
        {
            this.createId = createId ?? EntityId.CreateRuntimeId; this.random = random;
        }

        public sealed class GrowthPlan
        {
            private readonly GameSave target, prepared;
            private bool applied;
            public IReadOnlyList<string> NewRecordIds { get; }
            internal GrowthPlan(GameSave target, GameSave prepared)
            {
                this.target = target; this.prepared = prepared;
                NewRecordIds = prepared.ExperienceRecords.Skip(target.ExperienceRecords?.Count ?? 0).Select(x => x.Id)
                    .Concat(prepared.GrowthQuarterResults.Skip(target.GrowthQuarterResults?.Count ?? 0).Select(x => x.Id)).ToList();
            }
            public void Apply()
            {
                if (applied) return;
                foreach (var w in target.Wrestlers)
                {
                    var next = prepared.Wrestlers.Single(x => x.Id == w.Id);
                    w.Attributes = next.Attributes; w.Growth = next.Growth;
                }
                target.ExperienceRecords = prepared.ExperienceRecords;
                target.GrowthQuarterResults = prepared.GrowthQuarterResults;
                target.LastGrowthQuarterEnd = prepared.LastGrowthQuarterEnd; applied = true;
            }
        }

        private static GameSave Copy(GameSave save)
        {
            ValidateState(save);
            var copy = save.CopyForGrowthSimulation();
            copy.ExperienceRecords = (save.ExperienceRecords ?? new()).Select(x => x.Copy()).ToList();
            copy.GrowthQuarterResults = new(save.GrowthQuarterResults ?? new());
            return copy;
        }

        public GrowthPlan PrepareShow(ResultApplicationContext context, IReadOnlyDictionary<string, WrestlerMomentumState> projectedMomentum = null)
        {
            context.EnsureNotApplied();
            var save = context.Save; var copy = Copy(save);
            ProjectMomentum(copy, projectedMomentum);
            if (save.IsCancelledShow(context.Show)) throw new InvalidOperationException("취소된 쇼에는 경험을 반영할 수 없습니다.");
            var conditionChanges = context.MatchResults.SelectMany(x => x.WrestlerConditionChanges ?? new()).ToList();
            foreach (var changes in conditionChanges.GroupBy(x => x.WrestlerId))
            {
                var w = copy.Wrestlers.Single(x => x.Id == changes.Key);
                var delta = changes.Sum(x => x.ConditionDelta);
                if (!float.IsFinite(delta)) throw new InvalidOperationException("쇼의 체력 변화가 올바르지 않습니다.");
                w.Condition.Condition = Math.Clamp(w.Condition.Condition + delta, 0, 100);
            }
            foreach (var result in context.MatchResults.OrderBy(x => context.Show.TimelineEventIds.IndexOf(x.ShowEventId)))
                MatchHealth.ApplyResult(copy, result, context.Show.Date);
            foreach (var result in context.MatchResults.Where(x => string.IsNullOrEmpty(x.CancellationReason)))
            {
                var main = (string.IsNullOrEmpty(context.Show.MainEventId) ? context.Show.TimelineEventIds.LastOrDefault() : context.Show.MainEventId) == result.ShowEventId;
                var xp = 10 + (main ? 5 : 0) + (result.CriticReview?.DisplayedStars >= 4 ? 5 : 0) +
                    (main && context.Show.ShowType != ScheduledShowType.Regular ? 10 : 0);
                foreach (var id in result.ParticipantProfiles.SelectMany(x => x.MemberIds).Distinct())
                {
                    var w = copy.Wrestlers.Single(x => x.Id == id);
                    var progress = MatchProgress(result, w, xp);
                    AddExperience(copy, w, result.Id, context.Show.Date, ExperienceActivity.Match, xp, progress);
                }
            }
            foreach (var result in context.PromoResults.Where(x => string.IsNullOrEmpty(x.CancellationReason)))
            {
                var promo = save.PromoPlans.Single(x => x.Id == result.PromoPlanId);
                foreach (var id in promo.ParticipantIds.Distinct())
                {
                    var w = copy.Wrestlers.Single(x => x.Id == id);
                    var usage = result.ParticipantUsage?.SingleOrDefault(x => x.WrestlerId == id);
                    var alignment = usage?.Alignment ?? w.Roster.Alignment;
                    var disposition = usage?.Disposition ?? w.Presentation.PromoDisposition;
                    var spoken = promo.Presentation is PromoPresentation.InRingMic or PromoPresentation.Interview or PromoPresentation.BackstageConversation;
                    var p = new AttributeProgressState { Charisma = 3, Acting = spoken ? 1.5f : 3,
                        MicWork = spoken ? 3 : 0, Improvisation = spoken ? 2 : 0,
                        FaceWork = alignment == KayfabeAlignment.Face ? 3 : 0,
                        HeelWork = alignment == KayfabeAlignment.Heel ? 3 : 0,
                        Comedy = disposition == PromoDisposition.Comic ? 3 : 0 };
                    AddExperience(copy, w, result.Id, context.Show.Date, ExperienceActivity.Promo, 3, p);
                }
            }
            if (context.Show.Date.Equals(save.CurrentDate)) SettleDue(copy, new HashSet<string> { context.Show.Id });
            return Finish(save, copy);
        }

        public GrowthPlan PrepareDue(GameSave save, IEnumerable<string> ignoredShowIds = null, IReadOnlyDictionary<string, WrestlerMomentumState> projectedMomentum = null)
        {
            var copy = Copy(save); ProjectMomentum(copy, projectedMomentum);
            SettleDue(copy, new HashSet<string>(ignoredShowIds ?? Array.Empty<string>()));
            return Finish(save, copy);
        }

        public GrowthPlan PrepareTraining(GameSave save, TrainingWeekState week)
        {
            var copy = Copy(save);
            foreach (var result in week.Results.Where(x => x.TrainingDays > 0))
            {
                var w = copy.Wrestlers.Single(x => x.Id == result.WrestlerId);
                AddExperience(copy, w, week.Id, week.EndDate, ExperienceActivity.Training, result.Experience, result.Progress.Copy());
            }
            return Finish(save, copy);
        }

        private static void ProjectMomentum(GameSave save, IReadOnlyDictionary<string, WrestlerMomentumState> projected)
        {
            if (projected == null) return;
            foreach (var w in save.Wrestlers) w.Momentum = projected[w.Id].Copy();
        }

        private GrowthPlan Finish(GameSave save, GameSave copy)
        {
            var plan = new GrowthPlan(save, copy);
            ValidateNewIds(save, plan.NewRecordIds);
            ValidateState(copy);
            return plan;
        }

        private void AddExperience(GameSave save, WrestlerState w, string source, GameDate date,
            ExperienceActivity activity, float xp, AttributeProgressState p)
        {
            if (save.ExperienceRecords.Any(x => x.WrestlerId == w.Id && x.SourceResultId == source))
                throw new InvalidOperationException("이 활동의 경험은 이미 반영되었습니다.");
            if (save.LastGrowthQuarterEnd.HasValue && date.CompareTo(save.LastGrowthQuarterEnd.Value) <= 0)
                throw new InvalidOperationException("이미 정산한 분기의 활동은 반영할 수 없습니다.");
            var record = new WrestlerExperienceState { Id = createId(), WrestlerId = w.Id, SourceResultId = source,
                Date = date, Activity = activity, BaseExperience = xp, Progress = p };
            save.ExperienceRecords.Add(record); w.Growth.ExperienceLogIds.Add(record.Id);
            for (var i = 0; i < 17; i++) Set(w.Growth.GrowthProgress, i, Get(w.Growth.GrowthProgress, i) + Get(p, i));
        }

        private static AttributeProgressState MatchProgress(MatchResultState result, WrestlerState w, float xp)
        {
            var p = new AttributeProgressState();
            var events = result.EngineState?.Events;
            var actions = events?.Where(x => x.PerformerId == w.Id && x.Type is not (MatchActionType.Rest or MatchActionType.Recover or MatchActionType.MedicalStop or MatchActionType.ExecutionStop)).ToList();
            var received = events?.Count(x => (string.IsNullOrEmpty(x.SellingWrestlerId) ? x.IsPhysicalAttack && x.ReceiverId == w.Id : x.SellingWrestlerId == w.Id));
            var actual = events == null || (actions?.Count ?? 0) > 0 || received > 0;
            if (!actual) return p;
            p.RingPsychology = xp * .5f; p.RingImprovisation = xp * .25f;
            p.Stamina = xp * Math.Min(1.5f, Math.Max(.25f, result.ActualMatchDuration / 20f));
            if (events == null)
            {
                // Old results lack action evidence; award a limited style routine instead of inventing all skills.
                switch (w.Presentation.WrestlingStyleId)
                {
                    case "style_004": case "style_005": p.HighFlying = xp * .5f; break;
                    case "style_003": p.Technical = xp * .5f; break;
                    case "style_002": case "style_006": p.Power = xp * .5f; break;
                    default: p.Brawling = xp * .5f; break;
                }
                p.Selling = xp * .25f;
            }
            else
            {
                foreach (var action in actions)
                {
                    var skill = ActionSkill(action.Type);
                    if (!skill.HasValue && action.Type is MatchActionType.Signature or MatchActionType.Finisher or MatchActionType.Finish)
                    {
                        var style = result.WrestlerPerformances?.FirstOrDefault(x => x.WrestlerId == w.Id)?.WrestlingStyleId ??
                            result.EngineState.Participants.FirstOrDefault(x => x.Id == w.Id)?.WrestlingStyleId;
                        skill = style switch { "style_002" or "style_006" => WrestlerAttribute.Power,
                            "style_003" => WrestlerAttribute.Technical, "style_004" or "style_005" => WrestlerAttribute.HighFlying, _ => WrestlerAttribute.Brawling };
                    }
                    if (skill.HasValue) Set(p, (int)skill.Value, Get(p, (int)skill.Value) + xp / Math.Max(1, actions.Count));
                }
                p.Selling = received > 0 ? xp * Math.Min(1f, received.Value / (float)Math.Max(1, events.Count(x => x.IsPhysicalAttack || !string.IsNullOrEmpty(x.SellingWrestlerId)))) : 0;
            }
            if (result.SpotResults?.Any(x => x.WasApplied && !x.WasSkipped && (x.ActorId == w.Id || x.TargetId == w.Id || x.PartnerId == w.Id)) == true) p.SpotWork = xp * .5f;
            if (result.EngineState?.Rules?.Objective is MatchObjective.Escape or MatchObjective.Retrieval or MatchObjective.Table ||
                events?.Any(x => x.PerformerId == w.Id && x.Type is MatchActionType.ObjectClimb or MatchActionType.ObjectFinish or MatchActionType.ObjectSetup) == true)
                p.SpecialtyMatches = xp * .5f;
            var bonus = result.WrestlerPerformances?.SingleOrDefault(x => x.WrestlerId == w.Id);
            if (bonus != null)
            {
                foreach (var value in new[] { bonus.BrawlingGrowthBonus, bonus.PowerGrowthBonus, bonus.HighFlyingGrowthBonus, bonus.TechnicalGrowthBonus })
                    if (!float.IsFinite(value) || value < 0 || value > 1) throw new InvalidOperationException("스타일 성장 보너스가 올바르지 않습니다.");
                p.Brawling *= 1 + bonus.BrawlingGrowthBonus; p.Power *= 1 + bonus.PowerGrowthBonus;
                p.HighFlying *= 1 + bonus.HighFlyingGrowthBonus; p.Technical *= 1 + bonus.TechnicalGrowthBonus;
            }
            return p;
        }

        private static WrestlerAttribute? ActionSkill(MatchActionType action) => action switch
        {
            MatchActionType.BasicStrike or MatchActionType.HeavyStrike or MatchActionType.GroundAttack or MatchActionType.RunningAttack or
                MatchActionType.CornerAttack or MatchActionType.RingsideAttack or MatchActionType.DoubleTeam => WrestlerAttribute.Brawling,
            MatchActionType.Lift or MatchActionType.Grapple => WrestlerAttribute.Power,
            MatchActionType.Submission or MatchActionType.Chain or MatchActionType.TieUp or MatchActionType.Counter or
                MatchActionType.Hold or MatchActionType.StandingHold or MatchActionType.LimbAttack => WrestlerAttribute.Technical,
            MatchActionType.Dive or MatchActionType.Climb or MatchActionType.TopRopeAttack => WrestlerAttribute.HighFlying,
            _ => null
        };

        private void SettleDue(GameSave save, HashSet<string> ignored)
        {
            var last = save.LastGrowthQuarterEnd;
            if (!last.HasValue)
            {
                // A missing checkpoint has no safe historical start date.
                save.LastGrowthQuarterEnd = LatestQuarterEnd(save.CurrentDate, false); last = save.LastGrowthQuarterEnd;
            }
            var due = LatestQuarterEnd(save.CurrentDate, true);
            if (!due.HasValue) return;
            var end = last.HasValue ? NextQuarterEnd(last.Value) : due.Value;
            while (end.CompareTo(due.Value) <= 0)
            {
                if (end.Equals(save.CurrentDate) && save.Shows.Any(x => x.Date.Equals(end) && x.Status != ShowStatus.Completed && !save.IsCancelledShow(x) && !ignored.Contains(x.Id))) return;
                foreach (var w in save.Wrestlers.OrderBy(x => x.Id, StringComparer.Ordinal))
                {
                    if (save.Promotion != null && !string.IsNullOrEmpty(w.PromotionId) && w.PromotionId != save.Promotion.Id) continue;
                    if (save.ScoutCandidates.Any(x => x.WrestlerId == w.Id)) continue;
                    if (w.Identity.CreatedDate.Year > 0 && w.Identity.CreatedDate.CompareTo(end) > 0) continue;
                    SettleQuarter(save, w, end);
                }
                save.LastGrowthQuarterEnd = new(end);
                if (end.Year == 9999 && end.Month == 11) break;
                end = NextQuarterEnd(end);
            }
        }

        private void SettleQuarter(GameSave save, WrestlerState w, GameDate end)
        {
            if (save.GrowthQuarterResults.Any(x => x.WrestlerId == w.Id && x.EndDate.Equals(end)))
                throw new InvalidOperationException("분기 성장 이력이 체크포인트와 일치하지 않습니다.");
            var rng = random ?? new Random(StableSeed(save.WorldSeed, w.Id, end));
            var age = w.Identity.BirthDate.AgeOn(end);
            var stage = Stage(age);
            var result = new GrowthQuarterResultState { Id = createId(), WrestlerId = w.Id,
                StartDate = QuarterStart(end), EndDate = end, Stage = stage };
            var future = save.ExperienceRecords.Where(x => x.WrestlerId == w.Id && !x.SettledQuarter.HasValue && x.Date.CompareTo(end) > 0).ToList();
            for (var i = 0; i < 17; i++)
            {
                var reserved = future.Sum(x => Get(x.Progress, i));
                var available = Get(w.Growth.GrowthProgress, i) - reserved;
                if (available < -.001f) throw new InvalidOperationException("누적 경험이 미정산 기록보다 적습니다.");
                Set(result.ConsumedProgress, i, Math.Max(0, available)); Set(w.Growth.GrowthProgress, i, reserved);
            }
            var indices = Enumerable.Range(0, 17).ToArray();
            for (var i = indices.Length - 1; i > 0; i--) { var j = rng.Next(i + 1); (indices[i], indices[j]) = (indices[j], indices[i]); }
            foreach (var i in indices)
            {
                var chance = Get(result.ConsumedProgress, i) * 2 * Efficiency(w, age, i < 10);
                var before = (float)Attributes[i].GetValue(w.Attributes);
                var budget = i < 10 ? w.Growth.MatchPotentialCap * 10 - WrestlerOverallCalculator.MatchTotal(w.Attributes) :
                    w.Growth.PromoPotentialCap * 7 - WrestlerOverallCalculator.PromoTotal(w.Attributes);
                if (before >= 20 || budget <= 0 || chance <= 0 || rng.NextDouble() * 100 >= Math.Min(95, chance)) continue;
                var delta = Math.Min(Math.Min((float)Math.Round(.1 + rng.NextDouble() * .2, 2), budget), 20 - before);
                Change(w, result, i, before + delta, AttributeChangeCause.Growth);
            }
            if (end.Month == 5)
            {
                const int start = 37;
                if (age >= start)
                foreach (var attr in new[] { WrestlerAttribute.Power, WrestlerAttribute.HighFlying, WrestlerAttribute.Stamina })
                    if (rng.NextDouble() * 100 < Math.Min(90, 25 + (age - start) * 4))
                    {
                        var loss = Math.Min(1f, .1f + (age - start) * .02f + (float)rng.NextDouble() * .15f);
                        var before = (float)Attributes[(int)attr].GetValue(w.Attributes);
                        if (before <= 1) continue;
                        Change(w, result, (int)attr, Math.Max(1, (float)Math.Round(before - loss, 2)), AttributeChangeCause.Aging);
                    }
            }
            foreach (var record in save.ExperienceRecords.Where(x => x.WrestlerId == w.Id && !x.SettledQuarter.HasValue && x.Date.CompareTo(end) <= 0)) record.SettledQuarter = new(end);
            w.Growth.AgingState = stage; w.Growth.AttributeHistoryIds.Add(result.Id); save.GrowthQuarterResults.Add(result);
        }

        private static void Change(WrestlerState w, GrowthQuarterResultState result, int i, float after, AttributeChangeCause cause)
        {
            var before = (float)Attributes[i].GetValue(w.Attributes);
            if (Math.Abs(before - after) < .000001f) return;
            Attributes[i].SetValue(w.Attributes, after);
            result.Changes.Add(new() { Attribute = (WrestlerAttribute)i, Before = before, After = after, Cause = cause });
        }

        private static float Efficiency(WrestlerState w, int age, bool match)
        {
            var factor = !match || age == 0 ? 1 : age < 24 ? 1.4f : age <= 30 ? 1 : age <= 36 ? .35f : .15f;
            var injury = w.Condition.InjuryStatus switch { InjuryStatus.Minor => .8f, InjuryStatus.Moderate => .5f, InjuryStatus.Severe => .2f, _ => 1 };
            return factor * Math.Clamp(w.Condition.Condition / 100, .25f, 1) * injury * (1 + Math.Clamp(w.Momentum.Momentum / 1000, -.1f, .1f));
        }

        public static AgingState Stage(int age) => age is > 0 and < 24 ? AgingState.Young : age < 31 ? AgingState.Prime : age < 37 ? AgingState.Veteran : AgingState.Declining;
        public static OptionalGameDate LatestQuarterEnd(GameDate date, bool inclusive)
        {
            var day = new DateTime(date.Year, date.Month, date.Day);
            var month = date.Month;
            while (month > 0)
            {
                if (month is 2 or 5 or 8 or 11)
                {
                    var end = new DateTime(date.Year, month, DateTime.DaysInMonth(date.Year, month));
                    if (end < day || inclusive && end == day) return new(new GameDate(end.Year, end.Month, end.Day));
                }
                month--;
            }
            return date.Year > 1 ? new(new GameDate(date.Year - 1, 11, 30)) : default;
        }
        private static GameDate NextQuarterEnd(GameDate end)
        {
            var next = new DateTime(end.Year, end.Month, 1).AddMonths(3);
            return new(next.Year, next.Month, DateTime.DaysInMonth(next.Year, next.Month));
        }
        private static GameDate QuarterStart(GameDate end)
        {
            var start = new DateTime(end.Year, end.Month, 1).AddMonths(-2);
            return new(start.Year, start.Month, 1);
        }
        private static int StableSeed(int worldSeed, string id, GameDate end)
        {
            unchecked { var hash = worldSeed ^ 0x36D9821; foreach (var c in $"{id}:{end.Year}:{end.Month}") hash = hash * 31 + c; return hash; }
        }
        private static float Get(AttributeProgressState p, int i) => (float)Progress[i].GetValue(p);
        private static void Set(AttributeProgressState p, int i, float value) => Progress[i].SetValue(p, value);

        internal static void ValidateNewIds(GameSave save, IEnumerable<string> generated)
        {
            var ids = new HashSet<string>(ExistingIds(save), StringComparer.Ordinal);
            foreach (var id in generated)
                if (!EntityId.IsValidRuntimeId(id) || !ids.Add(id)) throw new InvalidOperationException("성장 기록 ID가 중복되거나 올바르지 않습니다.");
        }

        private static IEnumerable<string> ExistingIds(GameSave s) =>
            s.Wrestlers.Select(x => x.Id).Concat(s.Managers.Select(x => x.Id)).Concat(s.Contracts.Select(x => x.Id))
            .Concat(s.Shows.Select(x => x.Id)).Concat(s.Schedules.Select(x => x.Id)).Concat(s.VenueContracts.Select(x => x.Id))
            .Concat(s.ShowEvents.Select(x => x.Id)).Concat(s.MatchPlans.Select(x => x.Id)).Concat(s.PromoPlans.Select(x => x.Id))
            .Concat(s.MatchResults.Select(x => x.Id)).Concat(s.PromoResults.Select(x => x.Id)).Concat(s.ShowResults.Select(x => x.Id))
            .Concat(s.Transactions.Select(x => x.Id)).Concat(s.InboxMessages.Select(x => x.Id)).Concat(s.SalaryPayments.Select(x => x.Id))
            .Concat(s.ContractNegotiations.Select(x => x.Id)).Concat(s.ContractTerminations.Select(x => x.Id))
            .Concat(s.TagTeams.Select(x => x.Id)).Concat(s.Stables.Select(x => x.Id)).Concat(s.Championships.Select(x => x.Id))
            .Concat(s.SeasonResults.Select(x => x.Id)).Concat(s.SeasonResults.Select(x => x.Policy.Id))
            .Concat((s.ExperienceRecords ?? new()).Select(x => x.Id)).Concat((s.GrowthQuarterResults ?? new()).Select(x => x.Id))
            .Concat((s.CareerEvents ?? new()).Select(x => x.Id))
            .Concat(WrestlerBehaviorService.RecordIds(s))
            .Concat((s.Training?.History ?? new()).Select(x => x.Id))
            .Concat(new[] { s.Promotion?.Id, s.SeasonPolicy?.Id });

        public static void ValidateState(GameSave save)
        {
            if (save.LastGrowthQuarterEnd.HasValue && (!IsQuarterEnd(save.LastGrowthQuarterEnd.Value) || save.LastGrowthQuarterEnd.Value.CompareTo(save.CurrentDate) > 0))
                throw new InvalidOperationException("성장 체크포인트가 올바르지 않습니다.");
            var ids = new HashSet<string>(); var sources = new HashSet<string>(); var quarters = new HashSet<string>();
            foreach (var w in save.Wrestlers ?? new()) CheckProgress(w?.Growth?.GrowthProgress);
            foreach (var record in save.ExperienceRecords ?? new())
            {
                var w = save.Wrestlers.SingleOrDefault(x => x.Id == record?.WrestlerId);
                if (record == null || !EntityId.IsValidRuntimeId(record.Id) || !ids.Add(record.Id) || w == null ||
                    !w.Growth.ExperienceLogIds.Contains(record.Id) || !sources.Add($"{record.WrestlerId}:{record.SourceResultId}") ||
                    !float.IsFinite(record.BaseExperience) || record.BaseExperience < 0 || !Enum.IsDefined(typeof(ExperienceActivity), record.Activity) ||
                    !(record.Activity switch { ExperienceActivity.Match => save.MatchResults.Any(x => x.Id == record.SourceResultId),
                        ExperienceActivity.Promo => save.PromoResults.Any(x => x.Id == record.SourceResultId),
                        ExperienceActivity.Training => save.Training?.History?.Any(x => x != null && x.Id == record.SourceResultId && x.EndDate.Equals(record.Date) &&
                            x.Results?.Any(r => r != null && r.WrestlerId == record.WrestlerId && r.TrainingDays > 0) == true) == true, _ => false }) ||
                    !ValidDate(record.Date) || record.SettledQuarter.HasValue && (!IsQuarterEnd(record.SettledQuarter.Value) || record.Date.CompareTo(record.SettledQuarter.Value) > 0 ||
                        !save.LastGrowthQuarterEnd.HasValue || record.SettledQuarter.Value.CompareTo(save.LastGrowthQuarterEnd.Value) > 0))
                    throw new InvalidOperationException("선수 경험 기록이 올바르지 않습니다.");
                CheckProgress(record.Progress);
            }
            foreach (var result in save.GrowthQuarterResults ?? new())
            {
                var w = save.Wrestlers.SingleOrDefault(x => x.Id == result?.WrestlerId);
                if (result == null || !EntityId.IsValidRuntimeId(result.Id) || !ids.Add(result.Id) || w == null || !w.Growth.AttributeHistoryIds.Contains(result.Id) ||
                    !quarters.Add($"{result.WrestlerId}:{result.EndDate.Year}:{result.EndDate.Month}") || !IsQuarterEnd(result.EndDate) ||
                    !result.StartDate.Equals(QuarterStart(result.EndDate)) || !Enum.IsDefined(typeof(AgingState), result.Stage) ||
                    !save.LastGrowthQuarterEnd.HasValue || result.EndDate.CompareTo(save.LastGrowthQuarterEnd.Value) > 0 || result.Changes == null)
                    throw new InvalidOperationException("분기 성장 이력이 올바르지 않습니다.");
                CheckProgress(result.ConsumedProgress);
                foreach (var c in result.Changes)
                    if (c == null || !Enum.IsDefined(typeof(WrestlerAttribute), c.Attribute) || !Enum.IsDefined(typeof(AttributeChangeCause), c.Cause) ||
                        !float.IsFinite(c.Before) || !float.IsFinite(c.After) || c.Before < 1 || c.Before > 20 || c.After < 1 || c.After > 20 ||
                        (c.Cause == AttributeChangeCause.Growth ? c.After <= c.Before : c.After >= c.Before || result.EndDate.Month != 5 ||
                            c.Attribute is not (WrestlerAttribute.Power or WrestlerAttribute.HighFlying or WrestlerAttribute.Stamina)))
                        throw new InvalidOperationException("능력 변화 이력이 올바르지 않습니다.");
            }
        }
        private static void CheckProgress(AttributeProgressState p)
        {
            if (p == null || Enumerable.Range(0, 17).Any(i => !float.IsFinite(Get(p, i)) || Get(p, i) < 0))
                throw new InvalidOperationException("능력별 경험은 0 이상의 유한수여야 합니다.");
        }
        private static bool ValidDate(GameDate date) { try { _ = new DateTime(date.Year, date.Month, date.Day); return true; } catch (ArgumentOutOfRangeException) { return false; } }
        private static bool IsQuarterEnd(GameDate date) => ValidDate(date) && date.Month is 2 or 5 or 8 or 11 && date.Day == DateTime.DaysInMonth(date.Year, date.Month);
    }
}
