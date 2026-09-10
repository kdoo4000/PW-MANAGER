using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public enum MatchPhase { Opening, Neutral, Control, Comeback, BackAndForth, Climax, Finish }
    public enum MatchActionType { BasicStrike, HeavyStrike, Grapple, GroundAttack, Submission, Pin, Rest, Signature, Finisher }
    public enum MatchActionResult { FullSuccess, PartialSuccess, ExecutionFailure, ReceptionFailure, Countered, Submitted, OneCount, TwoCount, NearFall, Pinfall }

    [Serializable]
    public sealed class MatchEngineParticipant
    {
        public string Id;
        public WrestlerAttributesState Attributes = new();
        public float Fatigue;
        public float MatchControl = 50f;
        public float ActionReadiness = 50f;
        public bool IsDowned;
    }

    [Serializable]
    public sealed class MatchEngineState
    {
        public int ElapsedSeconds;
        public int TargetDurationSeconds = 720;
        public bool IsFinished;
        public bool IsAborted;
        public string FailureReason;
        public string WinnerId;
        public MatchPhase Phase;
        public float Intensity;
        public float Drama;
        public float CrowdHeat;
        public List<MatchEngineParticipant> Participants = new();
        public List<MatchEngineEvent> Events = new();

        public MatchEngineParticipant GetParticipant(string id) => Participants.Single(x => x.Id == id);
        public List<MatchEngineParticipant> GetOpponents(string id) => Participants.Where(x => x.Id != id).ToList();
        public List<MatchEngineParticipant> GetActiveParticipants() => Participants.Where(x => x != null).ToList();
    }

    [Serializable]
    public sealed class MatchEngineAction
    {
        public MatchActionType Type;
        public string MoveId;
        public string PerformerId;
        public string ReceiverId;
        public List<string> ReceiverIds = new();
        public float Score;
    }

    [Serializable]
    public sealed class MatchEngineEvent
    {
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
    }

    public sealed class MatchSimulator
    {
        public const int MaximumEvents = 500;
        private readonly Random random;
        private readonly int maximumEvents;
        private readonly MatchStateMachine stateMachine;

        public MatchSimulator(int seed, int maximumEvents = MaximumEvents)
        {
            if (maximumEvents < 0) throw new ArgumentOutOfRangeException(nameof(maximumEvents));
            random = new Random(seed);
            this.maximumEvents = maximumEvents;
            stateMachine = new MatchStateMachine(random);
        }

        public MatchEngineState Simulate(MatchEngineParticipant first, MatchEngineParticipant second,
            int targetDurationSeconds = 720) => Simulate(new[] { first, second }, targetDurationSeconds);

        public MatchEngineState Simulate(IEnumerable<MatchEngineParticipant> participants, int targetDurationSeconds = 720)
        {
            if (participants == null) throw new ArgumentNullException(nameof(participants));
            var participantList = participants.ToList();
            if (participantList.Count < 2 || participantList.Any(x => x == null || string.IsNullOrWhiteSpace(x.Id)) ||
                participantList.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != participantList.Count)
                throw new ArgumentException("A match requires at least two participants with distinct ids.", nameof(participants));
            if (targetDurationSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(targetDurationSeconds));
            foreach (var participant in participantList) ValidateAttributes(participant.Attributes);

            var state = new MatchEngineState
            {
                Participants = participantList,
                TargetDurationSeconds = targetDurationSeconds,
                Phase = MatchPhase.Opening
            };
            stateMachine.Initialize(state);
            while (!state.IsFinished && state.Events.Count < maximumEvents) Step(state);
            if (!state.IsFinished) Abort(state);
            return state;
        }

        public MatchEngineEvent Step(MatchEngineState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.IsFinished || state.IsAborted) throw new InvalidOperationException("The match has already ended.");
            stateMachine.Update(state);
            var performer = PerformerSelector.Select(state, random, stateMachine.CurrentState);
            var target = TargetSelector.Select(state, performer, random);
            var actions = ActionGenerator.Generate(state, performer, target);
            foreach (var action in actions) action.Score = ActionEvaluator.Score(state, action, stateMachine.CurrentState);
            var selected = ActionSelector.Select(actions, random);
            var matchEvent = ActionResolver.Resolve(state, selected, random);
            state.Events.Add(matchEvent);
            CrowdSimulator.Apply(state, matchEvent);
            UpdateParticipantReadiness(state, matchEvent);
            state.ElapsedSeconds += random.Next(8, 26);
            return matchEvent;
        }

        private static void UpdateParticipantReadiness(MatchEngineState state, MatchEngineEvent matchEvent)
        {
            foreach (var participant in state.Participants)
            {
                var change = participant.Id == matchEvent.PerformerId ? (matchEvent.IsMajorMoment ? -24f : -14f) : 6f;
                if (participant.Id == matchEvent.ReceiverId) change = matchEvent.Result is MatchActionResult.ExecutionFailure or MatchActionResult.Countered ? 16f : -matchEvent.Impact;
                participant.ActionReadiness = Clamp(participant.ActionReadiness + change, 0f, 100f);
            }
        }

        private static void Abort(MatchEngineState state) { state.IsAborted = true; state.FailureReason = "MaximumEvents reached without a finish."; }

        private static void ValidateAttributes(WrestlerAttributesState value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            foreach (var attribute in new[] { value.Brawling, value.Power, value.Technical, value.HighFlying, value.Selling, value.Stamina, value.RingPsychology })
                if (float.IsNaN(attribute) || float.IsInfinity(attribute) || attribute < 0f || attribute > 20f)
                    throw new ArgumentOutOfRangeException(nameof(value), "Match attributes must be finite and between 0 and 20.");
        }

        private static float Clamp(float value, float minimum, float maximum) => Math.Max(minimum, Math.Min(maximum, value));
    }

    public static class PerformerSelector
    {
        public static MatchEngineParticipant Select(MatchEngineState state, Random random, IMatchState phaseState = null)
        {
            var candidates = state.GetActiveParticipants();
            if (candidates.Count == 0) throw new InvalidOperationException("No participant can act.");
            var weights = candidates.Select(x => Math.Exp(Math.Min(40d, Score(state, x, phaseState) / 24f))).ToArray();
            return candidates[WeightedIndex(weights, random)];
        }

        public static float Score(MatchEngineState state, MatchEngineParticipant participant, IMatchState phaseState = null)
        {
            var score = 20f + participant.MatchControl * .55f + participant.ActionReadiness * .75f - participant.Fatigue * .35f;
            if (participant.IsDowned) score -= 45f;
            if (state.Events.TakeLast(2).Any(x => x.PerformerId == participant.Id)) score -= 28f;
            if (state.Events.TakeLast(3).Any(x => x.ReceiverId == participant.Id)) score += state.Phase == MatchPhase.Comeback ? 30f : 10f;
            score += phaseState?.GetPerformerScoreModifier(state, participant) ?? 0f;
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
            var score = 20f + target.Fatigue * .35f + target.MatchControl * .2f + (target.IsDowned ? 8f : 0f);
            if (state.Events.TakeLast(4).Any(x => x.PerformerId == target.Id && x.ReceiverId == performer.Id)) score += 18f;
            if (state.Events.LastOrDefault()?.ReceiverId == target.Id) score -= 10f;
            return Math.Max(.01f, score);
        }
    }

    public static class ActionGenerator
    {
        public static List<MatchEngineAction> Generate(MatchEngineState state, MatchEngineParticipant performer, MatchEngineParticipant receiver)
        {
            if (performer.IsDowned)
                return new List<MatchEngineAction> { New(MatchActionType.Rest, performer, receiver) };
            var types = receiver.IsDowned
                ? new[] { MatchActionType.GroundAttack, MatchActionType.Submission, MatchActionType.Pin, MatchActionType.Rest }
                : new[] { MatchActionType.BasicStrike, MatchActionType.HeavyStrike, MatchActionType.Grapple, MatchActionType.Rest };
            var result = types.Select(x => New(x, performer, receiver)).ToList();
            if (performer.MatchControl >= 65f)
                result.Add(New(MatchActionType.Signature, performer, receiver));
            if (performer.MatchControl >= 78f)
                result.Add(New(MatchActionType.Finisher, performer, receiver));
            return result;
        }

        private static MatchEngineAction New(MatchActionType type, MatchEngineParticipant performer, MatchEngineParticipant receiver) => new()
        {
            Type = type, PerformerId = performer.Id, ReceiverId = receiver.Id, ReceiverIds = new List<string> { receiver.Id }
        };
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
                MatchActionType.HeavyStrike => 18f + performer.Attributes.Brawling + performer.Attributes.Power * .5f,
                MatchActionType.Grapple => 25f + performer.Attributes.Technical,
                MatchActionType.GroundAttack => 25f + performer.Attributes.Brawling,
                MatchActionType.Submission => 5f + performer.Attributes.Technical + receiver.Fatigue * .2f,
                MatchActionType.Pin => 8f + receiver.Fatigue * .35f + state.Drama * .1f,
                MatchActionType.Rest => 4f + performer.Fatigue * .25f,
                MatchActionType.Signature => 28f + performer.MatchControl * .2f,
                MatchActionType.Finisher => 38f + performer.MatchControl * .25f,
                _ => 1f
            };
            if (state.Drama > 50f && action.Type is MatchActionType.Pin or MatchActionType.Submission or MatchActionType.Signature or MatchActionType.Finisher) score *= 1.25f;
            if (state.Intensity > 40f && action.Type is MatchActionType.HeavyStrike or MatchActionType.Grapple or MatchActionType.Signature) score *= 1.2f;
            if (state.CrowdHeat < 25f && action.Type is MatchActionType.HeavyStrike or MatchActionType.Signature or MatchActionType.Finisher) score *= 1.25f;
            var repeats = state.Events.TakeLast(4).Count(x => x.PerformerId == action.PerformerId && x.Type == action.Type);
            return Math.Max(.01f, score * (float)Math.Pow(.45f, repeats) * (phaseState?.GetActionScoreModifier(state, action) ?? 1f));
        }
    }

    public static class ActionSelector
    {
        public static MatchEngineAction Select(IReadOnlyList<MatchEngineAction> actions, Random random, float temperature = 18f)
        {
            if (actions == null || actions.Count == 0) throw new ArgumentException("At least one action is required.", nameof(actions));
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (temperature <= 0f) throw new ArgumentOutOfRangeException(nameof(temperature));
            var weights = actions.Select(x => Math.Exp(Math.Min(40d, x.Score / temperature))).ToArray();
            return actions[PerformerSelector.WeightedIndex(weights, random)];
        }
    }

    public static class ActionResolver
    {
        public static MatchEngineEvent Resolve(MatchEngineState state, MatchEngineAction action, Random random)
        {
            var performer = state.GetParticipant(action.PerformerId);
            var receiver = state.GetParticipant(action.ReceiverId);
            if (action.Type == MatchActionType.Rest) return Rest(state, performer, receiver);
            if (action.Type == MatchActionType.Pin) return Pin(state, performer, receiver, random);
            if (action.Type == MatchActionType.Submission) return Submission(state, performer, receiver, random);

            var ability = Ability(performer, action.Type) - performer.Fatigue * .08f;
            var executionChance = Clamp(.58f + (ability - 10f) * .025f, .25f, .92f);
            var receptionChance = Clamp(.72f + (receiver.Attributes.Selling - 10f) * .02f - receiver.Fatigue * .002f, .35f, .95f);
            var execution = random.NextDouble() < executionChance;
            var reception = random.NextDouble() < receptionChance;
            var result = !execution ? MatchActionResult.ExecutionFailure : reception ? MatchActionResult.FullSuccess : MatchActionResult.ReceptionFailure;
            var impact = execution ? Impact(action.Type) * (reception ? 1f : 1.2f) : 1f;
            var control = execution ? Math.Min(12f, impact * .65f) : -6f;
            receiver.Fatigue = Clamp(receiver.Fatigue + impact, 0f, 100f);
            performer.Fatigue = Clamp(performer.Fatigue + impact * .18f, 0f, 100f);
            performer.MatchControl = Clamp(performer.MatchControl + control, 0f, 100f);
            receiver.MatchControl = Clamp(receiver.MatchControl - control, 0f, 100f);
            receiver.IsDowned = execution && impact >= 7f;
            return Event(state, action, result, impact, control, action.Type >= MatchActionType.Signature);
        }

        private static MatchEngineEvent Rest(MatchEngineState state, MatchEngineParticipant performer, MatchEngineParticipant receiver)
        {
            performer.Fatigue = Clamp(performer.Fatigue - 4f, 0f, 100f);
            performer.IsDowned = false;
            return Event(state, new MatchEngineAction { Type = MatchActionType.Rest, PerformerId = performer.Id, ReceiverId = receiver.Id }, MatchActionResult.FullSuccess, 0f, -2f, false);
        }

        private static MatchEngineEvent Pin(MatchEngineState state, MatchEngineParticipant performer, MatchEngineParticipant receiver, Random random)
        {
            var lastImpact = state.Events.LastOrDefault(x => x.ReceiverId == receiver.Id)?.Impact ?? 0f;
            var finisher = state.Events.LastOrDefault(x => x.ReceiverId == receiver.Id)?.Type == MatchActionType.Finisher;
            var chance = PinfallChance(state, performer, receiver, lastImpact, finisher);
            var roll = random.NextDouble();
            var result = roll < chance ? MatchActionResult.Pinfall : roll < chance + .12f ? MatchActionResult.NearFall : roll < chance + .42f ? MatchActionResult.TwoCount : MatchActionResult.OneCount;
            if (result == MatchActionResult.Pinfall) { state.IsFinished = true; state.WinnerId = performer.Id; }
            else { receiver.IsDowned = false; receiver.MatchControl = Clamp(receiver.MatchControl + 8f, 0f, 100f); }
            return Event(state, new MatchEngineAction { Type = MatchActionType.Pin, PerformerId = performer.Id, ReceiverId = receiver.Id }, result, 0f, 0f, result >= MatchActionResult.NearFall);
        }

        public static float PinfallChance(MatchEngineState state, MatchEngineParticipant performer, MatchEngineParticipant receiver, float recentImpact, bool afterFinisher)
        {
            var chance = .01f + receiver.Fatigue * .0048f + recentImpact * .012f + performer.MatchControl * .001f - receiver.MatchControl * .0015f;
            if (afterFinisher) chance += .22f;
            if (state.Phase == MatchPhase.Opening) chance *= .08f;
            else if (state.Phase >= MatchPhase.Climax) chance += .08f;
            return Clamp(chance, .001f, .94f);
        }

        private static MatchEngineEvent Submission(MatchEngineState state, MatchEngineParticipant performer, MatchEngineParticipant receiver, Random random)
        {
            var chance = Clamp(.005f + receiver.Fatigue * .0035f + (performer.Attributes.Technical - receiver.Attributes.Technical) * .008f + (state.Phase >= MatchPhase.Climax ? .08f : 0f), .001f, .7f);
            var submitted = random.NextDouble() < chance;
            if (submitted) { state.IsFinished = true; state.WinnerId = performer.Id; }
            else { receiver.Fatigue = Clamp(receiver.Fatigue + 3f, 0f, 100f); receiver.IsDowned = false; }
            return Event(state, new MatchEngineAction { Type = MatchActionType.Submission, PerformerId = performer.Id, ReceiverId = receiver.Id }, submitted ? MatchActionResult.Submitted : MatchActionResult.Countered, 3f, submitted ? 8f : -4f, submitted);
        }

        private static MatchEngineEvent Event(MatchEngineState state, MatchEngineAction action, MatchActionResult result, float impact, float control, bool major) => new()
        {
            Type = action.Type, Result = result, PerformerId = action.PerformerId, ReceiverId = action.ReceiverId,
            MatchTimeSeconds = state.ElapsedSeconds, Phase = state.Phase, Impact = impact, FatigueChange = impact,
            MatchControlChange = control, CrowdReaction = major ? 8f : impact * .3f,
            DramaChange = major ? 10f : impact * .2f, IntensityChange = impact * .25f,
            IsMajorMoment = major, CommentaryKey = $"match.{action.Type.ToString().ToLowerInvariant()}.{result.ToString().ToLowerInvariant()}"
        };

        private static float Ability(MatchEngineParticipant performer, MatchActionType type) => type switch
        {
            MatchActionType.BasicStrike or MatchActionType.HeavyStrike or MatchActionType.GroundAttack => performer.Attributes.Brawling,
            MatchActionType.Grapple => performer.Attributes.Technical,
            MatchActionType.Signature or MatchActionType.Finisher => Math.Max(performer.Attributes.Power, Math.Max(performer.Attributes.Technical, performer.Attributes.HighFlying)),
            _ => performer.Attributes.RingPsychology
        };

        private static float Impact(MatchActionType type) => type switch
        {
            MatchActionType.BasicStrike => 3f, MatchActionType.Grapple => 4f, MatchActionType.HeavyStrike => 7f,
            MatchActionType.GroundAttack => 5f, MatchActionType.Signature => 12f, MatchActionType.Finisher => 18f, _ => 1f
        };

        private static float Clamp(float value, float minimum, float maximum) => Math.Max(minimum, Math.Min(maximum, value));
    }

    public static class CrowdSimulator
    {
        public static void Apply(MatchEngineState state, MatchEngineEvent matchEvent)
        {
            state.CrowdHeat = Clamp(state.CrowdHeat + matchEvent.CrowdReaction - (matchEvent.Type == MatchActionType.Rest ? 2f : 0f), 0f, 100f);
            state.Drama = Clamp(state.Drama + matchEvent.DramaChange, 0f, 100f);
            state.Intensity = Clamp(state.Intensity * .75f + matchEvent.IntensityChange, 0f, 100f);
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
