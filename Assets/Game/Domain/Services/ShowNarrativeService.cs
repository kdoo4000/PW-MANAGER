using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public sealed class PromoGenerationContext
    {
        public PromoPlanState Plan;
        public PromoResultState Result;
        public ShowEventState ShowEvent;
        public List<WrestlerState> Wrestlers = new();
        public List<ResultChangeState> RecentEvents = new();
        public List<string> MandatoryBeatIds = new();
        public List<string> MandatoryBeats = new();
    }

    public static class PromoContextBuilder
    {
        public static PromoGenerationContext Build(GameSave save, PromoResultState result)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            if (result == null) throw new ArgumentNullException(nameof(result));
            var plan = save.PromoPlans.Single(x => x?.Id == result.PromoPlanId);
            var showEvent = save.ShowEvents.Single(x => x?.Id == result.ShowEventId);
            var context = new PromoGenerationContext
            {
                Plan = plan,
                Result = result,
                ShowEvent = showEvent,
                Wrestlers = plan.ParticipantIds.Select(id => save.Wrestlers.Single(x => x?.Id == id)).ToList(),
                RecentEvents = (save.ShowResults ?? new List<ShowResultState>())
                    .Where(x => x != null && x.ShowId != result.ShowId)
                    .SelectMany(x => x.StoryChanges ?? new List<ResultChangeState>())
                    .Where(x => x != null && (plan.ParticipantIds.Contains(x.TargetId) || string.IsNullOrEmpty(x.TargetId)))
                    .TakeLast(5).ToList()
            };
            AddBeat(context, "opening", plan.OpeningSpot);
            AddBeat(context, "middle", plan.MiddleSpot);
            AddBeat(context, "closing", plan.ClosingSpot);
            return context;
        }

        private static void AddBeat(PromoGenerationContext context, string id, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            context.MandatoryBeatIds.Add(id);
            context.MandatoryBeats.Add(text.Trim());
        }
    }

    public static class PromoPromptBuilder
    {
        public const string SystemPrompt = "당신은 한국 프로레슬링 방송의 대본 작가다. 번역투가 아닌 짧고 자연스러운 한국어 구어체로 쓴다. 등장인물은 성인 프로레슬러답게 말하며 유치한 호칭, 장황한 설명, 영어와 한자·중국어·일본어를 쓰지 않는다. 게임에 기록된 사건과 결과는 절대적 사실이다. 경기, 챔피언십, 부상, 관계, 배신, 소속, 승패, 공격, 계약 및 스토리 사건을 새로 만들거나 바꾸지 않는다. 특히 필수 사건의 행위 주체, 대상, 원인과 결론을 바꾸지 않으며 거절을 수락으로 바꾸지 않는다. 창작 가능한 것은 말투, 몸짓, 침묵, 표정, 관중 반응과 경미한 무대 행동뿐이다. 선수의 선악 성향과 마이크 능력을 반영하되 능력이 낮아도 문장은 자연스러워야 한다. Summary와 Text는 한국어로만 작성하고 지정된 JSON만 반환한다.";

        public static string Build(PromoGenerationContext context)
        {
            var text = new StringBuilder(2048);
            text.AppendLine("[SCENE]").Append("Purpose: ").AppendLine(context.Plan.Purpose.ToString())
                .Append("Presentation: ").AppendLine(context.Plan.Presentation.ToString())
                .Append("DurationMinutes: ").AppendLine(context.ShowEvent.PlannedDuration.ToString())
                .Append("ParticipantIds: ").AppendLine(string.Join(", ", context.Plan.ParticipantIds))
                .Append("TargetIds: ").AppendLine(string.Join(", ", context.Plan.TargetIds ?? new List<string>()));
            foreach (var wrestler in context.Wrestlers)
            {
                var a = wrestler.Attributes;
                text.AppendLine("[CHARACTER]").Append("Id: ").AppendLine(wrestler.Id)
                    .Append("Name: ").AppendLine(string.IsNullOrWhiteSpace(wrestler.Identity.RingName) ? wrestler.Identity.LegalName : wrestler.Identity.RingName)
                    .Append("Alignment: ").AppendLine(wrestler.Roster.Alignment.ToString())
                    .Append("PromoArchetype: ").AppendLine(wrestler.Presentation.PromoArchetype.ToString())
                    .Append("Disposition: ").AppendLine(wrestler.Presentation.PromoDisposition.ToString())
                    .Append("TagTeamId: ").AppendLine(wrestler.Roster.ActiveTagTeamId ?? "none")
                    .Append("StableId: ").AppendLine(wrestler.Roster.ActiveStableId ?? "none")
                    .Append("Status: ").AppendLine(wrestler.Status.StatusValue.ToString())
                    .Append("Momentum: ").AppendLine(wrestler.Momentum.Momentum.ToString("0.0"))
                    .Append("Abilities /20: Charisma ").Append(a.Charisma.ToString("0.0")).Append(", MicWork ").Append(a.MicWork.ToString("0.0"))
                    .Append(", Improvisation ").Append(a.Improvisation.ToString("0.0")).Append(", Acting ").Append(a.Acting.ToString("0.0"))
                    .Append(", FaceWork ").Append(a.FaceWork.ToString("0.0")).Append(", HeelWork ").Append(a.HeelWork.ToString("0.0"))
                    .Append(", Comedy ").AppendLine(a.Comedy.ToString("0.0"))
                    .Append("MicWorkMeaning: ").AppendLine(AbilityMeaning(a.MicWork));
            }
            text.AppendLine("[RECENT CANONICAL EVENTS]");
            foreach (var item in context.RecentEvents) text.Append("- ").Append(item.ValueKey).Append(": ").AppendLine(item.Reason);
            text.AppendLine("[MANDATORY EVENTS]");
            for (var i = 0; i < context.MandatoryBeats.Count; i++) text.Append(context.MandatoryBeatIds[i]).Append(": ").AppendLine(context.MandatoryBeats[i]);
            text.AppendLine("[ACTUAL RESULT]").Append("PromoScore: ").AppendLine(context.Result.PromoScore.ToString("0.0"));
            foreach (var reason in context.Result.EvaluationReasons ?? new List<EvaluationReasonState>())
                text.Append(reason.Code).Append(": ").AppendLine(reason.Contribution.ToString("+0.0;-0.0;0.0"));
            AppendChanges(text, "StoryChanges", context.Result.StoryChanges);
            AppendChanges(text, "RelationshipChanges", context.Result.RelationshipChanges);
            text.AppendLine("[OUTPUT]")
                .AppendLine("Return JSON: {\"Summary\":\"...\",\"FulfilledBeatIds\":[\"opening\"],\"Lines\":[{\"Type\":\"Dialogue|Action|Crowd\",\"SpeakerId\":\"participant id or empty\",\"Text\":\"...\",\"Intensity\":1}]}.")
                .AppendLine("Use every mandatory event once, in listed order, and include its id in FulfilledBeatIds. Never add a gameplay event.")
                .AppendLine("필수 사건마다 누가 누구에게 무엇을 했는지 그대로 유지한다. 대사는 해당 인물이 실제로 말할 법한 짧은 구어체로 쓴다.")
                .AppendLine("직역체 표현, 같은 이름의 반복, 상황을 설명하는 대사, 유치한 도발을 피한다. 관중 반응은 짧게 쓴다.")
                .AppendLine("Summary는 한국어 60자 이하, Lines는 4-6개, 각 Text는 한국어 70자 이하로 쓴다. Intensity는 0-5다. JSON 밖에 아무것도 쓰지 않는다.");
            return text.ToString();
        }

        private static string AbilityMeaning(float value) => value >= 16f ? "elite, polished and precise" :
            value >= 11f ? "competent and clear" : value >= 6f ? "simple and uneven but readable" : "very limited, brief but readable";

        private static void AppendChanges(StringBuilder text, string heading, IEnumerable<ResultChangeState> changes)
        {
            foreach (var change in changes ?? Array.Empty<ResultChangeState>())
                text.Append(heading).Append(": ").Append(change.TargetId).Append(' ').Append(change.ValueKey).Append(' ')
                    .Append(change.Amount.ToString("+0.0;-0.0;0.0")).Append(" because ").AppendLine(change.Reason);
        }
    }

    public sealed class MatchNarrationService
    {
        public List<MatchSimulationBeatState> CreateBeats(MatchPlanState plan, MatchResultState result, GameSave save = null)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (result == null) throw new ArgumentNullException(nameof(result));
            var beats = new List<MatchSimulationBeatState>();
            var sides = (plan.Sides ?? new List<MatchSideState>()).Where(x => x?.MemberIds?.Any(id => !string.IsNullOrWhiteSpace(id)) == true)
                .Select(x => new MatchSideState { Id = x.Id, MemberIds = x.MemberIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToList() }).ToList();
            if (sides.Count == 0)
            {
                var legacy = (plan.ParticipantIds ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                var members = plan.MembersPerTeam > 1 && plan.TeamCount * plan.MembersPerTeam == legacy.Count ? plan.MembersPerTeam : 1;
                sides = legacy.Select((id, index) => (id, index)).GroupBy(x => x.index / members)
                    .Select(group => new MatchSideState { MemberIds = group.Select(x => x.id).ToList() }).ToList();
            }
            var participants = sides.SelectMany(x => x.MemberIds).ToList();
            var teamMatch = sides.Any(x => x.MemberIds.Count > 1);
            var multiWay = sides.Count > 2;
            var variation = new Random(result.ResultSeed);
            var actor = participants.FirstOrDefault();
            var target = sides?.Skip(1).FirstOrDefault()?.MemberIds.FirstOrDefault() ?? participants.Skip(1).FirstOrDefault();
            var winner = result.FinishPerformerId ?? result.WinnerId;
            var loser = result.LoserTargetId;
            AddSpot(beats, MatchBeatType.PlannedSpot, plan.OpeningSpot, actor, target, 0);
            AddEvaluatedSpots(beats, result, MatchSpotPhase.Entrance);
            if (EndsAt(result, MatchSpotPhase.Entrance)) return FinalizeBeats(beats, result, save);
            beats.Add(Beat(MatchBeatType.Opening, actor, target, null, 0));
            if (multiWay)
                beats.Add(Beat(MatchBeatType.Context, actor, target, teamMatch
                    ? Pick(variation, $"{sides.Count}개 팀이 맞붙습니다! 동료와 호흡을 맞추면서 다른 팀의 움직임도 살펴야 합니다.", $"여러 팀이 겨루는 경기입니다. 자기 팀의 공방에만 집중하다가는 다른 팀에 기회를 줄 수 있겠죠!")
                    : Pick(variation, $"{participants.Count}명이 각자 승리를 노립니다! 눈앞의 상대뿐 아니라 다른 선수들의 위치도 중요합니다.", "한 명만 상대하면 되는 경기가 아닙니다! 공격할 때도 다른 쪽에서 생길 빈틈을 생각해야겠죠."), 0));
            else if (teamMatch)
                beats.Add(Beat(MatchBeatType.Context, actor, target, Pick(variation,
                    "팀 대 팀의 승부입니다! 개인의 기량과 함께 동료끼리 호흡을 맞추는 능력이 중요하겠습니다.",
                    "혼자 잘하는 것만으로는 부족합니다. 서로의 장점을 살려줄 조합은 어느 쪽일까요?"), 0));
            var context = RelationshipContext(save, actor, target, result);
            if (context != null) beats.Add(Beat(MatchBeatType.Context, actor, target, context, 0));
            foreach (var special in MatchContexts(save, plan, result, actor, target).Take(teamMatch || multiWay ? 1 : 2))
                beats.Add(Beat(MatchBeatType.Context, actor, target, special, 0));
            var automaticExchangeCount = result.ActualMatchDuration <= 0 ? 0 :
                Math.Max(3, Math.Min(9, (int)Math.Ceiling(result.ActualMatchDuration / 4f)));
            beats.Add(Beat(MatchBeatType.Control, actor, target, null, 1));
            AddAutomaticExchanges(beats, sides, variation, 1, automaticExchangeCount / 3);
            AddAutomaticNormalSpot(beats, plan, save, sides, variation, MatchSpotPhase.Early, teamMatch);
            AddEvaluatedSpots(beats, result, MatchSpotPhase.Early);
            if (EndsAt(result, MatchSpotPhase.Early)) return FinalizeBeats(beats, result, save);
            beats.Add(Beat(MatchBeatType.Counter, target, actor, null, 1));
            beats.Add(Beat(MatchBeatType.Control, target, actor, null, 2));
            for (var sideIndex = 0; sideIndex < sides.Count; sideIndex++)
                foreach (var member in sides[sideIndex].MemberIds.Where(id => id != actor && id != target))
                {
                    var opponent = sides[(sideIndex + 1) % sides.Count].MemberIds.FirstOrDefault();
                    if (opponent != member && sides.Count > 1) beats.Add(Beat(MatchBeatType.Control, member, opponent, null, 2));
                }
            AddAutomaticExchanges(beats, sides, variation, 2, automaticExchangeCount / 3);
            AddAutomaticNormalSpot(beats, plan, save, sides, variation, MatchSpotPhase.Middle, teamMatch);
            if (multiWay || teamMatch)
                beats.Add(Beat(MatchBeatType.Context, actor, target, multiWay
                    ? Pick(variation, "여러 쪽을 동시에 경계해야 하는 승부입니다. 한 번 공격한 뒤의 위치 선정도 중요하겠죠.", "다른 진영의 움직임까지 읽어야 합니다! 눈앞의 공방에만 몰두할 수 없는 경기입니다.")
                    : Pick(variation, "동료의 상태를 살피는 판단도 필요합니다. 팀 전체의 여력을 어떻게 나눠 쓸지 보시죠.", "서로의 빈틈을 얼마나 잘 보완하느냐가 중요합니다. 개인전과는 다른 판단이 필요한 순간입니다."), 2));
            var tired = participants.Select(id => save?.Wrestlers?.FirstOrDefault(x => x?.Id == id))
                .FirstOrDefault(x => x?.Condition != null && x.Condition.Condition < 50f);
            if (tired != null) beats.Add(Beat(MatchBeatType.Context, tired.Id, null,
                $"{WrestlerName(tired)}, 오늘 컨디션이 좋지 않습니다. 긴 승부가 된다면 체력 배분이 중요하겠습니다.", 2));
            AddSpot(beats, MatchBeatType.PlannedSpot, plan.MiddleSpot, target, actor, 2);
            AddEvaluatedSpots(beats, result, MatchSpotPhase.Middle);
            if (EndsAt(result, MatchSpotPhase.Middle)) return FinalizeBeats(beats, result, save);
            AddMoves(beats, result, save, false);
            foreach (var incident in result.ExecutionEvents ?? new List<MatchExecutionEventState>())
                if (incident != null) beats.Add(Beat(MatchBeatType.Execution, incident.ParticipantIds?.FirstOrDefault(), null,
                    incident.EventType switch
                    {
                        MatchExecutionEventType.Injury => "선수에게 부상이 발생했습니다. 경기 상황을 주의 깊게 지켜봐야겠습니다.",
                        MatchExecutionEventType.Mistake => "아, 동작이 매끄럽게 이어지지 않았습니다! 흐름을 다시 잡아야 합니다.",
                        _ => "지금 장면, 훌륭했습니다! 이번 경기에서 기억에 남을 순간입니다."
                    }, 2));
            beats.Add(Beat(MatchBeatType.Comeback, actor, target, null, 3));
            AddAutomaticExchanges(beats, sides, variation, 3, automaticExchangeCount - automaticExchangeCount / 3 * 2);
            AddAutomaticNormalSpot(beats, plan, save, sides, variation, MatchSpotPhase.Late, teamMatch);
            AddEvaluatedSpots(beats, result, MatchSpotPhase.Late);
            if (EndsAt(result, MatchSpotPhase.Late)) return FinalizeBeats(beats, result, save);
            AddMoves(beats, result, save, true);
            beats.Add(Beat(MatchBeatType.FinisherAttempt, winner ?? actor, loser ?? target, null, 3));
            beats.Add(Beat(MatchBeatType.Finish, winner, loser, result.FinishType.ToString(), 4));
            if (result.FinishType != MatchFinishType.Draw && !string.IsNullOrEmpty(winner) &&
                sides.Any(x => x.MemberIds.Contains(winner) && x.MemberIds.Count > 1))
                beats.Add(Beat(MatchBeatType.Context, winner, loser, Pick(variation,
                    "결정적인 승부가 팀의 승리로 이어집니다! 함께 나선 동료들도 이 결과를 가져갑니다.",
                    "팀 대결의 승자가 정해졌습니다! 오늘의 승리는 동료들과 함께 기록됩니다."), 4));
            else if (multiWay && result.FinishType != MatchFinishType.Draw && participants.Contains(winner))
                beats.Add(Beat(MatchBeatType.Context, winner, loser, Pick(variation,
                    "여러 상대와 겨룬 끝에 승자가 나왔습니다! 복잡한 승부에서 끝내 결과를 가져갑니다.",
                    "여러 선수가 맞붙은 다인전, 마지막 승부를 결정지은 선수가 승리를 가져갑니다!"), 4));
            var winningWrestler = save?.Wrestlers?.FirstOrDefault(x => x?.Id == winner);
            var losingWrestler = save?.Wrestlers?.FirstOrDefault(x => x?.Id == loser);
            if (result.FinishType != MatchFinishType.Draw && participants.Count == 2 && winner != loser &&
                participants.Contains(winner) && participants.Contains(loser) && winningWrestler?.Status != null && losingWrestler?.Status != null &&
                losingWrestler.Status.StatusValue - winningWrestler.Status.StatusValue >= 25f)
                beats.Add(Beat(MatchBeatType.Context, winner, loser,
                    $"{WrestlerName(winningWrestler)}, 위상의 차이를 뒤집었습니다! 더 높은 평가를 받던 상대를 꺾는 값진 승리입니다!", 4));
            AddSpot(beats, MatchBeatType.PlannedSpot, plan.ClosingSpot, winner ?? actor, loser ?? target, 4);
            AddEvaluatedSpots(beats, result, MatchSpotPhase.PostMatch);
            return FinalizeBeats(beats, result, save);
        }

        public List<NarrativeLineState> Narrate(MatchPlanState plan, MatchResultState result, Func<string, string> name, GameSave save = null)
        {
            var random = new Random(result.ResultSeed);
            return CreateBeats(plan, result, save).Select(beat => new NarrativeLineState
            {
                Type = NarrativeLineType.Commentary,
                Intensity = beat.Importance,
                Text = Text(beat, name, random, save)
            }).ToList();
        }

        private static MatchSimulationBeatState Beat(MatchBeatType type, string actor, string target, string detail, int phase) => new()
        {
            BeatType = type, ActorId = actor, TargetId = target, Detail = detail, Phase = phase,
            Importance = type == MatchBeatType.Control ? 1 : type == MatchBeatType.Finish ? 5 : 3,
            CrowdReaction = phase * .25f
        };

        private static void AddMoves(List<MatchSimulationBeatState> beats, MatchResultState result, GameSave save, bool finisher)
        {
            foreach (var move in (result.MoveResults ?? new List<MoveResultState>()).Where(x => x != null && x.IsFinisher == finisher))
            {
                string Name(string id)
                {
                    var wrestler = save?.Wrestlers?.FirstOrDefault(x => x?.Id == id);
                    return wrestler == null ? "선수" : WrestlerName(wrestler);
                }
                var beat = Beat(MatchBeatType.Execution, move.ActorId, move.TargetId,
                    MatchMoveService.Narrate(move, Name), finisher ? 3 : 2);
                beat.Succeeded = move.Result >= SpotExecutionResult.Success;
                beats.Add(beat);
            }
        }

        private static void AddAutomaticExchanges(List<MatchSimulationBeatState> beats, IReadOnlyList<MatchSideState> sides,
            Random random, int phase, int count)
        {
            if (sides.Count < 2) return;
            for (var i = 0; i < count; i++)
            {
                var attackingSide = random.Next(sides.Count);
                var defendingSide = (attackingSide + 1 + random.Next(sides.Count - 1)) % sides.Count;
                var actor = sides[attackingSide].MemberIds[random.Next(sides[attackingSide].MemberIds.Count)];
                var target = sides[defendingSide].MemberIds[random.Next(sides[defendingSide].MemberIds.Count)];
                beats.Add(Beat(i % 2 == 0 ? MatchBeatType.Control : MatchBeatType.Counter, actor, target, null, phase));
            }
        }

        private static void AddAutomaticNormalSpot(List<MatchSimulationBeatState> beats, MatchPlanState plan, GameSave save,
            IReadOnlyList<MatchSideState> sides, Random random, MatchSpotPhase phase, bool teamMatch)
        {
            var available = NormalMatchSpotCatalog.Available(plan, phase, teamMatch);
            if (available.Count == 0 || sides.Count < 2) return;
            var spot = available[random.Next(available.Count)];
            var actorSide = random.Next(sides.Count);
            var targetSide = (actorSide + 1 + random.Next(sides.Count - 1)) % sides.Count;
            var actor = sides[actorSide].MemberIds[random.Next(sides[actorSide].MemberIds.Count)];
            var target = sides[targetSide].MemberIds[random.Next(sides[targetSide].MemberIds.Count)];
            string Name(string id)
            {
                var wrestler = save?.Wrestlers?.FirstOrDefault(x => x?.Id == id);
                return wrestler == null ? "선수" : WrestlerName(wrestler);
            }
            beats.Add(Beat(MatchBeatType.PlannedSpot, actor, target,
                spot.Text.Replace("{actor}", Name(actor)).Replace("{target}", Name(target)), (int)phase));
        }

        private static void AddEvaluatedSpots(List<MatchSimulationBeatState> beats, MatchResultState result, MatchSpotPhase phase)
        {
            foreach (var spot in (result.SpotResults ?? new List<SpotResultState>()).Where(x => x.Phase == phase))
            {
                var performance = spot.Result switch
                {
                    SpotExecutionResult.GreatSuccess => "타이밍이 완벽하게 맞았습니다!",
                    SpotExecutionResult.Success => "주도권을 놓치지 않습니다!",
                    SpotExecutionResult.Failure when spot.Recovered => "위태로웠지만 금세 전열을 가다듬습니다!",
                    SpotExecutionResult.Failure => "생각만큼 상대를 몰아붙이지 못합니다!",
                    _ => "아, 흐름을 완전히 놓칩니다! 빈틈이 커졌습니다!"
                };
                var repetition = spot.Result >= SpotExecutionResult.Success && spot.FinalSpotScore < 0
                    ? " 반복된 장면에 관중의 반응이 차갑습니다." : string.Empty;
                var beat = Beat(MatchBeatType.PlannedSpot, spot.ActorId, spot.TargetId,
                    spot.ScriptedMoment + " " + performance + repetition, (int)phase);
                beat.Succeeded = spot.Result >= SpotExecutionResult.Success;
                beat.CrowdReaction = spot.FinalSpotScore;
                beats.Add(beat);
            }
        }

        private static bool EndsAt(MatchResultState result, MatchSpotPhase phase) =>
            (result.SpotResults ?? new List<SpotResultState>()).Any(x => x.Phase == phase &&
                (x.ScriptedEffect == SpotScriptedEffect.MatchStopped || x.ScriptedEffect == SpotScriptedEffect.Disqualification));

        private static List<MatchSimulationBeatState> FinalizeBeats(List<MatchSimulationBeatState> beats, MatchResultState result, GameSave save)
        {
            var mainEvent = save?.Shows?.Any(x => x?.Id == result.ShowId && x.MainEventId == result.ShowEventId) == true;
            var totalSeconds = mainEvent ? 150f : Math.Max(60f, Math.Min(120f, Math.Max(1, result.ActualMatchDuration) * 6f));
            var weight = Math.Max(1, beats.Sum(x => x.Importance));
            for (var i = 0; i < beats.Count; i++)
            {
                beats[i].DurationSeconds = totalSeconds * beats[i].Importance / weight;
                beats[i].MatchProgress = (float)i / Math.Max(1, beats.Count - 1);
            }
            return beats;
        }

        private static void AddSpot(List<MatchSimulationBeatState> beats, MatchBeatType type, string detail, string actor, string target, int phase)
        { if (!string.IsNullOrWhiteSpace(detail)) beats.Add(Beat(type, actor, target, detail.Trim(), phase)); }

        private static string Text(MatchSimulationBeatState beat, Func<string, string> name, Random random, GameSave save)
        {
            var actor = string.IsNullOrEmpty(beat.ActorId) ? "선수" : name(beat.ActorId);
            var target = string.IsNullOrEmpty(beat.TargetId) ? "상대" : name(beat.TargetId);
            var wrestler = save?.Wrestlers?.FirstOrDefault(x => x?.Id == beat.ActorId);
            var style = wrestler?.Presentation?.WrestlingStyleId;
            if (style == null || !new[] { "style_001", "style_002", "style_003", "style_004", "style_005", "style_006", "style_007" }.Contains(style))
                style = Style(wrestler).ToString();
            return beat.BeatType switch
            {
                MatchBeatType.Opening => Pick(random,
                    $"공이 울렸습니다! {actor}, 그리고 {target}. 서로 거리를 재며 기회를 살핍니다.",
                    $"자, 경기 시작합니다! {actor}, {target}. 먼저 주도권을 잡는 쪽은 누구일까요?",
                    $"시작을 알리는 공입니다! {actor}, 조심스럽게 다가갑니다. {target}의 첫 대응을 살피는군요.",
                    $"드디어 맞붙습니다! {actor}, {target}. 첫 공방부터 눈여겨봐야겠습니다!"),
                MatchBeatType.Control => ControlText(style, actor, target, random),
                MatchBeatType.Counter => CounterText(style, actor, target, random),
                MatchBeatType.Comeback => Pick(random,
                    $"{actor}, 다시 거리를 벌립니다. 초반과 같은 공방을 허용하지 않겠다는 움직임입니다!",
                    $"{actor}, 호흡을 가다듬고 다시 나옵니다! {target}에게 흐름을 내줄 생각이 없습니다.",
                    $"{actor}, 물러서지 않습니다! 다시 공격에 나서며 경기의 흐름을 바꾸려 합니다!"),
                // Authored spots are quoted intact: assigning a guessed actor could reverse their meaning.
                MatchBeatType.PlannedSpot => $"자, 이 장면 보시죠! {beat.Detail}",
                MatchBeatType.Context or MatchBeatType.Execution => beat.Detail,
                MatchBeatType.FinisherAttempt => Pick(random,
                    "경기 막판입니다! 한 번의 실수가 승부를 가를 수 있습니다. 끝까지 집중해야 합니다!",
                    "이제 마무리를 노릴 시간입니다! 결정적인 공격을 연결할 틈이 생길까요?",
                    "마지막까지 긴장을 놓을 수 없습니다! 승부를 끝낼 기회를 누가 잡을까요?"),
                MatchBeatType.Finish => FinishText(beat.Detail, actor, target),
                _ => beat.Detail
            };
        }

        private static MatchArchetype Style(WrestlerState wrestler) => wrestler?.Presentation?.WrestlingStyleId switch
        {
            "style_001" => MatchArchetype.Brawler,
            "style_002" or "style_006" => MatchArchetype.Power,
            "style_003" => MatchArchetype.Technical,
            "style_004" or "style_005" => MatchArchetype.HighFlying,
            "style_007" => MatchArchetype.Balanced,
            _ => wrestler?.Presentation?.MatchArchetype ?? MatchArchetype.Balanced
        };

        private static string Pick(Random random, params string[] lines) => lines[random.Next(lines.Length)];

        private static string ControlText(string style, string actor, string target, Random random) => style switch
        {
            "style_001" or nameof(MatchArchetype.Brawler) => Pick(random,
                $"{actor}, 거리를 주지 않습니다! 타격을 이어가며 {target} 쪽으로 밀고 들어갑니다.",
                $"{actor}, 짧은 타격을 연달아 꽂습니다! {target}, 숨 돌릴 틈이 없습니다.",
                $"{actor}, 거칠게 파고듭니다! 몸을 맞대고 타격으로 압박하는군요!"),
            "style_002" or nameof(MatchArchetype.Power) => Pick(random,
                $"{actor}, 힘으로 중심을 무너뜨립니다! {target}, 정면 대결은 부담스럽겠습니다.",
                $"{actor}, 허리를 붙잡고 들어 올립니다! 힘으로 공방을 끝내려는 움직임입니다.",
                $"{actor}, 강하게 밀어붙입니다! {target}의 버티는 힘을 시험하는군요!"),
            "style_003" or nameof(MatchArchetype.Technical) => Pick(random,
                $"{actor}, 팔을 잡고 매트로 끌고 갑니다. {target}, 먼저 이 압박부터 풀어야 합니다.",
                $"{actor}, 손목에서 팔꿈치로 이어갑니다! 관절을 하나씩 통제하는 정교한 공방입니다.",
                $"{actor}, 매트에서 유리한 자세를 잡습니다. {target}의 빠져나갈 방향까지 막는군요!"),
            "style_004" or nameof(MatchArchetype.HighFlying) => Pick(random,
                $"{actor}, 로프를 타고 속도를 올립니다! {target}, 빠른 움직임을 따라가야 합니다.",
                $"{actor}, 높이 뛰어오릅니다! 공중에서 {target}을 향해 몸을 던집니다!",
                $"{actor}, 탄력을 살려 날아듭니다! 지상에만 머물지 않는 공격입니다!"),
            "style_005" => Pick(random,
                $"{actor}, 가위처럼 다리를 걸고 회전합니다! 루차 특유의 움직임으로 {target}을 넘깁니다!",
                $"{actor}, 몸을 비틀어 상대의 옆으로 돌아갑니다! 회전이 끊이지 않는 루차 공방입니다!",
                $"{actor}, 가볍게 뛰어올라 회전으로 중심을 빼앗습니다! {target}, 방향을 따라가기 어렵겠습니다!"),
            "style_006" => Pick(random,
                $"{actor}, 큰 보폭으로 공간을 좁힙니다! 거구가 다가오자 {target}의 움직일 곳이 줄어듭니다.",
                $"{actor}, 긴 팔로 상대를 붙잡습니다! 자이언트의 리치가 압박으로 이어집니다!",
                $"{actor}, 체중을 실어 누릅니다! {target}, 이 거대한 벽을 어떻게 벗어날까요?"),
            nameof(MatchArchetype.Fundamentals) => Pick(random,
                $"{actor}, 기본에 충실한 헤드록입니다. 자세를 낮추고 단단히 잡습니다.",
                $"{actor}, 차근차근 자세를 잡습니다. 기본기에서 빈틈을 주지 않는군요.",
                $"{actor}, 무리하지 않고 중심을 지킵니다. 안정적인 기본기로 압박합니다."),
            nameof(MatchArchetype.Spot) => Pick(random,
                $"{actor}, 크게 몸을 날립니다! 한 장면에 힘을 쏟는 과감한 공격입니다!",
                $"{actor}, 공격에 속도를 붙입니다! 큰 동작으로 단번에 흐름을 잡으려 합니다!",
                $"{actor}, 과감하게 치고 나옵니다! 위험을 감수하고 큰 기술을 노립니다!"),
            _ => Pick(random,
                $"{actor}, 타격 뒤에 곧바로 잡기입니다! 여러 공격을 섞어 {target}을 압박합니다.",
                $"{actor}, 거리에 맞춰 공격을 바꿉니다. 올라운더다운 유연한 경기 운영입니다!",
                $"{actor}, 서서 싸우다 매트로 이어갑니다! 한 가지 방식에 머물지 않습니다!")
        };

        private static string CounterText(string style, string actor, string target, Random random) => style switch
        {
            "style_001" or nameof(MatchArchetype.Brawler) => Pick(random,
                $"{actor}, 짧은 타격으로 끊습니다! 맞붙은 채로 반격하는 브롤러입니다!",
                $"{actor}, 물러서는 대신 맞받아칩니다! {target}도 타격을 경계해야 합니다!",
                $"{actor}, 거칠게 치고 나옵니다! 연속 타격으로 압박을 되돌려줍니다!"),
            "style_002" or nameof(MatchArchetype.Power) => Pick(random,
                $"{actor}, 버텨냅니다! 힘으로 상대를 밀어내며 흐름을 끊습니다.",
                $"{actor}, 들어 올리려는 상대를 오히려 밀어붙입니다! 힘으로 뒤집는 공방입니다!",
                $"{actor}, 중심을 낮추고 버팁니다! {target}의 공격을 완력으로 막아냅니다!"),
            "style_003" or nameof(MatchArchetype.Technical) => Pick(random,
                $"{actor}, 손목을 잡아 공격을 끊습니다! 이번에는 팔을 묶고 움직임을 제한합니다.",
                $"{actor}, 잡힌 팔을 돌려 관절기를 되겁니다! 기술로 풀어내는 반격입니다!",
                $"{actor}, 체중을 옮겨 위아래를 바꿉니다! 매트에서의 수 싸움이 좋습니다!"),
            "style_004" or nameof(MatchArchetype.HighFlying) => Pick(random,
                $"{actor}, 몸을 낮춰 피합니다! 빠르게 돌아서며 반격할 공간을 만듭니다.",
                $"{actor}, 훌쩍 뛰어넘습니다! 착지하자마자 다음 공격을 준비합니다!",
                $"{actor}, 로프의 탄력으로 되돌아옵니다! 속도를 살린 반격입니다!"),
            "style_005" => Pick(random,
                $"{actor}, 공중에서 몸을 돌려 빠져나옵니다! 루차 특유의 유연한 탈출입니다!",
                $"{actor}, 다리를 걸어 상대의 힘을 회전으로 바꿉니다! 순식간에 방향이 뒤집힙니다!",
                $"{actor}, 옆으로 굴러 잡기를 벗어납니다! 회전과 함께 반격까지 이어갑니다!"),
            "style_006" => Pick(random,
                $"{actor}, 긴 팔로 접근을 막습니다! 거구의 리치가 반격의 출발점입니다!",
                $"{actor}, 몸을 세워 버팁니다! {target}, 이 큰 체격을 넘어뜨리기가 쉽지 않습니다!",
                $"{actor}, 거대한 몸으로 길을 막습니다! 달려들던 {target}의 흐름이 끊깁니다!"),
            nameof(MatchArchetype.Fundamentals) => Pick(random,
                $"{actor}, 자세부터 바로잡습니다! 기본에 충실하게 잡기를 풀어냅니다.",
                $"{actor}, 발을 고쳐 딛고 버팁니다. 안정적인 중심이 반격을 만듭니다.",
                $"{actor}, 정석적인 탈출입니다! 서두르지 않고 다시 마주 섭니다."),
            nameof(MatchArchetype.Spot) => Pick(random,
                $"{actor}, 과감한 반격입니다! 큰 동작으로 흐름을 단번에 바꾸려 합니다!",
                $"{actor}, 틈을 보자마자 몸을 던집니다! 위험을 감수하는 선택입니다!",
                $"{actor}, 여기서 큰 기술을 시도합니다! 수비에만 머물 생각이 없습니다!"),
            _ => Pick(random,
                $"{actor}, 잡기를 풀고 타격으로 되받습니다! 반격의 방식도 다양합니다!",
                $"{actor}, 상대의 공격에 맞춰 자세를 바꿉니다! 올라운더의 대응력을 보여줍니다!",
                $"{actor}, 타격을 피하며 곧바로 붙잡습니다! 공방의 종류를 바꾸는 반격입니다!")
        };

        private static string FinishText(string finish, string actor, string target) => finish switch
        {
            nameof(MatchFinishType.Draw) => "공이 울립니다! 이번 경기는 무승부입니다. 끝내 승자를 가리지 못했습니다!",
            nameof(MatchFinishType.Submission) => $"항복을 받아냈습니다! {actor}, 서브미션 승리입니다!",
            nameof(MatchFinishType.RollUp) => $"기습적으로 말아 넣습니다! 하나, 둘, 셋! {actor}, 롤업으로 승리를 가져갑니다!",
            nameof(MatchFinishType.Disqualification) => $"심판이 경기를 중단합니다! 반칙 판정입니다. 승자는 {actor}입니다!",
            nameof(MatchFinishType.CountOut) => $"카운트가 끝났습니다! {actor}, 카운트아웃 승리를 거둡니다!",
            nameof(MatchFinishType.Escape) => $"탈출에 성공했습니다! {actor}, 경기를 끝냅니다!",
            nameof(MatchFinishType.ObjectRetrieval) => $"목표물을 확보합니다! {actor}, 승리를 가져갑니다!",
            nameof(MatchFinishType.TableBreak) => $"테이블이 부서집니다! {actor}, 승부를 결정짓습니다!",
            _ => $"{actor}, 커버합니다! 하나, 둘, 셋! {target}, 어깨를 들지 못했습니다!"
        };

        private static IEnumerable<MatchResultState> PreviousMatches(GameSave save, MatchResultState current) =>
            (save?.MatchResults ?? new List<MatchResultState>())
                .TakeWhile(x => string.IsNullOrEmpty(current.Id) || x?.Id != current.Id)
                .Where(x => x != null && x.ShowId != current.ShowId);

        private static bool Won(MatchResultState result, string id) => !string.IsNullOrEmpty(id) && result.FinishType != MatchFinishType.Draw &&
            (result.FinishPerformerId == id || result.WinnerId == id || result.IndirectWinnerIds?.Contains(id) == true ||
             (!string.IsNullOrEmpty(result.WinningSideId) && result.ParticipantProfiles?.Any(x => x?.SideId == result.WinningSideId && x.MemberIds?.Contains(id) == true) == true));

        private static bool Participated(MatchResultState result, string id) =>
            result.ParticipantProfiles?.Any(x => x?.MemberIds?.Contains(id) == true) == true ||
            result.WinnerId == id || result.FinishPerformerId == id || result.LoserTargetId == id ||
            result.IndirectWinnerIds?.Contains(id) == true || result.IndirectLoserIds?.Contains(id) == true;

        private static string WrestlerName(WrestlerState wrestler) =>
            string.IsNullOrWhiteSpace(wrestler?.Identity?.RingName) ? wrestler?.Identity?.LegalName ?? "선수" : wrestler.Identity.RingName;

        private static IEnumerable<string> MatchContexts(GameSave save, MatchPlanState plan, MatchResultState result, string actor, string target)
        {
            // Keep the opening brief: callers take at most two context lines in this priority order.
            switch (plan.MatchGimmickId)
            {
                case "gimmick_001": yield return "케이지 안에서 펼쳐지는 승부입니다. 제한된 공간을 누가 더 영리하게 활용할지 지켜보시죠!"; break;
                case "gimmick_002": yield return "래더 매치입니다! 상대를 제압하는 능력에 더해, 사다리를 활용할 타이밍도 중요하겠습니다."; break;
                case "gimmick_003": yield return "테이블 매치입니다! 평소와는 다른 위험 요소까지 생각하며 경기를 풀어가야 합니다."; break;
                case "gimmick_004": yield return "하드코어 경기입니다. 평소보다 거친 승부가 예상되는 만큼 선수들의 판단이 중요하겠습니다!"; break;
            }
            if (save == null) yield break;
            var first = save.Wrestlers?.FirstOrDefault(x => x?.Id == actor);
            var second = save.Wrestlers?.FirstOrDefault(x => x?.Id == target);
            foreach (var wrestler in new[] { first, second }.Where(x => x != null))
            {
                var streak = PreviousMatches(save, result).Where(x => Participated(x, wrestler.Id)).Reverse().TakeWhile(x => Won(x, wrestler.Id)).Count();
                if (streak >= 3) { yield return $"{WrestlerName(wrestler)}, 최근 {streak}연승입니다! 오늘도 이 흐름을 이어갈 수 있을까요?"; break; }
            }
            if (result.ParticipantProfiles?.Any(x => x != null && !string.IsNullOrEmpty(x.RepresentedTagTeamId) && x.AppliedChemistry >= 75f && x.MemberIds?.Count > 1) == true)
                yield return "팀 호흡이 좋은 조합이 나섭니다. 교대와 협력에서 그 장점을 얼마나 살릴지 기대됩니다!";
            if (first?.Identity == null || second?.Identity == null) yield break;
            var rookie = first.Identity.CareerYears <= second.Identity.CareerYears ? first : second;
            var veteran = rookie == first ? second : first;
            if (rookie.Identity.CareerYears <= 2 && veteran.Identity.CareerYears >= 8)
                yield return $"경력 {rookie.Identity.CareerYears}년의 {WrestlerName(rookie)}, {veteran.Identity.CareerYears}년 경력의 {WrestlerName(veteran)}를 상대합니다. 경험의 차이를 어떻게 넘어설까요?";
            if (first.Identity.WeightKg > 0 && second.Identity.WeightKg > 0 && Math.Abs(first.Identity.WeightKg - second.Identity.WeightKg) >= 25)
                yield return "체중 차이가 큰 대결입니다. 정면 힘겨루기와 빠른 움직임 중 어느 쪽이 승부의 열쇠가 될까요?";
            var firstStyle = Style(first);
            var secondStyle = Style(second);
            if ((firstStyle == MatchArchetype.Technical && secondStyle == MatchArchetype.HighFlying) ||
                (secondStyle == MatchArchetype.Technical && firstStyle == MatchArchetype.HighFlying))
                yield return "매트에서 풀어가려는 테크니션과 공중전을 노리는 하이플라이어의 대결입니다! 누가 자신의 장점을 살릴까요?";
            else if ((firstStyle == MatchArchetype.Power && secondStyle == MatchArchetype.HighFlying) ||
                (secondStyle == MatchArchetype.Power && firstStyle == MatchArchetype.HighFlying))
                yield return "힘과 공중 기술이 맞붙습니다! 붙잡아야 유리한 선수와 거리를 만들어야 하는 선수, 주도권 싸움이 기대됩니다.";
        }

        private static string RelationshipContext(GameSave save, string actor, string target, MatchResultState current)
        {
            if (save == null || string.IsNullOrEmpty(actor) || string.IsNullOrEmpty(target)) return null;
            var team = save.TagTeams?.FirstOrDefault(x => x?.MemberIds?.Contains(actor) == true && x.MemberIds.Contains(target));
            if (team != null) return team.Status == TagTeamStatus.Disbanded
                ? "한때 같은 태그팀이었던 두 선수입니다. 서로의 움직임을 잘 알고 있겠죠. 오늘은 맞은편에서 승부를 겨룹니다!"
                : "같은 팀으로 호흡을 맞춘 두 선수가 오늘은 맞붙습니다. 서로를 잘 아는 만큼 수 싸움이 중요하겠습니다!";
            var previous = PreviousMatches(save, current).LastOrDefault(x =>
                x.ParticipantProfiles?.Any(p => p?.MemberIds?.Contains(actor) == true) == true &&
                x.ParticipantProfiles.Any(p => p?.MemberIds?.Contains(target) == true && !p.MemberIds.Contains(actor)));
            if (previous != null)
            {
                var defeated = Won(previous, actor) ? target : Won(previous, target) ? actor : null;
                if (defeated != null)
                    return $"{WrestlerName(save.Wrestlers?.FirstOrDefault(x => x?.Id == defeated))}, 지난 맞대결에서는 패했습니다. 오늘은 설욕할 수 있을까요?";
                return "이전에 맞붙었던 두 선수가 다시 만났습니다! 지난 대결의 경험을 어떻게 활용할지 지켜보시죠.";
            }
            return null;
        }
    }

    public static class PromoFallbackNarration
    {
        public static PromoScriptState Create(PromoGenerationContext context)
        {
            var script = new PromoScriptState { Summary = $"{context.Plan.Purpose} 프로모", UsedFallback = true };
            for (var i = 0; i < context.MandatoryBeats.Count; i++)
            {
                script.FulfilledBeatIds.Add(context.MandatoryBeatIds[i]);
                script.Lines.Add(new NarrativeLineState { Type = NarrativeLineType.Action, Text = context.MandatoryBeats[i], Intensity = 2 });
            }
            if (script.Lines.Count == 0)
            {
                var speaker = context.Wrestlers.FirstOrDefault();
                if (speaker == null)
                {
                    script.Lines.Add(new NarrativeLineState { Type = NarrativeLineType.Action, Text = "준비된 영상이 상영됩니다.", Intensity = 2 });
                    return script;
                }
                var name = string.IsNullOrWhiteSpace(speaker.Identity.RingName) ? speaker.Identity.LegalName : speaker.Identity.RingName;
                script.Lines.Add(new NarrativeLineState { Type = NarrativeLineType.Dialogue, SpeakerId = speaker.Id, Text = $"{name}가 자신의 뜻을 관중에게 분명히 전합니다.", Intensity = 2 });
            }
            return script;
        }
    }
}
