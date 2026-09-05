using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public sealed class MatchMoveRules
    {
        public string Name;
        public float BrawlingWeight, PowerWeight, TechnicalWeight, HighFlyingWeight;
        public float ExecutionDifficulty, SellingDifficulty;
    }

    public static class MatchMoveService
    {
        public static List<MoveResultState> Evaluate(GameSave save, IReadOnlyList<MatchSideState> sides,
            MatchResultState match, Func<string, MatchMoveRules> findMove)
        {
            var results = new List<MoveResultState>();
            if (findMove == null || sides.Count < 2) return results;
            // Independent stream: move highlights never change match quality, booking, spots or injuries.
            var random = new Random(match.ResultSeed ^ 0x4d4f5645);
            foreach (var side in sides)
                foreach (var id in side.MemberIds)
                {
                    var actor = save.Wrestlers.Single(x => x.Id == id);
                    var opponents = sides.Where(x => x != side).SelectMany(x => x.MemberIds).ToList();
                    foreach (var finisher in new[] { false, true })
                    {
                        var ids = finisher ? actor.Presentation.FinisherMoveIds : actor.Presentation.SignatureMoveIds;
                        var moves = (ids ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()
                            .Select(x => (Id: x, Rules: findMove(x))).Where(x => x.Rules != null).ToList();
                        if (moves.Count == 0) continue;
                        // ponytail: one highlight per signature/finisher slot, not a full move-by-move combat simulation.
                        var move = moves[random.Next(moves.Count)];
                        var targetId = opponents[random.Next(opponents.Count)];
                        var target = save.Wrestlers.Single(x => x.Id == targetId);
                        var a = actor.Attributes;
                        var rules = move.Rules;
                        var execution = Adjust(a.Brawling * rules.BrawlingWeight + a.Power * rules.PowerWeight +
                            a.Technical * rules.TechnicalWeight + a.HighFlying * rules.HighFlyingWeight, actor, match);
                        var selling = Adjust(target.Attributes.Selling, target, match);
                        var probabilities = MatchSpotEvaluator.Probabilities((execution + selling) / 2,
                            (rules.ExecutionDifficulty + rules.SellingDifficulty) / 2);
                        var roll = random.NextDouble();
                        var outcome = 0;
                        while (outcome < 3 && roll >= probabilities[outcome]) { roll -= probabilities[outcome]; outcome++; }
                        results.Add(new MoveResultState
                        {
                            MoveId = move.Id, MoveName = rules.Name, ActorId = id, TargetId = target.Id,
                            IsFinisher = finisher, ExecutionScore = execution, SellingScore = selling,
                            Result = (SpotExecutionResult)outcome
                        });
                    }
                }
            return results;
        }

        private static float Adjust(float ability, WrestlerState wrestler, MatchResultState match)
        {
            if (float.IsNaN(ability) || float.IsInfinity(ability) || ability < 0 || ability > 20)
                throw new InvalidOperationException("무브셋 수행 능력치가 유효하지 않습니다.");
            var stamina = match.WrestlerPerformances?.FirstOrDefault(x => x.WrestlerId == wrestler.Id)?.StaminaPenalty ?? 0;
            var condition = wrestler.Condition.Condition;
            if (float.IsNaN(condition) || condition < 0 || condition > 100 ||
                float.IsNaN(stamina) || float.IsInfinity(stamina) || stamina < 0)
                throw new InvalidOperationException("무브셋 수행 컨디션이 유효하지 않습니다.");
            return Math.Max(0, ability - (100 - condition) / 20 - stamina);
        }

        public static string Narrate(MoveResultState move, Func<string, string> name)
        {
            var actor = name(move.ActorId);
            var success = move.Result >= SpotExecutionResult.Success;
            var (title, good, bad) = move.MoveId switch
            {
                "move_001" => ("러닝 니 스트라이크", "무릎이 정통으로 꽂힙니다!", "무릎이 빗맞습니다!"),
                "move_002" => ("디스커스 래리어트", "강렬한 래리어트! 그대로 쓰러뜨립니다!", "래리어트에 힘이 실리지 않습니다!"),
                "move_003" => ("스피닝 백피스트", "안면에 제대로 들어갑니다!", "주먹이 허공을 가릅니다!"),
                "move_004" => ("스피어", "몸통에 제대로 꽂힙니다!", "몸통을 제대로 파고들지 못합니다!"),
                "move_005" => ("싯아웃 파워밤", "매트에 거세게 내리꽂습니다!", "끝까지 들어 올리지 못합니다!"),
                "move_006" => ("초크슬램", "엄청난 높이에서 내리꽂습니다!", "들어 올리다 놓칩니다!"),
                "move_007" => ("스파인버스터", "등이 매트에 꽂힙니다!", "잡아챘지만 넘어뜨리지 못합니다!"),
                "move_008" => ("파일드라이버", "그대로 꽂아버립니다!", "들어 올렸지만 버티지 못합니다!"),
                "move_009" => ("저먼 수플렉스", "뒤로 크게 넘어갑니다!", "허리를 잡았지만 넘기지 못합니다!"),
                "move_010" => ("드래곤 수플렉스", "양팔이 묶인 채 내리꽂힙니다!", "팔을 놓칩니다! 넘기지 못합니다!"),
                "move_011" => ("크로스페이스", "깊게 걸렸습니다! 빠져나오기 어렵겠습니다!", "제대로 잠그지 못합니다! 압박이 풀립니다!"),
                "move_012" => ("앵클 록", "발목을 꽉 붙잡았습니다! 고통스럽겠습니다!", "발목을 놓칩니다! 압박이 풀립니다!"),
                "move_013" => ("프로그 스플래시", "몸 위로 제대로 떨어집니다!", "정확하게 덮치지 못합니다!"),
                "move_014" => ("문설트", "정확하게 덮칩니다! 엄청난 충격입니다!", "목표를 벗어납니다!"),
                "move_015" => ("슈팅 스타 프레스", "공중에서 그대로 덮칩니다!", "몸을 제대로 덮치지 못합니다!"),
                "move_016" => ("450 스플래시", "회전 끝에 정통으로 떨어집니다!", "낙하 지점이 빗나갑니다!"),
                "move_017" => ("스프링보드 커터", "커터가 제대로 들어갑니다!", "머리를 붙잡지 못합니다!"),
                "move_018" => ("다이빙 엘보 드롭", "팔꿈치가 정통으로 꽂힙니다!", "팔꿈치가 빗맞습니다!"),
                "move_019" => ("토네이도 DDT", "회전과 함께 매트에 꽂힙니다!", "머리를 놓치고 맙니다!"),
                "move_020" => ("슈퍼킥", "턱에 제대로 꽂힙니다!", "킥이 허공을 가릅니다!"),
                "move_021" => ("바이시클 킥", "강하게 걷어찹니다! 제대로 들어갔습니다!", "발끝이 빗나갑니다!"),
                "move_022" => ("데스 밸리 드라이버", "어깨 위에서 그대로 내리꽂습니다!", "어깨 위에서 놓치고 맙니다!"),
                "move_023" => ("브리징 수플렉스", "크게 넘깁니다! 브리지까지 단단합니다!", "넘겼지만 브리지가 무너집니다!"),
                "move_024" => ("코크스크루 센턴", "등으로 강하게 덮칩니다!", "정확하게 덮치지 못하고 비껴갑니다!"),
                _ => (string.IsNullOrWhiteSpace(move.MoveName) ? "기술" : move.MoveName,
                    "제대로 들어갑니다!", "제대로 들어가지 않습니다!")
            };
            var performance = move.Result switch
            {
                SpotExecutionResult.GreatSuccess => "상대를 몰아붙일 절호의 기회입니다!",
                SpotExecutionResult.Success => "",
                SpotExecutionResult.Failure => "",
                _ => "아, 완전히 흐름을 잃습니다! 이 틈을 조심해야 합니다!"
            };
            return $"{actor}의 {title}! {(success ? good : bad)} {performance}".TrimEnd();
        }
    }
}
