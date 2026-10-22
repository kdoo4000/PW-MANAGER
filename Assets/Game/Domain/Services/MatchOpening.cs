using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    [Serializable]
    public sealed class MatchOpeningState
    {
        public bool Initialized, Completed;
        public MatchOpeningStyle Style;
        public float Hostility, StatusGap;
        public string DominantSideId, InitiatorId;
        public int CompletedChains;
    }

    public static class MatchOpening
    {
        public static float Hostility(GameSave save, IEnumerable<MatchSideState> sides)
        {
            var teams = sides.SelectMany(s => s.MemberIds.Select(id => (Id: id, Side: s.Id))).ToDictionary(x => x.Id, x => x.Side);
            return (save.StoryRelationships ?? new()).Where(r => r != null && r.FirstWrestlerId != null && r.SecondWrestlerId != null &&
                teams.TryGetValue(r.FirstWrestlerId, out var a) && teams.TryGetValue(r.SecondWrestlerId, out var b) && a != b)
                .Select(r => MatchPhysics.Finite(r.Value) ? Math.Max(0f, -r.Value) : 0).DefaultIfEmpty(0).Max();
        }

        public static void Initialize(MatchEngineState s, Random random)
        {
            if (!Enum.IsDefined(typeof(MatchOpeningStyle), s.Rules.OpeningStyle) || !MatchPhysics.Finite(s.Rules.OpeningHostility) ||
                s.Rules.OpeningHostility < 0 || s.Rules.OpeningHostility > 100 || s.Participants.Any(p =>
                    !MatchPhysics.Finite(p.StatusValue) || p.StatusValue < 0 || p.StatusValue > 100 ||
                    !MatchPhysics.Finite(p.AmbushTendency) || p.AmbushTendency < 0 || p.AmbushTendency > 1))
                throw new ArgumentException("Invalid opening context.");
            var groups = s.Participants.GroupBy(p => p.SideId ?? p.Id).OrderByDescending(g => g.Average(p => p.StatusValue)).ToList();
            var o = s.Opening;
            o.Initialized = true;
            o.Hostility = s.Rules.OpeningHostility;
            o.StatusGap = groups.First().Average(p => p.StatusValue) - groups.Last().Average(p => p.StatusValue);
            o.DominantSideId = o.StatusGap >= 25 ? groups.First().Key : s.Rules.DominantSideId;
            var h = o.Hostility / 100d;
            var g = Math.Min(1, o.StatusGap / 50d);
            var weights = new[] { 80 * (1-h) * (1-h) * (1-.8*g), 1+180*h*h*h, 1+160*g*g, 5+18*s.Participants.Max(p => p.AmbushTendency) };
            o.Style = s.Rules.OpeningStyle == MatchOpeningStyle.Automatic
                ? (MatchOpeningStyle)(1 + PerformerSelector.WeightedIndex(weights, random)) : s.Rules.OpeningStyle;
            var candidates = s.GetActiveParticipants().ToList();
            if (o.DominantSideId != null && o.Style == MatchOpeningStyle.Dominance)
                candidates = candidates.Where(p => (p.SideId ?? p.Id) == o.DominantSideId).ToList();
            else if (o.StatusGap >= 25 && o.Style == MatchOpeningStyle.Clash)
                candidates = candidates.Where(p => (p.SideId ?? p.Id) != o.DominantSideId).ToList();
            if (candidates.Count == 0) candidates = s.GetActiveParticipants().ToList();
            o.InitiatorId = candidates[PerformerSelector.WeightedIndex(candidates.Select(p => o.Style == MatchOpeningStyle.Ambush ? 1d+3*p.AmbushTendency : 1d).ToArray(), random)].Id;
        }

        public static int MaximumSeconds(MatchEngineState s) => Math.Max(6, Math.Min(120, s.TargetDurationSeconds / 5));
        public static bool CanChain(MatchEngineState s) => s.Phase == MatchPhase.Opening && !s.Opening.Completed &&
            s.Opening.Style == MatchOpeningStyle.Probe && !s.Events.Any(e => e.Impact > 0 && e.Type != MatchActionType.Chain);
        public static bool CanLeave(MatchEngineState s)
        {
            if (!s.Opening.Initialized) return true;
            if (s.Exchange?.Kind == ExchangeKind.Chain) return false;
            if (s.Events.Any(e => e.Type == MatchActionType.Finisher || e.Type == MatchActionType.Spot || e.Impact >= 9)) return true;
            if (s.ElapsedSeconds >= MaximumSeconds(s)) return true;
            if (s.Opening.Style != MatchOpeningStyle.Probe) return s.ElapsedSeconds >= 10;
            return s.ElapsedSeconds >= Math.Min(75, s.TargetDurationSeconds / 10) && s.Opening.CompletedChains > 0 &&
                s.Participants.Max(p => p.MatchControl) - s.Participants.Min(p => p.MatchControl) >= 8;
        }
        public static float PerformerBonus(MatchEngineState s, MatchEngineParticipant p) =>
            s.Phase == MatchPhase.Opening && (s.Opening.Style is MatchOpeningStyle.Clash or MatchOpeningStyle.Dominance) &&
            (p.SideId ?? p.Id) == s.Opening.DominantSideId ? Math.Min(65, s.Opening.StatusGap) : 0;
    }
}
