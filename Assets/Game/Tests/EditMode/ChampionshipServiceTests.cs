using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;

namespace PWManager.Tests
{
    public sealed class ChampionshipServiceTests
    {
        [Test]
        public void Create_SupportsVacantIndividualWorldAndAssignedTagTitles()
        {
            var save = Save();
            var vacant = ChampionshipService.Create(save, " 개인 ", ChampionshipType.Individual);
            var world = ChampionshipService.Create(save, "월드", ChampionshipType.Individual, true, "a");
            var tag = ChampionshipService.Create(save, "태그", ChampionshipType.TagTeam, holderId: "team-ab");
            Assert.That(vacant.Name, Is.EqualTo("WWE 개인 Championship"));
            Assert.That(vacant.HolderId, Is.Null);
            Assert.That(world.IsWorldChampionship, Is.True);
            Assert.That(tag.Reigns[0].MemberIds, Is.EquivalentTo(new[] { "a", "b" }));
            Assert.That(ChampionshipService.ReignCount(save, "a"), Is.EqualTo(2));
            Assert.That(ChampionshipService.ReignCount(save, "a", true), Is.EqualTo(1));
            ChampionshipService.ValidateState(save);
        }

        [Test]
        public void Create_FormatsPromotionNameAndAllowsSameInputForIndividualAndTagTeam()
        {
            var save = Save();
            save.Promotion.Name = " WWE ";
            save.Promotion.Abbreviation = "OTHER";
            var individual = ChampionshipService.Create(save, " World ", ChampionshipType.Individual, true);
            var tag = ChampionshipService.Create(save, " World ", ChampionshipType.TagTeam);
            Assert.That(individual.Name, Is.EqualTo("WWE World Championship"));
            Assert.That(tag.Name, Is.EqualTo("WWE World Tag Team Championship"));
            Assert.Throws<InvalidOperationException>(() => ChampionshipService.Create(save, "world", ChampionshipType.Individual));
            Assert.Throws<InvalidOperationException>(() => ChampionshipService.Create(save, "world", ChampionshipType.TagTeam));
        }

        [Test]
        public void Create_InvalidTypeHolderAndDuplicateName_DoNotAddTitles()
        {
            var save = Save();
            Assert.Throws<InvalidOperationException>(() => ChampionshipService.Create(save, "", ChampionshipType.Individual));
            Assert.Throws<InvalidOperationException>(() => ChampionshipService.Create(save, "월드 태그", ChampionshipType.TagTeam, true));
            Assert.Throws<InvalidOperationException>(() => ChampionshipService.Create(save, "타이틀", ChampionshipType.Individual, holderId: "missing"));
            Assert.That(save.Championships, Is.Empty);
            ChampionshipService.Create(save, "타이틀", ChampionshipType.Individual);
            Assert.Throws<InvalidOperationException>(() => ChampionshipService.Create(save, " 타이틀 ", ChampionshipType.Individual));
            Assert.That(save.Championships, Has.Count.EqualTo(1));
        }

        [Test]
        public void VacateAndReassign_ClosesHistoryAndCountsNewReign()
        {
            var save = Save();
            var title = ChampionshipService.Create(save, "월드", ChampionshipType.Individual, true, "a");
            ChampionshipService.SetHolder(save, title, "a");
            Assert.That(title.Reigns, Has.Count.EqualTo(1));
            save.CurrentDate = save.CurrentDate.AddDays(7);
            ChampionshipService.SetHolder(save, title, null);
            Assert.That(title.HolderId, Is.Null);
            Assert.That(title.Reigns[0].LostDate.Value, Is.EqualTo(save.CurrentDate));
            ChampionshipService.SetHolder(save, title, "a");
            Assert.That(ChampionshipService.ReignCount(save, "a", true), Is.EqualTo(2));
            ChampionshipService.ValidateState(save);
        }

