using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public enum MatchPosition { Center, Ropes, Corner, TopRope, Apron, Outside }
    public enum BodyRegion { Head, Body, Arms, Legs }
    public enum MoveKind { Automatic, Strike, Grapple, Aerial, Submission }
    public enum MatchObjective { PinOrSubmission, Escape, Retrieval, Table }
    public enum ExchangeKind { FollowUp, Cover, Hold, Counter, Object, CountOut, Chain, RopeSequence, CornerSequence, Preparation, Transition, GripCombo, Ringside, TopContest, DoubleDown, Comeback }
    public enum ChainPosition { TieUp, WristLock, Hammerlock, Headlock, WaistLock }

    [Serializable]
    public sealed class MatchRuntimeRules
    {
        public bool Tag, Tornado, Elimination, IronMan, ImportantMatch;
        public bool Pinfall = true, Submission = true, RopeBreak = true, Disqualification = true, CountOut = true;
        public int FallsToWin = 1, CountOutSeconds = 10;
        public MatchObjective Objective;
        public string DominantSideId;
        public MatchOpeningStyle OpeningStyle;
        public float OpeningHostility;
        public float InjuryAggravationMultiplier = 1;

        public bool Allows(MatchFinishType finish) => finish switch
        {
            MatchFinishType.Pinfall or MatchFinishType.RollUp => Pinfall,
            MatchFinishType.Submission => Submission,
            MatchFinishType.Disqualification => Disqualification,
            MatchFinishType.CountOut => CountOut,
            MatchFinishType.Escape => Objective == MatchObjective.Escape,
            MatchFinishType.ObjectRetrieval => Objective == MatchObjective.Retrieval,
            MatchFinishType.TableBreak => Objective == MatchObjective.Table,
            MatchFinishType.Draw => true,
            _ => false
        };
    }

    [Serializable]
    public sealed class MatchExchange
    {
        public int Id, CauseIndex, StartedAt, Beats, RefereeCount, CountOutResets;
        public string ActorId, TargetId, MoveId;
        public ExchangeKind Kind;
        public MatchActionType ActionType;
        public BodyRegion Region;
        public bool Booked;
        public ChainPosition ChainPosition;
    }

    // One exchange owns its continuations. Selection resumes only after escape, break or completion.
    public static class MatchPhysics
    {
        public static float AttackImpact(MatchActionType type, bool receiverDowned) => type switch
        {
            MatchActionType.BasicStrike or MatchActionType.LimbAttack or MatchActionType.StandingHold => 2f,
            MatchActionType.HeavyStrike or MatchActionType.CornerAttack => 6f,
            MatchActionType.Grapple => 5f, MatchActionType.GroundAttack => 3f,
            MatchActionType.Signature or MatchActionType.TopRopeAttack => 9f, MatchActionType.Finisher => 15f,
            MatchActionType.Dive or MatchActionType.DoubleTeam => 10f,
            MatchActionType.RunningAttack => receiverDowned ? 4f : 6f, MatchActionType.RingsideAttack => 4f, _ => 2f
        };

        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static float Clamp(float value) => Math.Max(0, Math.Min(100, value));
        public static bool InRing(MatchEngineParticipant p) => p.Position != MatchPosition.Outside && p.Position != MatchPosition.Apron;
        public static bool Legal(MatchEngineState s, MatchEngineParticipant p) => !p.IsEliminated && (!s.Rules.Tag || s.Rules.Tornado || p.IsLegal);
        public static float Damage(MatchEngineParticipant p, BodyRegion region) => region switch
        { BodyRegion.Head => p.HeadDamage, BodyRegion.Arms => p.ArmDamage, BodyRegion.Legs => p.LegDamage, _ => p.BodyDamage };

        public static void Hurt(MatchEngineParticipant p, BodyRegion region, float amount)
        {
            switch (region)
            {
                case BodyRegion.Head: p.HeadDamage = Clamp(p.HeadDamage + amount); break;
                case BodyRegion.Arms: p.ArmDamage = Clamp(p.ArmDamage + amount); break;
                case BodyRegion.Legs: p.LegDamage = Clamp(p.LegDamage + amount); break;
                default: p.BodyDamage = Clamp(p.BodyDamage + amount); break;
            }
        }

        public static MatchEngineEvent Next(MatchEngineState s, Random random, IMatchState phase)
        {
            if ((s.Rules.IronMan || s.Booking?.FinishType == MatchFinishType.Draw) && s.ElapsedSeconds >= s.TargetDurationSeconds - 1)
                return TimeExpired(s);
            var spot = ApplyDueSpot(s, random);
            if (spot != null) return spot;
            foreach (var participant in s.Participants) PlanFinisher(s, participant, random);
            var remaining = s.LatestFinishSeconds - s.ElapsedSeconds;
            if (s.Exchange?.Booked == true && remaining > 1) return Continue(s, random);
            if (remaining <= 1)
            {
                if (s.Booking != null && s.Booking.FinishType != MatchFinishType.Draw && !s.Rules.IronMan)
                {
                    s.IsAborted = true;
                    s.FailureReason = "Booking finish prerequisites were not reached before the deadline.";
                    return Event(s, MatchActionType.Rest, MatchActionResult.Interrupted, null, null, 1);
                }
                return TimeExpired(s);
            }
            if (s.Exchange != null)
            {
                var cause = s.Exchange.CauseIndex >= 0 && s.Exchange.CauseIndex < s.Events.Count ? s.Events[s.Exchange.CauseIndex] : null;
                bool landedDive = cause?.Type == MatchActionType.Dive || MatchTechniques.Find(cause)?.Rules.Kind == MoveKind.Aerial;
                if (s.Booking != null && s.BookingSequence && s.Participants.Count == 2 && !s.Rules.Tag &&
                    s.Exchange.Kind == ExchangeKind.FollowUp && s.GetParticipant(s.Exchange.ActorId).Position != MatchPosition.TopRope &&
                    cause?.Type is not (MatchActionType.Finisher or MatchActionType.Signature) && !MatchMoveMotion.IsBridgingSuplex(s.Exchange.MoveId))
                    s.Exchange = null;
                else if (s.Exchange.Kind != ExchangeKind.FollowUp || !landedDive ||
                    s.GetParticipant(s.Exchange.ActorId).Position == MatchPosition.TopRope) return Continue(s, random);
            }
            if (s.RescueRequired) return BookedAction(s, random);
            if (s.Booking != null && s.Booking.FinishType != MatchFinishType.Draw &&
                (s.BookingSequence || s.TargetDurationSeconds - s.ElapsedSeconds <= BookedPreparationSeconds(s)) &&
                (!s.Rules.IronMan || s.Participants.Where(p => p.SideId == s.GetParticipant(s.Booking.WinnerId).SideId).Sum(p => p.Falls) <=
                s.Participants.Where(p => p.SideId != s.GetParticipant(s.Booking.WinnerId).SideId).GroupBy(p => p.SideId).Max(g => g.Sum(p => p.Falls))))
            {
                s.Exchange = null;
                s.BookingSequence = true;
                return BookedAction(s, random);
            }
            if (s.Exchange != null) return Continue(s, random);
            s.BookingSequence = false;
            var first = !s.Events.Any(e => e.Type != MatchActionType.Spot);
            var actor = first ? s.GetActiveParticipants().FirstOrDefault(p => p.Id == s.Opening.InitiatorId) : null;
            actor ??= PerformerSelector.Select(s, random, phase);
            var targets = s.GetOpponents(actor.Id).Where(x => Legal(s, x)).ToList();
            if (targets.Count == 0) throw new InvalidOperationException("No legal opponent.");
            var target = targets[PerformerSelector.WeightedIndex(targets.Select(x => Math.Exp(TargetSelector.Score(s, actor, x) / 24)).ToArray(), random)];
            if (first && !actor.IsDowned && !target.IsDowned && InRing(actor) && InRing(target))
            {
                var initial = s.Opening.Style == MatchOpeningStyle.Probe ? MatchActionType.Probe : MatchActionType.HeavyStrike;
                if (MatchTechniques.Candidates(initial, actor, target).Count == 0) initial = MatchActionType.BasicStrike;
                var openingMove = s.Opening.Style == MatchOpeningStyle.Ambush && random.NextDouble() < .65
                    ? actor.FinisherMoveIds.Concat(new[] { actor.FinisherMoveId }).FirstOrDefault(id => !string.IsNullOrEmpty(id) && UsableAssignedMove(s, actor, id) && CanMove(s, actor, target, id)) : null;
                if (openingMove != null) initial = MatchActionType.Finisher;
                return Resolve(s, Action(initial, actor, target, openingMove, continuation: s.Opening.Style == MatchOpeningStyle.Ambush), random);
            }
            var actions = Actions(s, actor, target);
            foreach (var action in actions) action.Score = ActionEvaluator.Score(s, action, phase);
            return Resolve(s, ActionSelector.Select(actions, random), random);
        }

        public static List<MatchEngineAction> Actions(MatchEngineState s, MatchEngineParticipant a, MatchEngineParticipant b)
        {
            var list = new List<MatchEngineAction>();
            bool TimeForHold() => s.Participants.Count != 2 || s.Rules.Tag || s.Booking == null || s.Rules.IronMan ||
                string.IsNullOrEmpty(s.Booking.WinnerId) || string.IsNullOrEmpty(s.Booking.LoserId) ||
                s.Booking.FinishType == MatchFinishType.Draw || s.TargetDurationSeconds - s.ElapsedSeconds > BookedPreparationSeconds(s) + 30;
            void Add(MatchActionType type, string move = null, string partner = null) => list.Add(new MatchEngineAction
            { Type = type, PerformerId = a.Id, ReceiverId = b.Id, MoveId = move, PartnerId = partner, TargetRegion = a.TargetRegion, ReceiverIds = new() { b.Id } });
            if (!Legal(s, a) || !Legal(s, b)) return list;
            if (a.IsDowned)
            {
                Add(MatchActionType.Recover);
                if ((s.Exchange == null && s.Participants.Count == 2 && !s.Rules.Tag ||
                    s.Exchange?.Kind == ExchangeKind.FollowUp && s.Exchange.ActorId == a.Id && s.Exchange.TargetId == b.Id) && GroundedReady(s, a, b))
                {
                    if (s.Rules.Pinfall && s.RefereeUnavailableUntil <= s.ElapsedSeconds && CanRetryCover(s, a, b)) Add(MatchActionType.Pin);
                    // A fresh hold would divert a committed finish; a cover can still follow the landing.
                    if (s.Exchange?.Kind == ExchangeKind.FollowUp && !s.BookingSequence && s.Rules.Submission && TimeForHold() &&
                        MatchTechniques.Candidates(MatchActionType.Submission, a, b).Count > 0) Add(MatchActionType.Submission);
                }
                return list;
            }
            if (!InRing(a) || !InRing(b)) { Add(MatchActionType.Reposition); return list; }
            if (a.Position == MatchPosition.TopRope)
            { Add(MatchTechniques.Candidates(MatchActionType.Dive, a, b).Count > 0 ? MatchActionType.Dive : MatchActionType.Reposition); return list; }
            if (!b.IsDowned && MatchOpening.CanChain(s) && s.Opening.CompletedChains == 0 && s.ElapsedSeconds < MatchOpening.MaximumSeconds(s))
            {
                foreach (var type in new[] { MatchActionType.Probe, MatchActionType.TieUp })
                    if (MatchTechniques.Candidates(type, a, b).Count > 0) Add(type);
                if (list.Count > 0) return list;
            }
            if (b.IsDowned)
            {
                Add(MatchActionType.GroundAttack); Add(MatchActionType.LimbAttack);
                if (s.Rules.Submission && (!s.Rules.RopeBreak || b.Position != MatchPosition.Ropes) && TimeForHold()) Add(MatchActionType.Submission);
                if (s.Rules.Pinfall && s.RefereeUnavailableUntil <= s.ElapsedSeconds && (!s.Rules.RopeBreak || b.Position != MatchPosition.Ropes) && CanRetryCover(s, a, b)) Add(MatchActionType.Pin);
                if (a.Eligibility.HighFlying >= 8 && !MatchHealth.Restricted(a, BodyRegion.Legs) && MatchTechniques.Candidates(MatchActionType.Dive, a, b).Count > 0) Add(MatchActionType.Climb);
                if (CanPrepareOffense(s, a, b))
                {
                    if (MatchTechniques.Candidates(MatchActionType.Grapple, a, b, MatchPosture.Standing).Count > 0 ||
                        MatchTechniques.Candidates(MatchActionType.BasicStrike, a, b, MatchPosture.Standing).Count > 0)
                        Add(MatchActionType.Lift);
                    foreach (var posture in new[] { MatchPosture.Prone, MatchPosture.Seated, MatchPosture.Kneeling })
                    {
                        if (posture == b.Posture) continue;
                        var goal = new[] { MatchActionType.Submission, MatchActionType.LimbAttack, MatchActionType.GroundAttack }
                            .Where(type => type != MatchActionType.Submission || s.Rules.Submission && TimeForHold() &&
                                (!s.Rules.RopeBreak || b.Position != MatchPosition.Ropes))
                            .SelectMany(type => MatchTechniques.Candidates(type, a, b, posture))
                            .Where(t => t.Rules.Situation is MoveSituation.GroundProne or MoveSituation.GroundSeated or MoveSituation.GroundKneeling)
                            .OrderBy(t => s.Events.TakeLast(12).Count(e => e.PerformerId == a.Id && e.TechniqueId == t.Id))
                            .FirstOrDefault();
                        if (goal != null) Add(MatchActionType.TurnOver, goal.Id);
                    }
                }
            }
            else
            {
                Add(MatchActionType.BasicStrike); Add(MatchActionType.HeavyStrike); Add(MatchActionType.Grapple); Add(MatchActionType.LimbAttack);
                if (s.Rules.Submission && (!s.Rules.RopeBreak || b.Position != MatchPosition.Ropes) && TimeForHold()) Add(MatchActionType.StandingHold);
                if (a.Eligibility.HighFlying >= 8 && !MatchHealth.Restricted(a, BodyRegion.Legs) && b.Position != MatchPosition.TopRope &&
                    MatchTechniques.Candidates(MatchActionType.Dive, a, b).Count > 0) Add(MatchActionType.Climb);
            }
            if (!b.IsDowned && MatchOpening.CanChain(s))
            { Add(MatchActionType.Probe); Add(MatchActionType.TieUp); }
            if (s.Rules.RopeBreak && MatchMovement.CanReachRopes(b) && s.Rules.Submission && TimeForHold() && CanPrepareOffense(s, a, b))
            {
                var hold = MatchTechniques.Candidates(b.IsDowned ? MatchActionType.Submission : MatchActionType.StandingHold, a, b)
                    .OrderBy(t => s.Events.TakeLast(12).Count(e => e.PerformerId == a.Id && e.TechniqueId == t.Id)).FirstOrDefault();
                if (hold != null) Add(MatchActionType.Reposition, hold.Id);
            }
            if (!list.Any(x => x.Type == MatchActionType.Reposition) &&
                (b.Position == MatchPosition.Ropes || !MatchHealth.Restricted(a, BodyRegion.Legs) && !MatchHealth.Restricted(a, BodyRegion.Arms) &&
                (b.Position == MatchPosition.Corner || b.Position == MatchPosition.Center && MatchMovement.DistanceFromCenter(b) > .65f)))
                Add(MatchActionType.Reposition);
            // Movement begins a linked exchange; a runner cannot randomly stop for another action.
            var movementReady = !MatchHealth.Restricted(a, BodyRegion.Legs) && a.Fatigue < 80 && b.Position != MatchPosition.TopRope &&
                s.TargetDurationSeconds - s.ElapsedSeconds > 70 &&
                (s.Phase != MatchPhase.Opening || s.Opening.Style != MatchOpeningStyle.Probe || s.Opening.Completed);
            if (movementReady)
            {
                if (MatchTechniques.Candidates(MatchActionType.RunningAttack, a, b).Count > 0) Add(MatchActionType.RopeRun);
                if (!b.IsDowned && !MatchHealth.Restricted(a, BodyRegion.Arms) && !MatchHealth.Restricted(b, BodyRegion.Legs))
                {
                    if (MatchTechniques.Candidates(MatchActionType.RunningAttack, a, b).Count > 0) Add(MatchActionType.IrishWhip);
                    if (MatchTechniques.Candidates(MatchActionType.CornerAttack, a, b).Count > 0) Add(MatchActionType.CornerWhip);
                }
            }
            if (!b.IsDowned)
            {
                Add(MatchActionType.Retreat);
                if (movementReady && s.RingsideEligible && s.Participants.Count == 2 &&
                    s.Rules.Objective == MatchObjective.PinOrSubmission && s.ElapsedSeconds >= 90 &&
                    (!s.AnnounceTableEligible || s.ElapsedSeconds >= s.TargetDurationSeconds * .4f) &&
                    s.TargetDurationSeconds - s.ElapsedSeconds > 150 && (!s.Rules.CountOut || s.Rules.CountOutSeconds > 5) &&
                    s.Events.Count(e => e.Type == MatchActionType.ExitRing) < (s.Rules.ImportantMatch ? 2 : 1) &&
                    !s.Events.Any(e => e.Type == MatchActionType.ExitRing && s.ElapsedSeconds - e.MatchTimeSeconds < 180))
                    Add(MatchActionType.ExitRing);
            }
            Add(MatchActionType.Rest);
            if (!s.Events.TakeLast(4).Any(e => e.Type == MatchActionType.Taunt && e.PerformerId == a.Id)) Add(MatchActionType.Taunt);
            foreach (var id in a.SignatureMoveIds.Concat(new[] { a.SignatureMoveId }).Where(x => !string.IsNullOrEmpty(x)).Distinct())
                if (a.MatchControl >= 52 && UsableAssignedMove(s, a, id) && CanMove(s, a, b, id)) Add(MatchActionType.Signature, id);
            var finishers = a.FinisherMoveIds.Concat(new[] { a.FinisherMoveId }).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
            var lastFinisher = s.Events.LastOrDefault(x => x.PerformerId == a.Id && x.Type == MatchActionType.Finisher);
            var primed = (a.MatchControl >= 66 || Damage(b, a.TargetRegion) >= 60 && a.MatchControl >= 50) &&
                (lastFinisher == null || s.ElapsedSeconds - lastFinisher.MatchTimeSeconds >= 55);
            if (primed)
            {
                foreach (var id in finishers) if (UsableAssignedMove(s, a, id) && CanMove(s, a, b, id)) Add(MatchActionType.Finisher, id);
                if (finishers.Count == 0 && a.MatchControl >= 78 && MatchTechniques.Candidates(MatchActionType.Finisher, a, b).Count > 0)
                    Add(MatchActionType.Finisher);
                if (b.IsDowned && !list.Any(x => x.Type == MatchActionType.Lift) &&
                    finishers.Any(id => s.FindMove?.Invoke(id) is MatchMoveRules m && Kind(m) is MoveKind.Strike or MoveKind.Grapple))
                    Add(MatchActionType.Lift);
                if (!string.IsNullOrEmpty(a.PlannedFinisherMoveId) && s.FindMove?.Invoke(a.PlannedFinisherMoveId) is MatchMoveRules goal)
                {
                    if (Kind(goal) == MoveKind.Aerial && CanMove(s, a, b, a.PlannedFinisherMoveId)) Add(MatchActionType.Climb, a.PlannedFinisherMoveId);
                    else if (b.IsDowned && !CanMove(s, a, b, a.PlannedFinisherMoveId) && Kind(goal) == MoveKind.Submission) Add(MatchActionType.TurnOver);
                }
            }
            var partner = s.Participants.FirstOrDefault(x => x.Id != a.Id && x.SideId == a.SideId && !x.IsEliminated && !x.IsDowned);
            if (s.Rules.Tag && !s.Rules.Tornado && partner != null)
            {
                Add(a.Position == MatchPosition.Corner ? MatchActionType.Tag : MatchActionType.Reposition, partner: partner.Id);
                if (a.Position == MatchPosition.Corner && a.MatchControl > 55) Add(MatchActionType.DoubleTeam, partner: partner.Id);
            }
            if (s.Rules.Objective != MatchObjective.PinOrSubmission && b.IsDowned)
                Add(s.ObjectReady ? MatchActionType.ObjectClimb : MatchActionType.ObjectSetup);
            list.RemoveAll(x => x.Type is MatchActionType.BasicStrike or MatchActionType.HeavyStrike or MatchActionType.Grapple or
                MatchActionType.GroundAttack or MatchActionType.LimbAttack or MatchActionType.Submission or MatchActionType.StandingHold or
                MatchActionType.Probe or MatchActionType.TieUp or MatchActionType.DoubleTeam &&
                MatchTechniques.Candidates(x.Type, a, b).Count == 0);
            return list;
        }

        public static bool CanMove(MatchEngineState s, MatchEngineParticipant a, MatchEngineParticipant b, string id)
        {
            var technique = MatchTechniques.Find(id);
            if (technique != null && (!technique.Enabled || !technique.AllowsStyle(a.WrestlingStyleId) || !technique.MeetsRequirement(a.Eligibility))) return false;
            var move = technique?.Rules ?? s.FindMove?.Invoke(id);
            if (!MatchTechniques.CanExecute(move, a) || !MatchHealth.CanReceive(move, b)) return false;
            if (move == null) return true;
            if (move.Situation != MoveSituation.Automatic)
            {
                if (!MatchTechniques.IsSupported(move.Situation) || !MatchTechniques.FitsSituation(move, a, b)) return false;
                if (move.Situation == MoveSituation.Table) return s.AnnounceTableEligible && s.AnnounceTableReady &&
                    !s.AnnounceTableBroken && s.Rules.ImportantMatch && !s.Rules.Disqualification &&
                    (!s.Rules.CountOut || s.Rules.CountOutSeconds >= 30);
                if (Kind(move) == MoveKind.Aerial) return !MatchHealth.Restricted(a, BodyRegion.Legs);
                if (Kind(move) == MoveKind.Submission) return s.Rules.Submission && (!s.Rules.RopeBreak || b.Position != MatchPosition.Ropes);
                return Kind(move) != MoveKind.Grapple || !MatchHealth.Restricted(a, BodyRegion.Arms);
            }
            return Kind(move) switch
            {
                MoveKind.Aerial => b.IsDowned && !MatchHealth.Restricted(a, BodyRegion.Legs),
                MoveKind.Submission => b.IsDowned && s.Rules.Submission && (!s.Rules.RopeBreak || b.Position != MatchPosition.Ropes),
                MoveKind.Grapple => !b.IsDowned && !MatchHealth.Restricted(a, BodyRegion.Arms),
                _ => !b.IsDowned
            };
        }

        private static bool UsableAssignedMove(MatchEngineState s, MatchEngineParticipant a, string id)
        {
            var technique = MatchTechniques.Find(id);
            if (technique != null && (!technique.Enabled || !technique.AllowsStyle(a.WrestlingStyleId) || !technique.MeetsRequirement(a.Eligibility))) return false;
            var move = s.FindMove?.Invoke(id);
            return MatchTechniques.CanExecute(move, a);
        }

        private static void PlanFinisher(MatchEngineState s, MatchEngineParticipant a, Random random)
        {
            if (s.RescueRequired) return;
            var candidates = a.FinisherMoveIds.Concat(new[] { a.FinisherMoveId }).Where(id => !string.IsNullOrEmpty(id) && UsableAssignedMove(s, a, id)).Distinct().ToList();
            if (s.Booking?.WinnerId == a.Id && s.Booking.FinishType is MatchFinishType.Pinfall or MatchFinishType.RollUp or MatchFinishType.Submission)
            {
                bool submission = s.Booking.FinishType == MatchFinishType.Submission;
                bool Compatible(string id) => s.FindMove?.Invoke(id) is not MatchMoveRules rule || (Kind(rule) == MoveKind.Submission) == submission;
                var compatible = candidates.Where(Compatible).ToList();
                if (compatible.Count == 0)
                    compatible = a.SignatureMoveIds.Concat(new[] { a.SignatureMoveId }).Where(id => !string.IsNullOrEmpty(id) &&
                        UsableAssignedMove(s, a, id) && Compatible(id)).Distinct().ToList();
                if (compatible.Count > 0) candidates = compatible;
            }
            if (!candidates.Contains(a.PlannedFinisherMoveId))
            {
                var opponent = s.GetOpponents(a.Id).FirstOrDefault();
                var weights = candidates.Select(id => 1d + (opponent == null ? 0 :
                    Damage(opponent, s.FindMove?.Invoke(id)?.TargetRegion ?? a.TargetRegion) * a.Attributes.RingPsychology / 100)).ToArray();
                a.PlannedFinisherMoveId = candidates.Count == 0 ? null : candidates[PerformerSelector.WeightedIndex(weights, random)];
            }
            if (!string.IsNullOrEmpty(a.PlannedFinisherMoveId) && s.FindMove?.Invoke(a.PlannedFinisherMoveId) is MatchMoveRules target)
                a.TargetRegion = target.TargetRegion;
        }

        private static int CoverSeconds(MatchEngineParticipant a, float? fatigue = null) => 2 +
            (int)Math.Ceiling(Math.Max(0, (fatigue ?? a.Fatigue) - 70) / 15 * (1 - a.Attributes.RingPsychology / 40));

        private static bool GroundedReady(MatchEngineState s, MatchEngineParticipant a, MatchEngineParticipant b) =>
            Legal(s, a) && Legal(s, b) && InRing(a) && InRing(b) && b.IsDowned &&
            a.Position != MatchPosition.TopRope && a.Stun < 12 && a.Fatigue < 80 && a.RealInjury < InjuryStatus.Moderate &&
            (!s.Rules.RopeBreak || b.Position != MatchPosition.Ropes);

        private static bool CanPrepareOffense(MatchEngineState s, MatchEngineParticipant a, MatchEngineParticipant b) =>
            s.Participants.Count == 2 && !s.Rules.Tag && !s.BookingSequence && InRing(a) && InRing(b) &&
            !a.IsDowned && a.Position != MatchPosition.TopRope && b.Position != MatchPosition.TopRope &&
            a.Stun < 12 && a.Fatigue < 80 && !MatchHealth.Restricted(a, BodyRegion.Arms) && !MatchHealth.Restricted(a, BodyRegion.Legs) &&
            s.TargetDurationSeconds - s.ElapsedSeconds > 70 &&
            !(b.LastImpactWasFinisher && s.ElapsedSeconds - b.LastImpactTime <= 30) &&
            !s.Events.Any(e => e.PerformerId == a.Id && e.ReceiverId == b.Id &&
                e.Type is MatchActionType.Lift or MatchActionType.TurnOver && s.ElapsedSeconds - e.MatchTimeSeconds < 12);

        private static bool CanRetryCover(MatchEngineState s, MatchEngineParticipant a, MatchEngineParticipant b)
        {
            int last = s.Events.FindLastIndex(e => e.Type == MatchActionType.Pin && e.PerformerId == a.Id && e.ReceiverId == b.Id);
            if (last < 0 || s.Events[last].Result != MatchActionResult.OneCount &&
                (s.Participants.Count != 2 || s.Rules.Tag || s.Events[last].Result is not (MatchActionResult.TwoCount or MatchActionResult.NearFall))) return true;
            // An easy kickout needs fresh offense, not another turn-over of the same opponent.
            return s.Events.Skip(last + 1).Any(e => e.ReceiverId == b.Id && e.Impact > 0);
        }

        public static MoveKind Kind(MatchMoveRules move) => move.Kind != MoveKind.Automatic ? move.Kind :
            move.HighFlyingWeight >= .5f ? MoveKind.Aerial : move.TechnicalWeight > move.BrawlingWeight ? MoveKind.Grapple : MoveKind.Strike;

        public static MatchEngineEvent Resolve(MatchEngineState s, MatchEngineAction action, Random random)
        {
            var a = s.GetParticipant(action.PerformerId); var b = s.GetParticipant(action.ReceiverId);
            if ((!Legal(s, a) && (action.Type != MatchActionType.Recover || a.IsEliminated)) || !Legal(s, b) || a.SideId != null && a.SideId == b.SideId)
                throw new InvalidOperationException("Illegal exchange participants.");
            var type = action.Type;
            var move = string.IsNullOrEmpty(action.MoveId) ? null : MatchTechniques.Find(action.MoveId)?.Rules ?? s.FindMove?.Invoke(action.MoveId);
            var continuingCombo = s.Exchange?.Kind == ExchangeKind.GripCombo;
            bool bridgeCover = type == MatchActionType.Pin && MatchMoveMotion.IsBridgingSuplex(action.MoveId) &&
                a.Posture == MatchPosture.Supine && a.Grip == MatchGrip.Waist && a.GripTargetId == b.Id;
            if (type is MatchActionType.Recover or MatchActionType.Rest)
            {
                var recovering = a.IsDowned;
                bool readyRise = recovering && s.Participants.Count == 2 && !s.Rules.Tag && b.IsDowned &&
                    InRing(a) && InRing(b) && a.Position != MatchPosition.TopRope && a.Posture == MatchPosture.Seated &&
                    a.Stun <= 0 && s.ElapsedSeconds - a.LastImpactTime >= 15 && a.Fatigue < 80 &&
                    a.RealInjury < InjuryStatus.Moderate && !MatchHealth.Restricted(a, BodyRegion.Legs);
                int seconds = recovering ? RecoverySeconds(a) : 10;
                var recovery = Event(s, recovering ? MatchActionType.Recover : type, MatchActionResult.FullSuccess, a, b, seconds);
                RecoverFatigue(a, recovery.DurationSeconds);
                if (recovering)
                {
                    // An unhurt seated wrestler can stand in one motion, without another idle decision.
                    if (readyRise) MatchMovement.RecoverPosture(a);
                    MatchMovement.RecoverPosture(a);
                }
                recovery.Detail = recovering ? a.Posture.ToString().ToLowerInvariant() : null;
                return recovery;
            }
            bool groundAction = a.IsDowned && GroundedReady(s, a, b) && (type == MatchActionType.Pin || type == MatchActionType.Submission &&
                (string.IsNullOrEmpty(action.MoveId) ? MatchTechniques.Candidates(type, a, b).Count > 0 :
                    move != null && Kind(move) == MoveKind.Submission && MatchMoveMotion.ActorEndPosture(MatchTechniques.Find(action.MoveId)?.Id ?? action.MoveId) != MatchPosture.Standing));
            if (a.IsDowned && !bridgeCover && !groundAction) throw new InvalidOperationException("A downed participant must recover before attacking.");
            if (type is MatchActionType.Probe or MatchActionType.TieUp)
            {
                if (b.IsDowned || !InRing(a) || !InRing(b)) throw new InvalidOperationException("Opening hand fighting requires standing opponents in the ring.");
                var openingTechnique = MatchTechniques.Select(s, type, a, b, random);
                Spend(a, .5f); Spend(b, .5f);
                if (type == MatchActionType.TieUp) Start(s, ExchangeKind.Chain, a, b);
                var openingEvent = Event(s, type, MatchActionResult.Held, a, b, type == MatchActionType.Probe ? 5 : 6);
                openingEvent.TechniqueId = openingTechnique.Id; openingEvent.TechniqueName = openingTechnique.Rules.Name;
                return openingEvent;
            }
            if (type is MatchActionType.IrishWhip or MatchActionType.CornerWhip or MatchActionType.RopeRun)
            {
                if (!InRing(a) || !InRing(b) || a.Position == MatchPosition.TopRope || b.Position == MatchPosition.TopRope ||
                    MatchHealth.Restricted(a, BodyRegion.Legs) || a.Fatigue >= 80 || type != MatchActionType.RopeRun && (b.IsDowned || MatchHealth.Restricted(a, BodyRegion.Arms) || MatchHealth.Restricted(b, BodyRegion.Legs)))
                    throw new InvalidOperationException("Running and whipping require usable limbs and opponents in the ring.");
                Spend(a, 2);
                if (type == MatchActionType.RopeRun)
                {
                    a.Position = MatchPosition.Ropes;
                    Start(s, ExchangeKind.RopeSequence, a, b);
                    s.Exchange.ActionType = type;
                    return Event(s, type, MatchActionResult.FullSuccess, a, b, 3, detail: "run-up");
                }
                var reversed = random.NextDouble() < Math.Max(.04, Math.Min(.35, .12 +
                    (b.Attributes.Technical - a.Attributes.Power) * .008 - b.Stun * .002));
                var attacker = reversed ? b : a; var runner = reversed ? a : b;
                attacker.Position = MatchPosition.Center;
                runner.Position = type == MatchActionType.CornerWhip ? MatchPosition.Corner : MatchPosition.Ropes;
                Spend(runner, 1);
                Start(s, type == MatchActionType.CornerWhip ? ExchangeKind.CornerSequence : ExchangeKind.RopeSequence, attacker, runner);
                s.Exchange.ActionType = type;
                return Event(s, type, reversed ? MatchActionResult.Countered : MatchActionResult.FullSuccess, a, b, 4,
                    detail: reversed ? "reversed-whip" : "whip");
            }
            if (type == MatchActionType.Retreat)
            {
                if (!InRing(a) || !InRing(b)) throw new InvalidOperationException("Retreat requires the ring.");
                a.Position = MatchPosition.Center;
                RecoverFatigue(a, Math.Min(4, Math.Max(1, s.TargetDurationSeconds - s.ElapsedSeconds - 1)));
                return Event(s, type, MatchActionResult.FullSuccess, a, b, 4, detail: "reset-distance");
            }
            if ((type is MatchActionType.RunningAttack or MatchActionType.CornerAttack or MatchActionType.RingsideAttack or MatchActionType.TopRopeAttack) && !action.IsContinuation || type is MatchActionType.Evade or MatchActionType.Pursue or MatchActionType.PrepareAttack)
                throw new InvalidOperationException("Movement follow-ups require their setup exchange.");
            if (type == MatchActionType.ExitRing)
            {
                if (!InRing(a) || !InRing(b) || a.Position == MatchPosition.TopRope || b.Position == MatchPosition.TopRope || MatchHealth.Restricted(a, BodyRegion.Legs))
                    throw new InvalidOperationException("Leaving the ring requires a standing wrestler with usable legs.");
                a.Position = MatchPosition.Outside; Spend(a, 1);
                Start(s, ExchangeKind.Ringside, a, b);
                return Event(s, type, MatchActionResult.FullSuccess, a, b, 3, detail: "roll-out");
            }
            if (type == MatchActionType.Lift)
            {
                if (!b.IsDowned || !InRing(a) || !InRing(b)) throw new InvalidOperationException("Lifting needs a grounded opponent in the ring.");
                b.IsDowned = false; b.Posture = MatchPosture.Standing; Spend(a, 1.5f);
                if (!s.BookingSequence && s.Participants.Count == 2 && !s.Rules.Tag)
                {
                    var followUps = Actions(s, a, b).Where(x => x.Type is MatchActionType.BasicStrike or MatchActionType.HeavyStrike or
                        MatchActionType.Grapple or MatchActionType.StandingHold or MatchActionType.IrishWhip or MatchActionType.CornerWhip or
                        MatchActionType.Signature or MatchActionType.Finisher).ToList();
                    if (followUps.Count > 0)
                    {
                        foreach (var follow in followUps) follow.Score = ActionEvaluator.Score(s, follow);
                        var next = ActionSelector.Select(followUps, random);
                        Start(s, ExchangeKind.Preparation, a, b, next.MoveId); s.Exchange.ActionType = next.Type;
                    }
                }
                return Event(s, type, MatchActionResult.FullSuccess, a, b, 4);
            }
            if (type == MatchActionType.TurnOver)
            {
                if (!b.IsDowned) throw new InvalidOperationException("Turning requires a grounded opponent.");
                var goal = MatchTechniques.Find(action.MoveId ?? a.PlannedFinisherMoveId)?.Rules ?? s.FindMove?.Invoke(action.MoveId ?? a.PlannedFinisherMoveId);
                b.Posture = goal?.Situation switch { MoveSituation.GroundProne => MatchPosture.Prone,
                    MoveSituation.GroundSeated => MatchPosture.Seated, MoveSituation.GroundKneeling => MatchPosture.Kneeling, _ => MatchPosture.Supine };
                Spend(a, 1);
                var preparedTechnique = MatchTechniques.Find(action.MoveId);
                if (!s.BookingSequence && s.Participants.Count == 2 && !s.Rules.Tag && preparedTechnique?.CanUseGenerally == true)
                { Start(s, ExchangeKind.Preparation, a, b, action.MoveId); s.Exchange.ActionType = preparedTechnique.Action; }
                return Event(s, type, MatchActionResult.FullSuccess, a, b, 3, detail: "turn-over");
            }
            if (type == MatchActionType.Reposition)
            {
                if (a.Position == MatchPosition.TopRope) a.Position = MatchPosition.Corner;
                else if (!InRing(a)) a.Position = MatchPosition.Center;
                else if (!InRing(b) || b.Position == MatchPosition.Ropes || action.PartnerId == null &&
                    (b.Position == MatchPosition.Corner || MatchMovement.DistanceFromCenter(b) > .65f))
                {
                    b.Position = MatchPosition.Center;
                    if (action.PartnerId == null) a.Position = MatchPosition.Center;
                }
                else a.Position = action.PartnerId != null ? MatchPosition.Corner : MatchPosition.Center;
                Spend(a, 1);
                var hold = MatchTechniques.Find(action.MoveId);
                if (!s.BookingSequence && s.Participants.Count == 2 && !s.Rules.Tag &&
                    hold?.CanUseGenerally == true && hold.Action is MatchActionType.Submission or MatchActionType.StandingHold)
                { Start(s, ExchangeKind.Preparation, a, b, hold.Id); s.Exchange.ActionType = hold.Action; }
                return Event(s, type, MatchActionResult.FullSuccess, a, b, 5, detail: action.PartnerId != null ? "corner" : "center");
            }
            if (type == MatchActionType.Tag)
            {
                var partner = s.GetParticipant(action.PartnerId);
                if (a.Position != MatchPosition.Corner || partner.SideId != a.SideId || partner.IsEliminated || partner.IsDowned)
                    throw new InvalidOperationException("Tag requires a ready teammate at the corner.");
                a.IsLegal = false; a.Position = MatchPosition.Apron;
                partner.IsLegal = true; partner.Position = MatchPosition.Center; partner.ActionReadiness = 85;
                var e = Event(s, type, MatchActionResult.FullSuccess, a, b, 3, detail: a.Fatigue > 35 ? "hot-tag" : "tag");
                e.PartnerId = partner.Id; return e;
            }
            if (type == MatchActionType.DoubleTeam)
            {
                var partner = s.Participants.FirstOrDefault(x => x.Id == action.PartnerId);
                if (partner == null || partner.SideId != a.SideId || partner.IsEliminated || partner.IsDowned ||
                    a.Position != MatchPosition.Corner || !s.Rules.Tag)
                    throw new InvalidOperationException("A double team needs a ready partner at the corner.");
            }
            if (type == MatchActionType.Climb)
            {
                if (!InRing(a) || !InRing(b) || MatchHealth.Restricted(a, BodyRegion.Legs)) throw new InvalidOperationException("Aerial setup needs opponents in the ring and usable legs.");
                a.Position = MatchPosition.TopRope; Spend(a, 2);
                Start(s, ExchangeKind.FollowUp, a, b, action.MoveId);
                s.Exchange.ActionType = action.MoveId != null && a.FinisherMoveIds.Concat(new[] { a.FinisherMoveId }).Contains(action.MoveId)
                    ? MatchActionType.Finisher : MatchActionType.Signature;
                return Event(s, type, MatchActionResult.FullSuccess, a, b, 5);
            }
            if (type == MatchActionType.Taunt)
            {
                var options = new[] { "challenge", "showboat", "mock", "finisher-signal", "stall" };
                var role = a.Alignment == KayfabeAlignment.Heel ? a.Attributes.HeelWork : a.Attributes.FaceWork;
                var weights = new double[] { 3 + a.Attributes.FaceWork * .2f, 2 + a.Attributes.Charisma * .4f,
                    1 + a.Attributes.HeelWork * (a.Alignment == KayfabeAlignment.Heel ? .5f : .1f),
                    a.MatchControl > 60 ? 3 + a.Attributes.Acting * .3f : .2, a.Fatigue > 55 ? 5 : .3 };
                var chosen = options[PerformerSelector.WeightedIndex(weights, random)];
                var seconds = chosen == "stall" ? 6 : 3 + (int)(a.Attributes.Acting / 10);
                if (chosen == "stall") RecoverFatigue(a, seconds);
                string response = null;
                RecoverFatigue(b, seconds);
                if (b.IsDowned && b.Stun < 25)
                { MatchMovement.RecoverPosture(b); response = "taunt-recover"; }
                else if (!b.IsDowned && b.Stun < 30)
                {
                    var opening = Math.Max(.05, Math.Min(.65, .18 + (b.Attributes.RingImprovisation + b.Attributes.Technical - a.Attributes.RingPsychology) * .012));
                    if (random.NextDouble() < opening)
                    { Start(s, ExchangeKind.Counter, b, a); response = "taunt-counter"; a.MatchControl = Clamp(a.MatchControl - 5); }
                    else { b.ActionReadiness = Clamp(b.ActionReadiness + 10); response = "taunt-approach"; }
                }
                var taunt = Event(s, type, MatchActionResult.FullSuccess, a, b, seconds, detail: response);
                taunt.KayfabeAction = chosen;
                taunt.ExecutionQuality = Math.Min(1, .4f + (a.Attributes.Charisma + a.Attributes.Acting + role) / 100);
                return taunt;
            }
            if (type == MatchActionType.ObjectSetup)
            {
                if (s.Rules.Objective == MatchObjective.PinOrSubmission) throw new InvalidOperationException("No match object.");
                s.ObjectReady = true;
                return Event(s, type, MatchActionResult.FullSuccess, a, b, 8);
            }
            if (type == MatchActionType.ObjectClimb)
            {
                if (!s.ObjectReady) throw new InvalidOperationException("Object must be prepared first.");
                Start(s, ExchangeKind.Object, a, b);
                return Event(s, type, MatchActionResult.FullSuccess, a, b, 6);
            }
            if (type == MatchActionType.RingsideAttack)
            {
                if (a.Position != MatchPosition.Outside || b.Position != MatchPosition.Outside)
                    throw new InvalidOperationException("Ringside attacks need both opponents outside.");
            }
            else if (type == MatchActionType.Dive && move != null && MatchTechniques.IsOutsideDive(move.Situation))
            {
                if (!InRing(a) || b.Position != MatchPosition.Outside || !MatchTechniques.CanExecute(move, a) ||
                    (MatchTechniques.IsRopeDive(move.Situation) ? a.Position != MatchPosition.Ropes : a.Position != MatchPosition.TopRope))
                    throw new InvalidOperationException("An outside dive requires its launch position and a ringside target.");
            }
            else if (!InRing(a) || !InRing(b)) throw new InvalidOperationException("This action requires the ring.");
            if (type == MatchActionType.StandingHold && (b.IsDowned || !s.Rules.Submission || s.Rules.RopeBreak && b.Position == MatchPosition.Ropes))
                throw new InvalidOperationException("A standing hold needs a standing opponent away from a rope break.");
            if (type is MatchActionType.GroundAttack or MatchActionType.Submission && !b.IsDowned)
                throw new InvalidOperationException("Ground actions need a grounded opponent.");
            if (type == MatchActionType.Pin)
            {
                bool rollUp = s.BookingSequence && s.Booking?.FinishType == MatchFinishType.RollUp;
                if (!b.IsDowned && !rollUp || !s.Rules.Pinfall || s.RefereeUnavailableUntil > s.ElapsedSeconds) throw new InvalidOperationException("Illegal cover.");
                if (b.Posture is MatchPosture.Prone or MatchPosture.Seated or MatchPosture.Kneeling)
                {
                    b.Posture = MatchPosture.Supine; Spend(a, 1);
                    Start(s, ExchangeKind.FollowUp, a, b);
                    s.Exchange.ActionType = MatchActionType.Pin;
                    return Event(s, MatchActionType.TurnOver, MatchActionResult.FullSuccess, a, b, 3, detail: "pin-setup");
                }
                Start(s, ExchangeKind.Cover, a, b, bridgeCover ? action.MoveId : null);
                b.IsDowned = true; b.Posture = MatchPosture.Supine;
                return Event(s, MatchActionType.Cover, MatchActionResult.FullSuccess, a, b, CoverSeconds(a) + (rollUp ? 1 : 0), detail: bridgeCover ? "bridge" : rollUp ? "roll-up" : null);
            }
            if (move != null && !CanMove(s, a, b, action.MoveId)) throw new InvalidOperationException("Move prerequisites are not met.");
            if (!s.BookingSequence && !action.IsContinuation && move != null &&
                (type is MatchActionType.Signature or MatchActionType.Finisher) && Kind(move) != MoveKind.Aerial)
            {
                Start(s, ExchangeKind.Preparation, a, b, action.MoveId); s.Exchange.ActionType = type;
                var setup = Event(s, MatchActionType.PrepareAttack, MatchActionResult.Held, a, b, 3);
                setup.TechniqueName = move.Name;
                return setup;
            }
            if (string.IsNullOrEmpty(action.MoveId) && MatchTechniques.Candidates(type, a, b).Count == 0)
            {
                s.Exchange = null;
                return Event(s, MatchActionType.Retreat, MatchActionResult.Interrupted, a, b, 3, detail: "unsafe-attack-cancelled");
            }
            var technique = string.IsNullOrEmpty(action.MoveId) ? MatchTechniques.Select(s, type, a, b, random) : null;
            move ??= technique?.Rules;
            if (!MatchTechniques.CanExecute(move, a)) throw new InvalidOperationException("Injury prevents executing this technique.");
            var kind = move == null ? (type == MatchActionType.Submission ? MoveKind.Submission : type == MatchActionType.Dive ? MoveKind.Aerial : MoveKind.Strike) : Kind(move);
            bool desperation = !s.BookingSequence && !continuingCombo && kind != MoveKind.Submission && !MatchMoveMotion.IsBridgingSuplex(action.MoveId ?? technique?.Id) &&
                type is MatchActionType.HeavyStrike or MatchActionType.Signature or MatchActionType.Counter &&
                s.TargetDurationSeconds >= 900 && s.ElapsedSeconds > s.TargetDurationSeconds * .3f &&
                s.TargetDurationSeconds - s.ElapsedSeconds > 180 && b.MatchControl - a.MatchControl >= 15 &&
                (a.Fatigue >= 30 || a.Stun >= 12) && !s.Events.Any(e => e.Detail == "desperation-strike" && s.ElapsedSeconds - e.MatchTimeSeconds < 240);
            bool outsideDive = move != null && MatchTechniques.IsOutsideDive(move.Situation);
            if (kind == MoveKind.Aerial && a.Position != MatchPosition.TopRope && !outsideDive)
                return Resolve(s, new MatchEngineAction { Type = MatchActionType.Climb, PerformerId = a.Id, ReceiverId = b.Id, MoveId = action.MoveId }, random);
            var ability = move == null ? type switch
            {
                MatchActionType.Grapple or MatchActionType.LimbAttack or MatchActionType.Submission => a.Attributes.Technical,
                MatchActionType.Dive => a.Attributes.HighFlying,
                MatchActionType.Finisher => a.Attributes.Power,
                _ => a.Attributes.Brawling
            } : a.Attributes.Brawling * move.BrawlingWeight + a.Attributes.Power * move.PowerWeight +
                a.Attributes.Technical * move.TechnicalWeight + a.Attributes.HighFlying * move.HighFlyingWeight;
            var impairment = a.Fatigue * .035f + (int)a.RealInjury * .5f;
            var quality = Math.Max(.15f, Math.Min(1, .91f + (ability - (move?.ExecutionDifficulty ?? 6)) * .012f - impairment * .015f));
            var reception = Math.Max(.5f, Math.Min(.99f, .94f + (b.Attributes.Selling - (move?.SellingDifficulty ?? 7)) * .004f));
            var performed = random.NextDouble() < quality;
            var received = random.NextDouble() < reception;
            var defended = !s.BookingSequence && !b.IsDowned && random.NextDouble() < Math.Max(.025, .12 + (b.Attributes.Technical - ability) * .007 - b.Stun * .002);
            var outcome = defended ? MatchActionResult.Countered : !performed ? MatchActionResult.ExecutionFailure : !received ? MatchActionResult.ReceptionFailure : MatchActionResult.FullSuccess;
            Spend(a, kind == MoveKind.Aerial ? 4 : 2);
            if (kind == MoveKind.Aerial) a.Position = outsideDive ? MatchPosition.Outside : MatchPosition.Center;
            var region = move?.TargetRegion ?? (type == MatchActionType.LimbAttack || kind == MoveKind.Submission ? a.TargetRegion : BodyRegion.Body);
            var impact = move?.Situation == MoveSituation.Table ? 18f :
                type == MatchActionType.RingsideAttack && move?.PowerWeight == 1 ? 6f : AttackImpact(type, b.IsDowned);
            if (defended)
            {
                if (outsideDive) { a.IsDowned = true; a.Stun = Clamp(a.Stun + 10); Hurt(a, BodyRegion.Body, 3); }
                else if (type != MatchActionType.RingsideAttack) Start(s, ExchangeKind.Counter, b, a);
                a.MatchControl = Clamp(a.MatchControl - 5); b.MatchControl = Clamp(b.MatchControl + 5);
                impact = 0;
            }
            else if (outcome == MatchActionResult.ExecutionFailure)
            {
                impact = 0;
                if (kind == MoveKind.Aerial) { a.IsDowned = true; a.Stun = Clamp(a.Stun + 12); Hurt(a, BodyRegion.Body, 3); }
                else if (type != MatchActionType.RingsideAttack)
                    Start(s, ExchangeKind.Transition, a, b);
            }
            else
            {
                Hit(s, b, region, impact, type == MatchActionType.Finisher);
                if (type == MatchActionType.LimbAttack) Hurt(b, region, 4);
                a.MatchControl = Clamp(a.MatchControl + 5); b.MatchControl = Clamp(b.MatchControl - 5);
                if (type == MatchActionType.Finisher) a.MatchControl = Clamp(a.MatchControl - 24);
                else if (type == MatchActionType.Signature) a.MatchControl = Clamp(a.MatchControl - 10);
                if (kind == MoveKind.Submission)
                {
                    if (!s.Rules.Submission) throw new InvalidOperationException("Submissions are disabled.");
                    Start(s, ExchangeKind.Hold, a, b, action.MoveId, region);
                    b.IsDowned = type != MatchActionType.StandingHold;
                }
                else if (!outsideDive && type != MatchActionType.RingsideAttack && (impact >= 6 || kind == MoveKind.Grapple ||
                    b.IsDowned && MatchMoveMotion.ActorEndPosture(technique?.Id ?? MatchTechniques.Find(action.MoveId)?.Id ?? action.MoveId) != MatchPosture.Standing))
                {
                    b.IsDowned = type != MatchActionType.StandingHold;
                    bool bridge = outcome == MatchActionResult.FullSuccess && MatchMoveMotion.IsBridgingSuplex(action.MoveId ?? technique?.Id);
                    Start(s, ExchangeKind.FollowUp, a, b, bridge ? action.MoveId ?? technique?.Id : null);
                    if (bridge) s.Exchange.ActionType = MatchActionType.Pin;
                    if (!bridge && !s.BookingSequence && !continuingCombo && type == MatchActionType.Finisher && move != null && kind == MoveKind.Grapple &&
                        s.ElapsedSeconds > s.TargetDurationSeconds * .5 && s.TargetDurationSeconds - s.ElapsedSeconds > 80 &&
                        !MatchHealth.Restricted(a, BodyRegion.Arms) && random.NextDouble() < .45)
                        Start(s, ExchangeKind.GripCombo, a, b, action.MoveId);
                }
            }
            var result = Event(s, type, outcome, a, b, type == MatchActionType.RingsideAttack && move?.Situation != MoveSituation.Table ? 2 :
                type == MatchActionType.BasicStrike || outsideDive ? 3 : kind == MoveKind.Aerial ? 5 : 7, impact);
            result.MoveId = action.MoveId; result.TargetRegion = region;
            result.TechniqueId = technique?.Id ?? MatchTechniques.Find(action.MoveId)?.Id ?? action.MoveId;
            result.TechniqueName = move?.Name;
            result.ExecutionQuality = performed && received ? quality : .35f;
            result.IsPhysicalAttack = true;
            result.PhysicalExecutionFailed = !performed && !defended;
            result.ContactDifficulty = move?.SellingDifficulty ?? 7;
            result.PartnerId = action.PartnerId;
            if (outsideDive)
                result.Detail = defended ? "outside-dive-evaded" : outcome == MatchActionResult.ExecutionFailure ? "outside-dive-missed" : "outside-dive";
            if (move?.Situation == MoveSituation.Table)
            {
                s.AnnounceTableReady = false;
                result.Detail = impact > 0 ? "announce-table-break" : "announce-table-failed";
                if (impact > 0) { s.AnnounceTableBroken = true; result.Result = MatchActionResult.TableBroken; }
            }
            if (desperation && impact >= 6 && outcome == MatchActionResult.FullSuccess &&
                random.NextDouble() < .14 + a.Attributes.RingPsychology * .006 + a.Attributes.Selling * .003)
            {
                a.IsDowned = b.IsDowned = true; a.Posture = b.Posture = MatchPosture.Supine;
                Start(s, ExchangeKind.DoubleDown, a, b);
                result.Detail = "desperation-strike";
            }
            return result;
        }

        private static MatchEngineEvent Continue(MatchEngineState s, Random random)
        {
            var x = s.Exchange;
            var a = s.GetParticipant(x.ActorId); var b = s.GetParticipant(x.TargetId);
            x.Beats++;
            MatchEngineEvent Done(MatchEngineEvent e)
            {
                e.ExchangeId = x.Id; e.CauseEventIndex = x.CauseIndex;
                if (x.Kind == ExchangeKind.Cover && MatchMoveMotion.IsBridgingSuplex(x.MoveId)) e.Detail = "bridge";
                if (x.Kind == ExchangeKind.Hold && e.Type == MatchActionType.Hold && x.CauseIndex < s.Events.Count)
                {
                    var cause = s.Events[x.CauseIndex];
                    e.TechniqueName = cause.TechniqueName ?? s.FindMove?.Invoke(x.MoveId)?.Name;
                }
                return e;
            }
            if (x.Kind is ExchangeKind.DoubleDown or ExchangeKind.Comeback)
                return Done(ContinueComeback(s, random, x, a, b));
            if (x.Kind is ExchangeKind.Preparation or ExchangeKind.Transition or ExchangeKind.GripCombo or ExchangeKind.Ringside or ExchangeKind.TopContest)
                return Done(ContinueSituation(s, random, x, a, b));
            if (x.Kind == ExchangeKind.Chain) return Done(ContinueChain(s, random, x, a, b));
            if (x.Kind is ExchangeKind.RopeSequence or ExchangeKind.CornerSequence)
                return Done(ContinueMovement(s, random, x, a, b));
            if (x.Kind == ExchangeKind.Counter)
            {
                s.Exchange = null;
                if (a.IsDowned) return Done(Resolve(s, Action(MatchActionType.Recover, a, b), random));
                var e = Resolve(s, Action(b.IsDowned ? MatchActionType.GroundAttack : random.NextDouble() < .5 ? MatchActionType.Grapple : MatchActionType.BasicStrike, a, b), random); e.Type = MatchActionType.Counter;
                if (e.Impact > 0) TryComeback(s, a, b);
                return Done(e);
            }
            if (x.Kind == ExchangeKind.FollowUp)
            {
                var groundedChoices = a.IsDowned ? Actions(s, a, b) : null;
                s.Exchange = null;
                bool bridge = MatchMoveMotion.IsBridgingSuplex(x.MoveId) && a.GripTargetId == b.Id &&
                    s.Rules.Pinfall && b.Posture == MatchPosture.Supine && s.RefereeUnavailableUntil <= s.ElapsedSeconds;
                if (a.IsDowned && !bridge && !groundedChoices.Any(action => action.Type != MatchActionType.Recover))
                {
                    if (MatchMoveMotion.IsBridgingSuplex(x.MoveId)) { x.MoveId = null; x.ActionType = default; }
                    s.Exchange = x;
                    return Done(Resolve(s, Action(MatchActionType.Recover, a, b), random));
                }
                if (a.Position == MatchPosition.TopRope)
                {
                    if (b.IsDowned && b.Posture is MatchPosture.Seated or MatchPosture.Kneeling && InRing(b) &&
                        !MatchHealth.Restricted(a, BodyRegion.Legs) && !MatchHealth.Restricted(b, BodyRegion.Legs) &&
                        !MatchHealth.Restricted(b, BodyRegion.Arms) &&
                        (string.IsNullOrEmpty(x.MoveId) ||
                         (MatchTechniques.Find(x.MoveId)?.Rules ?? s.FindMove?.Invoke(x.MoveId))?.Situation == MoveSituation.DivingStanding))
                    {
                        s.Exchange = x;
                        return Done(Resolve(s, Action(MatchActionType.Recover, b, a), random));
                    }
                    if (InRing(b) && !x.Booked && !s.BookingSequence && b.Stun < 25 && !MatchHealth.Restricted(b, BodyRegion.Legs) && !MatchHealth.Restricted(b, BodyRegion.Arms) && random.NextDouble() < .45)
                    {
                        if (b.IsDowned)
                        {
                            s.Exchange = x;
                            return Done(Resolve(s, Action(MatchActionType.Recover, b, a), random));
                        }
                        b.Position = MatchPosition.TopRope; Spend(b, 2);
                        Start(s, ExchangeKind.TopContest, b, a);
                        return Done(Event(s, MatchActionType.BreakUp, MatchActionResult.Interrupted, b, a, 4, detail: "aerial-interrupted"));
                    }
                    if (!InRing(b) || (!string.IsNullOrEmpty(x.MoveId) ? !CanMove(s, a, b, x.MoveId) : MatchTechniques.Candidates(MatchActionType.Dive, a, b).Count == 0))
                    {
                        a.Position = MatchPosition.Center;
                        if (InRing(b) && !b.IsDowned) Start(s, ExchangeKind.Counter, b, a);
                        return Done(Event(s, MatchActionType.Climb, MatchActionResult.Interrupted, a, b, 3, detail: "aerial-setup-lost"));
                    }
                    var dive = Resolve(s, Action(string.IsNullOrEmpty(x.MoveId) ? MatchActionType.Dive : x.ActionType, a, b, x.MoveId), random);
                    if (s.Exchange != null) { s.Exchange.Booked = x.Booked; s.Exchange.Id = x.Id; }
                    return Done(dive);
                }
                if (!bridge && !x.Booked && x.Beats == 1 && b.LastImpact >= 9 && !b.LastImpactWasFinisher && random.NextDouble() < .3)
                {
                    s.Exchange = x; RecoverFatigue(a, 4);
                    return Done(Event(s, MatchActionType.Rest, MatchActionResult.Held, a, b, 4, detail: "crowd-pause"));
                }
                if (s.Rules.Pinfall && b.IsDowned && s.RefereeUnavailableUntil <= s.ElapsedSeconds &&
                    (x.Booked || x.ActionType == MatchActionType.Pin || s.Participants.Count == 2 && !s.Rules.Tag && b.LastImpactWasFinisher &&
                     s.ElapsedSeconds - b.LastImpactTime <= 30 && CanRetryCover(s, a, b)))
                {
                    var cover = Resolve(s, Action(MatchActionType.Pin, a, b, bridge ? x.MoveId : null), random);
                    s.Exchange.Booked = x.Booked; s.Exchange.Id = x.Id;
                    return Done(cover);
                }
                var followUps = (groundedChoices ?? Actions(s, a, b)).Where(action => action.Type is MatchActionType.GroundAttack or MatchActionType.LimbAttack or
                    MatchActionType.Submission or MatchActionType.StandingHold or MatchActionType.Lift or MatchActionType.TurnOver or MatchActionType.Rest or
                    MatchActionType.Pin or MatchActionType.Reposition or MatchActionType.Climb or MatchActionType.Recover).ToList();
                foreach (var action in followUps) action.Score = ActionEvaluator.Score(s, action);
                return Done(Resolve(s, ActionSelector.Select(followUps, random), random));
            }
            if (x.Kind == ExchangeKind.CountOut)
            {
                if (x.MoveId == "disqualification")
                {
                    s.Exchange = null;
                    return Done(Award(s, a, b, MatchActionResult.Disqualification, MatchFinishType.Disqualification, true));
                }
                if (InRing(b)) { s.Exchange = null; return Done(Event(s, MatchActionType.Reposition, MatchActionResult.Escaped, b, a, 2)); }
                if (s.ElapsedSeconds - x.StartedAt >= s.Rules.CountOutSeconds)
                {
                    s.Exchange = null;
                    var finish = Award(s, a, b, MatchActionResult.CountOut, MatchFinishType.CountOut, x.Booked);
                    finish.RefereeCountFrom = x.RefereeCount + 1; finish.RefereeCount = 10;
                    return Done(finish);
                }
                var hold = Event(s, MatchActionType.Hold, MatchActionResult.Held, a, b, 2, detail: "count-out");
                int count = Math.Min(9, (s.ElapsedSeconds - x.StartedAt + hold.DurationSeconds) * 10 / s.Rules.CountOutSeconds);
                if (count > x.RefereeCount)
                {
                    hold.RefereeCountFrom = x.RefereeCount + 1; hold.RefereeCount = count;
                    x.RefereeCount = count;
                }
                return Done(hold);
            }
            if (x.Kind == ExchangeKind.Object)
            {
                if (!x.Booked && b.IsDowned && b.Stun < 12)
                {
                    x.Beats--;
                    return Done(Resolve(s, Action(MatchActionType.Recover, b, a), random));
                }
                if (!x.Booked && !b.IsDowned && random.NextDouble() < .65)
                {
                    a.IsDowned = true; a.Stun = Clamp(a.Stun + 15); s.Exchange = null;
                    return Done(Event(s, MatchActionType.BreakUp, MatchActionResult.Interrupted, b, a, 4, detail: "object"));
                }
                if (x.Beats < 3 && (!x.Booked || s.TargetDurationSeconds - s.ElapsedSeconds > 3))
                    return Done(Event(s, MatchActionType.ObjectClimb, MatchActionResult.Held, a, b, 5));
                s.Exchange = null;
                if (s.Booking != null && !x.Booked) return Done(Event(s, MatchActionType.ObjectFinish, MatchActionResult.Blocked, a, b, 3, detail: "object-retreat"));
                var finish = s.Rules.Objective switch { MatchObjective.Escape => MatchFinishType.Escape, MatchObjective.Table => MatchFinishType.TableBreak, _ => MatchFinishType.ObjectRetrieval };
                var outcome = finish switch { MatchFinishType.Escape => MatchActionResult.CageEscaped, MatchFinishType.TableBreak => MatchActionResult.TableBroken, _ => MatchActionResult.ObjectRetrieved };
                return Done(Award(s, a, b, outcome, finish, x.Booked));
            }
            if (s.Rules.RopeBreak && MatchMovement.CanReachRopes(b))
            {
                b.Position = MatchPosition.Ropes; s.Exchange = null;
                return Done(Event(s, MatchActionType.RopeBreak, MatchActionResult.RopeBreak, b, a, 2));
            }
            if (x.Booked && !s.Rules.IronMan && !s.RescueRequired && s.ElapsedSeconds + 3 < s.EarliestFinishSeconds &&
                (s.Rules.Elimination ? s.GetOpponents(a.Id).Count == 1 :
                    s.Participants.Where(p => p.SideId == a.SideId).Sum(p => p.Falls) >= s.Rules.FallsToWin - 1))
                x.Booked = false;
            if (!x.Booked)
            {
                var helper = s.Participants.FirstOrDefault(p => p.Id != a.Id && p.Id != b.Id && !p.IsEliminated && !p.IsDowned &&
                    p.Stun < 12 && p.Position != MatchPosition.Outside && p.SideId != a.SideId);
                if (helper != null && random.NextDouble() < .30)
                {
                    s.Exchange = null;
                    return Done(Event(s, MatchActionType.BreakUp, MatchActionResult.Interrupted, helper, a, 2, detail: b.Id));
                }
            }
            if (x.Kind == ExchangeKind.Cover)
            {
                s.Exchange = null;
                if (s.RefereeUnavailableUntil > s.ElapsedSeconds)
                    return Done(Event(s, MatchActionType.Pin, MatchActionResult.Interrupted, a, b, 3, detail: "referee"));
                var decay = (float)Math.Exp(-Math.Max(0, s.ElapsedSeconds - b.LastImpactTime) / 18f);
                var chance = ActionResolver.PinfallChance(s, a, b, b.LastImpact * decay, b.LastImpactWasFinisher && decay > .4f);
                if (x.Booked || random.NextDouble() < chance && CanAward(s, a, b))
                    return Done(Award(s, a, b, x.Booked && s.Booking.FinishType == MatchFinishType.RollUp ? MatchActionResult.RollUp : MatchActionResult.Pinfall,
                        x.Booked ? s.Booking.FinishType : MatchFinishType.Pinfall, x.Booked));
                var danger = chance + (b.LastImpactWasFinisher ? .25f : 0);
                var outcome = danger > .4f ? MatchActionResult.NearFall : danger > .16f ? MatchActionResult.TwoCount : MatchActionResult.OneCount;
                b.MatchControl = Clamp(b.MatchControl + 4);
                return Done(Event(s, MatchActionType.Pin, outcome, a, b, outcome == MatchActionResult.OneCount ? 1 : 3));
            }
            // Holds persist across several decisions; limb wear and ring position survive escape.
            Hurt(b, x.Region, 1.5f); Spend(a, .7f); Spend(b, 1);
            if (x.Booked || random.NextDouble() < .03 + Damage(b, x.Region) * .003 && CanAward(s, a, b))
            {
                s.Exchange = null;
                return Done(Award(s, a, b, !s.Rules.IronMan && b.HeadDamage > 60 ? MatchActionResult.PassedOut : MatchActionResult.Submitted, MatchFinishType.Submission, x.Booked));
            }
            if (x.Beats >= 3 || random.NextDouble() < .20 + b.Attributes.Technical * .008)
            {
                s.Exchange = null;
                if (!b.IsDowned)
                {
                    Start(s, ExchangeKind.Transition, b, a, region: BodyRegion.Arms);
                    s.Exchange.ActionType = MatchActionType.LimbAttack;
                }
                TryComeback(s, b, a);
                return Done(Event(s, MatchActionType.Hold, MatchActionResult.Escaped, b, a, 4, detail: !b.IsDowned ? "standing-hold-break" : null));
            }
            if (s.Rules.RopeBreak && random.NextDouble() < .4) b.Position = MatchPosition.Ropes;
            return Done(Event(s, MatchActionType.Hold, MatchActionResult.Held, a, b, 6, 1, b.Position == MatchPosition.Ropes ? "reach-ropes" : "hold"));
        }

        private static MatchEngineEvent ContinueSituation(MatchEngineState s, Random random, MatchExchange x,
            MatchEngineParticipant a, MatchEngineParticipant b)
        {
            if (x.Kind == ExchangeKind.Ringside)
            {
                MatchEngineEvent ReturnToRing(string detail = "ringside-return")
                {
                    s.Exchange = null; s.AnnounceTableReady = false; a.Position = b.Position = MatchPosition.Center;
                    return Event(s, MatchActionType.Reposition, MatchActionResult.FullSuccess, b, a, 2, detail: detail);
                }
                int outsideSeconds = s.ElapsedSeconds - x.StartedAt;
                bool HasTime(int seconds) => !s.Rules.CountOut || outsideSeconds + seconds <= s.Rules.CountOutSeconds;
                MatchEngineEvent PrepareDive()
                {
                    bool reenter = x.ActionType == MatchActionType.Climb;
                    var technique = MatchTechniques.Select(s, MatchActionType.Dive, b, a, random);
                    x.MoveId = technique.Id; x.ActionType = MatchActionType.Dive;
                    bool top = !MatchTechniques.IsRopeDive(technique.Rules.Situation);
                    b.Position = top ? MatchPosition.TopRope : MatchPosition.Ropes; Spend(b, 2);
                    var setup = Event(s, top ? MatchActionType.Climb : MatchActionType.RopeRun, MatchActionResult.FullSuccess, b, a,
                        reenter ? 3 : 1, detail: top ? "outside-climb" : "outside-run");
                    setup.TechniqueName = technique.Rules.Name; return setup;
                }
                if (!Legal(s, a) || !Legal(s, b) || s.TargetDurationSeconds - s.ElapsedSeconds <= 95)
                    return ReturnToRing();
                if (x.ActionType == MatchActionType.ExitRing)
                {
                    if (b.IsDowned || MatchHealth.Restricted(b, BodyRegion.Legs) || b.Fatigue >= 75 || a.Position != MatchPosition.Outside)
                        return ReturnToRing();
                    b.Position = MatchPosition.Outside; Spend(b, 1); x.StartedAt = s.ElapsedSeconds; x.RefereeCount = 0;
                    x.ActionType = default;
                    return Event(s, MatchActionType.Pursue, MatchActionResult.FullSuccess, b, a, 2, detail: "ringside-resume");
                }
                if (x.ActionType == MatchActionType.Climb)
                {
                    if (!HasTime(8) || b.IsDowned || !a.IsDowned) return ReturnToRing();
                    b.Position = MatchPosition.Center;
                    if (MatchTechniques.Candidates(MatchActionType.Dive, b, a).Count == 0) return ReturnToRing();
                    return PrepareDive();
                }
                if (s.Rules.CountOut && x.ActionType is not (MatchActionType.Dive or MatchActionType.ObjectSetup) &&
                    a.Position == MatchPosition.Outside && b.Position == MatchPosition.Outside && !a.IsDowned && !b.IsDowned &&
                    outsideSeconds >= s.Rules.CountOutSeconds * .5f && HasTime(2) && x.CountOutResets == 0 &&
                    !MatchHealth.Restricted(b, BodyRegion.Legs) && b.Fatigue < 75 && s.TargetDurationSeconds - s.ElapsedSeconds > 150 &&
                    random.NextDouble() < .25 + b.Attributes.RingPsychology * .015)
                {
                    b.Position = MatchPosition.Center; Spend(b, 1); x.CountOutResets++;
                    x.StartedAt = s.ElapsedSeconds; x.RefereeCount = 0; x.ActionType = MatchActionType.ExitRing;
                    return Event(s, MatchActionType.Reposition, MatchActionResult.FullSuccess, b, a, 2, detail: "ringside-count-reset");
                }
                if (!HasTime(3)) return ReturnToRing();
                int count = s.Rules.CountOut ? Math.Min(9, outsideSeconds * 10 / s.Rules.CountOutSeconds) : 0;
                if (count > x.RefereeCount && x.ActionType is not (MatchActionType.Dive or MatchActionType.ObjectSetup) &&
                    s.Events.LastOrDefault()?.Detail != "ringside-count")
                {
                    var call = Event(s, MatchActionType.Hold, MatchActionResult.Held, null, a, 1, detail: "ringside-count");
                    call.RefereeCountFrom = x.RefereeCount + 1; call.RefereeCount = count; x.RefereeCount = count; x.Beats--;
                    return call;
                }
                if (x.ActionType == MatchActionType.Dive)
                {
                    if (!HasTime(5) || a.Position != MatchPosition.Outside || b.IsDowned || !CanMove(s, b, a, x.MoveId)) return ReturnToRing();
                    var dive = Resolve(s, Action(MatchActionType.Dive, b, a, x.MoveId, continuation: true), random);
                    s.Exchange = x; x.ActionType = MatchActionType.Reposition;
                    return dive;
                }
                if (x.ActionType == MatchActionType.ObjectSetup)
                {
                    if (!HasTime(9) || a.IsDowned || b.IsDowned || !CanMove(s, b, a, "move_330")) return ReturnToRing();
                    x.ActionType = MatchActionType.Reposition;
                    if (a.Stun < 20 && random.NextDouble() < .2 + a.Attributes.Technical * .005)
                    {
                        s.AnnounceTableReady = false;
                        return Event(s, MatchActionType.BreakUp, MatchActionResult.Interrupted, a, b, 3, detail: "announce-table-blocked");
                    }
                    var table = Resolve(s, Action(MatchActionType.RingsideAttack, b, a, "move_330", continuation: true), random);
                    s.Exchange = x; return table;
                }
                if (x.ActionType == MatchActionType.Reposition)
                {
                    var down = b.IsDowned ? b : a.IsDowned ? a : null;
                    if (down != null && x.Beats < 5 && HasTime(12))
                        return Resolve(s, Action(MatchActionType.Recover, down, down == a ? b : a), random);
                    return ReturnToRing();
                }
                if (x.Beats >= 6 || a.Position != MatchPosition.Outside) return ReturnToRing();
                if (InRing(b))
                {
                    if (b.IsDowned || MatchHealth.Restricted(b, BodyRegion.Legs) || b.Fatigue >= 80) return ReturnToRing("ringside-wait");
                    if (!s.Events.Any(e => e.ExchangeId == x.Id && e.Detail == "ringside-stall") &&
                        a.Alignment == KayfabeAlignment.Heel && a.Stun < 30 && HasTime(5) &&
                        random.NextDouble() < .25 + a.Attributes.HeelWork * .015)
                    {
                        RecoverFatigue(a, 1);
                        return Event(s, MatchActionType.Retreat, MatchActionResult.Held, a, b, 1, detail: "ringside-stall");
                    }
                    if (random.NextDouble() < .1 + b.Attributes.RingPsychology * .006)
                        return ReturnToRing("ringside-wait");
                    var dives = MatchTechniques.Candidates(MatchActionType.Dive, b, a);
                    if (dives.Count > 0 && HasTime(6) &&
                        !s.Events.Any(e => e.Detail is "outside-run" or "outside-climb") &&
                        random.NextDouble() < .2 + b.Attributes.HighFlying * .015)
                    {
                        return PrepareDive();
                    }
                    if (!HasTime(6)) return ReturnToRing("ringside-wait");
                    b.Position = MatchPosition.Outside; Spend(b, 1);
                    return Event(s, MatchActionType.Pursue, MatchActionResult.FullSuccess, b, a, 1, detail: "ringside-pursuit");
                }
                if (a.IsDowned || b.IsDowned) return ReturnToRing();
                var tableMove = MatchTechniques.Find("move_330");
                if (s.AnnounceTableEligible && !s.AnnounceTableBroken && s.Rules.ImportantMatch && !s.Rules.Disqualification &&
                    s.Rules.Objective == MatchObjective.PinOrSubmission && HasTime(17) &&
                    s.ElapsedSeconds >= s.TargetDurationSeconds * .4 && s.TargetDurationSeconds - s.ElapsedSeconds > 150 &&
                    !s.Events.Any(e => e.Detail == "announce-table-setup") && b.Fatigue < 75 && !MatchHealth.Restricted(b, BodyRegion.Arms) &&
                    tableMove?.Enabled == true && tableMove.MeetsRequirement(b.Eligibility) &&
                    (!s.Rules.CountOut || s.Rules.CountOutSeconds >= 30))
                {
                    s.AnnounceTableReady = true; x.ActionType = MatchActionType.ObjectSetup; Spend(b, 2);
                    return Event(s, MatchActionType.ObjectSetup, MatchActionResult.Held, b, a, 8, detail: "announce-table-setup");
                }
                if (!HasTime(4)) return ReturnToRing();
                var ringside = Resolve(s, Action(MatchActionType.RingsideAttack, b, a, continuation: true), random);
                s.Exchange = x; x.ActionType = MatchActionType.Reposition;
                if (a.IsDowned && !b.IsDowned && b.Eligibility.HighFlying >= 10 && !MatchHealth.Restricted(b, BodyRegion.Legs) && b.Fatigue < 70 &&
                    HasTime(10) && !s.Events.Any(e => e.Detail is "outside-run" or "outside-climb") && random.NextDouble() < .25)
                    x.ActionType = MatchActionType.Climb;
                return ringside;
            }
            if (!Legal(s, a) || !Legal(s, b) || a.IsDowned || !InRing(a) || !InRing(b))
            {
                s.Exchange = null;
                return Event(s, MatchActionType.Rest, MatchActionResult.Interrupted, a, b, 2, detail: "exchange-interrupted");
            }
            if (x.Kind == ExchangeKind.TopContest)
            {
                s.Exchange = null;
                a.Position = b.Position = MatchPosition.Center;
                if (MatchHealth.Restricted(a, BodyRegion.Legs) || MatchHealth.Restricted(a, BodyRegion.Arms) || MatchTechniques.Candidates(MatchActionType.TopRopeAttack, a, b).Count == 0)
                    return Event(s, MatchActionType.Reposition, MatchActionResult.Interrupted, a, b, 3, detail: "corner-disengage");
                var top = Resolve(s, Action(MatchActionType.TopRopeAttack, a, b, continuation: true), random);
                if (top.Impact > 0) { Hurt(a, BodyRegion.Body, 2); Spend(a, 2); }
                return top;
            }
            if (x.Kind == ExchangeKind.GripCombo && x.Beats == 1)
            {
                b.IsDowned = false; Spend(a, 1.5f);
                return Event(s, MatchActionType.Lift, MatchActionResult.Held, a, b, 3, detail: "retained-grip");
            }
            if (x.Kind is ExchangeKind.Preparation or ExchangeKind.GripCombo)
            {
                if (b.Stun < 30 && (!b.IsDowned || x.Kind == ExchangeKind.Preparation && x.ActionType is MatchActionType.GroundAttack or MatchActionType.LimbAttack or MatchActionType.Submission) &&
                    random.NextDouble() < .18 + b.Attributes.Technical * .006 - a.MatchControl * .001)
                {
                    Start(s, ExchangeKind.Counter, b, a);
                    var blocked = Event(s, MatchActionType.PrepareAttack, MatchActionResult.Blocked, a, b, 3, detail: "grip-broken");
                    blocked.TechniqueName = MatchTechniques.Find(x.MoveId)?.Rules.Name ?? (x.MoveId == null ? null : s.FindMove?.Invoke(x.MoveId)?.Name);
                    return blocked;
                }
                if (x.MoveId == null ? !Actions(s, a, b).Any(action => action.Type == x.ActionType) : !CanMove(s, a, b, x.MoveId))
                {
                    s.Exchange = null;
                    return Event(s, MatchActionType.PrepareAttack, MatchActionResult.Interrupted, a, b, 2, detail: "setup-lost");
                }
                if (x.Kind == ExchangeKind.Preparation) s.Exchange = null;
                var follow = Resolve(s, Action(x.Kind == ExchangeKind.GripCombo ? MatchActionType.Finisher : x.ActionType,
                    a, b, x.MoveId, continuation: true), random);
                if (x.Kind == ExchangeKind.Preparation && x.ActionType is MatchActionType.GroundAttack or MatchActionType.LimbAttack or MatchActionType.Submission or MatchActionType.StandingHold)
                    follow.MoveId = null;
                if (x.Kind == ExchangeKind.GripCombo) follow.Detail = "grip-follow-up";
                return follow;
            }
            s.Exchange = null;
            var focus = a.TargetRegion;
            if (x.ActionType == MatchActionType.LimbAttack) a.TargetRegion = x.Region;
            var transition = Resolve(s, Action(x.ActionType == MatchActionType.LimbAttack ? MatchActionType.LimbAttack :
                b.IsDowned ? MatchActionType.GroundAttack : random.NextDouble() < .5 ? MatchActionType.BasicStrike : MatchActionType.Grapple,
                a, b, continuation: true), random);
            a.TargetRegion = focus;
            transition.Detail = "transition";
            return transition;
        }
        private static MatchEngineEvent ContinueMovement(MatchEngineState s, Random random, MatchExchange x,
            MatchEngineParticipant a, MatchEngineParticipant b)
        {
            if (!Legal(s, a) || !Legal(s, b) || a.IsDowned || !InRing(a) || !InRing(b))
            {
                s.Exchange = null;
                return Event(s, MatchActionType.Rest, MatchActionResult.Interrupted, a, b, 2, detail: "movement-interrupted");
            }
            if (x.Kind == ExchangeKind.CornerSequence)
            {
                if (x.Beats == 1 && random.NextDouble() < .55)
                {
                    s.Exchange = null; a.Position = MatchPosition.Corner;
                    var chops = Resolve(s, Action(MatchActionType.BasicStrike, a, b), random);
                    chops.Type = MatchActionType.CornerAttack; chops.Detail = "corner-chops";
                    if (s.Exchange == null) { s.Exchange = x; x.ActionType = MatchActionType.CornerAttack; }
                    return chops;
                }
                if (x.Beats == 2 && x.ActionType == MatchActionType.CornerAttack)
                {
                    a.Position = MatchPosition.Center; Spend(a, 1);
                    return Event(s, MatchActionType.Retreat, MatchActionResult.Held, a, b, 3, detail: "corner-run-up");
                }
                s.Exchange = null;
                var dodge = !b.IsDowned && !MatchHealth.Restricted(b, BodyRegion.Legs) && random.NextDouble() <
                    Math.Max(.05, .18 + (b.Attributes.HighFlying - a.Attributes.Brawling) * .008 - b.Stun * .002);
                a.Position = MatchPosition.Corner;
                if (dodge)
                {
                    b.Position = MatchPosition.Center; Spend(a, 2); Spend(b, 1);
                    Start(s, ExchangeKind.Counter, b, a);
                    return Event(s, MatchActionType.CornerAttack, MatchActionResult.Escaped, a, b, 4, detail: "missed-charge");
                }
                var charge = Resolve(s, Action(b.IsDowned ? MatchActionType.GroundAttack : MatchActionType.CornerAttack, a, b, continuation: true), random);
                charge.Type = MatchActionType.CornerAttack; charge.Detail = "corner-charge";
                return charge;
            }
            if (x.ActionType == MatchActionType.IrishWhip && x.Beats is 1 or 3)
            {
                b.Position = MatchPosition.Center; Spend(b, 1.5f);
                return Event(s, MatchActionType.RopeRun, MatchActionResult.FullSuccess, b, a, 3, detail: "rebound");
            }
            if (x.ActionType == MatchActionType.RopeRun && x.Beats == 1)
            {
                a.Position = MatchPosition.Center; Spend(a, 1.5f);
                return Event(s, MatchActionType.RopeRun, MatchActionResult.FullSuccess, a, b, 3, detail: "rebound");
            }
            if (x.ActionType == MatchActionType.IrishWhip && x.Beats == 2 && !b.IsDowned && !MatchHealth.Restricted(a, BodyRegion.Legs) &&
                random.NextDouble() < .22 + a.Attributes.HighFlying * .006 - a.Fatigue * .001)
            {
                b.Position = MatchPosition.Ropes; Spend(a, 1); Spend(b, 1.5f);
                var leap = a.Attributes.HighFlying >= a.Attributes.Power && !MatchHealth.Restricted(a, BodyRegion.Legs);
                return Event(s, MatchActionType.Evade, MatchActionResult.Escaped, a, b, 3, detail: leap ? "leapfrog" : "duck-under");
            }
            s.Exchange = null;
            a.Position = MatchPosition.Center; b.Position = MatchPosition.Center;
            bool grounded = b.IsDowned;
            var attack = Resolve(s, Action(MatchActionType.RunningAttack, a, b, continuation: true), random);
            attack.Type = MatchActionType.RunningAttack;
            attack.Detail = x.ActionType == MatchActionType.IrishWhip ? "rebound-catch" : grounded ? "running-drop" : "running-strike";
            return attack;
        }
        private static MatchEngineEvent ContinueChain(MatchEngineState s, Random random, MatchExchange x, MatchEngineParticipant a, MatchEngineParticipant b)
        {
            MatchEngineEvent Named(string id, MatchActionResult result)
            {
                var e = Event(s, MatchActionType.Chain, result, a, b, 6);
                e.TechniqueId = id; e.TechniqueName = MatchTechniques.Find(id).Rules.Name;
                return e;
            }
            Spend(a, .6f); Spend(b, .6f);
            if (x.Beats >= 5 || s.ElapsedSeconds >= MatchOpening.MaximumSeconds(s) || s.TargetDurationSeconds - s.ElapsedSeconds <= 55 || random.NextDouble() < .12)
            {
                s.Exchange = null; s.Opening.CompletedChains++;
                return Named("chain-reset", MatchActionResult.Escaped);
            }
            if (x.ChainPosition != ChainPosition.TieUp && random.NextDouble() < .25 + (b.Attributes.Technical-a.Attributes.Technical)*.018)
            {
                (a, b) = (b, a); x.ActorId = a.Id; x.TargetId = b.Id;
                a.MatchControl = Clamp(a.MatchControl+4); b.MatchControl = Clamp(b.MatchControl-4);
                var wrist = x.ChainPosition is ChainPosition.WristLock or ChainPosition.Hammerlock;
                x.ChainPosition = wrist ? ChainPosition.WristLock : ChainPosition.WaistLock;
                return Named(wrist ? "chain-rollthrough" : "chain-switch", MatchActionResult.FullSuccess);
            }
            if (x.ChainPosition is ChainPosition.Headlock or ChainPosition.WaistLock)
            {
                Hit(s, b, BodyRegion.Body, 3, false); b.IsDowned = true;
                a.MatchControl = Clamp(a.MatchControl+6); b.MatchControl = Clamp(b.MatchControl-6);
                s.Exchange = null; s.Opening.CompletedChains++;
                return Named(x.ChainPosition == ChainPosition.Headlock ? "chain-headlock-takedown" : "chain-waistlock-takedown", MatchActionResult.FullSuccess);
            }
            x.ChainPosition = x.ChainPosition switch
            {
                ChainPosition.TieUp => random.NextDouble() < .5 ? ChainPosition.Headlock : ChainPosition.WristLock,
                ChainPosition.WristLock => ChainPosition.Hammerlock,
                _ => ChainPosition.WaistLock
            };
            a.MatchControl = Clamp(a.MatchControl+3); b.MatchControl = Clamp(b.MatchControl-3);
            return Named(x.ChainPosition switch { ChainPosition.Headlock => "chain-headlock", ChainPosition.WristLock => "chain-wristlock", ChainPosition.Hammerlock => "chain-hammerlock", _ => "chain-waistlock" }, MatchActionResult.Held);
        }

        private static bool CanAward(MatchEngineState s, MatchEngineParticipant a, MatchEngineParticipant b)
        {
            if (s.RefereeUnavailableUntil > s.ElapsedSeconds) return false;
            if (s.Booking == null) return true;
            if (s.Rules.IronMan) return s.ElapsedSeconds < s.TargetDurationSeconds - 60 &&
                s.Participants.Where(x => x.SideId == a.SideId).Sum(x => x.Falls) <= s.GetParticipant(s.Booking.WinnerId).Falls;
            if (s.Rules.Elimination) return b.Id != s.Booking.WinnerId && b.Id != s.Booking.LoserId && s.GetOpponents(a.Id).Count > 1;
            return s.Participants.Where(x => x.SideId == a.SideId).Sum(x => x.Falls) < s.Rules.FallsToWin - 1;
        }

        private static MatchEngineEvent Award(MatchEngineState s, MatchEngineParticipant a, MatchEngineParticipant b,
            MatchActionResult outcome, MatchFinishType finish, bool booked)
        {
            if (!s.Rules.IronMan && s.ElapsedSeconds + 3 > s.LatestFinishSeconds)
            {
                s.IsAborted = true; s.FailureReason = "A legal finish could not complete within the allowed time.";
                return Event(s, MatchActionType.Rest, MatchActionResult.Interrupted, a, b, 1);
            }
            a.Falls++;
            if (s.Rules.Elimination) b.IsEliminated = true;
            var score = s.Participants.Where(x => x.SideId == a.SideId).Sum(x => x.Falls);
            var complete = s.Rules.IronMan ? false :
                s.Rules.Elimination ? s.GetOpponents(a.Id).Count == 0 : score >= s.Rules.FallsToWin;
            if (complete) return End(s, a, b, outcome, finish, 3);
            s.Exchange = null;
            // A fall is not a recovery reset: fatigue and limb damage carry into the next fall.
            if (s.Rules.Elimination && s.Rules.Tag && !s.Rules.Tornado)
            {
                var replacement = s.Participants.FirstOrDefault(x => x.SideId == b.SideId && !x.IsEliminated);
                if (replacement != null) { replacement.IsLegal = true; replacement.Position = MatchPosition.Center; }
            }
            return Event(s, MatchActionType.Fall, outcome, a, b, 5);
        }

        private static MatchEngineEvent End(MatchEngineState s, MatchEngineParticipant a, MatchEngineParticipant b,
            MatchActionResult outcome, MatchFinishType finish, int seconds)
        {
            s.IsFinished = true; s.WinnerId = a?.Id; s.ActualFinish = finish; s.Phase = MatchPhase.Finish; s.Exchange = null;
            return Event(s, MatchActionType.Finish, outcome, a, b, seconds);
        }

        private static MatchEngineEvent TimeExpired(MatchEngineState s)
        {
            var leader = s.Rules.IronMan ? s.Participants.GroupBy(p => p.SideId).OrderByDescending(g => g.Sum(p => p.Falls)).ToList() : null;
            var winner = leader != null && leader[0].Sum(p => p.Falls) > leader[1].Sum(p => p.Falls) ? leader[0].First() : null;
            if (s.Rules.IronMan && s.Booking?.WinnerId != null && winner?.SideId != s.GetParticipant(s.Booking.WinnerId).SideId)
            { s.IsAborted = true; s.FailureReason = "Booked Iron Man side did not lead at time expiry."; }
            return End(s, winner, null, winner == null ? MatchActionResult.Draw : MatchActionResult.TimeLimitDecision,
                winner == null ? MatchFinishType.Draw : s.Booking?.FinishType ?? MatchFinishType.Pinfall, 1);
        }

        public static int BookedPreparationSeconds(MatchEngineState s)
        {
            var a = s.GetParticipant(s.Booking.WinnerId); var b = s.GetParticipant(s.Booking.LoserId);
            var finish = s.Booking.FinishType;
            var id = a.FinisherMoveIds.Concat(new[] { a.FinisherMoveId }).Concat(a.SignatureMoveIds).Concat(new[] { a.SignatureMoveId })
                .Where(id => !string.IsNullOrEmpty(id) && UsableAssignedMove(s, a, id))
                .OrderByDescending(id => id == a.PlannedFinisherMoveId)
                .FirstOrDefault(id => (Kind(s.FindMove?.Invoke(id) ?? MatchTechniques.Find(id)?.Rules ?? new MatchMoveRules()) == MoveKind.Submission) == (finish == MatchFinishType.Submission));
            var move = string.IsNullOrEmpty(id) ? null : s.FindMove?.Invoke(id) ?? MatchTechniques.Find(id)?.Rules;
            var kind = move == null ? finish == MatchFinishType.Submission ? MoveKind.Submission : MoveKind.Strike : Kind(move);
            int seconds = finish switch
            {
                MatchFinishType.RollUp => CoverSeconds(a) + 4,
                MatchFinishType.Submission => 10,
                MatchFinishType.CountOut => s.Rules.CountOutSeconds + 6,
                MatchFinishType.Disqualification => 6,
                MatchFinishType.Escape or MatchFinishType.ObjectRetrieval or MatchFinishType.TableBreak => s.ObjectReady ? 17 : 22,
                _ => (kind == MoveKind.Aerial ? 5 : 7) + CoverSeconds(a) + 3
            };
            if (a.IsDowned) seconds += RecoverySeconds(a) * (a.Posture == MatchPosture.Kneeling ? 1 : a.Posture == MatchPosture.Seated ? 2 : 3);
            if (!Legal(s, a) || !Legal(s, b)) seconds += 8;
            if (!InRing(a) || !InRing(b) || MatchMovement.DistanceFromCenter(b) > .25f) seconds += 4;
            if (kind == MoveKind.Aerial && a.Position != MatchPosition.TopRope) seconds += 5;
            if (move != null && !CanMove(s, a, b, id)) seconds += b.IsDowned ? 4 : 7;
            if (move?.EndPosture is MatchPosture.Prone or MatchPosture.Seated or MatchPosture.Kneeling) seconds += 3;
            if ((a.Stun >= 12 || a.Fatigue >= 80 || a.RealInjury >= InjuryStatus.Moderate) &&
                (id == null || kind == MoveKind.Aerial || MatchMoveMotion.ActorEndPosture(id) != MatchPosture.Standing && !MatchMoveMotion.IsBridgingSuplex(id))) seconds += RecoverySeconds(a) * 3;
            int falls = s.Rules.Elimination ? s.GetOpponents(a.Id).Count : s.Rules.IronMan ?
                Math.Max(1, s.Participants.Where(p => p.SideId != a.SideId).GroupBy(p => p.SideId).Max(g => g.Sum(p => p.Falls)) -
                    s.Participants.Where(p => p.SideId == a.SideId).Sum(p => p.Falls) + 1) :
                Math.Max(1, s.Rules.FallsToWin - s.Participants.Where(p => p.SideId == a.SideId).Sum(p => p.Falls));
            seconds += (falls - 1) * 18;
            // Short matches get a proportionate margin; low improvisation needs more preparation.
            return seconds + 7 + RecoverySeconds(a) * 3 + Math.Min(s.TargetDurationSeconds / 10, seconds / 2) + Math.Min(s.TargetDurationSeconds / 20, 3 + (int)((40 - a.Attributes.RingImprovisation - a.Attributes.RingPsychology) / 4)) +
                (s.Rules.IronMan ? 30 : 0);
        }

        private static MatchEngineEvent BookedAction(MatchEngineState s, Random random)
        {
            var a = s.GetParticipant(s.Booking.WinnerId); var b = s.GetParticipant(s.Booking.LoserId);
            if (s.Rules.Elimination)
                b = s.GetOpponents(a.Id).FirstOrDefault(x => x.Id != s.Booking.LoserId && Legal(s, x)) ?? b;
            var remaining = s.TargetDurationSeconds - s.ElapsedSeconds - (s.Rules.IronMan ? 30 : 0);
            foreach (var p in new[] { a, b })
            {
                if (!Legal(s, p))
                {
                    var active = s.Participants.Single(x => x.SideId == p.SideId && Legal(s, x));
                    if (active.IsDowned) return Resolve(s, Action(MatchActionType.Recover, active, s.GetOpponents(active.Id).First(x => Legal(s, x))), random);
                    if (p.IsDowned) return Resolve(s, Action(MatchActionType.Recover, p, s.GetOpponents(p.Id).First(x => Legal(s, x))), random);
                    if (active.Position != MatchPosition.Corner)
                        return Resolve(s, new MatchEngineAction { Type = MatchActionType.Reposition, PerformerId = active.Id,
                            ReceiverId = s.GetOpponents(active.Id).First(x => Legal(s, x)).Id, PartnerId = p.Id }, random);
                    return Resolve(s, new MatchEngineAction { Type = MatchActionType.Tag, PerformerId = active.Id,
                        ReceiverId = s.GetOpponents(active.Id).First(x => Legal(s, x)).Id, PartnerId = p.Id }, random);
                }
            }
            if (a.IsDowned) return Resolve(s, Action(MatchActionType.Recover, a, b), random);
            if (s.RescueRequired && !s.RescuePrepared)
            {
                var candidates = a.FinisherMoveIds.Concat(new[] { a.FinisherMoveId }).Concat(a.SignatureMoveIds).Concat(new[] { a.SignatureMoveId })
                    .Concat(MatchTechniques.All.Where(t => t.Enabled && t.CanUseGenerally && t.AllowsStyle(a.WrestlingStyleId) &&
                        t.MeetsRequirement(a.Eligibility)).Select(t => t.Id))
                    .Where(id => !string.IsNullOrEmpty(id) && id != a.ReplacedFinishMoveId).Distinct()
                    .Select(id => (id, rule: s.FindMove?.Invoke(id) ?? MatchTechniques.Find(id)?.Rules))
                    .Where(x => x.rule != null && MatchTechniques.CanExecute(x.rule, a) && MatchHealth.CanReceive(x.rule, b) &&
                        (s.Rules.Objective != MatchObjective.PinOrSubmission ||
                            s.Rules.Allows(Kind(x.rule) == MoveKind.Submission ? MatchFinishType.Submission : MatchFinishType.Pinfall)) &&
                        (x.rule.Situation == MoveSituation.Automatic || MatchTechniques.IsSupported(x.rule.Situation)))
                    .OrderByDescending(x => (Kind(x.rule) == MoveKind.Submission) == (s.Booking.FinishType == MatchFinishType.Submission))
                    .ThenByDescending(x => x.id == a.SignatureMoveId || a.SignatureMoveIds.Contains(x.id))
                    .ThenBy(x => x.rule.ExecutionDifficulty).ToList();
                var alternative = candidates.FirstOrDefault();
                if (alternative.rule == null)
                {
                    if (s.Rules.Pinfall && !MatchHealth.Restricted(a, BodyRegion.Head) && !MatchHealth.Restricted(b, BodyRegion.Head))
                    { s.Booking.FinishType = MatchFinishType.RollUp; a.PlannedFinisherMoveId = null; }
                    else { s.IsAborted = true; s.FailureReason = "No safe legal finish is available after the actual injury.";
                        return Event(s, MatchActionType.Rest, MatchActionResult.Interrupted, a, b, 1); }
                }
                else
                {
                    var newFinish = s.Rules.Objective != MatchObjective.PinOrSubmission ? s.Booking.FinishType :
                        Kind(alternative.rule) == MoveKind.Submission ? MatchFinishType.Submission : MatchFinishType.Pinfall;
                    if (!s.Rules.Allows(newFinish))
                    { s.IsAborted = true; s.FailureReason = "The safe alternative is not allowed by the match rules.";
                        return Event(s, MatchActionType.Rest, MatchActionResult.Interrupted, a, b, 1); }
                    s.Booking.FinishType = newFinish;
                    a.PlannedFinisherMoveId = alternative.id; a.TargetRegion = alternative.rule.TargetRegion;
                }
                s.RescuePrepared = true;
                var preparation = Resolve(s, Action(MatchActionType.Reposition, a, b), random);
                var improvisation = (a.Attributes.RingImprovisation + b.Attributes.RingImprovisation) / 2;
                preparation.DurationSeconds = 12 - (int)Math.Floor(improvisation / 2);
                preparation.ExecutionQuality = .5f + improvisation * .025f;
                preparation.IsRescuePreparation = true;
                return preparation;
            }
            var saver = s.Participants.FirstOrDefault(p => p.Id != a.Id && p.Id != b.Id && p.SideId != a.SideId &&
                !p.IsEliminated && !p.IsDowned && p.Stun < 12 && p.Position != MatchPosition.Outside);
            if (saver != null)
            {
                Hit(s, saver, BodyRegion.Body, 6, false); Spend(a, 2);
                return Event(s, MatchActionType.BreakUp, MatchActionResult.FullSuccess, a, saver, 4, 6, "clear-save");
            }
            if (!InRing(a) || !InRing(b) || a.Position is not (MatchPosition.Center or MatchPosition.TopRope) || MatchMovement.CanReachRopes(b))
            {
                a.Position = MatchPosition.Center; b.Position = MatchPosition.Center;
                return Event(s, MatchActionType.Reposition, MatchActionResult.FullSuccess, a, b, 4, detail: "center");
            }
            if (s.RefereeUnavailableUntil > s.ElapsedSeconds)
                return Event(s, MatchActionType.Rest, MatchActionResult.Held, a, b, 5, detail: "referee-recovery");
            var finish = s.Booking.FinishType;
            var assigned = a.FinisherMoveIds.Concat(new[] { a.FinisherMoveId }).Distinct().Select(id => (id, type: MatchActionType.Finisher))
                .Concat(a.SignatureMoveIds.Concat(new[] { a.SignatureMoveId }).Distinct().Select(id => (id, type: MatchActionType.Signature)))
                .Concat(s.RescueRequired && a.PlannedFinisherMoveId != null ? new[] { (id: a.PlannedFinisherMoveId, type: MatchActionType.Finisher) } : Array.Empty<(string id, MatchActionType type)>())
                .Where(x => !string.IsNullOrEmpty(x.id) && (!s.RescueRequired || x.id != a.ReplacedFinishMoveId) && UsableAssignedMove(s, a, x.id) &&
                    MatchHealth.CanReceive(s.FindMove?.Invoke(x.id) ?? MatchTechniques.Find(x.id)?.Rules, b) &&
                    (s.FindMove?.Invoke(x.id) is not MatchMoveRules m || (Kind(m) == MoveKind.Submission) == (finish == MatchFinishType.Submission)))
                .OrderBy(x => s.Participants.Count == 2 && !s.Rules.Tag && s.Events.Count(e => e.PerformerId == a.Id && e.MoveId == x.id &&
                    e.MatchTimeSeconds >= s.EarliestFinishSeconds - 60 &&
                    e.Result is MatchActionResult.ExecutionFailure or MatchActionResult.ReceptionFailure) >= 2 ? 1 : 0)
                .ThenByDescending(x => x.type == MatchActionType.Finisher).ThenByDescending(x => x.id == a.PlannedFinisherMoveId).ToList();
            var selected = assigned.FirstOrDefault();
            if (selected.id == null && finish == MatchFinishType.Submission)
            {
                var general = MatchTechniques.All.Where(m => m.Enabled && m.CanUseGenerally && m.Action == MatchActionType.Submission &&
                    MatchTechniques.IsSupported(m.Rules.Situation) && m.AllowsStyle(a.WrestlingStyleId) && m.MeetsRequirement(a.Eligibility) && MatchTechniques.CanExecute(m.Rules, a))
                    .OrderByDescending(m => m.Rules.TargetRegion == a.TargetRegion).ThenByDescending(m => Damage(b, m.Rules.TargetRegion)).FirstOrDefault();
                if (general == null)
                {
                    s.IsAborted = true; s.FailureReason = "No executable submission can satisfy the booking with the current injuries.";
                    return Event(s, MatchActionType.Rest, MatchActionResult.Interrupted, a, b, 1);
                }
                selected = (general.Id, MatchActionType.Submission);
            }
            var selectedRule = string.IsNullOrEmpty(selected.id) ? null : s.FindMove?.Invoke(selected.id) ?? MatchTechniques.Find(selected.id)?.Rules;
            var kind = selectedRule == null ? finish == MatchFinishType.Submission ? MoveKind.Submission : MoveKind.Strike : Kind(selectedRule);
            var attackType = selected.id != null ? selected.type : finish == MatchFinishType.Submission ? MatchActionType.Submission :
                a.FinisherMoveIds.Count > 0 || !string.IsNullOrEmpty(a.FinisherMoveId) ? MatchActionType.HeavyStrike : MatchActionType.Finisher;
            bool generalFinish = selected.id == null && attackType == MatchActionType.Finisher &&
                MatchTechniques.Candidates(attackType, a, b).Count > 0;
            if (generalFinish)
            {
                var technique = MatchTechniques.Select(s, attackType, a, b, random);
                selected = (technique.Id, attackType); selectedRule = technique.Rules; kind = Kind(selectedRule);
            }
            bool centerFinish = finish is MatchFinishType.Pinfall or MatchFinishType.RollUp or MatchFinishType.Submission &&
                MatchMovement.DistanceFromCenter(b) > .25f;
            int naturalFinish = finish switch
            {
                MatchFinishType.RollUp => CoverSeconds(a) + 4,
                MatchFinishType.Submission => 10,
                MatchFinishType.CountOut => s.Rules.CountOutSeconds + 6,
                MatchFinishType.Disqualification => 6,
                MatchFinishType.Escape or MatchFinishType.ObjectRetrieval or MatchFinishType.TableBreak => s.ObjectReady ? 17 : 22,
                _ => (kind == MoveKind.Aerial ? 5 : 7) + CoverSeconds(a) + 3
            };
            if (centerFinish) naturalFinish += 4;
            bool attackFinish = finish is MatchFinishType.Pinfall or MatchFinishType.Submission;
            if (attackFinish && kind == MoveKind.Aerial && a.Position != MatchPosition.TopRope) naturalFinish += 5;
            if (attackFinish && selectedRule != null && !CanMove(s, a, b, selected.id)) naturalFinish += b.IsDowned ? 4 : 7;
            if (finish == MatchFinishType.Pinfall && !MatchMoveMotion.IsBridgingSuplex(selected.id))
            {
                if ((a.Stun >= 12 || a.Fatigue >= 80 || a.RealInjury >= InjuryStatus.Moderate) &&
                    (kind == MoveKind.Aerial || MatchMoveMotion.ActorEndPosture(selected.id) != MatchPosture.Standing))
                    naturalFinish += RecoverySeconds(a) * 3;
                if (selectedRule?.EndPosture is MatchPosture.Prone or MatchPosture.Seated or MatchPosture.Kneeling)
                    naturalFinish += 3;
            }
            bool finalFall = s.Rules.Elimination ? s.GetOpponents(a.Id).Count == 1 :
                s.Participants.Where(p => p.SideId == a.SideId).Sum(p => p.Falls) >= s.Rules.FallsToWin - 1;
            int retryMargin = s.Participants.Count == 2 && !s.Rules.Tag && finish is MatchFinishType.Pinfall or MatchFinishType.Submission
                ? Math.Min(s.TargetDurationSeconds / 10, (kind == MoveKind.Aerial ? 5 : 7) * 3) : 0;
            if (!s.RescueRequired && !s.Rules.IronMan && finalFall && s.ElapsedSeconds + naturalFinish + retryMargin < s.EarliestFinishSeconds)
            {
                // Keep wrestling until the decisive sequence fits; never hold a cover for the clock.
                var actions = MatchTechniques.Candidates(b.IsDowned ? MatchActionType.GroundAttack : MatchActionType.BasicStrike, a, b)
                    .Where(m => m.Rules.MotionProfile != MoveMotionProfile.GroundDrop && MatchMoveMotion.ActorEndPosture(m.Id) == MatchPosture.Standing)
                    .Select(m => Action(b.IsDowned ? MatchActionType.GroundAttack : MatchActionType.BasicStrike, a, b, m.Id)).ToList();
                if (actions.Count == 0) return Resolve(s, Action(b.IsDowned ? MatchActionType.Lift : MatchActionType.BasicStrike, a, b), random);
                foreach (var action in actions) action.Score = ActionEvaluator.Score(s, action);
                var ordinary = Resolve(s, ActionSelector.Select(actions, random), random);
                ordinary.MoveId = null;
                return ordinary;
            }
            if (centerFinish)
            {
                a.Position = b.Position = MatchPosition.Center;
                return Event(s, MatchActionType.Reposition, MatchActionResult.FullSuccess, a, b, 4, detail: "center");
            }
            if (finish == MatchFinishType.Disqualification)
            {
                Hit(s, a, BodyRegion.Body, 5, false);
                var foul = Event(s, MatchActionType.Spot, MatchActionResult.FullSuccess, b, a, 3, detail: "illegal-strike");
                Start(s, ExchangeKind.Cover, a, b); s.Exchange.Booked = true;
                // Adjudication is recorded after the observed foul, not an unrelated timer result.
                s.Exchange.Kind = ExchangeKind.CountOut; s.Exchange.MoveId = "disqualification";
                return foul;
            }
            if (finish == MatchFinishType.CountOut)
            {
                b.Position = MatchPosition.Outside; b.IsDowned = true;
                Start(s, ExchangeKind.CountOut, a, b); s.Exchange.Booked = true;
                s.Exchange.StartedAt = s.ElapsedSeconds + 3;
                return Event(s, MatchActionType.Reposition, MatchActionResult.FullSuccess, a, b, 3, detail: "throw-out");
            }
            if (finish is MatchFinishType.Escape or MatchFinishType.ObjectRetrieval or MatchFinishType.TableBreak)
            {
                if (!s.ObjectReady) { s.ObjectReady = true; return Event(s, MatchActionType.ObjectSetup, MatchActionResult.FullSuccess, a, b, 5); }
                if (s.RescueRequired && selected.id != null && !s.Events.Any(e => e.MoveId == selected.id &&
                    e.IsPhysicalAttack && e.Result == MatchActionResult.FullSuccess && e.MatchTimeSeconds >= s.RescueDeadline - 90))
                {
                    if (!CanMove(s, a, b, selected.id))
                        return Resolve(s, Action(b.IsDowned ? MatchActionType.Lift : MatchActionType.HeavyStrike, a, b), random);
                    var safeAttack = Resolve(s, Action(attackType, a, b, selected.id), random);
                    s.Exchange = null;
                    return safeAttack;
                }
                if (!b.IsDowned)
                {
                    var knockdown = Resolve(s, Action(MatchActionType.HeavyStrike, a, b), random);
                    s.Exchange = null;
                    return knockdown;
                }
                Start(s, ExchangeKind.Object, a, b); s.Exchange.Booked = true;
                return Event(s, MatchActionType.ObjectClimb, MatchActionResult.FullSuccess, a, b, 4);
            }
            if (finish == MatchFinishType.RollUp)
            {
                var roll = Resolve(s, Action(MatchActionType.Pin, a, b), random);
                s.Exchange.Booked = true;
                return roll;
            }
            if (selected.id == null && attackType == MatchActionType.Finisher && b.IsDowned &&
                MatchTechniques.Candidates(attackType, a, b).Count == 0)
                return Resolve(s, Action(MatchActionType.TurnOver, a, b), random);
            if (!string.IsNullOrEmpty(selected.id) && !CanMove(s, a, b, selected.id))
            {
                if (b.IsDowned && (selectedRule.Situation != MoveSituation.Automatic ? !MatchTechniques.IsGrounded(selectedRule.Situation) : kind is MoveKind.Grapple or MoveKind.Strike))
                    return Resolve(s, Action(MatchActionType.Lift, a, b), random);
                if (b.IsDowned) return Resolve(s, Action(MatchActionType.TurnOver, a, b, selected.id), random);
                if (MatchTechniques.IsRear(selectedRule?.Situation ?? MoveSituation.Automatic))
                {
                    var setup = Event(s, MatchActionType.PrepareAttack, MatchActionResult.Held, a, b, 3, detail: "rear-setup");
                    setup.MoveId = selected.id;
                    return setup;
                }
                if (selectedRule?.Situation == MoveSituation.CornerStanding)
                {
                    b.Position = MatchPosition.Corner;
                    return Event(s, MatchActionType.CornerWhip, MatchActionResult.FullSuccess, a, b, 4, detail: "corner-finish-setup");
                }
                var knockdown = Resolve(s, Action(MatchActionType.HeavyStrike, a, b), random); s.Exchange = null;
                return knockdown;
            }
            if (finish == MatchFinishType.Submission)
            {
                if (!b.IsDowned && selectedRule?.Situation is not (MoveSituation.StandingSubmission or MoveSituation.StandingRearSubmission))
                {
                    var knockdown = Resolve(s, Action(MatchActionType.HeavyStrike, a, b), random);
                    s.Exchange = null;
                    return knockdown;
                }
                var e = Resolve(s, Action(attackType, a, b, selected.id), random);
                if (s.Exchange?.Kind == ExchangeKind.Hold) s.Exchange.Booked = true;
                else s.Exchange = null;
                return e;
            }
            if (attackType == MatchActionType.HeavyStrike && b.IsDowned) return Resolve(s, Action(MatchActionType.Lift, a, b), random);
            var attack = Resolve(s, Action(attackType, a, b, selected.id), random);
            if (generalFinish) attack.MoveId = null;
            if (s.Exchange?.Kind == ExchangeKind.FollowUp) s.Exchange.Booked = true;
            else s.Exchange = null;
            return attack;
        }

        private static MatchEngineEvent ApplyDueSpot(MatchEngineState s, Random random)
        {
            for (var i = 0; i < s.ScheduledSpots.Count; i++)
            {
                var spot = s.ScheduledSpots[i];
                if (spot.Phase == MatchSpotPhase.PostMatch || spot.WasSkipped || s.AppliedSpots.Contains(i)) continue;
                var at = spot.Phase switch { MatchSpotPhase.Entrance => 0, MatchSpotPhase.Early => .2f, MatchSpotPhase.Middle => .5f, _ => .8f };
                if (s.ElapsedSeconds < s.TargetDurationSeconds * at) continue;
                var target = s.GetParticipant(spot.TargetId);
                var actor = s.Participants.FirstOrDefault(x => x.Id == spot.ActorId);
                var definition = SpecialMatchSpotCatalog.Find(spot.SpotId);
                bool interrupts = spot.ScriptedEffect != SpotScriptedEffect.None || actor == null ||
                    definition?.ActorScope == SpotActorScope.Outsider || definition?.Relationship == SpotRelationship.Teammates ||
                    spot.SpotId is "spot_005" or "spot_006" || spot.Phase == MatchSpotPhase.Entrance;
                float until = spot.Phase switch { MatchSpotPhase.Entrance => .2f, MatchSpotPhase.Early => .5f, MatchSpotPhase.Middle => .8f, _ => 1.05f };
                string reason = target.IsEliminated || actor?.IsEliminated == true ? "참가 선수가 경기에 남아 있지 않습니다." :
                    target.RealInjury == InjuryStatus.Severe || actor?.RealInjury == InjuryStatus.Severe ? "선수의 부상으로 안전하게 실행할 수 없습니다." : null;
                if (actor != null && MatchHealth.Restricted(actor, BodyRegion.Arms))
                    reason = "선수의 팔 부상으로 예정된 공격을 안전하게 실행할 수 없습니다.";
                if (spot.ScriptedEffect == SpotScriptedEffect.Disqualification && !s.Rules.Disqualification)
                    reason = "이 경기에서는 실격 판정을 사용할 수 없습니다.";
                if (!interrupts && s.ElapsedSeconds >= s.TargetDurationSeconds * until)
                    reason = "지정된 실행 구간이 지났습니다.";
                var move = string.IsNullOrEmpty(spot.MoveId) ? null : s.FindMove?.Invoke(spot.MoveId) ?? MatchTechniques.Find(spot.MoveId)?.Rules;
                if (!string.IsNullOrEmpty(spot.MoveId) && (move == null || actor == null || !MatchTechniques.CanExecute(move, actor) || !MatchHealth.CanReceive(move, target)))
                    reason = "예정된 기술을 안전하게 시전하거나 접수할 수 없습니다.";
                if (reason == null && !interrupts && (s.Exchange != null || !Legal(s, actor) || !Legal(s, target) || actor.IsDowned || !InRing(actor) || !InRing(target) ||
                    actor.Stun >= 12 || move != null && (!CanMove(s, actor, target, spot.MoveId) ||
                    Kind(move) == MoveKind.Aerial && actor.Position != MatchPosition.TopRope && !MatchTechniques.IsOutsideDive(move.Situation))))
                {
                    if (s.ElapsedSeconds < s.TargetDurationSeconds * until) continue;
                    reason = "지정 구간 안에 필요한 자세와 공방 조건이 갖춰지지 않았습니다.";
                }
                if (reason != null)
                {
                    spot.WasSkipped = true; spot.SkipReason = reason; spot.FinalSpotScore = 0;
                    var skipped = Event(s, MatchActionType.Spot, MatchActionResult.Interrupted, null, null, 1, detail: "spot-skipped:" + spot.SpotId);
                    skipped.TechniqueName = reason;
                    return skipped;
                }
                s.AppliedSpots.Add(i); spot.WasApplied = true;
                var interrupted = s.Exchange;
                s.Exchange = null;
                if (move != null)
                {
                    var action = MatchTechniques.Find(spot.MoveId)?.Action ?? (Kind(move) == MoveKind.Submission ? MatchActionType.Submission : MatchActionType.HeavyStrike);
                    var execution = Resolve(s, Action(action, actor, target, spot.MoveId), random);
                    if (execution.Result is MatchActionResult.ExecutionFailure or MatchActionResult.ReceptionFailure or MatchActionResult.Countered)
                    {
                        spot.Result = SpotExecutionResult.Failure;
                        spot.FinalSpotScore = definition == null ? 0 : MatchSpotEvaluator.Score(definition.BasePoint, spot.Result, spot.RepeatCount);
                    }
                    return execution;
                }
                Hit(s, target, BodyRegion.Body, spot.Result >= SpotExecutionResult.Success ? 7 : 2, false);
                if (spot.SpotId is "spot_005" or "spot_006") s.RefereeUnavailableUntil = s.ElapsedSeconds + 18;
                var e = Event(s, MatchActionType.Spot, MatchActionResult.FullSuccess, actor, target, 4, detail: spot.SpotId);
                if (interrupted != null)
                { e.ExchangeId = interrupted.Id; e.CauseEventIndex = interrupted.CauseIndex < s.Events.Count ? interrupted.CauseIndex : -1; }
                e.PerformerId = spot.ActorId;
                if (spot.ScriptedEffect != SpotScriptedEffect.None)
                {
                    s.Phase = MatchPhase.Finish;
                    s.IsFinished = true; s.ActualFinish = spot.ScriptedEffect == SpotScriptedEffect.Disqualification ? MatchFinishType.Disqualification : MatchFinishType.Draw;
                    s.WinnerId = s.ActualFinish == MatchFinishType.Disqualification ? target.Id : null;
                    e.Type = MatchActionType.Finish; e.Result = s.WinnerId == null ? MatchActionResult.Draw : MatchActionResult.Disqualification;
                    e.PerformerId = s.WinnerId; e.ReceiverId = actor?.Id;
                }
                return e;
            }
            return null;
        }


        private static MatchEngineEvent ContinueComeback(MatchEngineState s, Random random, MatchExchange x, MatchEngineParticipant a, MatchEngineParticipant b)
        {
            if (x.Kind == ExchangeKind.DoubleDown)
            {
                if (x.Beats == 1)
                {
                    foreach (var p in new[] { a, b }) RecoverFatigue(p, 6);
                    return Event(s, MatchActionType.Rest, MatchActionResult.Held, a, b, 6, detail: "double-down");
                }
                if (x.Beats == 2)
                {
                    a.Posture = b.Posture = MatchPosture.Seated;
                    return Event(s, MatchActionType.Recover, MatchActionResult.Held, a, b, 4, detail: "double-rise");
                }
                if (x.Beats <= 6)
                {
                    var striker = x.Beats % 2 == 1 ? a : b; var target = striker == a ? b : a;
                    if (x.Beats >= 5) striker.Posture = target.Posture = x.Beats == 5 ? MatchPosture.Kneeling : MatchPosture.Standing;
                    striker.IsDowned = target.IsDowned = x.Beats < 6;
                    Hit(s, target, BodyRegion.Head, 2, false); Spend(striker, .8f);
                    var punch = Event(s, MatchActionType.BasicStrike, MatchActionResult.FullSuccess, striker, target, 3, 2, "boo-yay");
                    punch.TargetRegion = BodyRegion.Head;
                    punch.CrowdReaction = striker.Alignment == KayfabeAlignment.Face ? 2 + striker.Attributes.FaceWork * .1f : 1 + striker.Attributes.HeelWork * .1f;
                    return punch;
                }
                s.Exchange = null;
                var face = new[] { a, b }.Where(p => p.Alignment == KayfabeAlignment.Face).OrderByDescending(p => p.Attributes.FaceWork).FirstOrDefault();
                if (face == null || face.Fatigue >= 90 || random.NextDouble() >= .25 + face.Attributes.FaceWork * .02 + face.Attributes.RingPsychology * .01 - face.Fatigue * .003)
                    return Event(s, MatchActionType.Retreat, MatchActionResult.Held, a, b, 3, detail: "exchange-break");
                var opponent = face == a ? b : a;
                Start(s, ExchangeKind.Comeback, face, opponent);
                return Event(s, MatchActionType.Taunt, MatchActionResult.Held, face, opponent, 2, detail: "comeback-rally");
            }
            s.Exchange = null;
            if (a.IsDowned || b.IsDowned || s.TargetDurationSeconds - s.ElapsedSeconds <= 30)
                return Resolve(s, Action(a.IsDowned ? MatchActionType.Recover : b.IsDowned ? MatchActionType.GroundAttack : MatchActionType.BasicStrike, a, b), random);
            var attack = Resolve(s, Action(x.Beats < 3 ? MatchActionType.BasicStrike : MatchActionType.HeavyStrike, a, b, continuation: true), random);
            attack.Detail = attack.Impact > 0 ? "comeback-strike" : "comeback-cutoff";
            if (attack.Result == MatchActionResult.FullSuccess && x.Beats < 3) s.Exchange = x;
            return attack;
        }

        private static void TryComeback(MatchEngineState s, MatchEngineParticipant a, MatchEngineParticipant b)
        {
            if (s.BookingSequence || s.Phase == MatchPhase.Opening || a.IsDowned || b.IsDowned || a.Fatigue >= 90 || a.Stun >= 30 ||
                s.TargetDurationSeconds - s.ElapsedSeconds <= 60 || s.Rules.DominantSideId == b.SideId ||
                s.Events.Any(e => e.Detail == "comeback-strike" && s.ElapsedSeconds - e.MatchTimeSeconds < 90)) return;
            if (b.MatchControl - a.MatchControl < 15 && s.Events.TakeLast(6).Count(e => e.ReceiverId == a.Id && e.Impact > 0) < 3) return;
            Start(s, ExchangeKind.Comeback, a, b);
        }

        public static int RecoverySeconds(MatchEngineParticipant p) => 3 + (int)Math.Ceiling(Math.Min(9,
            p.Stun / 12f + (p.Posture is MatchPosture.Seated or MatchPosture.Kneeling ? p.LegDamage / 35f : p.BodyDamage / 50f)));

        public static void RecoverFatigue(MatchEngineParticipant p, int seconds)
        {
            float floor = Math.Min(95, p.StartingFatigue + p.Exertion * .12f);
            p.Fatigue = Math.Max(Math.Min(p.Fatigue, floor), p.Fatigue - seconds * (.1f + p.Attributes.Stamina * .015f));
        }

        private static MatchEngineAction Action(MatchActionType type, MatchEngineParticipant a, MatchEngineParticipant b, string move = null, bool continuation = false) =>
            new() { Type = move == null && type is MatchActionType.BasicStrike or MatchActionType.HeavyStrike or MatchActionType.Grapple or
                MatchActionType.GroundAttack or MatchActionType.LimbAttack or MatchActionType.Submission or MatchActionType.StandingHold &&
                MatchTechniques.Candidates(type, a, b).Count == 0 ? b.IsDowned ? MatchActionType.GroundAttack : MatchActionType.BasicStrike : type,
                PerformerId = a.Id, ReceiverId = b.Id, MoveId = move, TargetRegion = a.TargetRegion, IsContinuation = continuation };

        private static void Start(MatchEngineState s, ExchangeKind kind, MatchEngineParticipant a, MatchEngineParticipant b,
            string move = null, BodyRegion region = BodyRegion.Body) => s.Exchange = new MatchExchange
        { Id = ++s.NextExchangeId, CauseIndex = s.Events.Count, ActorId = a.Id, TargetId = b.Id, MoveId = move,
            Kind = kind, Region = region, StartedAt = s.ElapsedSeconds };

        private static void Spend(MatchEngineParticipant p, float work)
        {
            var cost = work * (1.25f - p.Attributes.Stamina * .035f);
            p.Fatigue = Clamp(p.Fatigue + cost); p.Exertion += cost;
        }

        private static void Hit(MatchEngineState s, MatchEngineParticipant b, BodyRegion region, float impact, bool finisher)
        {
            Spend(b, impact * .35f); Hurt(b, region, impact * .45f);
            b.Stun = Clamp(b.Stun + impact * 1.6f); b.LastImpact = impact;
            b.LastImpactTime = s.ElapsedSeconds; b.LastImpactWasFinisher = finisher;
            if (impact >= 6) b.IsDowned = true;
        }

        private static MatchEngineEvent Event(MatchEngineState s, MatchActionType type, MatchActionResult result,
            MatchEngineParticipant a, MatchEngineParticipant b, int seconds, float impact = 0, string detail = null)
        {
            var major = type is MatchActionType.Finish or MatchActionType.Fall or MatchActionType.Tag or MatchActionType.BreakUp || result == MatchActionResult.NearFall || impact >= 9;
            var repeats = s.Events.TakeLast(12).Count(x => x.Type == type && x.PerformerId == a?.Id);
            var reaction = major ? Math.Max(1, 7 - repeats) : impact * .2f;
            if (result is MatchActionResult.ExecutionFailure or MatchActionResult.ReceptionFailure) reaction = -2;
            return new MatchEngineEvent { Type = type, Result = result, PerformerId = a?.Id, ReceiverId = b?.Id,
                DurationSeconds = s.IsFinished || s.Booking != null && !s.Rules.IronMan && s.Booking.FinishType != MatchFinishType.Draw
                    ? seconds : Math.Min(seconds, Math.Max(1, s.LatestFinishSeconds - s.ElapsedSeconds - 1)),
                MatchTimeSeconds = s.ElapsedSeconds, Phase = s.Phase, Impact = impact,
                ExchangeId = s.Exchange?.Id ?? ++s.NextExchangeId,
                CauseEventIndex = s.Exchange != null && s.Exchange.CauseIndex < s.Events.Count ? s.Exchange.CauseIndex : -1,
                Detail = detail, IsMajorMoment = major, CrowdReaction = reaction, DramaChange = major ? reaction : -.15f,
                IntensityChange = Math.Min(20, impact * 1.1f) };
        }
    }
}
