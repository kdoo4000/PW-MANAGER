using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public enum MatchPhase { Opening, Neutral, Control, Comeback, BackAndForth, Climax, Finish }
    public enum MatchActionType { BasicStrike, HeavyStrike, Grapple, GroundAttack, Submission, Pin, Rest, Signature, Finisher, Finish, Recover, Reposition, Climb, Dive, LimbAttack, Counter, Cover, Hold, RopeBreak, Tag, DoubleTeam, BreakUp, Taunt, ObjectSetup, ObjectClimb, ObjectFinish, Spot, Fall, Lift, Probe, TieUp, Chain, IrishWhip, CornerWhip, RopeRun, RunningAttack, CornerAttack, Evade, Retreat, StandingHold, PrepareAttack, ExitRing, Pursue, RingsideAttack, TopRopeAttack, TurnOver, MedicalStop, ExecutionStop }
    public enum MatchActionResult { FullSuccess, PartialSuccess, ExecutionFailure, ReceptionFailure, Countered, Submitted, OneCount, TwoCount, NearFall, Pinfall, RollUp, Disqualification, CountOut, Draw, Escaped, Blocked, RopeBreak, Interrupted, Held, PassedOut, ObjectRetrieved, TableBroken, CageEscaped, TimeLimitDecision }

    [Serializable]
    public sealed class MatchEngineBooking
    {
        public string WinnerId;
        public string LoserId;
        public MatchFinishType FinishType;
    }

    [Serializable]
    public sealed class MatchEngineParticipant
    {
        public string Id;
        public string WrestlingStyleId;
        public WrestlerAttributesState Attributes = new();
        public WrestlerAttributesState MoveEligibilityAttributes;
        public WrestlerAttributesState Eligibility => MoveEligibilityAttributes ?? Attributes;
        public int InjuryProneness = 10;
        public int Collegiality = 10, RiskTaking = 10;
        public float Fatigue, StartingFatigue;
        public float MatchControl = 50f;
        public float ActionReadiness = 50f;
        public bool IsDowned;
        public MatchPosture Posture;
        public float RingX, RingZ, Height, FacingX = 1, FacingZ;
        public MatchGrip Grip;
        public string GripTargetId;
        public KayfabeAlignment Alignment;
        public string SideId;
        public float StatusValue, AmbushTendency;
        public bool IsLegal = true, IsEliminated;
        public MatchPosition Position;
        public float HeadDamage, BodyDamage, ArmDamage, LegDamage, Stun, Exertion;
        public int LastImpactTime, Falls;
        public float LastImpact;
        public bool LastImpactWasFinisher;
        public BodyRegion TargetRegion = BodyRegion.Legs;
        public List<string> SignatureMoveIds = new(), FinisherMoveIds = new();
        public string SignatureMoveId;
        public string FinisherMoveId;
        public string PlannedFinisherMoveId;
        public InjuryStatus RealInjury;
        public BodyRegion RealInjuryRegion = BodyRegion.Body;
        public bool InjuredThisMatch;
        public string ReplacedFinishMoveId;
    }

    [Serializable]
    public sealed class MatchEngineState
    {
        public int EngineVersion;
        public bool SpatialInitialized;
        public MatchRuntimeRules Rules = new();
        public MatchOpeningState Opening = new();
        public MatchExchange Exchange;
        public int NextExchangeId, RefereeUnavailableUntil;
        public bool ObjectReady, BookingSequence;
        public bool RingsideEligible, AnnounceTableEligible, AnnounceTableReady, AnnounceTableBroken;
        public MatchFinishType ActualFinish;
        public List<SpotResultState> ScheduledSpots = new();
        public List<int> AppliedSpots = new();
        [NonSerialized] public Func<string, MatchMoveRules> FindMove;
        public int ElapsedSeconds;
        public int TargetDurationSeconds = 720;
        public int DurationToleranceSeconds => !Rules.IronMan && Booking != null && Booking.FinishType != MatchFinishType.Draw ? TargetDurationSeconds / 20 : 0;
        public int EarliestFinishSeconds => TargetDurationSeconds - DurationToleranceSeconds;
        public bool AccidentsEnabled = true;
        public bool WasStoppedByInjury, RescueRequired, RescuePrepared;
        public bool WasStoppedByExecution;
        public string MedicalStopWrestlerId;
        public int RescueDeadline;
        public int LatestFinishSeconds => Math.Max(TargetDurationSeconds + DurationToleranceSeconds, RescueDeadline);
        public bool IsFinished;
        public bool IsAborted;
        public string FailureReason;
        public string WinnerId;
        public MatchPhase Phase;
        public float Intensity;
        public float Drama;
        public float CrowdHeat;
        public MatchEngineBooking Booking;
        public List<MatchEngineParticipant> Participants = new();
        public List<MatchEngineEvent> Events = new();

        public MatchEngineParticipant GetParticipant(string id) => Participants.Single(x => x.Id == id);
        public List<MatchEngineParticipant> GetOpponents(string id) => Participants.Where(x => x.Id != id && !x.IsEliminated && (string.IsNullOrEmpty(x.SideId) || x.SideId != GetParticipant(id).SideId)).ToList();
        public List<MatchEngineParticipant> GetActiveParticipants() => Participants.Where(x => x != null && !x.IsEliminated && (x.IsLegal || !Rules.Tag || Rules.Tornado)).ToList();
    }

    [Serializable]
    public sealed class MatchEngineAction
    {
        public MatchActionType Type;
        public string MoveId;
        public string PerformerId;
        public string ReceiverId;
        public List<string> ReceiverIds = new();
        public bool IsContinuation;
        public float Score;
        public BodyRegion TargetRegion;
        public string PartnerId;
    }

    [Serializable]
    public sealed class MatchParticipantSnapshot
    {
        public string Id;
        public float Fatigue, MatchControl, ActionReadiness;
        public bool IsDowned;
        public MatchPosture Posture;
        public float RingX, RingZ, Height, FacingX, FacingZ;
        public MatchGrip Grip;
        public string GripTargetId;
        public MatchPosition Position;
        public bool IsLegal, IsEliminated;
        public float HeadDamage, BodyDamage, ArmDamage, LegDamage, Stun;
        public int Falls;
        public string PlannedFinisherMoveId;
        public BodyRegion TargetRegion;
        public InjuryStatus RealInjury;
        public BodyRegion RealInjuryRegion;
    }

    [Serializable]
    public sealed class MatchEngineSnapshot
    {
        public MatchPhase Phase;
        public float Intensity, Drama, CrowdHeat;
        public List<MatchParticipantSnapshot> Participants;

        public static MatchEngineSnapshot Capture(MatchEngineState state) => new()
        {
            Phase = state.Phase, Intensity = state.Intensity, Drama = state.Drama, CrowdHeat = state.CrowdHeat,
            Participants = state.Participants.Select(x => new MatchParticipantSnapshot
            {
                Id = x.Id, Fatigue = x.Fatigue, MatchControl = x.MatchControl,
                ActionReadiness = x.ActionReadiness, IsDowned = x.IsDowned,
                PlannedFinisherMoveId = x.PlannedFinisherMoveId, TargetRegion = x.TargetRegion,
                RealInjury = x.RealInjury, RealInjuryRegion = x.RealInjuryRegion,
                Posture = x.Posture, RingX = x.RingX, RingZ = x.RingZ, Height = x.Height,
                FacingX = x.FacingX, FacingZ = x.FacingZ, Grip = x.Grip, GripTargetId = x.GripTargetId,
                Position = x.Position, IsLegal = x.IsLegal, IsEliminated = x.IsEliminated,
                HeadDamage = x.HeadDamage, BodyDamage = x.BodyDamage, ArmDamage = x.ArmDamage, LegDamage = x.LegDamage, Stun = x.Stun, Falls = x.Falls
            }).ToList()
        };
    }

    [Serializable]
    public sealed class MatchEngineEvent
    {
        public int ExchangeId;
        public int CauseEventIndex = -1;
        public int RefereeCountFrom, RefereeCount;
        public string PartnerId, Detail;
        public string KayfabeAction;
        public string TechniqueId, TechniqueName;
        public float ExecutionQuality = 1f;
        public bool HasDecisionEvidence, ContextualChoice = true, ValidCover;
        public bool TargetCompleted, PreparationAvailable, IsPreparation, IntentionalCombination;
        public string GoalMoveId;
        public BodyRegion GoalRegion;
        public float PinRisk, SellingQuality = .75f;
        public bool SellingProtectedRegion, SellingContradiction;
        public string SellingWrestlerId;
        public BodyRegion SellingRegion;
        public bool IsPhysicalAttack, PhysicalExecutionFailed;
        public bool IsRescuePreparation;
        public float ContactDifficulty;
        public string InjuredWrestlerId;
        public InjuryStatus InjurySeverity;
        public BodyRegion InjuryRegion;
        public BodyRegion TargetRegion;
        public MatchActionType Type;
        public MatchActionResult Result;
        public string PerformerId;
        public string ReceiverId;
        public int MatchTimeSeconds;
        public MatchPhase Phase;
        public float Impact;
        public float FatigueChange;
        public float MatchControlChange;
        public float CrowdReaction;
        public float DramaChange;
        public float IntensityChange;
        public bool IsMajorMoment;
        public string CommentaryKey;
        public int DurationSeconds;
        public string MoveId;
        public List<MatchMotionFrame> Motion;
        public float MotionSeconds;
        public MatchEngineSnapshot Before;
        public MatchEngineSnapshot After;
    }

    public sealed class MatchSimulator
    {
        public const int MaximumEvents = 2000;
        private readonly Random random;
        private readonly Random safetyRandom;
        private readonly int maximumEvents;
        private readonly MatchStateMachine stateMachine;
        private readonly Func<string, MatchMoveRules> findMove;
        private readonly bool accidentsEnabled;

        public MatchSimulator(int seed, int maximumEvents = MaximumEvents, Func<string, MatchMoveRules> findMove = null,
            bool accidentsEnabled = true)
        {
            if (maximumEvents < 0) throw new ArgumentOutOfRangeException(nameof(maximumEvents));
            random = new Random(seed);
            safetyRandom = new Random(unchecked(seed ^ 0x51A7E239));
            this.maximumEvents = maximumEvents;
            this.findMove = findMove;
            this.accidentsEnabled = accidentsEnabled;
            stateMachine = new MatchStateMachine(random);
        }

        public MatchEngineState Simulate(MatchEngineParticipant first, MatchEngineParticipant second,
            int targetDurationSeconds = 720) => Simulate(new[] { first, second }, targetDurationSeconds);

        public MatchEngineState Simulate(IEnumerable<MatchEngineParticipant> participants, int targetDurationSeconds = 720,
            MatchEngineBooking booking = null, MatchRuntimeRules rules = null, IEnumerable<SpotResultState> spots = null)
        {
            if (participants == null) throw new ArgumentNullException(nameof(participants));
            var list = participants.ToList();
            if (list.Count < 2 || list.Any(x => x == null || string.IsNullOrWhiteSpace(x.Id)) || list.Select(x => x.Id).Distinct().Count() != list.Count)
                throw new ArgumentException("A match requires distinct participants.", nameof(participants));
            if (targetDurationSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(targetDurationSeconds));
            foreach (var p in list)
            {
                if (p.Attributes == null) throw new ArgumentException("Attributes are required.");
                foreach (var v in new[] { p.Eligibility.Brawling, p.Eligibility.Power, p.Eligibility.Technical, p.Eligibility.HighFlying })
                    if (!MatchPhysics.Finite(v) || v < 0 || v > 20) throw new ArgumentOutOfRangeException(nameof(participants));
                if (p.InjuryProneness is < 1 or > 20) throw new ArgumentException("Injury proneness must be between 1 and 20.");
                if (p.Collegiality is < 1 or > 20 || p.RiskTaking is < 1 or > 20) throw new ArgumentException("Match personality values must be between 1 and 20.");
                foreach (var v in new[] { p.Attributes.Brawling, p.Attributes.Power, p.Attributes.Technical, p.Attributes.HighFlying,
                    p.Attributes.Selling, p.Attributes.Stamina, p.Attributes.RingPsychology, p.Attributes.RingImprovisation,
                    p.Attributes.Charisma, p.Attributes.Acting, p.Attributes.FaceWork, p.Attributes.HeelWork })
                    if (!MatchPhysics.Finite(v) || v < 0 || v > 20) throw new ArgumentOutOfRangeException(nameof(participants));
                foreach (var v in new[] { p.Fatigue, p.MatchControl, p.ActionReadiness, p.HeadDamage, p.BodyDamage, p.ArmDamage, p.LegDamage, p.Stun, p.StartingFatigue })
                    if (!MatchPhysics.Finite(v) || v < 0 || v > 100) throw new ArgumentOutOfRangeException(nameof(participants));
                if (!MatchPhysics.Finite(p.Exertion) || p.Exertion < 0) throw new ArgumentOutOfRangeException(nameof(participants));
                p.StartingFatigue = Math.Max(p.StartingFatigue, p.Fatigue);
                p.SideId ??= p.Id;
            }
            rules ??= new MatchRuntimeRules();
            if (rules.FallsToWin < 1 || rules.CountOutSeconds < 1 || !Enum.IsDefined(typeof(MatchObjective), rules.Objective))
                throw new ArgumentException("Invalid match rules.");
            if (booking != null && (!Enum.IsDefined(typeof(MatchFinishType), booking.FinishType) || !rules.Allows(booking.FinishType) ||
                (booking.FinishType != MatchFinishType.Draw && (booking.WinnerId == booking.LoserId ||
                !list.Any(x => x.Id == booking.WinnerId) || !list.Any(x => x.Id == booking.LoserId) ||
                list.Single(x => x.Id == booking.WinnerId).SideId == list.Single(x => x.Id == booking.LoserId).SideId))))
                throw new ArgumentException("Booking must have opposing participants and a legal finish.", nameof(booking));
            if (rules.Tag && !rules.Tornado)
                foreach (var side in list.GroupBy(x => x.SideId))
                    foreach (var p in side) { p.IsLegal = p == side.First(); if (!p.IsLegal) p.Position = MatchPosition.Apron; }
            // Singles roster order is not a tactical choice; tag starter order remains explicit.
            if (!rules.Tag) list = list.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
            var state = new MatchEngineState { EngineVersion = 6, Participants = list, TargetDurationSeconds = targetDurationSeconds,
                Booking = booking == null ? null : new MatchEngineBooking { WinnerId = booking.WinnerId,
                    LoserId = booking.LoserId, FinishType = booking.FinishType }, Rules = rules, FindMove = findMove,
                AccidentsEnabled = accidentsEnabled, ScheduledSpots = spots?.ToList() ?? new List<SpotResultState>() };
            state.AnnounceTableEligible = list.Count == 2 && rules.Objective == MatchObjective.PinOrSubmission &&
                rules.ImportantMatch && !rules.Disqualification && (!rules.CountOut || rules.CountOutSeconds >= 30) &&
                targetDurationSeconds >= 900 && random.NextDouble() < .12;
            state.RingsideEligible = state.AnnounceTableEligible || list.Count == 2 &&
                random.NextDouble() < (list.Any(p => p.Alignment == KayfabeAlignment.Heel) ? .45 : .18);
            stateMachine.Initialize(state);
            while (!state.IsFinished && !state.IsAborted && state.Events.Count < maximumEvents) Step(state);
            if (!state.IsFinished && !state.IsAborted) { state.IsAborted = true; state.FailureReason = "MaximumEvents reached without a finish."; }
            if (state.IsFinished) CompleteSpots(state);
            return state;
        }

        public MatchEngineEvent Step(MatchEngineState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.IsFinished || state.IsAborted) throw new InvalidOperationException("The match has already ended.");
            if (state.WasStoppedByInjury) { stateMachine.TransitionTo(state, MatchPhase.Finish); MatchHealth.RecordStop(state); CompleteSpots(state); return state.Events.Last(); }
            MatchMovement.Initialize(state);
            if (!state.Opening.Initialized) MatchOpening.Initialize(state, random);
            var before = MatchEngineSnapshot.Capture(state);
            stateMachine.Update(state);
            var combination = state.Exchange?.Kind is ExchangeKind.GripCombo or ExchangeKind.Hold or ExchangeKind.Chain;
            var e = MatchPhysics.Next(state, random, stateMachine.CurrentState);
            stateMachine.TransitionTo(state, state.IsFinished || state.BookingSequence ? MatchPhase.Finish :
                state.Exchange?.Kind == ExchangeKind.Comeback ? MatchPhase.Comeback : state.Phase);
            e.IntentionalCombination = combination;
            e.Before = before;
            MatchMovement.Apply(state, e, before);
            MatchHealth.RecordEvidence(state, e, before);
            MatchHealth.Observe(state, e, safetyRandom);
            e.MatchTimeSeconds = state.ElapsedSeconds;
            e.Phase = state.IsFinished ? MatchPhase.Finish : state.Phase;
            if (e.DurationSeconds < 1) e.DurationSeconds = 1;
            state.ElapsedSeconds += e.DurationSeconds;
            foreach (var p in state.Participants)
            {
                p.Stun = Math.Max(0, p.Stun - e.DurationSeconds * .35f);
                if (p.Id != e.PerformerId && p.Id != e.ReceiverId)
                    MatchPhysics.RecoverFatigue(p, e.DurationSeconds);
                p.ActionReadiness = MatchPhysics.Clamp(p.ActionReadiness + (p.Id == e.PerformerId ? -8 : 5));
            }
            CrowdSimulator.Apply(state, e);
            e.After = MatchEngineSnapshot.Capture(state);
            var rb = before.Participants.FirstOrDefault(x => x.Id == e.ReceiverId);
            var ra = e.After.Participants.FirstOrDefault(x => x.Id == e.ReceiverId);
            e.FatigueChange = rb == null ? 0 : ra.Fatigue - rb.Fatigue;
            var ab = before.Participants.FirstOrDefault(x => x.Id == e.PerformerId);
            e.MatchControlChange = ab == null ? 0 : state.GetParticipant(e.PerformerId).MatchControl - ab.MatchControl;
            e.CommentaryKey = $"match.{e.Type.ToString().ToLowerInvariant()}.{e.Result.ToString().ToLowerInvariant()}";
            state.Events.Add(e);
            if (state.WasStoppedByInjury) { stateMachine.TransitionTo(state, MatchPhase.Finish); MatchHealth.RecordStop(state); }
            else if (state.IsAborted) RecordExecutionStop(state);
            if (state.IsFinished) CompleteSpots(state);
            return e;
        }

        private static void CompleteSpots(MatchEngineState state)
        {
            bool stopped = state.WasStoppedByInjury || state.WasStoppedByExecution ||
                state.ScheduledSpots.Any(spot => spot.WasApplied && spot.ScriptedEffect == SpotScriptedEffect.MatchStopped);
            for (int i = 0; i < state.ScheduledSpots.Count; i++)
            {
                var spot = state.ScheduledSpots[i];
                if (spot.WasSkipped || state.AppliedSpots.Contains(i)) continue;
                var actor = state.Participants.FirstOrDefault(p => p.Id == spot.ActorId);
                var target = state.GetParticipant(spot.TargetId);
                if (spot.Phase == MatchSpotPhase.PostMatch && !stopped &&
                    target.RealInjury != InjuryStatus.Severe && actor?.RealInjury != InjuryStatus.Severe &&
                    (actor == null || !MatchHealth.Restricted(actor, BodyRegion.Arms)))
                { spot.WasApplied = true; state.AppliedSpots.Add(i); continue; }
                spot.WasSkipped = true; spot.FinalSpotScore = 0;
                spot.SkipReason = stopped ? "경기 중단으로 미실행" : "실행 가능한 시점 없이 경기가 종료됐습니다.";
            }
        }

        private void RecordExecutionStop(MatchEngineState state)
        {
            var before = MatchEngineSnapshot.Capture(state);
            stateMachine.TransitionTo(state, MatchPhase.Finish);
            state.IsAborted = false; state.IsFinished = true; state.WasStoppedByExecution = true;
            state.WinnerId = null; state.ActualFinish = MatchFinishType.Draw; state.Exchange = null;
            var stop = new MatchEngineEvent { Type = MatchActionType.ExecutionStop, Result = MatchActionResult.Interrupted,
                Detail = state.FailureReason, DurationSeconds = 1, MatchTimeSeconds = state.ElapsedSeconds,
                Phase = MatchPhase.Finish, ExchangeId = ++state.NextExchangeId, CauseEventIndex = state.Events.Count - 1,
                Before = before, IsMajorMoment = true, CommentaryKey = "match.execution-stop" };
            MatchMovement.Apply(state, stop, before);
            state.ElapsedSeconds++;
            stop.After = MatchEngineSnapshot.Capture(state);
            state.Events.Add(stop);
        }
    }

    public static class PerformerSelector
    {
        public static MatchEngineParticipant Select(MatchEngineState state, Random random, IMatchState phaseState = null)
        {
            var candidates = state.GetActiveParticipants();
            if (candidates.Count == 0) throw new InvalidOperationException("No participant can act.");
            var weights = candidates.Select(x => (double)Score(state, x, phaseState)).ToArray();
            return candidates[WeightedIndex(weights, random)];
        }

        public static float Score(MatchEngineState state, MatchEngineParticipant participant, IMatchState phaseState = null)
        {
            var score = 20f + participant.MatchControl * .55f + participant.ActionReadiness * .75f - participant.Fatigue * .35f;
            if (participant.IsDowned) score -= 45f;
            if (state.Events.TakeLast(2).Any(x => x.PerformerId == participant.Id)) score -= state.Rules.DominantSideId == participant.SideId ? 2f : 18f;
            if (state.Events.TakeLast(3).Any(x => x.ReceiverId == participant.Id)) score += state.Phase == MatchPhase.Comeback ? 30f : 10f;
            score += phaseState?.GetPerformerScoreModifier(state, participant) ?? 0f;
            score += MatchOpening.PerformerBonus(state, participant);
            return Math.Max(.01f, score);
        }

        internal static int WeightedIndex(IReadOnlyList<double> weights, Random random)
        {
            var roll = random.NextDouble() * weights.Sum();
            for (var i = 0; i < weights.Count; i++) if ((roll -= weights[i]) <= 0d) return i;
            return weights.Count - 1;
        }
    }

    public static class TargetSelector
    {
        public static MatchEngineParticipant Select(MatchEngineState state, MatchEngineParticipant performer, Random random)
        {
            var candidates = state.GetOpponents(performer.Id);
            if (candidates.Count == 1) return candidates[0];
            var weights = candidates.Select(x => Math.Exp(Math.Min(40d, Score(state, performer, x) / 24f))).ToArray();
            return candidates[PerformerSelector.WeightedIndex(weights, random)];
        }

        public static float Score(MatchEngineState state, MatchEngineParticipant performer, MatchEngineParticipant target)
        {
            var score = 20f + (target.IsLegal ? 12f : -100f) + target.Fatigue * .35f + target.MatchControl * .2f + (target.IsDowned ? 8f : 0f);
            if (state.Events.TakeLast(4).Any(x => x.PerformerId == target.Id && x.ReceiverId == performer.Id)) score += 18f;
            if (state.Events.LastOrDefault()?.ReceiverId == target.Id) score -= 10f;
            return Math.Max(.01f, score);
        }
    }

    public static class ActionGenerator
    {
        public static List<MatchEngineAction> Generate(MatchEngineState state, MatchEngineParticipant performer, MatchEngineParticipant receiver)
        {
            return MatchPhysics.Actions(state, performer, receiver);
        }
    }

    public static class ActionEvaluator
    {
        public static float Score(MatchEngineState state, MatchEngineAction action, IMatchState phaseState = null)
        {
            var performer = state.GetParticipant(action.PerformerId);
            var receiver = state.GetParticipant(action.ReceiverId);
            var score = action.Type switch
            {
                MatchActionType.BasicStrike => 30f + performer.Attributes.Brawling,
                MatchActionType.Probe => 35f,
                MatchActionType.TieUp => 35f + performer.Attributes.Technical,
                MatchActionType.HeavyStrike => 18f + performer.Attributes.Brawling + performer.Attributes.Power * .5f,
                MatchActionType.Grapple => 25f + performer.Attributes.Technical,
                MatchActionType.GroundAttack => 25f + performer.Attributes.Brawling,
                MatchActionType.Submission => 5f + performer.Attributes.Technical + receiver.Fatigue * .2f,
                MatchActionType.Pin => 8f + receiver.Fatigue * .35f + state.Drama * .1f,
                MatchActionType.Rest => 2f + performer.Fatigue * .2f,
                MatchActionType.Recover => performer.Stun < 12 && performer.Fatigue < 80 && receiver.IsDowned
                    ? 2f + performer.Fatigue * .2f : 100f,
                MatchActionType.Reposition => 12f,
                MatchActionType.IrishWhip => 18f + performer.Attributes.Power * .7f + performer.Attributes.Technical * .3f,
                MatchActionType.CornerWhip => 15f + performer.Attributes.Brawling * .7f + performer.Attributes.Power * .3f,
                MatchActionType.RopeRun => 12f + performer.Attributes.HighFlying - performer.LegDamage * .12f - performer.Fatigue * .08f,
                MatchActionType.StandingHold => 6f + performer.Attributes.Technical + performer.Attributes.Power * .4f,
                MatchActionType.ExitRing => 1f + performer.Stun * .35f + performer.Fatigue * .1f +
                    (performer.Alignment == KayfabeAlignment.Heel ? performer.Attributes.HeelWork * .5f +
                        Math.Max(0, receiver.MatchControl - performer.MatchControl) * .2f : 0),
                MatchActionType.Retreat => 2f + performer.Fatigue * .18f + performer.Stun * .25f,
                MatchActionType.Lift => 24f,
                MatchActionType.TurnOver => action.MoveId != null ? 10f + receiver.Stun * .1f : 18f + receiver.Stun * .2f,
                MatchActionType.Climb => 8f + performer.Attributes.HighFlying,
                MatchActionType.LimbAttack => 12f + performer.Attributes.Technical + performer.Attributes.RingPsychology * .5f + Math.Max(0, 12 - Math.Abs(MatchPhysics.Damage(receiver, performer.TargetRegion) - 35) * .3f),
                MatchActionType.Tag => 6f + performer.Fatigue * .7f,
                MatchActionType.DoubleTeam => 22f,
                MatchActionType.Taunt => 3f + performer.Attributes.Charisma * .35f + performer.Attributes.Acting * .2f,
                MatchActionType.ObjectSetup or MatchActionType.ObjectClimb => 30f,
                MatchActionType.Signature => 28f + performer.MatchControl * .2f,
                MatchActionType.Finisher => (12f + performer.MatchControl * .12f) * (.3f + Math.Min(1f, (float)state.ElapsedSeconds / state.TargetDurationSeconds)),
                _ => 1f
            };
            if (state.Drama > 50f && action.Type is MatchActionType.Pin or MatchActionType.Submission or MatchActionType.Signature or MatchActionType.Finisher) score *= 1.25f;
            if (state.Intensity > 40f && action.Type is MatchActionType.HeavyStrike or MatchActionType.Grapple or MatchActionType.Signature) score *= 1.2f;
            if (state.CrowdHeat < 25f && action.Type is MatchActionType.HeavyStrike or MatchActionType.Signature or MatchActionType.Finisher) score *= 1.25f;
            score *= TacticalFit(state, action, performer, receiver);
            if (action.Type is MatchActionType.Climb or MatchActionType.Dive or MatchActionType.TopRopeAttack or MatchActionType.ObjectSetup or MatchActionType.ObjectClimb)
                score *= (float)WrestlerHiddenRules.RiskPreferenceWeight(performer.RiskTaking, .5f);
            bool singles = state.Participants.Count == 2 && !state.Rules.Tag;
            // Recovery and hold ticks must not erase the opponent's memory of recent offense.
            var repeats = singles ? state.Events.Where(x => x.PerformerId == action.PerformerId && (string.IsNullOrEmpty(x.ReceiverId) || x.ReceiverId == action.ReceiverId) &&
                (x.IsPhysicalAttack || x.Type is MatchActionType.BasicStrike or MatchActionType.HeavyStrike or MatchActionType.Grapple or
                    MatchActionType.GroundAttack or MatchActionType.LimbAttack or MatchActionType.Submission or MatchActionType.StandingHold or
                    MatchActionType.Signature or MatchActionType.Finisher) && !x.IntentionalCombination).TakeLast(4).Count(x => x.Type == action.Type) :
                state.Events.TakeLast(4).Count(x => x.PerformerId == action.PerformerId && x.Type == action.Type);
            if (!singles && action.Type == MatchActionType.LimbAttack && !string.IsNullOrEmpty(performer.PlannedFinisherMoveId) &&
                MatchPhysics.Damage(receiver, performer.TargetRegion) < 60 || state.Exchange?.Kind is ExchangeKind.GripCombo or ExchangeKind.Hold or ExchangeKind.Chain) repeats = 0;
            if (singles && state.Rules.IronMan)
            {
                var own = state.Participants.Where(x => x.SideId == performer.SideId).Sum(x => x.Falls);
                var opposition = state.GetOpponents(performer.Id).GroupBy(x => x.SideId).Max(g => g.Sum(x => x.Falls));
                var urgency = Math.Clamp(1f - (state.TargetDurationSeconds - state.ElapsedSeconds) / 180f, 0, 1);
                if (own <= opposition)
                    score *= action.Type is MatchActionType.Pin or MatchActionType.Submission or MatchActionType.Finisher ? 1 + urgency * 3 :
                        action.Type is MatchActionType.Rest or MatchActionType.Retreat or MatchActionType.Taunt or MatchActionType.StandingHold ? 1 - urgency * .9f : 1;
                else if (action.Type is MatchActionType.Retreat or MatchActionType.Rest) score *= 1 + urgency * 2;
            }
            return Math.Max(.01f, score * (float)Math.Pow(state.Rules.DominantSideId == performer.SideId ? .9f : .65f, repeats) * (phaseState?.GetActionScoreModifier(state, action) ?? 1f));
        }

        private static float TacticalFit(MatchEngineState state, MatchEngineAction action, MatchEngineParticipant a, MatchEngineParticipant b)
        {
            var fit = 0f;
            var tired = Math.Max(0, (a.Fatigue - 45) / 55);
            var vulnerable = Math.Min(1, (b.Stun + b.LastImpact * 2) / 50);
            var recent = state.ElapsedSeconds - b.LastImpactTime < 15;
            switch (action.Type)
            {
                case MatchActionType.Rest: case MatchActionType.Retreat:
                    fit = tired * 1.6f - (b.IsDowned && recent ? vulnerable * .7f : 0); break;
                case MatchActionType.HeavyStrike: case MatchActionType.Grapple: case MatchActionType.Climb: case MatchActionType.RopeRun:
                    fit = -tired * .7f + (b.IsDowned && recent ? .25f : 0); break;
                case MatchActionType.IrishWhip: case MatchActionType.CornerWhip:
                    fit = (b.Stun > 5 ? .35f : 0) - tired * .6f - Math.Max(0, (b.LegDamage - 50) / 50); break;
                case MatchActionType.Pin:
                    fit = !MatchHealth.LegalCover(state, a, b) ? -3 :
                        MatchHealth.PinRisk(state, a, b) >= .16f ? 1 + MatchHealth.PinRisk(state, a, b) * 4 : -1.1f;
                    if (recent && b.LastImpactWasFinisher && MatchHealth.LegalCover(state, a, b)) fit += 2;
                    break;
                case MatchActionType.Reposition:
                    if (action.PartnerId == null && b.Position is MatchPosition.Center or MatchPosition.Ropes or MatchPosition.Corner)
                        fit = Math.Clamp((MatchMovement.DistanceFromCenter(b) - .6f) / .3f, 0, 1) * 3 -
                            (recent && b.LastImpactWasFinisher ? 1.5f : 0) - tired * .5f;
                    break;
                case MatchActionType.LimbAttack:
                    fit = .5f - MatchPhysics.Damage(b, a.TargetRegion) / 80f; break;
                case MatchActionType.Submission: case MatchActionType.StandingHold:
                    fit = Math.Min(1, MatchPhysics.Damage(b, a.TargetRegion) / 50); break;
                case MatchActionType.Signature: case MatchActionType.Finisher:
                    fit = vulnerable + Math.Min(1, state.ElapsedSeconds / (float)state.TargetDurationSeconds) - .5f - tired;
                    if (action.Type == MatchActionType.Finisher && action.MoveId == a.PlannedFinisherMoveId && !string.IsNullOrEmpty(action.MoveId)) fit += .6f;
                    var previous = state.Events.LastOrDefault(e => e.PerformerId == a.Id && e.Type == MatchActionType.Finisher &&
                        e.ReceiverId == b.Id && e.MoveId == action.MoveId && e.Result is MatchActionResult.FullSuccess or MatchActionResult.PartialSuccess);
                    if (previous != null && b.Stun < 20 && state.ElapsedSeconds - previous.MatchTimeSeconds < 150)
                    {
                        var prior = previous.After?.Participants.FirstOrDefault(p => p.Id == b.Id);
                        float priorDamage = prior == null ? 0 : previous.TargetRegion switch { BodyRegion.Head => prior.HeadDamage,
                            BodyRegion.Arms => prior.ArmDamage, BodyRegion.Legs => prior.LegDamage, _ => prior.BodyDamage };
                        fit -= MatchPhysics.Damage(b, previous.TargetRegion) - priorDamage < 8 ? 1.1f : .3f;
                    }
                    break;
                case MatchActionType.Lift: case MatchActionType.TurnOver:
                    if (!string.IsNullOrEmpty(a.PlannedFinisherMoveId) && a.MatchControl >= 66) fit = .9f - tired;
                    break;
                case MatchActionType.Taunt:
                    fit = b.IsDowned && b.Stun > 20 ? .5f : -.8f;
                    if (b.LastImpactWasFinisher && recent) fit -= 1.2f;
                    break;
                case MatchActionType.Probe:
                    fit = -state.Events.TakeLast(3).Count(x => x.PerformerId == a.Id && x.Type == MatchActionType.Probe) * .6f; break;
            }
            if (!string.IsNullOrEmpty(a.PlannedFinisherMoveId) && state.FindMove?.Invoke(a.PlannedFinisherMoveId) is MatchMoveRules goal)
            {
                if (action.Type == MatchActionType.LimbAttack && MatchPhysics.Damage(b, a.TargetRegion) < 60) fit += .45f;
                if (MatchPhysics.Damage(b, a.TargetRegion) >= 60)
                {
                    if (action.Type == MatchActionType.LimbAttack) fit -= 1.1f;
                    if (state.Participants.Count == 2 && !state.Rules.Tag && action.Type is MatchActionType.Submission or MatchActionType.StandingHold && MatchPhysics.Kind(goal) != MoveKind.Submission)
                        fit -= 1.5f;
                    if (MatchHealth.IsPreparation(action.Type) && !MatchPhysics.CanMove(state, a, b, a.PlannedFinisherMoveId)) fit += 1.8f;
                }
                if (action.Type == MatchActionType.Climb && action.MoveId == a.PlannedFinisherMoveId) fit += .9f;
                if (!b.IsDowned && MatchTechniques.IsGrounded(goal.Situation) && action.Type is MatchActionType.HeavyStrike or MatchActionType.Grapple) fit += .55f;
            }
            return Math.Max(.1f, 1 + fit * a.Attributes.RingPsychology / 20);
        }
    }

    public static class ActionSelector
    {
        public static MatchEngineAction Select(IReadOnlyList<MatchEngineAction> actions, Random random, float temperature = 18f)
        {
            if (actions == null || actions.Count == 0) throw new ArgumentException("At least one action is required.", nameof(actions));
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (temperature <= 0f) throw new ArgumentOutOfRangeException(nameof(temperature));
            var weights = actions.Select(x => Math.Pow(Math.Max(.01f, x.Score), 18d / temperature)).ToArray();
            return actions[PerformerSelector.WeightedIndex(weights, random)];
        }
    }

    public static class ActionResolver
    {
        public static MatchEngineEvent Resolve(MatchEngineState state, MatchEngineAction action, Random random)
        {
            return MatchPhysics.Resolve(state, action, random);
        }

        public static float PinfallChance(MatchEngineState state, MatchEngineParticipant performer, MatchEngineParticipant receiver, float recentImpact, bool afterFinisher)
        {
            var chance = .005f + receiver.Fatigue * .002f + receiver.Stun * .005f + recentImpact * .007f + receiver.BodyDamage * .002f;
            if (afterFinisher) chance += .20f;
            if (state.Phase == MatchPhase.Opening) chance *= .08f;
            return Math.Max(.001f, Math.Min(.92f, chance));
        }
    }

    public static class CrowdSimulator
    {
        public static void Apply(MatchEngineState state, MatchEngineEvent matchEvent)
        {
            state.CrowdHeat = Clamp(state.CrowdHeat + matchEvent.CrowdReaction - (matchEvent.Type == MatchActionType.Rest ? 2f : 0f), 0f, 100f);
            state.Drama = Clamp(state.Drama * .995f + matchEvent.DramaChange, 0f, 100f);
            state.Intensity = Clamp(state.Intensity * .8f + matchEvent.IntensityChange, 0f, 100f);
        }

        private static float Clamp(float value, float minimum, float maximum) => Math.Max(minimum, Math.Min(maximum, value));
    }

    public static class CommentaryGenerator
    {
        public static string Generate(MatchEngineEvent value, Func<string, string> name = null)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            name ??= id => id;
            var time = $"{value.MatchTimeSeconds / 60:00}:{value.MatchTimeSeconds % 60:00}";
            return $"{time} {name(value.PerformerId)} - {value.Type} - {value.Result}";
        }
    }

    public static class MatchDebugRunner
    {
        public static MatchEngineState Run(MatchEngineParticipant first, MatchEngineParticipant second, int seed, Action<string> write)
        {
            var state = new MatchSimulator(seed).Simulate(first, second);
            foreach (var matchEvent in state.Events) write?.Invoke(CommentaryGenerator.Generate(matchEvent));
            write?.Invoke(state.IsAborted ? $"Aborted: {state.FailureReason}" : $"Winner: {state.WinnerId}");
            return state;
        }
    }
}
