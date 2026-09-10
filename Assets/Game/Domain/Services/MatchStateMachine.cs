using System;
using System.Collections.Generic;
using System.Linq;

namespace PWManager.Domain.Services
{
    public sealed class PhaseTransitionCandidate
    {
        public MatchPhase Target;
        public float Score;
    }

    public interface IMatchState
    {
        MatchPhase Phase { get; }
        void Enter(MatchEngineState context);
        void Update(MatchEngineState context);
        void Exit(MatchEngineState context);
        IReadOnlyList<PhaseTransitionCandidate> EvaluateTransitions(MatchEngineState context);
        float GetActionScoreModifier(MatchEngineState context, MatchEngineAction action);
        float GetPerformerScoreModifier(MatchEngineState context, MatchEngineParticipant participant);
    }

    public sealed class MatchStateMachine
    {
        private readonly Dictionary<MatchPhase, IMatchState> states;
        private readonly Random random;

        public IMatchState CurrentState { get; private set; }

        public MatchStateMachine(Random random)
        {
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            states = new IMatchState[]
            {
                new OpeningMatchState(), new NeutralMatchState(), new ControlMatchState(), new ComebackMatchState(),
                new BackAndForthMatchState(), new ClimaxMatchState(), new FinishMatchState()
            }.ToDictionary(x => x.Phase);
        }

        public void Initialize(MatchEngineState context, MatchPhase phase = MatchPhase.Opening)
        {
            CurrentState = states[phase];
            context.Phase = phase;
            CurrentState.Enter(context);
        }

        public void Update(MatchEngineState context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (CurrentState == null) Initialize(context, context.Phase);
            else context.Phase = CurrentState.Phase;
            CurrentState.Update(context);
            var candidates = CurrentState.EvaluateTransitions(context);
            if (candidates == null || candidates.Count == 0) return;
            var weights = candidates.Select(x => ValidWeight(x.Score)).ToArray();
            if (weights.Sum() <= 0d) return;
            var next = candidates[PerformerSelector.WeightedIndex(weights, random)].Target;
            if (next == CurrentState.Phase) return;
            CurrentState.Exit(context);
            CurrentState = states[next];
            context.Phase = next;
            CurrentState.Enter(context);
        }

        private static double ValidWeight(float value) => float.IsNaN(value) || float.IsInfinity(value) || value <= 0f ? 0d : value;
    }

    public abstract class MatchStateBase : IMatchState
    {
        private static readonly MatchPhase[] Phases = (MatchPhase[])Enum.GetValues(typeof(MatchPhase));
        public abstract MatchPhase Phase { get; }
        protected abstract IReadOnlyList<float> BaseWeights { get; }

        public virtual void Enter(MatchEngineState context) { }
        public virtual void Update(MatchEngineState context) { }
        public virtual void Exit(MatchEngineState context) { }

        public IReadOnlyList<PhaseTransitionCandidate> EvaluateTransitions(MatchEngineState context)
        {
            var result = new List<PhaseTransitionCandidate>(Phases.Length);
            for (var i = 0; i < Phases.Length; i++)
                result.Add(new PhaseTransitionCandidate { Target = Phases[i], Score = Math.Max(.01f, BaseWeights[i] * TransitionModifier(context, Phases[i])) });
            return result;
        }

        public virtual float GetActionScoreModifier(MatchEngineState context, MatchEngineAction action) => 1f;
        public virtual float GetPerformerScoreModifier(MatchEngineState context, MatchEngineParticipant participant) => 0f;

        protected virtual float TransitionModifier(MatchEngineState context, MatchPhase target)
        {
            var progress = Math.Max(0f, (float)context.ElapsedSeconds / context.TargetDurationSeconds);
            var participants = context.GetActiveParticipants();
            var averageFatigue = participants.Average(x => x.Fatigue);
            var controlSpread = participants.Max(x => x.MatchControl) - participants.Min(x => x.MatchControl);
            var recent = context.Events.TakeLast(6).ToList();
            var last = recent.LastOrDefault();
            var nearFall = recent.Any(x => x.Result == MatchActionResult.NearFall);
            var decisive = last != null && (last.Type == MatchActionType.Finisher || last.Impact >= 15f);
            var dominantPerformer = recent.Count >= 3 && recent.GroupBy(x => x.PerformerId).Max(x => x.Count()) >= recent.Count - 1;
            var repeatedlyHit = recent.Count >= 3 && recent.GroupBy(x => x.ReceiverId).Max(x => x.Count()) >= recent.Count - 1;
            var modifier = 1f;
            if (target == Phase) modifier *= 1f + Math.Min(1.5f, TrailingPhaseEvents(context) * .18f);
            if (target is MatchPhase.Neutral or MatchPhase.Control) modifier *= Math.Max(.2f, 1.4f - progress);
            if (target == MatchPhase.Control) modifier *= 1f + controlSpread / 45f;
            if (target == MatchPhase.Comeback) modifier *= 1f + controlSpread / 35f + averageFatigue / 100f + (dominantPerformer || repeatedlyHit ? .8f : 0f);
            if (target == MatchPhase.BackAndForth) modifier *= 1f + context.Intensity / 80f + context.CrowdHeat / 150f + (nearFall ? 1.2f : 0f);
            if (target == MatchPhase.Climax) modifier *= .35f + progress + context.Drama / 80f + context.CrowdHeat / 120f + (decisive ? 2f : 0f);
            if (target == MatchPhase.Finish) modifier *= .08f + progress * progress + averageFatigue / 120f + (decisive ? 5f : 0f) + (nearFall ? 1.5f : 0f);
            if (TransitionCount(context) >= 3 && target != Phase) modifier *= .45f;
            return modifier;
        }

