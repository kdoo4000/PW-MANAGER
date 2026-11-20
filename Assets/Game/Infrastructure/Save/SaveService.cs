using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Domain.Validation;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveService
    {
        private static readonly Regex SlotNamePattern = new(
            "^[\\p{L}\\p{N}_-]+$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly string saveDirectory;
        private readonly Dictionary<int, ISaveMigration> migrationsByVersion = new();
        private static readonly byte[] CompressedSignature = Encoding.ASCII.GetBytes("PWMZ1");

        public SaveService(string saveDirectory, IEnumerable<ISaveMigration> migrations = null)
        {
            if (string.IsNullOrWhiteSpace(saveDirectory))
                throw new ArgumentException("Save directory is required.", nameof(saveDirectory));

            this.saveDirectory = Path.GetFullPath(saveDirectory);
            migrations ??= new ISaveMigration[] { new SaveVersion0To1Migration(), new SaveVersion1To2Migration(), new SaveVersion2To3Migration(), new SaveVersion3To4Migration(), new SaveVersion4To5Migration(), new SaveVersion5To6Migration(), new SaveVersion6To7Migration(), new SaveVersion7To8Migration(), new SaveVersion8To9Migration(), new SaveVersion9To10Migration(), new SaveVersion10To11Migration(), new SaveVersion11To12Migration(), new SaveVersion12To13Migration(), new SaveVersion13To14Migration(), new SaveVersion14To15Migration(), new SaveVersion15To16Migration(), new SaveVersion16To17Migration(), new SaveVersion17To18Migration(), new SaveVersion18To19Migration(), new SaveVersion19To20Migration(), new SaveVersion20To21Migration(), new SaveVersion21To22Migration(), new SaveVersion22To23Migration(), new SaveVersion23To24Migration(), new SaveVersion24To25Migration(), new SaveVersion25To26Migration(), new SaveVersion26To27Migration(), new SaveVersion27To28Migration(), new SaveVersion28To29Migration(), new SaveVersion29To30Migration() };
            foreach (var migration in migrations)
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

            save.SystemNames = SystemNames.CreateDefaults();
            WrestlerHiddenRules.InitializeSave(save);
            WrestlerAdaptationService.InitializeSave(save);
            foreach (var contract in save.Contracts ?? new())
                if (contract != null && string.IsNullOrEmpty(contract.PromotionId)) contract.PromotionId = save.Promotion?.Id;
            StorylineService.UpgradeRelationships(save);
            ChampionshipService.Upgrade(save);
            NormalizePlayerBirthDate(save);
            save.UpdatedAtUtc = DateTime.UtcNow.ToString("O");

            var errors = GameSaveValidator.Validate(save);
            if (errors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));

            Directory.CreateDirectory(saveDirectory);
            var paths = GetPaths(slotName);
            try
            {
                var expectedHash = WriteDurably(paths.Temp, save);
                ValidateSerializedFile(paths.Temp, expectedHash);

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

        public bool HasBackup(string slotName)
        {
            ValidateSlotName(slotName);
            return File.Exists(GetPaths(slotName).Backup);
        }

        public IReadOnlyList<string> ListSlots()
        {
            if (!Directory.Exists(saveDirectory)) return Array.Empty<string>();
            return Directory.EnumerateFiles(saveDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(x => !string.IsNullOrWhiteSpace(x) && SlotNamePattern.IsMatch(x))
                .ToList();
        }

        public DateTime GetLastWriteTime(string slotName)
        {
            ValidateSlotName(slotName);
            return File.GetLastWriteTime(GetPaths(slotName).Save);
        }

        private GameSave LoadFile(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Save file was not found.", path);

            GameSave save;
            try
            {
                save = ReadPayload(path, payload =>
                {
                    using var text = new StreamReader(payload, new UTF8Encoding(false, true), true, 65536, true);
                    return SaveJson.Read(text);
                });
            }
            catch (Exception exception)
            {
                throw new InvalidDataException("저장 파일이 손상되었거나 지원하지 않는 형식입니다.", exception);
            }

            if (save == null) throw new InvalidDataException("Save file is empty.");
            NormalizeOptionalState(save);
            MigrateToCurrentVersion(save);
            WrestlerHiddenRules.InitializeSave(save);
            WrestlerAdaptationService.InitializeSave(save);

            var errors = GameSaveValidator.Validate(save);
            if (errors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
            foreach (var wrestler in save.Wrestlers) wrestler.FanReaction.Upgrade();
            foreach (var wrestler in save.Wrestlers.Where(x => x != null))
                if (wrestler.Presentation?.Appearance != null || string.IsNullOrEmpty(wrestler.Presentation?.PortraitResourcePath))
                    PortraitAppearanceRules.Ensure(wrestler);
            if (save.Player != null)
            {
                var playerWrestler = save.Wrestlers.FirstOrDefault(x => x.Id == save.Player.WrestlerId);
                save.Player.Appearance ??= playerWrestler?.Presentation.Appearance?.Clone() ?? PortraitAppearanceRules.Generate(
                    new WrestlerIdentityState { Gender = save.Player.Gender, BodyType = PortraitAppearanceRules.BodyType(save.Player.HeightCm, save.Player.WeightKg) switch
                    { 2 => WrestlerBodyType.Lightweight, 3 => WrestlerBodyType.Heavyweight, _ => WrestlerBodyType.Balanced } },
                    save.Player.WrestlingStyleId, new System.Random(PortraitAppearanceRules.StableSeed(save.Player.Name)));
                PortraitAppearanceRules.Normalize(save.Player.Appearance, save.Player.Gender);
            }
            FanAudienceService.Initialize(save);
            return save;
        }

        private static void NormalizeOptionalState(GameSave save)
        {
            save.SystemNames = SystemNames.CreateDefaults();
            NormalizePlayerBirthDate(save);
            if (save.Promotion != null && save.Promotion.Audience?.IsInitialized != true) save.Promotion.Audience = null;
            if (save.Promotion != null && string.IsNullOrWhiteSpace(save.Promotion.Abbreviation))
                save.Promotion.Abbreviation = PromotionState.CreateAbbreviation(save.Promotion.Name);
            save.Shows ??= new List<ShowState>();
            save.UndiscoveredWrestlerIds ??= new();
            save.OtherPromotions ??= new();
            save.WrestlerBehaviors ??= new();
            save.LockerRoomRelationships ??= new();
            save.LockerRoomEvents ??= new();
            save.TagTeams ??= new List<TagTeamState>();
            save.Championships ??= new List<ChampionshipState>();
            save.SalaryPayments ??= new List<SalaryPaymentState>();
            save.ContractNegotiations ??= new List<ContractNegotiationState>();
            save.ContractTerminations ??= new List<ContractTerminationState>();
            save.SeasonResults ??= new List<SeasonResultState>();
            save.SponsorSeasons ??= new List<SponsorSeasonState>();
            save.ExperienceRecords ??= new(); save.GrowthQuarterResults ??= new(); save.CareerEvents ??= new();
            ChampionshipService.Upgrade(save);
            save.Stables ??= new List<StableState>();
            save.StoryRelationships ??= new List<StoryRelationshipState>();
            save.Storylines ??= new List<StorylineState>();
            save.StoryCandidates ??= new List<StoryCandidateState>();
            StorylineService.UpgradeRelationships(save);
            save.ShowEvents ??= new List<ShowEventState>();
            save.MatchPlans ??= new List<MatchPlanState>();
            save.PromoPlans ??= new List<PromoPlanState>();
            save.ShowResults ??= new List<ShowResultState>();
            save.MatchResults ??= new List<MatchResultState>();
            foreach (var result in save.MatchResults.Where(x => x != null))
            {
                result.SimulationBeats ??= new List<MatchSimulationBeatState>();
                result.NarrativeLines ??= new List<NarrativeLineState>();
            }
            save.PromoResults ??= new List<PromoResultState>();
            save.InboxMessages ??= new List<InboxMessageState>();
            foreach (var venue in save.VenueContracts ?? new List<VenueContractState>())
                if (venue != null) venue.ProductionCost = HourlyVenueCost(venue.VenueId, venue.ProductionCost);
            save.ScoutAssignments ??= new List<ScoutAssignmentState>();
            save.ScoutCandidates ??= new List<ScoutCandidateState>();
            foreach (var candidate in save.ScoutCandidates.Where(x => x != null))
            {
                candidate.ValueEstimates ??= new List<ScoutValueEstimate>();
                candidate.RevealedTraitIds ??= new List<string>();
            }
            if (save.SeasonPolicy != null && string.IsNullOrEmpty(save.SeasonPolicy.Id))
                save.SeasonPolicy = null;
            if (save.SeasonPolicy != null)
            {
                if (save.SeasonPolicy.RegularShowDurationMinutes <= 0) save.SeasonPolicy.RegularShowDurationMinutes = 120;
                save.SeasonPolicy.PpvShowTitles ??= new List<PpvTitlePolicyState>();
                foreach (var policy in save.SeasonPolicy.PpvShowTitles.Where(x => x != null && x.DurationMinutes <= 0))
                    policy.DurationMinutes = save.Shows.FirstOrDefault(x => x != null && x.ShowType != ScheduledShowType.Regular && x.Date.Month == policy.Month)?.DurationLimit ?? 120;
            }
        }

        private static void NormalizePlayerBirthDate(GameSave save)
        {
            if (save.Player == null || save.Player.BirthDate.Year > 0 || save.Player.Age <= 0) return;
            save.Player.BirthDate = new GameDate(2026 - save.Player.Age, 6, 1);
            save.Player.Age = 0;
        }

        private static long HourlyVenueCost(string venueId, long cost) => (venueId, cost) switch
        {
            ("venue_001", 500) => 250,
            ("venue_002", 4000) => 2000,
            ("venue_003", 60000) => 20000,
            ("venue_004", 250000) => 80000,
            ("venue_005", 1000000) => 250000,
            ("venue_006", 5000000) => 1000000,
            _ => cost
        };

        private void MigrateToCurrentVersion(GameSave save)
        {
            if (save.SaveVersion < 0 || save.SaveVersion > GameSave.CurrentSaveVersion)
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

        private static void ValidateSerializedFile(string path, byte[] expectedHash)
        {
            // The source state was validated before writing. Verify every restored byte without allocating a second game.
            ReadPayload(path, payload =>
            {
                using var sha = SHA256.Create();
                if (!expectedHash.SequenceEqual(sha.ComputeHash(payload)))
                    throw new InvalidDataException("Serialized save differs from the written state.");
                return true;
            });
        }

        private static byte[] WriteDurably(string path, GameSave save)
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
            writer.Write(CompressedSignature);
            writer.Write(0);
            writer.Write(new byte[32]);
            writer.Flush();
            long length;
            using var contentHash = SHA256.Create();
            using (var gzip = new GZipStream(stream, CompressionLevel.Optimal, true))
            {
                using var counted = new SavePayloadStream(gzip, int.MaxValue);
                using var hashed = new CryptoStream(counted, contentHash, CryptoStreamMode.Write, true);
                using var text = new StreamWriter(hashed, new UTF8Encoding(false), 65536, true);
                SaveJson.Write(text, save);
                text.Flush();
                hashed.FlushFinalBlock();
                length = counted.BytesProcessed;
            }
            stream.Position = CompressedSignature.Length + sizeof(int) + 32;
            using var sha = SHA256.Create();
            var payloadHash = sha.ComputeHash(stream);
            stream.Position = CompressedSignature.Length;
            writer.Write(checked((int)length));
            writer.Write(payloadHash);
            writer.Flush();
            stream.Flush(true);
            return contentHash.Hash;
        }

        private static T ReadPayload<T>(string path, Func<Stream, T> read)
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            var signature = reader.ReadBytes(CompressedSignature.Length);
            if (!signature.SequenceEqual(CompressedSignature))
            {
                if (signature.Take(4).SequenceEqual(CompressedSignature.Take(4)))
                    throw new InvalidDataException("Unsupported compressed save format.");
                stream.Position = 0;
                return read(stream);
            }

            var length = reader.ReadInt32();
            if (length <= 0) throw new InvalidDataException("Invalid save length.");
            var expectedHash = reader.ReadBytes(32);
            var payloadStart = stream.Position;
            using var sha = SHA256.Create();
            if (!expectedHash.SequenceEqual(sha.ComputeHash(stream)))
                throw new InvalidDataException("Compressed save checksum mismatch.");
            stream.Position = payloadStart;
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var counted = new SavePayloadStream(gzip, length, true);
            var result = read(counted);
            if (counted.ReadByte() != -1) throw new InvalidDataException("Unread compressed save content.");
            return result;
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
