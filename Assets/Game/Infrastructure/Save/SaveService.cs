using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Domain.Validation;
using UnityEngine;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveService
    {
        private static readonly Regex SlotNamePattern = new(
            "^[A-Za-z0-9_-]+$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly string saveDirectory;
        private readonly Dictionary<int, ISaveMigration> migrationsByVersion = new();

        public SaveService(string saveDirectory, IEnumerable<ISaveMigration> migrations = null)
        {
            if (string.IsNullOrWhiteSpace(saveDirectory))
                throw new ArgumentException("Save directory is required.", nameof(saveDirectory));

            this.saveDirectory = Path.GetFullPath(saveDirectory);
            foreach (var migration in migrations ?? Array.Empty<ISaveMigration>())
            {
                if (migration == null) throw new ArgumentException("Migration cannot be null.", nameof(migrations));
                if (migration.ToVersion != migration.FromVersion + 1)
                    throw new ArgumentException("Migrations must advance exactly one version.", nameof(migrations));
                if (!migrationsByVersion.TryAdd(migration.FromVersion, migration))
                    throw new ArgumentException($"Duplicate migration from version {migration.FromVersion}.", nameof(migrations));
            }
        }

        public void Save(string slotName, GameSave save)
        {
            ValidateSlotName(slotName);
            if (save == null) throw new ArgumentNullException(nameof(save));
            if (save.SaveVersion != GameSave.CurrentSaveVersion)
                throw new InvalidDataException($"Save version must be {GameSave.CurrentSaveVersion}.");

            var errors = GameSaveValidator.Validate(save);
            if (errors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));

            Directory.CreateDirectory(saveDirectory);
            var paths = GetPaths(slotName);
            var json = JsonUtility.ToJson(save, true);

            try
            {
                WriteDurably(paths.Temp, json);
                ValidateSerializedFile(paths.Temp);

                if (File.Exists(paths.Save)) File.Replace(paths.Temp, paths.Save, paths.Backup);
                else File.Move(paths.Temp, paths.Save);
            }
            finally
            {
                if (File.Exists(paths.Temp)) File.Delete(paths.Temp);
            }
        }

        public GameSave Load(string slotName)
        {
            ValidateSlotName(slotName);
            return LoadFile(GetPaths(slotName).Save);
        }

        public GameSave LoadBackup(string slotName)
        {
            ValidateSlotName(slotName);
            return LoadFile(GetPaths(slotName).Backup);
        }

        private GameSave LoadFile(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Save file was not found.", path);

            GameSave save;
            try
            {
                save = JsonUtility.FromJson<GameSave>(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                throw new InvalidDataException("Save file is not valid JSON.", exception);
            }

            if (save == null) throw new InvalidDataException("Save file is empty.");
            NormalizeOptionalState(save);
            MigrateToCurrentVersion(save);

            var errors = GameSaveValidator.Validate(save);
            if (errors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
            return save;
        }

        private static void NormalizeOptionalState(GameSave save)
        {
            save.Shows ??= new List<ShowState>();
            save.TagTeams ??= new List<TagTeamState>();
            save.ShowEvents ??= new List<ShowEventState>();
            save.MatchPlans ??= new List<MatchPlanState>();
            foreach (var match in save.MatchPlans)
                if (match != null && match.MatchTypeId == "matchtype_003")
                    match.MatchTypeId = "matchtype_001";
            save.PromoPlans ??= new List<PromoPlanState>();
            save.ShowResults ??= new List<ShowResultState>();
            save.MatchResults ??= new List<MatchResultState>();
            save.PromoResults ??= new List<PromoResultState>();
            if (save.SeasonPolicy != null && string.IsNullOrEmpty(save.SeasonPolicy.Id))
                save.SeasonPolicy = null;
        }

        private void MigrateToCurrentVersion(GameSave save)
        {
            if (save.SaveVersion < 1 || save.SaveVersion > GameSave.CurrentSaveVersion)
                throw new InvalidDataException($"Unsupported save version: {save.SaveVersion}.");

            while (save.SaveVersion < GameSave.CurrentSaveVersion)
            {
                if (!migrationsByVersion.TryGetValue(save.SaveVersion, out var migration))
                    throw new InvalidDataException($"Missing migration from version {save.SaveVersion}.");
                migration.Migrate(save);
                if (save.SaveVersion != migration.ToVersion)
                    throw new InvalidDataException($"Migration from version {migration.FromVersion} did not set version {migration.ToVersion}.");
            }
        }

        private void ValidateSerializedFile(string path)
        {
            var restored = JsonUtility.FromJson<GameSave>(File.ReadAllText(path));
            if (restored == null || restored.SaveVersion != GameSave.CurrentSaveVersion)
                throw new InvalidDataException("Serialized save failed basic validation.");
        }

        private static void WriteDurably(string path, string content)
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(content);
            writer.Flush();
            stream.Flush(true);
        }

        private (string Save, string Temp, string Backup) GetPaths(string slotName)
        {
            var basePath = Path.Combine(saveDirectory, slotName);
            return ($"{basePath}.json", $"{basePath}.tmp", $"{basePath}.bak");
        }

        private static void ValidateSlotName(string slotName)
        {
            if (string.IsNullOrWhiteSpace(slotName) || !SlotNamePattern.IsMatch(slotName))
                throw new ArgumentException("Slot name may contain only letters, numbers, underscores, and hyphens.", nameof(slotName));
        }
    }
}