        [TestCase(MatchFinishType.Draw)]
        [TestCase(MatchFinishType.Disqualification)]
        [TestCase(MatchFinishType.CountOut)]
        public void ProtectedFinish_KeepsChampionAndDoesNotAwardVacancy(MatchFinishType finish)
        {
            foreach (var champion in new[] { "a", null })
            {
                var save = Save();
                var title = ChampionshipService.Create(save, "개인", ChampionshipType.Individual, holderId: champion);
                var result = Match(save, title, false);
                result.FinishType = finish;
                var updated = ChampionshipService.PrepareResults(save, new[] { result }, save.CurrentDate).Single();
                Assert.That(updated.HolderId, Is.EqualTo(champion));
                Assert.That(updated.Reigns.Count, Is.EqualTo(title.Reigns.Count));
            }
        }

        [Test]
        public void Results_StageVacancyChangeAndDefenseWithoutMutatingOriginal()
        {
            var save = Save();
            var title = ChampionshipService.Create(save, "월드", ChampionshipType.Individual, true);
            var result = Match(save, title, false);
            var updated = ChampionshipService.PrepareResults(save, new[] { result }, save.CurrentDate.AddDays(7));
            Assert.That(title.HolderId, Is.Null);
            Assert.That(updated[0].HolderId, Is.EqualTo("b"));
            Assert.That(updated[0].Reigns[0].SourceResultId, Is.EqualTo(result.Id));
            save.Championships = updated;
            result.Id = Guid.NewGuid().ToString();
            updated = ChampionshipService.PrepareResults(save, new[] { result }, save.CurrentDate.AddDays(14));
            Assert.That(updated[0].Reigns, Has.Count.EqualTo(1));
            Assert.That(updated[0].Reigns[0].Defenses, Is.EqualTo(1));
            Assert.That(save.Championships[0].Reigns[0].Defenses, Is.Zero);
            result.WinningSideId = "side-a";
            result.Id = Guid.NewGuid().ToString();
            updated = ChampionshipService.PrepareResults(save, new[] { result }, save.CurrentDate.AddDays(21));
            Assert.That(updated[0].HolderId, Is.EqualTo("a"));
            Assert.That(updated[0].Reigns[0].LostDate.HasValue, Is.True);
        }

        [Test]
        public void TagResult_AwardsWinningRegisteredTeamAndMemberHistory()
        {
            var save = Save();
            var title = ChampionshipService.Create(save, "태그", ChampionshipType.TagTeam, holderId: "team-ab");
            var result = Match(save, title, true);
            save.Championships = ChampionshipService.PrepareResults(save, new[] { result }, save.CurrentDate);
            Assert.That(save.Championships[0].HolderId, Is.EqualTo("team-cd"));
            Assert.That(ChampionshipService.ReignCount(save, "c"), Is.EqualTo(1));
            Assert.That(ChampionshipService.ReignCount(save, "d"), Is.EqualTo(1));
            Assert.That(ChampionshipService.ReignCount(save, "c", true), Is.Zero);
            ChampionshipService.ValidateState(save);
        }

        [Test]
        public void Match_RejectsMissingChampionAndMismatchedTeam()
        {
            var save = Save();
            var title = ChampionshipService.Create(save, "개인", ChampionshipType.Individual, holderId: "c");
            Match(save, title, false);
            Assert.Throws<InvalidOperationException>(() => ChampionshipService.ValidateMatch(save, save.MatchPlans[0]));
            var tag = ChampionshipService.Create(save, "태그", ChampionshipType.TagTeam);
            Match(save, tag, true);
            save.MatchPlans.Last().Sides[0].RepresentedTagTeamId = "team-cd";
            Assert.Throws<InvalidOperationException>(() => ChampionshipService.ValidateMatch(save, save.MatchPlans.Last()));
        }

        [Test]
        public void TagMatch_ResolvesRegisteredTeamFromMembersLikeMatchEvaluator()
        {
            var save = Save();
            var title = ChampionshipService.Create(save, "태그", ChampionshipType.TagTeam, holderId: "team-ab");
            var result = Match(save, title, true);
            foreach (var side in save.MatchPlans[0].Sides) side.RepresentedTagTeamId = null;
            foreach (var side in result.ParticipantProfiles) side.RepresentedTagTeamId = null;
            ChampionshipService.ValidateMatch(save, save.MatchPlans[0]);
            Assert.That(ChampionshipService.PrepareResults(save, new[] { result }, save.CurrentDate)[0].HolderId, Is.EqualTo("team-cd"));
        }

