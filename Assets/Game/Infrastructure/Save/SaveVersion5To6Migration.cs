using System;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion5To6Migration : ISaveMigration
    {
        public int FromVersion => 5;
        public int ToVersion => 6;
        public void Migrate(GameSave save)
        {
            foreach (var wrestler in save.Wrestlers ?? new())
            {
                if (wrestler?.Attributes == null || wrestler.Growth == null) continue;
                wrestler.Growth.MatchPotentialCap = Capacity(wrestler.Growth.MatchPotentialCap, WrestlerOverallCalculator.MatchTotal(wrestler.Attributes) / 10f);
                wrestler.Growth.PromoPotentialCap = Capacity(wrestler.Growth.PromoPotentialCap, WrestlerOverallCalculator.PromoTotal(wrestler.Attributes) / 7f);
            }
            save.SaveVersion = ToVersion;
        }

        private static float Capacity(float potential, float currentAverage) =>
            float.IsFinite(potential) && potential >= 1f && potential <= 20f && float.IsFinite(currentAverage) && currentAverage >= 1f && currentAverage <= 20f
                ? Math.Max(potential, currentAverage) : potential;
    }
}
