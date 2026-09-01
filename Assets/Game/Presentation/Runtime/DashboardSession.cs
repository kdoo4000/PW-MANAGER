using System;
using System.IO;
using System.Collections.Generic;
using PWManager.Domain.Models;
using PWManager.Infrastructure.Save;
using UnityEngine;

namespace PWManager.Presentation
{
    public static class DashboardSession
    {
        public static GameSave ActiveSave { get; private set; }
        public static event Action<GameSave> SaveChanged;

        public static void SetActiveSave(GameSave save)
        {
            ActiveSave = save;
            SaveChanged?.Invoke(save);
        }

        public static void PersistActive()
        {
            if (ActiveSave == null) return;
            Service().Save("autosave", ActiveSave);
            SaveChanged?.Invoke(ActiveSave);
        }

        public static void SaveAs(string slotName)
        {
            if (ActiveSave == null) return;
            Service().Save(slotName, ActiveSave);
        }

        public static void Load(string slotName) => SetActiveSave(Service().Load(slotName));
        public static IReadOnlyList<string> ListSlots() => Service().ListSlots();
        public static DateTime GetLastWriteTime(string slotName) => Service().GetLastWriteTime(slotName);

        public static void Clear() => SetActiveSave(null);

        private static SaveService Service() => new(Path.Combine(Application.persistentDataPath, "Saves"));
    }
}
