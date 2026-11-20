using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion12To13Migration : ISaveMigration
    {
        public int FromVersion => 12;
        public int ToVersion => 13;

        public void Migrate(GameSave save)
        {
            foreach (var assignment in save.ScoutAssignments ?? new())
            {
                if (assignment?.Status != ScoutAssignmentStatus.InProgress) continue;
                var deadline = assignment.StartDate.AddDays(ScoutAssignmentState.DurationDays);
                if (assignment.CompleteDate.CompareTo(deadline) > 0) assignment.CompleteDate = deadline;
            }
            save.SaveVersion = ToVersion;
        }
    }
}