        [Test]
        public void InterruptedOrCancelledResult_DoesNotChangeChampion()
        {
            var save = Save();
            var title = ChampionshipService.Create(save, "개인", ChampionshipType.Individual, holderId: "a");
            var result = Match(save, title, false);
            result.WasStoppedByInjury = true;
            Assert.That(ChampionshipService.PrepareResults(save, new[] { result }, save.CurrentDate)[0].HolderId, Is.EqualTo("a"));
            result.WasStoppedByInjury = false; result.WasStoppedBySpot = true;
            Assert.That(ChampionshipService.PrepareResults(save, new[] { result }, save.CurrentDate)[0].HolderId, Is.EqualTo("a"));
            result.WasStoppedBySpot = false; result.CancellationReason = "취소";
            Assert.That(ChampionshipService.PrepareResults(save, new[] { result }, save.CurrentDate)[0].HolderId, Is.EqualTo("a"));
        }

        [Test]
        public void Serialization_PreservesTypesHolderHistoryAndOldSaveDefaults()
        {
            var save = Save();
            ChampionshipService.Create(save, "월드", ChampionshipType.Individual, true, "a");
            ChampionshipService.Create(save, "태그", ChampionshipType.TagTeam, holderId: "team-ab");
            var restored = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            ChampionshipService.ValidateState(restored);
            Assert.That(restored.Championships[0].IsWorldChampionship, Is.True);
            Assert.That(restored.Championships[1].HolderId, Is.EqualTo("team-ab"));
            Assert.That(ChampionshipService.ReignCount(restored, "a"), Is.EqualTo(2));
            var old = JsonUtility.FromJson<GameSave>("{}");
            Assert.That(ChampionshipService.ReignCount(old, "a"), Is.Zero);
        }

        private static GameSave Save()
        {
            var save = new GameSave { Promotion = new PromotionState { Id = "promotion", Name = "WWE" }, CurrentDate = new GameDate(2026, 6, 1) };
            foreach (var id in new[] { "a", "b", "c", "d" })
                save.Wrestlers.Add(new WrestlerState { Id = id, PromotionId = "promotion" });
            save.TagTeams.Add(new TagTeamState { Id = "team-ab", PromotionId = "promotion", Name = "AB", MemberIds = new() { "a", "b" } });
            save.TagTeams.Add(new TagTeamState { Id = "team-cd", PromotionId = "promotion", Name = "CD", MemberIds = new() { "c", "d" } });
            return save;
        }

        [Test]
        public void Prestige_StartsHigherForWorldAndUpgradesOldTitlesWithoutResettingZero()
        {
            var save = Save();
            var world = ChampionshipService.Create(save, "World", ChampionshipType.Individual, true, "a");
            world.Reigns[0].Defenses = 4;
            var tag = ChampionshipService.Create(save, "Tag", ChampionshipType.TagTeam);
            Assert.That(world.Prestige, Is.EqualTo(60));
            Assert.That(tag.Prestige, Is.EqualTo(30));
            world.HasPrestige = false; world.Prestige = 0; world.MatchHistory = null;
            tag.Prestige = 0;
            ChampionshipService.Upgrade(save);
            Assert.That(world.Prestige, Is.EqualTo(60));
            Assert.That(world.MatchHistory, Is.Empty);
            Assert.That(world.Reigns[0].Defenses, Is.EqualTo(4));
            Assert.That(tag.Prestige, Is.Zero);
            ChampionshipService.Upgrade(save);
            Assert.That(tag.Prestige, Is.Zero);
        }

