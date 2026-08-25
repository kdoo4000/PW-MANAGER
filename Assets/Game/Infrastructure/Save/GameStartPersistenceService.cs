using System;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class GameStartPersistenceService
    {
        private readonly GameStartService gameStartService;
        private readonly SaveService saveService;

        public GameStartPersistenceService(SaveService saveService, GameStartService gameStartService = null)
        {
            this.saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
            this.gameStartService = gameStartService ?? new GameStartService();
        }

        public GameSave CreateSaveAndReload(string slotName, GameStartRequest request)
        {
            var generated = gameStartService.CreateInitialSave(request);
            saveService.Save(slotName, generated);
            var restored = saveService.Load(slotName);
            if (!string.Equals(restored.SaveId, generated.SaveId, StringComparison.Ordinal))
                throw new InvalidOperationException("The reloaded save does not match the generated initial save.");
            return restored;
        }
    }
}
