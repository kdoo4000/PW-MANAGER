using System;
using NUnit.Framework;
using PWManager.Domain.Models;
using UnityEngine;
using GameEntityId = PWManager.Domain.Identifiers.EntityId;

namespace PWManager.Tests
{
    public sealed class GameSaveTests
    {
        [Test]
        public void CreateNew_InitializesRootMetadata()
        {
            var now = new DateTime(2026, 8, 18, 4, 0, 0, DateTimeKind.Utc);

            var save = GameSave.CreateNew(12345, now);

            Assert.That(save.SaveVersion, Is.EqualTo(GameSave.CurrentSaveVersion));
            Assert.That(GameEntityId.IsValidRuntimeId(save.SaveId), Is.True);
            Assert.That(save.CreatedAtUtc, Is.EqualTo(now.ToString("O")));
            Assert.That(save.UpdatedAtUtc, Is.EqualTo(now.ToString("O")));
            Assert.That(save.WorldSeed, Is.EqualTo(12345));
            Assert.That(save.CurrentDate, Is.EqualTo(new GameDate(2026, 6, 1)));
            Assert.That(save.ProcessedIds, Is.Empty);
            Assert.That(save.Wrestlers, Is.Empty);
            Assert.That(save.Managers, Is.Empty);
            Assert.That(save.Contracts, Is.Empty);
            Assert.That(save.StaffDepartments, Is.Empty);
            Assert.That(save.ScoutAssignments, Is.Empty);
            Assert.That(save.ScoutCandidates, Is.Empty);
            Assert.That(save.VenueContracts, Is.Empty);
            Assert.That(save.SeasonPolicy, Is.Null);
            Assert.That(save.Schedules, Is.Empty);
            Assert.That(save.ShowResults, Is.Empty);
            Assert.That(save.MatchResults, Is.Empty);
            Assert.That(save.PromoResults, Is.Empty);
            Assert.That(save.Transactions, Is.Empty);
            Assert.That(save.SystemNames.Find(x => x.Id == "attribute.ring_psychology").KoreanName, Is.EqualTo("경기 운영"));
            Assert.That(save.SystemNames.Find(x => x.Id == "attribute.ring_psychology").EnglishName, Is.EqualTo("Ring Psychology"));
        }

        [Test]
        public void JsonRoundTrip_PreservesRootMetadata()
        {
            var save = GameSave.CreateNew(9876, new DateTime(2026, 8, 18, 4, 0, 0, DateTimeKind.Utc));
            save.ProcessedIds.Add(GameEntityId.CreateRuntimeId());

            var json = JsonUtility.ToJson(save);
            var restored = JsonUtility.FromJson<GameSave>(json);

            Assert.That(restored.SaveVersion, Is.EqualTo(save.SaveVersion));
            Assert.That(restored.SaveId, Is.EqualTo(save.SaveId));
            Assert.That(restored.CurrentDate, Is.EqualTo(save.CurrentDate));
            Assert.That(restored.ProcessedIds, Is.EqualTo(save.ProcessedIds));
            Assert.That(restored.SystemNames[0].KoreanName, Is.Not.Empty);
            Assert.That(restored.SystemNames[0].EnglishName, Is.Not.Empty);
        }

        [Test]
        public void CreateNew_WithNonUtcTimestamp_IsRejected()
        {
            var localTime = new DateTime(2026, 8, 18, 13, 0, 0, DateTimeKind.Local);

            Assert.Throws<ArgumentException>(() => GameSave.CreateNew(1, localTime));
        }

        [Test]
        public void GameDate_WithInvalidDate_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameDate(2026, 2, 30));
        }

        [Test]
        public void AgeOn_UsesWhetherBirthdayHasPassed()
        {
            var birthDate = new GameDate(2000, 9, 7);

            Assert.That(birthDate.AgeOn(new GameDate(2026, 9, 6)), Is.EqualTo(25));
            Assert.That(birthDate.AgeOn(new GameDate(2026, 9, 7)), Is.EqualTo(26));
        }

    }
}