        [Test]
        public void DefenseHistory_SnapshotsParticipantsOutcomeAndPrestigeWithoutMutatingSave()
        {
            var save = Save(); Standing(save, 80);
            var title = ChampionshipService.Create(save, "World", ChampionshipType.Individual, holderId: "a");
            var result = Match(save, title, false); result.WinningSideId = "side-a";
            result.ShowId = "show"; result.FinalMatchQuality = 12;
            result.FanReaction = new FanReactionResultState { IsCalculated = true, Family = 70, Light = 70, Mania = 70 };
            var updated = ChampionshipService.PrepareResults(save, new[] { result }, save.CurrentDate.AddDays(1))[0];
            var history = updated.MatchHistory.Single();
            Assert.That(title.MatchHistory, Is.Empty);
            Assert.That(title.Prestige, Is.EqualTo(30));
            Assert.That(history.ChallengerIds, Is.EqualTo(new[] { "b" }));
            Assert.That(history.WinnerMemberIds, Is.EqualTo(new[] { "a" }));
            Assert.That(history.SuccessfulDefense, Is.True);
            Assert.That(history.SourceResultId, Is.EqualTo(result.Id));
            Assert.That(history.ShowId, Is.EqualTo("show"));
            Assert.That(history.Date, Is.EqualTo(save.CurrentDate.AddDays(1)));
            Assert.That(history.PrestigeAfter, Is.EqualTo(33.9f).Within(.001));
            save.Championships[0] = updated;
            var replay = ChampionshipService.PrepareResults(save, new[] { result }, save.CurrentDate)[0];
            Assert.That(replay.MatchHistory, Has.Count.EqualTo(1));
            Assert.That(replay.Prestige, Is.EqualTo(updated.Prestige));
            Assert.That(replay.Reigns[0].Defenses, Is.EqualTo(1));
            var restored = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            ChampionshipService.ValidateState(restored);
            Assert.That(restored.Championships[0].MatchHistory[0].ChallengerIds, Is.EqualTo(new[] { "b" }));
        }

        [TestCase(MatchFinishType.Disqualification)]
        [TestCase(MatchFinishType.CountOut)]
        [TestCase(MatchFinishType.Draw)]
        public void InconclusiveDefense_IsRecordedWithoutChangingChampion(MatchFinishType finish)
        {
            var save = Save();
            var title = ChampionshipService.Create(save, "World", ChampionshipType.Individual, true, "a");
            var result = Match(save, title, false); result.FinishType = finish;
            var updated = ChampionshipService.PrepareResults(save, new[] { result }, save.CurrentDate)[0];
            Assert.That(updated.HolderId, Is.EqualTo("a"));
            Assert.That(updated.MatchHistory[0].FinishType, Is.EqualTo(finish));
            Assert.That(updated.MatchHistory[0].SuccessfulDefense, Is.False);
            Assert.That(updated.MatchHistory[0].ChampionBeforeId, Is.EqualTo("a"));
            Assert.That(updated.MatchHistory[0].ChampionAfterId, Is.EqualTo("a"));
            if (finish == MatchFinishType.Draw) Assert.That(updated.MatchHistory[0].WinnerMemberIds, Is.Empty);
        }

        [Test]
        public void TitleChangeAndInterruption_RecordActualOutcomeAndCancellationAddsNothing()
        {
            var save = Save();
            var title = ChampionshipService.Create(save, "World", ChampionshipType.Individual, holderId: "a");
            var result = Match(save, title, false);
            var updated = ChampionshipService.PrepareResults(save, new[] { result }, save.CurrentDate)[0];
            Assert.That(updated.MatchHistory[0].ChampionBeforeId, Is.EqualTo("a"));
            Assert.That(updated.MatchHistory[0].ChampionAfterId, Is.EqualTo("b"));
            Assert.That(updated.MatchHistory[0].SuccessfulDefense, Is.False);
            result.WasStoppedByInjury = true;
            updated = ChampionshipService.PrepareResults(save, new[] { result }, save.CurrentDate)[0];
            Assert.That(updated.MatchHistory[0].WasStopped, Is.True);
            Assert.That(updated.HolderId, Is.EqualTo("a"));
            result.CancellationReason = "취소";
            Assert.That(ChampionshipService.PrepareResults(save, new[] { result }, save.CurrentDate)[0].MatchHistory, Is.Empty);
        }

