using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion21To22Migration : ISaveMigration
    {
        public int FromVersion => 21;
        public int ToVersion => 22;

        public void Migrate(GameSave save)
        {
            foreach (var story in save.Storylines ?? new()) story.EventIds ??= new();
            foreach (var result in save.ShowResults ?? new())
            {
                result.StoryEvents ??= new();
                result.CreatedStorylines ??= new();
            }
            save.SaveVersion = ToVersion;
        }
    }
}
