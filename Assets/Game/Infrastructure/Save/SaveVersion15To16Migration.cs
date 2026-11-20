using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion15To16Migration : ISaveMigration
    {
        public int FromVersion => 15;
        public int ToVersion => 16;
        public void Migrate(GameSave save)
        {
            foreach (var candidate in save.ScoutCandidates)
            {
                if (candidate?.ValueEstimates == null) continue;
                foreach (var estimate in candidate.ValueEstimates)
                {
                    if (estimate == null || estimate.HasEvaluation) continue;
                    // Keep the original report's assessment instead of rolling a new opinion on load.
                    estimate.IsFullyRevealed = estimate.Minimum == estimate.Maximum;
                    estimate.Assessment = ScoutAssessmentRules.Assess((estimate.Minimum + estimate.Maximum) * .5f);
                    estimate.HasEvaluation = true;
                }
            }
            save.SaveVersion = ToVersion;
        }
    }
}
