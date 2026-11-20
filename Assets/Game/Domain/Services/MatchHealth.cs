using System;
using System.Linq;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public static class MatchHealth
    {
        public static bool Restricted(MatchEngineParticipant wrestler, BodyRegion region) =>
            wrestler.RealInjury >= InjuryStatus.Moderate &&
            (wrestler.RealInjury == InjuryStatus.Severe || wrestler.RealInjuryRegion == region);

        public static bool CanUse(MatchMoveRules move, MatchEngineParticipant actor)
        {
            if (actor.RealInjury == InjuryStatus.Severe) return false;
            if (move == null || actor.RealInjury < InjuryStatus.Moderate) return true;
            var kind = MatchPhysics.Kind(move);
            if (Restricted(actor, BodyRegion.Head)) return false;
            if (Restricted(actor, BodyRegion.Legs) && (kind == MoveKind.Aerial || move.ExecutionStyle == MoveExecutionStyle.Running)) return false;
            return !(kind is MoveKind.Grapple or MoveKind.Submission &&
                (Restricted(actor, BodyRegion.Arms) || Restricted(actor, BodyRegion.Body)));
        }

        public static bool CanReceive(MatchMoveRules move, MatchEngineParticipant target) =>
            target.RealInjury != InjuryStatus.Severe && (move == null || target.RealInjury < InjuryStatus.Moderate ||
            move.TargetRegion != target.RealInjuryRegion && !(MatchPhysics.Kind(move) is MoveKind.Aerial or MoveKind.Grapple &&
            target.RealInjuryRegion is BodyRegion.Body or BodyRegion.Head));

        public static void ApplyInjury(MatchEngineState state, MatchEngineParticipant target, InjuryStatus severity, BodyRegion region)
        {
            if (!Enum.IsDefined(typeof(InjuryStatus), severity) || severity == InjuryStatus.None ||
                !Enum.IsDefined(typeof(BodyRegion), region) || !state.Participants.Contains(target))
                throw new ArgumentException("Invalid actual injury.");
            if (severity > target.RealInjury) { target.RealInjury = severity; target.RealInjuryRegion = region; }
            target.InjuredThisMatch = true;
            if (severity == InjuryStatus.Severe)
            {
                state.WasStoppedByInjury = true;
                state.MedicalStopWrestlerId = target.Id;
                state.Exchange = null;
                state.IsFinished = false;
            }
            else if (severity == InjuryStatus.Moderate && state.Booking != null && state.Booking.FinishType != MatchFinishType.Draw)
            {
                state.RescueRequired = true; state.RescuePrepared = false;
                state.RescueDeadline = state.ElapsedSeconds + 90;
                state.BookingSequence = true; state.Exchange = null;
                foreach (var p in state.Participants)
                {
                    p.ReplacedFinishMoveId = p.PlannedFinisherMoveId ?? p.FinisherMoveId ?? p.FinisherMoveIds.FirstOrDefault();
                    p.PlannedFinisherMoveId = null;
                }
            }
        }

        public static void Observe(MatchEngineState state, MatchEngineEvent e, Random random)
        {
            if (!state.AccidentsEnabled || !e.IsPhysicalAttack || e.Result == MatchActionResult.Countered) return;
            var a = state.GetParticipant(e.PerformerId); var b = state.GetParticipant(e.ReceiverId);
            var move = MatchTechniques.Find(e.TechniqueId)?.Rules ?? (string.IsNullOrEmpty(e.MoveId) ? null : state.FindMove?.Invoke(e.MoveId));
            bool aerial = e.Type is MatchActionType.Dive or MatchActionType.TopRopeAttack || move != null && MatchPhysics.Kind(move) == MoveKind.Aerial;
            var contactChance = Math.Clamp(.95f + (b.Attributes.Technical - e.ContactDifficulty) * .004f - b.Fatigue * .0005f, .5f, .99f);
            bool contactFault = random.NextDouble() >= contactChance;
            bool risky = aerial || move?.Situation == MoveSituation.Table ||
                move != null && MatchPhysics.Kind(move) == MoveKind.Grapple && move.ExecutionDifficulty >= 15;
            if (!e.PhysicalExecutionFailed && !contactFault && !risky) return;
            var victim = e.PhysicalExecutionFailed && aerial ? a : contactFault || risky && !e.PhysicalExecutionFailed ? b : a;
            var probability = (e.PhysicalExecutionFailed || contactFault ? .01 : .0001) * (1 + victim.Fatigue / 100);
            probability *= WrestlerHiddenRules.InjuryRiskMultiplier(victim.InjuryProneness);
            if (victim == b) probability *= WrestlerHiddenRules.OpponentProtectionMultiplier(a.Collegiality);
            if (victim.RealInjury != InjuryStatus.None) probability *= state.Rules.InjuryAggravationMultiplier;
            if (random.NextDouble() >= probability) return;
            var roll = random.NextDouble();
            var severity = roll < .8 ? InjuryStatus.Minor : roll < .98 ? InjuryStatus.Moderate : InjuryStatus.Severe;
            var region = victim == a && aerial ? BodyRegion.Body : e.TargetRegion;
            ApplyInjury(state, victim, severity, region);
            e.InjuredWrestlerId = victim.Id; e.InjurySeverity = severity; e.InjuryRegion = region;
            e.IsMajorMoment = true;
        }

        public static void RecordStop(MatchEngineState state)
        {
            var before = MatchEngineSnapshot.Capture(state);
            state.IsFinished = true; state.IsAborted = false; state.WinnerId = null;
            state.ActualFinish = MatchFinishType.Draw; state.Phase = MatchPhase.Finish; state.Exchange = null;
            var victim = state.GetParticipant(state.MedicalStopWrestlerId);
            var stop = new MatchEngineEvent
            {
                Type = MatchActionType.MedicalStop, Result = MatchActionResult.Interrupted,
                ReceiverId = victim.Id, InjuredWrestlerId = victim.Id, InjurySeverity = InjuryStatus.Severe,
                InjuryRegion = victim.RealInjuryRegion, MatchTimeSeconds = state.ElapsedSeconds,
                Phase = MatchPhase.Finish, DurationSeconds = 1, ExchangeId = ++state.NextExchangeId,
                CauseEventIndex = state.Events.Count - 1, Before = before, After = MatchEngineSnapshot.Capture(state),
                IsMajorMoment = true, CommentaryKey = "match.medical-stop"
            };
            MatchMovement.Apply(state, stop, before);
            stop.After = MatchEngineSnapshot.Capture(state);
            state.Events.Add(stop);
            state.ElapsedSeconds++;
        }

        public static bool IsPreparation(MatchActionType type) => type is MatchActionType.Lift or MatchActionType.TurnOver or
            MatchActionType.Climb or MatchActionType.Reposition or MatchActionType.PrepareAttack or MatchActionType.Retreat;

        public static float PinRisk(MatchEngineState state, MatchEngineParticipant a, MatchEngineParticipant b)
        {
            var decay = (float)Math.Exp(-Math.Max(0, state.ElapsedSeconds - b.LastImpactTime) / 18d);
            return ActionResolver.PinfallChance(state, a, b, b.LastImpact * decay, b.LastImpactWasFinisher && decay > .4f);
        }

        public static bool LegalCover(MatchEngineState state, MatchEngineParticipant a, MatchEngineParticipant b) =>
            state.Rules.Pinfall && a.IsLegal && b.IsLegal && !a.IsEliminated && !b.IsEliminated &&
            MatchPhysics.InRing(a) && MatchPhysics.InRing(b) && b.IsDowned &&
            state.RefereeUnavailableUntil <= state.ElapsedSeconds && (!state.Rules.RopeBreak || !MatchMovement.CanReachRopes(b));

        public static void RecordEvidence(MatchEngineState state, MatchEngineEvent e, MatchEngineSnapshot before)
        {
            if (string.IsNullOrEmpty(e.PerformerId) || string.IsNullOrEmpty(e.ReceiverId)) return;
            var a = state.Participants.FirstOrDefault(p => p.Id == e.PerformerId);
            var b = state.Participants.FirstOrDefault(p => p.Id == e.ReceiverId);
            if (a == null || b == null) return;
            var prior = before.Participants.First(p => p.Id == b.Id);
            var priorDamage = a.TargetRegion switch { BodyRegion.Head => prior.HeadDamage, BodyRegion.Arms => prior.ArmDamage,
                BodyRegion.Legs => prior.LegDamage, _ => prior.BodyDamage };
            e.GoalMoveId = a.PlannedFinisherMoveId; e.GoalRegion = a.TargetRegion;
            e.TargetCompleted = priorDamage >= 60;
            e.IsPreparation = IsPreparation(e.Type);
            e.PreparationAvailable = e.TargetCompleted && !a.IsDowned && MatchPhysics.Actions(state, a, b).Any(x => IsPreparation(x.Type));
            e.HasDecisionEvidence = true;
            var id = e.TechniqueId ?? e.MoveId;
            e.ContextualChoice = e.IntentionalCombination || !e.TargetCompleted || !e.PreparationAvailable || id == null ||
                !state.Events.TakeLast(12).Any(x => x.PerformerId == a.Id && (x.TechniqueId ?? x.MoveId) == id);
            e.PinRisk = PinRisk(state, a, b);
            e.ValidCover = LegalCover(state, a, b) && (e.PinRisk >= .16f || b.LastImpactWasFinisher &&
                state.ElapsedSeconds - b.LastImpactTime <= 15 || state.Booking?.FinishType == MatchFinishType.RollUp && state.Booking.WinnerId == a.Id);
            var rule = MatchTechniques.Find(e)?.Rules ?? (string.IsNullOrEmpty(e.MoveId) ? null : state.FindMove?.Invoke(e.MoveId));
            bool usesLegs = e.Type is MatchActionType.Dive or MatchActionType.TopRopeAttack or MatchActionType.RunningAttack ||
                rule?.MotionProfile == MoveMotionProfile.Kick || rule?.ExecutionStyle == MoveExecutionStyle.Running;
            bool usesArms = e.Type is MatchActionType.Grapple or MatchActionType.Submission ||
                rule != null && MatchPhysics.Kind(rule) is MoveKind.Grapple or MoveKind.Submission;
            bool stressesDamage = e.IsPhysicalAttack && (usesLegs && a.LegDamage >= 25 ||
                usesArms && (a.ArmDamage >= 25 || a.BodyDamage >= 25));
            var seller = stressesDamage || !e.IsPhysicalAttack ? a : b;
            var mostDamaged = new[] { BodyRegion.Head, BodyRegion.Body, BodyRegion.Arms, BodyRegion.Legs }
                .OrderByDescending(region => MatchPhysics.Damage(seller, region)).First();
            if (stressesDamage) mostDamaged = usesLegs && a.LegDamage >= 25 ? BodyRegion.Legs :
                a.ArmDamage >= 25 ? BodyRegion.Arms : BodyRegion.Body;
            bool damaged = MatchPhysics.Damage(seller, mostDamaged) > 0;
            e.SellingWrestlerId = seller.Id; e.SellingRegion = mostDamaged;
            e.SellingProtectedRegion = damaged && seller.Attributes.Selling >= 10;
            var priorProtection = state.Events.TakeLast(3).Any(x => x.SellingWrestlerId == seller.Id &&
                x.SellingRegion == mostDamaged && x.SellingProtectedRegion);
            e.SellingContradiction = stressesDamage && !e.SellingProtectedRegion && !priorProtection;
            e.SellingQuality = Math.Clamp(.5f + seller.Attributes.Selling * .025f + (e.SellingProtectedRegion ? .2f : 0) -
                (e.SellingContradiction ? .2f : 0), 0, 1);
        }

        public static int RecoveryDays(InjuryStatus severity) => severity switch
        { InjuryStatus.Minor => 7, InjuryStatus.Moderate => 21, InjuryStatus.Severe => 60, _ => 0 };

        public static void ApplyResult(GameSave save, MatchResultState result, GameDate date)
        {
            var members = result.ParticipantProfiles.SelectMany(p => p.MemberIds).ToHashSet();
            foreach (var change in result.InjuryChanges)
            {
                var wrestler = save.Wrestlers.SingleOrDefault(w => w.Id == change.WrestlerId);
                if (wrestler == null || change.SourceResultId != result.Id || !members.Contains(change.WrestlerId) ||
                    !Enum.IsDefined(typeof(InjuryStatus), change.Severity) || change.Severity == InjuryStatus.None ||
                    !Enum.IsDefined(typeof(BodyRegion), change.Region) || change.RecoveryDays < 1 || change.RecoveryDays > 365)
                    throw new InvalidOperationException("Invalid match injury change.");
                var until = date.AddDays(change.RecoveryDays);
                if (change.IsKayfabe)
                {
                    wrestler.KayfabeInjury = new KayfabeInjuryState { Severity = change.Severity, Region = change.Region,
                        StartDate = date, ReturnDate = new OptionalGameDate(until), Absent = change.Absent, SourceResultId = result.Id };
                    continue;
                }
                var recovery = Math.Max(1, (int)Math.Ceiling(change.RecoveryDays * StaffTeamEffects.For(save, StaffDepartmentType.Medical).Recovery));
                ApplyActualInjury(wrestler, change.Severity, change.Region, date, recovery);
            }
        }

        public static void ApplyActualInjury(WrestlerState wrestler, InjuryStatus severity, BodyRegion region, GameDate date, int recoveryDays)
        {
            if (wrestler == null || !Enum.IsDefined(typeof(InjuryStatus), severity) || severity == InjuryStatus.None ||
                !Enum.IsDefined(typeof(BodyRegion), region) || recoveryDays < 1 || recoveryDays > 365)
                throw new InvalidOperationException("실제 부상 상태가 올바르지 않습니다.");
            var until = date.AddDays(recoveryDays); var health = wrestler.Condition;
            if (!health.InjuryControlsAvailability)
            { health.AvailabilityBeforeInjury = health.Availability; health.InjuryControlsAvailability = true; }
            if (severity > health.InjuryStatus) { health.InjuryStatus = severity; health.InjuryRegion = region; }
            if (!health.RecoveryDate.HasValue || health.RecoveryDate.Value.CompareTo(until) < 0) health.RecoveryDate = new OptionalGameDate(until);
            var availability = health.InjuryStatus switch { InjuryStatus.Minor => WrestlerAvailability.Limited,
                InjuryStatus.Moderate => WrestlerAvailability.MatchUnavailable, _ => WrestlerAvailability.Unavailable };
            health.Availability = (WrestlerAvailability)Math.Max((int)health.Availability, (int)availability);
        }

        public static void Recover(WrestlerState wrestler, GameDate date)
        {
            var health = wrestler.Condition;
            if (health.InjuryStatus != InjuryStatus.None && health.RecoveryDate.HasValue && health.RecoveryDate.Value.CompareTo(date) <= 0)
            {
                var imposed = health.InjuryStatus switch { InjuryStatus.Minor => WrestlerAvailability.Limited,
                    InjuryStatus.Moderate => WrestlerAvailability.MatchUnavailable, _ => WrestlerAvailability.Unavailable };
                if (health.InjuryControlsAvailability && health.Availability == imposed) health.Availability = health.AvailabilityBeforeInjury;
                health.InjuryStatus = InjuryStatus.None; health.RecoveryDate = default; health.InjuryControlsAvailability = false;
            }
            if (wrestler.KayfabeInjury?.ReturnDate.HasValue == true && wrestler.KayfabeInjury.ReturnDate.Value.CompareTo(date) <= 0)
                wrestler.KayfabeInjury = new();
        }
    }
}