        [Test]
        public void Prestige_RespondsIndependentlyToQualityFansChampionAndChallenger()
        {
            float Evaluate(float quality, float fans, float champion, float challenger)
            {
                var save = Save(); Standing(save, 50);
                Standing(save, champion, "a"); Standing(save, challenger, "b");
                var title = ChampionshipService.Create(save, "World", ChampionshipType.Individual, holderId: "a");
                var result = Match(save, title, false); result.WinningSideId = "side-a";
                result.FinalMatchQuality = quality / 5;
                result.FanReaction = new FanReactionResultState { IsCalculated = true, Family = fans, Light = fans, Mania = fans };
                return ChampionshipService.PrepareResults(save, new[] { result }, save.CurrentDate)[0].Prestige;
            }
            var baseline = Evaluate(50, 50, 50, 50);
            Assert.That(Evaluate(80, 50, 50, 50), Is.GreaterThan(baseline));
            Assert.That(Evaluate(50, 80, 50, 50), Is.GreaterThan(baseline));
            Assert.That(Evaluate(50, 50, 80, 50), Is.GreaterThan(baseline));
            Assert.That(Evaluate(50, 50, 50, 80), Is.GreaterThan(baseline));
            Assert.That(Evaluate(0, 0, 0, 0), Is.LessThan(30));
            Assert.That(Evaluate(100, 100, 100, 100), Is.EqualTo(35));
        }

        [Test]
        public void Prestige_ManualChampionChangeUsesStandingAndInvalidResultCannotMutateSave()
        {
            var save = Save(); Standing(save, 0, "a"); Standing(save, 100, "b");
            var title = ChampionshipService.Create(save, "World", ChampionshipType.Individual, holderId: "a");
            ChampionshipService.SetHolder(save, title, "b");
            Assert.That(title.Prestige, Is.EqualTo(33));
            var result = Match(save, title, false); result.FinalMatchQuality = float.NaN;
            Assert.Throws<InvalidOperationException>(() => ChampionshipService.PrepareResults(save, new[] { result }, save.CurrentDate));
            Assert.That(title.Prestige, Is.EqualTo(33));
            Assert.That(title.MatchHistory, Is.Empty);
            title.Prestige = float.PositiveInfinity;
            Assert.Throws<InvalidOperationException>(() => ChampionshipService.ValidateState(save));
        }

        private static void Standing(GameSave save, float value, string id = null)
        {
            foreach (var wrestler in save.Wrestlers.Where(x => id == null || x.Id == id))
            {
                wrestler.Status.StatusValue = (int)value;
                wrestler.FanReaction = new WrestlerFanReactionState { UsesTwoAxes = true,
                    Mark = new FanResponseState { Interest = value }, Casual = new FanResponseState { Interest = value },
                    Hardcore = new FanResponseState { Interest = value } };
            }
        }

        private static MatchResultState Match(GameSave save, ChampionshipState title, bool tag)
        {
            var plan = new MatchPlanState { Id = Guid.NewGuid().ToString(), ChampionshipId = title.Id };
            plan.Sides.Add(new MatchSideState { Id = "side-a", MemberIds = tag ? new() { "a", "b" } : new() { "a" }, RepresentedTagTeamId = tag ? "team-ab" : null });
            plan.Sides.Add(new MatchSideState { Id = "side-b", MemberIds = tag ? new() { "c", "d" } : new() { "b" }, RepresentedTagTeamId = tag ? "team-cd" : null });
            save.MatchPlans.Add(plan);
            return new MatchResultState { Id = Guid.NewGuid().ToString(), MatchPlanId = plan.Id,
                WinningSideId = "side-b", FinishType = MatchFinishType.Pinfall,
                ParticipantProfiles = plan.Sides.Select(x => new MatchParticipantProfileState
                    { SideId = x.Id, MemberIds = new(x.MemberIds), RepresentedTagTeamId = x.RepresentedTagTeamId }).ToList() };
        }
    }
}