        protected static int TrailingPhaseEvents(MatchEngineState context) => context.Events.AsEnumerable().Reverse().TakeWhile(x => x.Phase == context.Phase).Count();

        private static int TransitionCount(MatchEngineState context)
        {
            var phases = context.Events.TakeLast(8).Select(x => x.Phase).ToList();
            var count = 0;
            for (var i = 1; i < phases.Count; i++) if (phases[i] != phases[i - 1]) count++;
            return count;
        }

        protected static bool Is(MatchEngineAction action, params MatchActionType[] types) => types.Contains(action.Type);
    }

    public sealed class OpeningMatchState : MatchStateBase
    {
        public override MatchPhase Phase => MatchPhase.Opening;
        protected override IReadOnlyList<float> BaseWeights { get; } = new[] { 120f, 100f, 50f, 5f, 15f, 4f, 1f };
        public override float GetActionScoreModifier(MatchEngineState context, MatchEngineAction action) =>
            Is(action, MatchActionType.BasicStrike, MatchActionType.Grapple) ? 1.35f : action.Type == MatchActionType.Pin ? .2f :
            action.Type == MatchActionType.Signature ? .45f : action.Type == MatchActionType.Finisher ? .12f : 1f;
    }

    public sealed class NeutralMatchState : MatchStateBase
    {
        public override MatchPhase Phase => MatchPhase.Neutral;
        protected override IReadOnlyList<float> BaseWeights { get; } = new[] { 12f, 105f, 55f, 8f, 40f, 8f, 1f };
        public override float GetPerformerScoreModifier(MatchEngineState context, MatchEngineParticipant participant) => (50f - participant.MatchControl) * .08f;
    }

    public sealed class ControlMatchState : MatchStateBase
    {
        public override MatchPhase Phase => MatchPhase.Control;
        protected override IReadOnlyList<float> BaseWeights { get; } = new[] { 2f, 18f, 110f, 35f, 40f, 15f, 2f };
        public override float GetPerformerScoreModifier(MatchEngineState context, MatchEngineParticipant participant) =>
            participant.MatchControl >= context.Participants.Max(x => x.MatchControl) ? 12f : 0f;
    }

    public sealed class ComebackMatchState : MatchStateBase
    {
        public override MatchPhase Phase => MatchPhase.Comeback;
        protected override IReadOnlyList<float> BaseWeights { get; } = new[] { 1f, 8f, 55f, 65f, 80f, 28f, 3f };
        public override float GetActionScoreModifier(MatchEngineState context, MatchEngineAction action) =>
            Is(action, MatchActionType.HeavyStrike, MatchActionType.Signature) ? 1.55f : action.Type == MatchActionType.Rest ? .55f : 1f;
        public override float GetPerformerScoreModifier(MatchEngineState context, MatchEngineParticipant participant)
        {
            var lowest = context.Participants.Min(x => x.MatchControl);
            var recentlyHit = context.Events.TakeLast(3).Any(x => x.ReceiverId == participant.Id);
            return (participant.MatchControl <= lowest ? 22f : 0f) + (recentlyHit ? 14f : 0f) + participant.ActionReadiness * .08f;
        }
    }

    public sealed class BackAndForthMatchState : MatchStateBase
    {
        public override MatchPhase Phase => MatchPhase.BackAndForth;
        protected override IReadOnlyList<float> BaseWeights { get; } = new[] { 1f, 8f, 35f, 25f, 105f, 55f, 5f };
        public override float GetActionScoreModifier(MatchEngineState context, MatchEngineAction action) => Is(action, MatchActionType.HeavyStrike, MatchActionType.Signature) ? 1.4f : 1f;
        public override float GetPerformerScoreModifier(MatchEngineState context, MatchEngineParticipant participant) =>
            context.Events.TakeLast(2).Any(x => x.PerformerId == participant.Id) ? -18f : 8f;
    }

    public sealed class ClimaxMatchState : MatchStateBase
    {
        public override MatchPhase Phase => MatchPhase.Climax;
        protected override IReadOnlyList<float> BaseWeights { get; } = new[] { .5f, 2f, 12f, 10f, 45f, 110f, 38f };
        public override float GetActionScoreModifier(MatchEngineState context, MatchEngineAction action) =>
            Is(action, MatchActionType.Signature, MatchActionType.Finisher, MatchActionType.Pin, MatchActionType.Submission) ? 1.65f : 1f;
    }

    public sealed class FinishMatchState : MatchStateBase
    {
        public override MatchPhase Phase => MatchPhase.Finish;
        protected override IReadOnlyList<float> BaseWeights { get; } = new[] { .2f, 1f, 5f, 4f, 18f, 35f, 115f };
        public override float GetActionScoreModifier(MatchEngineState context, MatchEngineAction action) =>
            Is(action, MatchActionType.Finisher, MatchActionType.Pin, MatchActionType.Submission) ? 4f : Is(action, MatchActionType.Signature, MatchActionType.HeavyStrike) ? 1.8f : .65f;
    }
}
