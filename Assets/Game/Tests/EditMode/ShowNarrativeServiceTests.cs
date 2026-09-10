using System.Collections.Generic;
using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Tests
{
    public sealed class ShowNarrativeServiceTests
    {
        [Test]
        public void EngineCommentary_UsesRecordedRecoveryDownCounterAndFinish()
        {
            var value = new MatchEngineEvent
            {
                PerformerId = "a", ReceiverId = "b", Type = MatchActionType.Rest, Result = MatchActionResult.FullSuccess,
                Before = new MatchEngineSnapshot { Participants = new List<MatchParticipantSnapshot>
                    { new() { Id = "a", Fatigue = 80, IsDowned = true }, new() { Id = "b" } } },
                After = new MatchEngineSnapshot { Participants = new List<MatchParticipantSnapshot>
                    { new() { Id = "a", Fatigue = 76 }, new() { Id = "b" } } }
            };
            var result = new MatchResultState { EngineState = new MatchEngineState { Events = { value } } };
            string Line() => MatchNarrationService.NarrateEngineEvent(result, 0, id => id == "a" ? "공격 선수" : "상대 선수");
            Assert.That(Line(), Does.Contain("몸을 일으킵니다"));
            value.Before.Participants[0].IsDowned = false;
            Assert.That(Line(), Does.Contain("숨을 고릅니다").And.Not.Contain("일으킵니다"));
            value.Type = MatchActionType.HeavyStrike;
            value.After.Participants[1].IsDowned = true;
            Assert.That(Line(), Does.Contain("쓰러집니다"));
            value.After.Participants[1].IsDowned = false;
            Assert.That(Line(), Does.Not.Contain("쓰러집니다"));
            value.Result = MatchActionResult.ExecutionFailure;
            Assert.That(Line(), Does.Contain("맞히지 못합니다").And.Not.Contain("반격"));
            value.Result = MatchActionResult.Countered;
            Assert.That(Line(), Does.StartWith("상대 선수").And.Contain("반격"));
            value.Type = MatchActionType.Pin;
            value.Result = MatchActionResult.NearFall;
            Assert.That(Line(), Does.Contain("셋 직전"));
            value.Result = MatchActionResult.Pinfall;
            Assert.That(Line(), Does.Contain("하나, 둘, 셋").And.Not.Contain("일찍"));
            value.Type = MatchActionType.Submission;
            value.Result = MatchActionResult.Submitted;
            Assert.That(Line(), Does.Contain("서브미션 승리").And.Not.Contain("빠져나옵니다"));
            value.Type = MatchActionType.Grapple;
            value.Result = MatchActionResult.FullSuccess;
            value.Before = value.After = null;
            Assert.That(Line(), Does.Contain("유리한 자세").And.Not.Contain("쓰러집니다"));
        }

        [Test]
        public void PromoContext_UsesCanonicalResultAndFallbackKeepsMandatoryOrder()
        {
            var save = new GameSave();
            save.Wrestlers.Add(new WrestlerState
            {
                Identity = new WrestlerIdentityState { Id = "heel", RingName = "The Heel" },
                Roster = new WrestlerRosterState { Alignment = KayfabeAlignment.Heel },
                Presentation = new WrestlerPresentationState { PromoArchetype = PromoArchetype.Mic },
                Attributes = new WrestlerAttributesState { MicWork = 18.2f, Charisma = 17.4f }
            });
            save.PromoPlans.Add(new PromoPlanState
            {
                Id = "promo", Purpose = PromoPurpose.Challenge, Presentation = PromoPresentation.InRingMic,
                ParticipantIds = { "heel" }, OpeningSpot = "챔피언을 부른다", ClosingSpot = "도전을 선언한다"
            });
            save.ShowEvents.Add(new ShowEventState { Id = "event", DetailId = "promo", EventType = ShowEventType.Promo, PlannedDuration = 8 });
            var result = new PromoResultState
            {
                Id = "result", PromoPlanId = "promo", ShowEventId = "event", PromoScore = 91f,
                EvaluationReasons = new List<EvaluationReasonState> { new() { Code = "promo.base-performance", Contribution = 91f } }
            };

            var context = PromoContextBuilder.Build(save, result);
            var prompt = PromoPromptBuilder.Build(context);
            var fallback = PromoFallbackNarration.Create(context);

            Assert.That(prompt, Does.Contain("MicWork 18.2"));
            Assert.That(prompt, Does.Contain("PromoScore: 91.0"));
            Assert.That(fallback.UsedFallback, Is.True);
            Assert.That(fallback.FulfilledBeatIds, Is.EqualTo(new[] { "opening", "closing" }));
            Assert.That(fallback.Lines[0].Text, Is.EqualTo("챔피언을 부른다"));
            Assert.That(fallback.Lines[1].Text, Is.EqualTo("도전을 선언한다"));
        }

        [Test]
        public void MoveNarration_All24MovesHaveDistinctOutcomeLinesAndKeepNames()
        {
            var all = new HashSet<string>();
            for (var i = 1; i <= 24; i++)
            {
                var lines = new HashSet<string>();
                foreach (SpotExecutionResult outcome in Enum.GetValues(typeof(SpotExecutionResult)))
                {
                    var move = new MoveResultState { MoveId = $"move_{i:000}", ActorId = "주체", TargetId = "상대", Result = outcome };
                    var line = MatchMoveService.Narrate(move, id => id);
                    Assert.That(line, Does.StartWith("주체의 "));
                    foreach (var forbidden in new[] { "받아주는", "호흡", "계획한", "준비한", "동선", "시그니처", "피니셔" })
                        Assert.That(line, Does.Not.Contain(forbidden));
                    Assert.That(line, Does.Not.Contain("move_"));
                    Assert.That(line, Does.Not.Contain("부상"));
                    Assert.That(line, Does.Not.Contain("승리"));
                    Assert.That(lines.Add(line), Is.True);
                    Assert.That(all.Add(line), Is.True);
                    move.IsFinisher = true;
                    Assert.That(MatchMoveService.Narrate(move, id => id), Is.EqualTo(line));
                }
            }
            Assert.That(all.Count, Is.EqualTo(96));
        }

        [Test]
        public void MoveEvaluation_UsesRegisteredMovesOpponentsAbilitiesAndStoredOutcomes()
        {
            var save = new GameSave();
            foreach (var id in new[] { "a", "partner", "b", "other" })
                save.Wrestlers.Add(new WrestlerState
                {
                    Id = id, Identity = new WrestlerIdentityState { Id = id, RingName = id },
                    Attributes = new WrestlerAttributesState { Power = 15, Selling = 15 },
                    Presentation = new WrestlerPresentationState { SignatureMoveIds = { "move_006" }, FinisherMoveIds = { "move_007" } }
                });
            var plan = new MatchPlanState { Sides = { new MatchSideState { MemberIds = { "a", "partner" } },
                new MatchSideState { MemberIds = { "b", "other" } } } };
            var result = new MatchResultState { ResultSeed = 73, WinnerId = "b", LoserTargetId = "a", ActualMatchDuration = 15 };
            MatchMoveRules Rules(string id) => new() { Name = id, PowerWeight = 1, ExecutionDifficulty = 12, SellingDifficulty = 10 };
            result.MoveResults = MatchMoveService.Evaluate(save, plan.Sides, result, Rules);
            Assert.That(result.MoveResults.Count, Is.EqualTo(8));
            var again = MatchMoveService.Evaluate(save, plan.Sides, result, Rules);
            Assert.That(again.Select(x => (x.MoveId, x.ActorId, x.TargetId, x.Result)),
                Is.EqualTo(result.MoveResults.Select(x => (x.MoveId, x.ActorId, x.TargetId, x.Result))));
            foreach (var move in result.MoveResults)
            {
                Assert.That(plan.Sides.Single(x => x.MemberIds.Contains(move.ActorId)).MemberIds, Does.Not.Contain(move.TargetId));
                Assert.That(move.MoveId, Is.EqualTo(move.IsFinisher ? "move_007" : "move_006"));
            }
            foreach (var wrestler in save.Wrestlers) { wrestler.Attributes.Power = 1; wrestler.Attributes.Selling = 1; }
            var weaker = MatchMoveService.Evaluate(save, plan.Sides, result, Rules);
            for (var i = 0; i < weaker.Count; i++)
            {
                Assert.That(weaker[i].ExecutionScore, Is.LessThan(result.MoveResults[i].ExecutionScore));
                Assert.That((int)weaker[i].Result, Is.LessThanOrEqualTo((int)result.MoveResults[i].Result));
            }
            var service = new MatchNarrationService();
            var beats = service.CreateBeats(plan, result, save);
            var moves = beats.Where(x => x.BeatType == MatchBeatType.Execution).ToList();
            Assert.That(moves.Count, Is.EqualTo(8));
            Assert.That(moves.Take(4).All(x => x.Phase == 2 && x.Detail.Contains("초크슬램")), Is.True);
            Assert.That(moves.Skip(4).All(x => x.Phase == 3 && x.Detail.Contains("스파인버스터")), Is.True);
            Assert.That(beats.Sum(x => x.DurationSeconds), Is.EqualTo(90).Within(.001));
            foreach (var move in result.MoveResults) move.Result = SpotExecutionResult.Disaster;
            Assert.That(service.CreateBeats(plan, result, save).Where(x => x.BeatType == MatchBeatType.Execution).All(x => !x.Succeeded), Is.True);
            Assert.That(result.WinnerId, Is.EqualTo("b"));
            Assert.That(result.ExecutionEvents, Is.Empty);
            foreach (var wrestler in save.Wrestlers) { wrestler.Presentation.SignatureMoveIds.Clear(); wrestler.Presentation.FinisherMoveIds.Clear(); }
            Assert.That(MatchMoveService.Evaluate(save, plan.Sides, result, Rules), Is.Empty);
            result.MoveResults = null;
            Assert.That(service.CreateBeats(plan, result, save).Any(x => x.BeatType == MatchBeatType.Execution), Is.False);
        }

        [Test]
        public void MatchNarration_SameSeed_IsDeterministicAndNeedsNoLlm()
        {
            var plan = new MatchPlanState { ParticipantIds = { "a", "b" }, OpeningSpot = "코너로 몰아붙입니다." };
            var result = new MatchResultState
            {
                WinnerId = "a", FinishPerformerId = "a", LoserTargetId = "b",
                FinishType = MatchFinishType.Pinfall, ResultSeed = 73
            };
            var service = new MatchNarrationService();

            var first = service.Narrate(plan, result, id => id.ToUpperInvariant());
            var second = service.Narrate(plan, result, id => id.ToUpperInvariant());

            Assert.That(second.ConvertAll(x => x.Text), Is.EqualTo(first.ConvertAll(x => x.Text)));
            Assert.That(first.Count, Is.EqualTo(service.CreateBeats(plan, result).Count));
            Assert.That(first[0].Text, Does.Contain(plan.OpeningSpot));
            Assert.That(first[^1].Text, Does.Contain("하나, 둘, 셋"));
        }

        [Test]
        public void MatchNarration_FillsLongerMatchesWithMoreAutomaticExchanges()
        {
            var plan = new MatchPlanState { ParticipantIds = { "a", "b" }, MiddleSpot = "지정한 순간을 수행합니다." };
            var service = new MatchNarrationService();
            var shortMatch = new MatchResultState { ResultSeed = 7, ActualMatchDuration = 8, WinnerId = "a", LoserTargetId = "b" };
            var longMatch = new MatchResultState { ResultSeed = 7, ActualMatchDuration = 28, WinnerId = "a", LoserTargetId = "b" };

            var shortBeats = service.CreateBeats(plan, shortMatch);
            var longBeats = service.CreateBeats(plan, longMatch);
            int Exchanges(IEnumerable<MatchSimulationBeatState> beats) => beats.Count(x =>
                x.BeatType == MatchBeatType.Control || x.BeatType == MatchBeatType.Counter);

            Assert.That(Exchanges(longBeats), Is.GreaterThan(Exchanges(shortBeats)));
            Assert.That(longBeats.Single(x => x.Detail == plan.MiddleSpot).BeatType, Is.EqualTo(MatchBeatType.PlannedSpot));
            Assert.That(service.CreateBeats(plan, longMatch).Select(x => (x.BeatType, x.ActorId, x.TargetId)),
                Is.EqualTo(longBeats.Select(x => (x.BeatType, x.ActorId, x.TargetId))));
        }

        [Test]
        public void MatchNarration_StylesAndRecordedRelationshipsChangeCommentary()
        {
            var save = new GameSave();
            save.Wrestlers.Add(new WrestlerState { Identity = new WrestlerIdentityState { Id = "a" }, Presentation = new WrestlerPresentationState { WrestlingStyleId = "style_003" } });
            save.Wrestlers.Add(new WrestlerState { Identity = new WrestlerIdentityState { Id = "b" }, Presentation = new WrestlerPresentationState { WrestlingStyleId = "style_001" } });
            var plan = new MatchPlanState { ParticipantIds = { "a", "b" } };
            var result = new MatchResultState { WinnerId = "b", LoserTargetId = "a", FinishType = MatchFinishType.Submission };
            var service = new MatchNarrationService();
            var plain = service.Narrate(plan, result, id => id, save);
            Assert.That(plain.Any(x => x.Text.Contains("매트") || x.Text.Contains("관절")), Is.True);
            Assert.That(plain.Any(x => x.Text.Contains("타격")), Is.True);
            Assert.That(plain.Any(x => x.Text.Contains("같은 태그팀")), Is.False);
            save.TagTeams.Add(new TagTeamState { Status = TagTeamStatus.Disbanded, MemberIds = { "a", "b" } });
            var related = service.Narrate(plan, result, id => id, save);
            Assert.That(related.Any(x => x.Text.Contains("한때 같은 태그팀")), Is.True);
            Assert.That(related[^1].Text, Does.Contain("b, 서브미션 승리"));
        }

        [Test]
        public void MatchNarration_AllStylesHaveDistinctVariedControlAndCounter()
        {
            var service = new MatchNarrationService();
            var plan = new MatchPlanState { ParticipantIds = { "a", "b" } };
            var allStyleLines = new HashSet<string>();
            for (var style = 1; style <= 7; style++)
            {
                var save = new GameSave();
                foreach (var id in new[] { "a", "b" })
                    save.Wrestlers.Add(new WrestlerState { Identity = new WrestlerIdentityState { Id = id },
                        Presentation = new WrestlerPresentationState { WrestlingStyleId = $"style_{style:000}" } });
                foreach (var type in new[] { MatchBeatType.Control, MatchBeatType.Counter })
                {
                    var variants = new HashSet<string>();
                    for (var seed = 0; seed < 100; seed++)
                    {
                        var result = new MatchResultState { ResultSeed = seed };
                        var beats = service.CreateBeats(plan, result, save);
                        var lines = service.Narrate(plan, result, _ => "선수", save);
                        variants.Add(lines[beats.FindIndex(x => x.BeatType == type)].Text);
                    }
                    Assert.That(variants.Count, Is.EqualTo(3), $"style {style} / {type}");
                    foreach (var line in variants) Assert.That(allStyleLines.Add(line), Is.True, line);
                }
            }
        }

        [Test]
        public void MatchBeats_TeamsAndMultiWayUseEveryParticipantWithoutFriendlyAttacks()
        {
            var service = new MatchNarrationService();
            foreach (var members in new[] { 1, 2 })
                foreach (var teams in new[] { 2, 3, 4 })
                {
                    var plan = new MatchPlanState { TeamCount = teams, MembersPerTeam = members };
                    for (var side = 0; side < teams; side++)
                        plan.Sides.Add(new MatchSideState { MemberIds = Enumerable.Range(0, members).Select(i => $"{side}-{i}").ToList() });
                    var result = new MatchResultState { WinnerId = "0-0", LoserTargetId = "1-0", ActualMatchDuration = 15 };
                    var beats = service.CreateBeats(plan, result);
                    var controls = beats.Where(x => x.BeatType == MatchBeatType.Control).ToList();
                    Assert.That(controls.Select(x => x.ActorId).Distinct(), Is.EquivalentTo(plan.Sides.SelectMany(x => x.MemberIds)));
                    foreach (var beat in controls)
                        Assert.That(plan.Sides.Single(x => x.MemberIds.Contains(beat.ActorId)).MemberIds, Does.Not.Contain(beat.TargetId));
                    Assert.That(beats.Sum(x => x.DurationSeconds), Is.EqualTo(90f).Within(.001f));
                    Assert.That(service.Narrate(plan, result, id => id).Select(x => x.Text),
                        Is.EqualTo(service.Narrate(plan, result, id => id).Select(x => x.Text)));
                    plan.ParticipantIds = plan.Sides.SelectMany(x => x.MemberIds).ToList();
                    plan.Sides.Clear();
                    Assert.That(service.CreateBeats(plan, result).Select(x => x.Detail), Is.EqualTo(beats.Select(x => x.Detail)));
                    result.FinishType = MatchFinishType.Draw;
                    Assert.That(service.CreateBeats(plan, result).Last().BeatType, Is.EqualTo(MatchBeatType.Finish));
                }
        }

        [Test]
        public void MatchBeats_RespectOpposingSidesSpotsAndCanonicalIncidents()
        {
            var plan = new MatchPlanState
            {
                Sides = { new MatchSideState { MemberIds = { "a", "partner" } }, new MatchSideState { MemberIds = { "b", "other" } } },
                OpeningSpot = "먼저 악수를 거절한다.", MiddleSpot = "서로 떨어진다.", ClosingSpot = "다시 마주 선다."
            };
            var result = new MatchResultState { WinnerId = "b", LoserTargetId = "a", FinishType = MatchFinishType.Pinfall };
            result.ExecutionEvents.Add(new MatchExecutionEventState { EventType = MatchExecutionEventType.Mistake, ParticipantIds = { "a" } });
            var service = new MatchNarrationService();
            var beats = service.CreateBeats(plan, result);
            Assert.That(beats[0].TargetId, Is.EqualTo("b"));
            Assert.That(beats.Where(x => x.BeatType == MatchBeatType.PlannedSpot &&
                new[] { plan.OpeningSpot, plan.MiddleSpot, plan.ClosingSpot }.Contains(x.Detail)).Select(x => x.Detail),
                Is.EqualTo(new[] { plan.OpeningSpot, plan.MiddleSpot, plan.ClosingSpot }));
            Assert.That(beats[0].Detail, Is.EqualTo(plan.OpeningSpot));
            Assert.That(beats[^1].Detail, Is.EqualTo(plan.ClosingSpot));
            Assert.That(beats.ToList().FindIndex(x => x.BeatType == MatchBeatType.Finish), Is.LessThan(beats.Count - 1));
            Assert.That(beats.Count(x => x.BeatType == MatchBeatType.Execution), Is.EqualTo(1));
            Assert.That(beats[^1].ActorId, Is.EqualTo("b"));
            Assert.That(service.Narrate(plan, result, id => id).Any(x => x.Text.Contains("부상")), Is.False);
            Assert.That(result.WinnerId, Is.EqualTo("b"));
            Assert.That(result.ExecutionEvents, Has.Count.EqualTo(1));
        }

        [Test]
        public void MatchBeats_HighlightsTakeLongerAndWholeMatchFitsBudget()
        {
            var plan = new MatchPlanState { ParticipantIds = { "a", "b" } };
            var result = new MatchResultState { ShowId = "show", ShowEventId = "event", ActualMatchDuration = 15 };
            var service = new MatchNarrationService();
            var beats = service.CreateBeats(plan, result);
            Assert.That(beats.Sum(x => x.DurationSeconds), Is.EqualTo(90f).Within(.001f));
            Assert.That(beats[^1].DurationSeconds, Is.GreaterThan(beats.First(x => x.BeatType == MatchBeatType.Control).DurationSeconds));
            Assert.That(beats.Select(x => x.MatchProgress), Is.Ordered);
            Assert.That(beats[^1].MatchProgress, Is.EqualTo(1f));
            var save = new GameSave();
            save.Shows.Add(new ShowState { Id = "show", MainEventId = "event" });
            Assert.That(service.CreateBeats(plan, result, save).Sum(x => x.DurationSeconds), Is.EqualTo(150f).Within(.001f));
        }

        [Test]
        public void MatchNarration_AllFinishTypesAreKoreanAndDrawNeedsNoWinner()
        {
            var plan = new MatchPlanState { ParticipantIds = { "a", "b" } };
            foreach (MatchFinishType finish in Enum.GetValues(typeof(MatchFinishType)))
            {
                var result = new MatchResultState { FinishType = finish, WinnerId = finish == MatchFinishType.Draw ? null : "a", LoserTargetId = "b" };
                var text = new MatchNarrationService().Narrate(plan, result, id => id)[^1].Text;
                Assert.That(text, Does.Not.Contain(finish.ToString()));
                if (finish == MatchFinishType.Draw) Assert.That(text, Does.Contain("무승부"));
                else Assert.That(text, Does.Contain("a"));
            }
        }

        [Test]
        public void PromoFallback_ProductionOnlyNeedsNoSpeaker()
        {
            var script = PromoFallbackNarration.Create(new PromoGenerationContext
            {
                Plan = new PromoPlanState { Presentation = PromoPresentation.VideoPackage }
            });
            Assert.That(script.Lines, Has.Count.EqualTo(1));
            Assert.That(script.Lines[0].Type, Is.EqualTo(NarrativeLineType.Action));
        }

        [Test]
        public void SpecialContext_UsesStoredTraitsLimitsOpeningAndRevealsUpsetOnlyAfterFinish()
        {
            var save = new GameSave();
            var first = new WrestlerState { Identity = new WrestlerIdentityState { Id = "a", RingName = "신예", CareerYears = 1, WeightKg = 80 } };
            var second = new WrestlerState { Identity = new WrestlerIdentityState { Id = "b", RingName = "베테랑", CareerYears = 10, WeightKg = 110 } };
            first.Presentation.WrestlingStyleId = "style_003";
            second.Presentation.WrestlingStyleId = "style_004";
            first.Condition.Condition = 40;
            second.Condition.Condition = 100;
            first.Status.StatusValue = 10;
            second.Status.StatusValue = 60;
            save.Wrestlers.AddRange(new[] { first, second });
            var plan = new MatchPlanState { ParticipantIds = { "a", "b" } };
            var result = new MatchResultState { Id = "current", ShowId = "today", WinnerId = "a", LoserTargetId = "b", FinishType = MatchFinishType.Pinfall, ActualMatchDuration = 15 };
            var service = new MatchNarrationService();
            var beats = service.CreateBeats(plan, result, save);
            Assert.That(beats.Count(x => x.BeatType == MatchBeatType.Context && x.Phase == 0), Is.EqualTo(2));
            Assert.That(beats.Any(x => x.Detail?.Contains("경력 1년") == true), Is.True);
            Assert.That(beats.Any(x => x.Detail?.Contains("체중 차이") == true), Is.True);
            Assert.That(beats.Any(x => x.Detail?.Contains("컨디션이 좋지") == true), Is.True);
            Assert.That(beats.FindIndex(x => x.Detail?.Contains("위상의 차이를 뒤집") == true), Is.GreaterThan(beats.FindIndex(x => x.BeatType == MatchBeatType.Finish)));
            Assert.That(beats.Sum(x => x.DurationSeconds), Is.EqualTo(90f).Within(.001f));
            second.Identity.CareerYears = 2;
            second.Identity.WeightKg = 90;
            first.Condition.Condition = 100;
            beats = service.CreateBeats(plan, result, save);
            Assert.That(beats.Any(x => x.Detail?.Contains("테크니션과 공중전") == true), Is.True);
            Assert.That(beats.Any(x => x.Detail?.Contains("컨디션이 좋지") == true), Is.False);
            first.Presentation.WrestlingStyleId = "style_002";
            Assert.That(service.CreateBeats(plan, result, save).Any(x => x.Detail?.Contains("힘과 공중 기술") == true), Is.True);
            result.FinishType = MatchFinishType.Draw;
            Assert.That(service.CreateBeats(plan, result, save).Any(x => x.Detail?.Contains("위상의 차이를 뒤집") == true), Is.False);
            plan.ParticipantIds.Add("partner");
            result.FinishType = MatchFinishType.Pinfall;
            Assert.That(service.CreateBeats(plan, result, save).Any(x => x.Detail?.Contains("위상의 차이를 뒤집") == true), Is.False);
        }

        [Test]
        public void SpecialContext_StreakAndRevengeExcludeCurrentFutureAndSameShowResults()
        {
            var save = new GameSave();
            save.Wrestlers.Add(new WrestlerState { Identity = new WrestlerIdentityState { Id = "a", RingName = "승자" } });
            save.Wrestlers.Add(new WrestlerState { Identity = new WrestlerIdentityState { Id = "b", RingName = "도전자" } });
            var plan = new MatchPlanState { ParticipantIds = { "a", "b" } };
            var result = new MatchResultState { Id = "current", ShowId = "today" };
            var service = new MatchNarrationService();
            Assert.That(service.CreateBeats(plan, result, save).Any(x => x.Detail?.Contains("연승") == true), Is.False);
            for (var i = 0; i < 3; i++) save.MatchResults.Add(new MatchResultState
            {
                Id = "past" + i, ShowId = "past-show" + i, WinnerId = "a", LoserTargetId = "b", FinishType = MatchFinishType.Pinfall,
                ParticipantProfiles = { new MatchParticipantProfileState { SideId = "first", MemberIds = { "a" } }, new MatchParticipantProfileState { SideId = "second", MemberIds = { "b" } } }
            });
            save.MatchResults.Add(new MatchResultState { Id = "earlier-today", ShowId = "today", WinnerId = "b", LoserTargetId = "a" });
            save.MatchResults.Add(result);
            save.MatchResults.Add(new MatchResultState { Id = "future", ShowId = "later", WinnerId = "b", LoserTargetId = "a" });
            var beats = service.CreateBeats(plan, result, save);
            Assert.That(beats.Any(x => x.Detail?.Contains("승자, 최근 3연승") == true), Is.True);
            Assert.That(beats.Any(x => x.Detail?.Contains("도전자, 지난 맞대결에서는 패했습니다") == true), Is.True);
            save.MatchResults[1].FinishType = MatchFinishType.Draw;
            Assert.That(service.CreateBeats(plan, result, save).Any(x => x.Detail?.Contains("연승") == true), Is.False);
            save.TagTeams.Add(new TagTeamState { Status = TagTeamStatus.Disbanded, MemberIds = { "a", "b" } });
            beats = service.CreateBeats(plan, result, save);
            Assert.That(beats.Any(x => x.Detail?.Contains("한때 같은 태그팀") == true), Is.True);
            Assert.That(beats.Any(x => x.Detail?.Contains("설욕") == true), Is.False);
        }

        [Test]
        public void SpecialContext_GimmicksAndTeamChemistryDoNotInventActions()
        {
            var service = new MatchNarrationService();
            var plan = new MatchPlanState { ParticipantIds = { "a", "b" } };
            var result = new MatchResultState();
            foreach (var pair in new[] { ("gimmick_001", "케이지"), ("gimmick_002", "래더 매치"), ("gimmick_003", "테이블 매치"), ("gimmick_004", "하드코어") })
            {
                plan.MatchGimmickId = pair.Item1;
                var contexts = service.CreateBeats(plan, result).Where(x => x.BeatType == MatchBeatType.Context).ToList();
                Assert.That(contexts, Has.Count.EqualTo(1));
                Assert.That(contexts[0].Detail, Does.Contain(pair.Item2));
            }
            plan.MatchGimmickId = "gimmick_000";
            result.ParticipantProfiles.Add(new MatchParticipantProfileState { RepresentedTagTeamId = "team", MemberIds = { "a", "partner" }, AppliedChemistry = 80 });
            Assert.That(service.CreateBeats(plan, result, new GameSave()).Any(x => x.Detail?.Contains("팀 호흡이 좋은") == true), Is.True);
            result.ParticipantProfiles[0].AppliedChemistry = 50;
            Assert.That(service.CreateBeats(plan, result, new GameSave()).Any(x => x.Detail?.Contains("팀 호흡이 좋은") == true), Is.False);
        }
    }
}
