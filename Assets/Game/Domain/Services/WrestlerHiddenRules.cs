using System;
using System.Linq;
using System.Reflection;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public static class WrestlerHiddenRules
    {
        private static readonly FieldInfo[] Fields = typeof(WrestlerHiddenState).GetFields().OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();

        public static WrestlerHiddenState Create(int seed, string wrestlerId)
        {
            unchecked
            {
                var hash = 2166136261u ^ (uint)seed ^ 0x4849444eu;
                foreach (var character in wrestlerId ?? string.Empty) { hash ^= character; hash *= 16777619u; }
                var random = new Random((int)(hash & 0x7fffffffu));
                var result = new WrestlerHiddenState();
                foreach (var field in Fields)
                {
                    var normal = Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
                    field.SetValue(result, Math.Clamp((int)Math.Round(10.5 + 3.5 * normal, MidpointRounding.AwayFromZero), 1, 20));
                }
                return result;
            }
        }

        public static void InitializeSave(GameSave save)
        {
            foreach (var wrestler in save.Wrestlers ?? new())
            {
                if (wrestler == null) continue;
                wrestler.Hidden ??= Create(save.WorldSeed, wrestler.Id);
                wrestler.Presentation?.TraitIds?.Clear();
            }
            foreach (var report in save.ScoutCandidates ?? new()) report?.RevealedTraitIds?.Clear();
        }

        public static bool IsValid(WrestlerHiddenState hidden) => hidden == null || Fields.All(x => (int)x.GetValue(hidden) is >= 1 and <= 20);
        public static float TrainingEffort(WrestlerState wrestler) => 1 + ((wrestler.Hidden?.Professionalism ?? 10) - 10) / 50f;
        public static float InjuryRiskMultiplier(int injuryProneness) => 1 + (injuryProneness - 10) / 20f;
        public static float OpponentProtectionMultiplier(int collegiality) => 1 + (10 - collegiality) / 25f;
        public static float BigMatchAdjustment(WrestlerState wrestler, float importance) => ((wrestler.Hidden?.BigMatchPerformance ?? 10) - 10) / 20f * importance;
        public static double RiskPreferenceWeight(int riskTaking, float challenge) => Math.Exp((riskTaking - 10) / 10d * Math.Clamp(challenge, -1, 1));
        public static float PerformanceVariance(WrestlerState wrestler)
        {
            var consistency = wrestler.Hidden?.Consistency ?? 10;
            return consistency <= 10 ? .5f + (10 - consistency) / 18f : .5f - (consistency - 10) / 40f;
        }
    }
}
