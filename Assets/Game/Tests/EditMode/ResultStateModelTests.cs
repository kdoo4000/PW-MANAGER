using System;
using NUnit.Framework;
using PWManager.Domain.Models;
using UnityEngine;
using GameEntityId = PWManager.Domain.Identifiers.EntityId;

namespace PWManager.Tests
{
    public sealed class ResultStateModelTests
    {
        [Test]
        public void JsonRoundTrip_PreservesResultRootsAndBreakdowns()
        {
            var save = GameSave.CreateNew(7, new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc));
            var showId = GameEntityId.CreateRuntimeId();
            var matchResultId = GameEntityId.CreateRuntimeId();
            var promoResultId = GameEntityId.CreateRuntimeId();
            save.TagTeams.Add(new TagTeamState
            {
                Id = GameEntityId.CreateRuntimeId(), PromotionId = "promotion", Chemistry = 70f,
                MemberIds = { "wrestler_a", "wrestler_b" }
            });

            save.MatchResults.Add(new MatchResultState
            {
                Id = matchResultId,
                ShowId = showId,
                ShowVersion = 2,
                PlannedDuration = 20,
                ActualMatchDuration = 17,
                FinalMatchQuality = 14.5f,
                TechnicalEvaluation = new TechnicalEvaluationBreakdownState { Performance = 8f, Structure = 3f },
                ParticipantProfiles =
                {
                    new MatchParticipantProfileState
                    {
                        SideId = "side-a", MemberIds = { "wrestler_a", "wrestler_b" }, AppliedChemistry = 70f
                    }
                },
                FanReaction = new FanReactionResultState { Mania = 70f, Light = 60f, Family = 50f },
                ResultSeed = 123
            });
            save.PromoResults.Add(new PromoResultState
            {
                Id = promoResultId,
                ShowId = showId,
                ShowVersion = 2,
                PromoScore = 81f,
                ResultSeed = 456
            });
            save.ShowResults.Add(new ShowResultState
            {
                Id = GameEntityId.CreateRuntimeId(),
                ShowId = showId,
                ShowVersion = 2,
                MatchResultIds = { matchResultId },
                PromoResultIds = { promoResultId }
            });

            var restored = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));

            Assert.That(restored.ShowResults[0].MatchResultIds, Is.EqualTo(new[] { matchResultId }));
            Assert.That(restored.MatchResults[0].ActualMatchDuration, Is.EqualTo(17));
            Assert.That(restored.MatchResults[0].TechnicalEvaluation.Performance, Is.EqualTo(8f));
            Assert.That(restored.MatchResults[0].ParticipantProfiles[0].AppliedChemistry, Is.EqualTo(70f));
            Assert.That(restored.TagTeams[0].MemberIds, Is.EqualTo(new[] { "wrestler_a", "wrestler_b" }));
            Assert.That(restored.PromoResults[0].PromoScore, Is.EqualTo(81f));
        }
    }
}
