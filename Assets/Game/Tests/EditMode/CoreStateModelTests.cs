using System;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Validation;
using UnityEngine;
using GameEntityId = PWManager.Domain.Identifiers.EntityId;

namespace PWManager.Tests
{
    public sealed class CoreStateModelTests
    {
        [Test]
        public void ValidCoreState_PassesValidationAndJsonRoundTrip()
        {
            var save = CreateValidSave();

            var errors = GameSaveValidator.Validate(save);
            var restored = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));

            Assert.That(errors, Is.Empty);
            Assert.That(restored.Promotion.Name, Is.EqualTo("PW Wrestling"));
            Assert.That(restored.Wrestlers, Has.Count.EqualTo(1));
            Assert.That(restored.Wrestlers[0].Identity.RingName, Is.EqualTo("Test Wrestler"));
            Assert.That(restored.Wrestlers[0].Presentation.WrestlingStyleId, Is.EqualTo("style_001"));
            Assert.That(restored.Wrestlers[0].Attributes.MatchOverall, Is.EqualTo(10f));
            Assert.That(restored.Contracts, Has.Count.EqualTo(1));
            Assert.That(restored.Transactions, Has.Count.EqualTo(1));
        }

        [Test]
        public void CurrentCash_IsDerivedFromInitialCashAndValidTransactions()
        {
            var save = CreateValidSave();
            save.Transactions.Add(new TransactionRecord
            {
                Id = GameEntityId.CreateRuntimeId(),
                PromotionId = save.Promotion.Id,
                Date = save.CurrentDate,
                Type = TransactionType.Refund,
                Amount = 500,
                ReasonId = GameEntityId.CreateRuntimeId(),
                IsVoided = true
            });

            Assert.That(save.Promotion.CalculateCurrentCash(save.Transactions), Is.EqualTo(9000));
        }

        [Test]
        public void InvalidRangesAndOverlappingContracts_AreReported()
        {
            var save = CreateValidSave();
            save.Wrestlers[0].Condition.Condition = 101;
            var original = save.Contracts[0];
            save.Contracts.Add(new ContractState
            {
                Id = GameEntityId.CreateRuntimeId(),
                PersonId = original.PersonId,
                Type = ContractType.Wrestler,
                StartDate = new GameDate(2026, 7, 1),
                EndDate = new GameDate(2027, 5, 31),
                Status = ContractStatus.Active
            });

            var errors = GameSaveValidator.Validate(save);

            Assert.That(errors, Has.Some.Contains("Condition"));
            Assert.That(errors, Has.Some.Contains("overlap"));
        }

        [Test]
        public void ManagerContract_RequiresExistingManager()
        {
            var save = CreateValidSave();
            save.Contracts.Add(new ContractState
            {
                Id = GameEntityId.CreateRuntimeId(),
                PersonId = GameEntityId.CreateRuntimeId(),
                Type = ContractType.Manager,
                StartDate = new GameDate(2026, 6, 1),
                EndDate = new GameDate(2027, 5, 31),
                Status = ContractStatus.Active
            });

            Assert.That(GameSaveValidator.Validate(save), Has.Some.Contains("missing manager"));
        }

        [Test]
        public void ValidSeasonOperations_PassValidationAndJsonRoundTrip()
        {
            var save = CreateValidSave();
            var venue = new VenueContractState
            {
                Id = GameEntityId.CreateRuntimeId(), VenueId = "venue_001",
                ContractType = VenueContractType.RegularSeason,
                StartDate = new GameDate(2026, 6, 1), EndDate = new GameDate(2027, 5, 31),
                ProductionCost = 100, Status = VenueContractStatus.Paid
            };
            var signature = new ScheduleState
            {
                Id = GameEntityId.CreateRuntimeId(), ShowType = ScheduledShowType.PpvSignature,
                Date = new GameDate(2027, 4, 25), Status = ScheduleStatus.Confirmed
            };
            save.VenueContracts.Add(venue);
            save.Schedules.Add(signature);
            save.StaffDepartments.Add(new StaffDepartmentState
            {
                Id = GameEntityId.CreateRuntimeId(), DepartmentType = StaffDepartmentType.Medical,
                CurrentLevel = 1, UnlockedLevel = 1
            });
            save.SeasonPolicy = new SeasonPolicyState
            {
                Id = GameEntityId.CreateRuntimeId(), StartDate = new GameDate(2026, 6, 1),
                EndDate = new GameDate(2027, 5, 31), RegularShowFrequency = RegularShowFrequency.Weekly,
                PpvFrequency = PpvFrequency.Quarterly, RegularVenueContractId = venue.Id,
                SignaturePpvScheduleId = signature.Id
            };
            save.Promotion.SeasonPolicyId = save.SeasonPolicy.Id;

            Assert.That(GameSaveValidator.Validate(save), Is.Empty);
            var restored = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            Assert.That(restored.SeasonPolicy.RegularVenueContractId, Is.EqualTo(venue.Id));
        }

        private static GameSave CreateValidSave()
        {
            var save = GameSave.CreateNew(123, new DateTime(2026, 8, 18, 4, 0, 0, DateTimeKind.Utc));
            save.Promotion = new PromotionState
            {
                Id = GameEntityId.CreateRuntimeId(),
                Name = "PW Wrestling",
                InitialCash = 10000,
                PromotionPrestige = 0
            };

            var wrestler = new WrestlerState
            {
                Id = GameEntityId.CreateRuntimeId(),
                PromotionId = save.Promotion.Id
            };
            wrestler.Identity.LegalName = "Test Person";
            wrestler.Identity.RingName = "Test Wrestler";
            wrestler.Identity.Gender = WrestlerGender.Male;
            wrestler.Identity.Background = WrestlerBackground.Rookie;
            wrestler.Identity.CreatedDate = save.CurrentDate;
            wrestler.Identity.HeightCm = 183;
            wrestler.Identity.WeightKg = 90;
            wrestler.Identity.BodyType = WrestlerBodyType.Balanced;
            wrestler.Presentation.WrestlingStyleId = "style_001";
            wrestler.Presentation.TraitIds.Add("trait_001");
            wrestler.Presentation.SignatureMoveIds.Add("move_001");
            wrestler.Presentation.FinisherMoveIds.Add("move_002");
            SetAllAttributes(wrestler.Attributes, 10);
            wrestler.Growth.MatchPotentialCap = 12;
            wrestler.Growth.PromoPotentialCap = 12;
            wrestler.Condition.Satisfaction = 50;
            wrestler.Status.DebutDate = new GameDate(2025, 1, 1);
            save.Wrestlers.Add(wrestler);

            var contract = new ContractState
            {
                Id = GameEntityId.CreateRuntimeId(),
                PersonId = wrestler.Id,
                Type = ContractType.Wrestler,
                StartDate = new GameDate(2026, 6, 1),
                EndDate = new GameDate(2027, 5, 31),
                SigningBonus = 1000,
                MonthlySalary = 500,
                Status = ContractStatus.Active
            };
            save.Contracts.Add(contract);
            save.Transactions.Add(new TransactionRecord
            {
                Id = GameEntityId.CreateRuntimeId(),
                PromotionId = save.Promotion.Id,
                Date = save.CurrentDate,
                Type = TransactionType.SigningFee,
                Amount = -1000,
                ReasonId = contract.Id
            });
            return save;
        }

        private static void SetAllAttributes(WrestlerAttributesState attributes, float value)
        {
            attributes.RingPsychology = value;
            attributes.RingImprovisation = value;
            attributes.Technical = value;
            attributes.Brawling = value;
            attributes.Power = value;
            attributes.HighFlying = value;
            attributes.SpotWork = value;
            attributes.SpecialtyMatches = value;
            attributes.Selling = value;
            attributes.Stamina = value;
            attributes.Charisma = value;
            attributes.MicWork = value;
            attributes.Improvisation = value;
            attributes.Acting = value;
            attributes.FaceWork = value;
            attributes.HeelWork = value;
            attributes.Comedy = value;
        }
    }
}
