using System;
using System.Linq;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public static class MatchEventNarration
    {
        private static string WithObject(string text)
        {
            var last = text.Length == 0 ? ' ' : text[text.Length - 1];
            return text + (last >= 0xAC00 && last <= 0xD7A3 && (last - 0xAC00) % 28 != 0 ? "을" : "를");
        }

        public static string Text(MatchEngineEvent e, MatchResultState match, Func<string, string> name)
        {
            var text = CoreText(e, match, name);
            if (e.Type == MatchActionType.MedicalStop) return text;
            if (e.InjurySeverity != InjuryStatus.None)
                text += $" {name(e.InjuredWrestlerId)}에게 실제 부상이 발생했습니다.";
            if (e.IsRescuePreparation) text += " 두 선수가 상황에 맞춰 마무리를 바꿉니다.";
            if (e.SellingProtectedRegion && e.SellingQuality >= .8f && (e.Type == MatchActionType.Recover || e.Type == MatchActionType.LimbAttack ||
                e.IsPhysicalAttack && e.SellingWrestlerId == e.PerformerId))
            {
                var region = e.SellingRegion switch { BodyRegion.Head => "머리", BodyRegion.Arms => "팔", BodyRegion.Legs => "다리", _ => "몸통" };
                text += $" {name(e.SellingWrestlerId)}, {region}을 감싸며 고통을 이어갑니다.";
            }
            return text;
        }

        private static string CoreText(MatchEngineEvent e, MatchResultState match, Func<string, string> name)
        {
            string Name(string id) => string.IsNullOrEmpty(id) ? "선수" : name(id);
            var a = Name(e.PerformerId); var b = Name(e.ReceiverId);
            if (e.Type == MatchActionType.MedicalStop) return $"{Name(e.InjuredWrestlerId)}의 중상으로 심판이 즉시 경기를 중단합니다. 승자 없이 의료 중단으로 종료됩니다.";
            var region = e.TargetRegion switch { BodyRegion.Head => "머리", BodyRegion.Arms => "팔", BodyRegion.Legs => "다리", _ => "몸통" };
            var move = !string.IsNullOrEmpty(e.TechniqueName) ? e.TechniqueName : match.MoveResults?.FirstOrDefault(x => x.MoveId == e.MoveId)?.MoveName;
            var endingSpot = match.SpotResults?.FirstOrDefault(x => x.SpotId == e.Detail && x.ScriptedEffect != SpotScriptedEffect.None);
            if (endingSpot != null) return endingSpot.ScriptedMoment;
            if (e.Detail == "desperation-strike") return $"{a}, 밀리던 흐름에서 회심의 일격! 두 선수 모두 매트에 쓰러집니다!";
            if (e.Detail == "ringside-stall") return $"{a}, 링 밖을 돌며 숨을 고릅니다! {b}에게 내려오라는 손짓을 합니다.";
            string countLine = "";
            if (e.RefereeCountFrom > 0 && e.RefereeCount >= e.RefereeCountFrom)
            {
                var numbers = new[] { "하나", "둘", "셋", "넷", "다섯", "여섯", "일곱", "여덟", "아홉", "열" };
                countLine = "심판, 링 밖의 선수들에게 카운트를 셉니다. " +
                    string.Join("! ", Enumerable.Range(e.RefereeCountFrom, e.RefereeCount - e.RefereeCountFrom + 1).Select(n => numbers[n - 1])) + "!";
            }
            if (e.Detail is "ringside-count" or "count-out" && countLine.Length > 0) return countLine;
            if (e.Detail == "ringside-count-reset") return $"{a}, 잠깐 링 안으로 들어가 심판의 카운트를 초기화합니다! {b}는 아직 링 밖에 있습니다.";
            if (e.Detail == "ringside-resume") return $"{a}, 다시 링 밖으로 내려가 {b}를 쫓습니다! 심판도 하나부터 다시 세기 시작합니다.";
            if (e.Detail == "ringside-wait") return $"{a}, 추격을 멈추고 링에서 기다립니다. {b}도 다시 링으로 돌아옵니다.";
            if (e.Detail == "ringside-return") return $"{a}와 {b}, 몸을 추스르며 링 안으로 돌아옵니다!";
            if (e.Detail == "outside-run") return $"{a}, 링 밖의 {b}를 겨냥해 {WithObject(move)} 준비하며 로프로 달립니다!";
            if (e.Detail == "outside-climb") return $"{a}, 링 밖의 {b}를 보고 탑로프에 올라 {WithObject(move)} 준비합니다!";
            if (e.Detail == "outside-dive-evaded") return $"{b}, {a}의 {WithObject(move)} 옆으로 피합니다! {a}가 링 밖 바닥에 떨어집니다!";
            if (e.Detail == "outside-dive-missed") return $"{a}, {move}의 착지를 놓치고 링 밖 바닥에 떨어집니다!";
            if (e.Detail == "outside-dive") return $"{a}, 링 밖의 {b}에게 {WithObject(move)} 작렬시킵니다! 두 선수가 링 밖으로 떨어집니다!";
            if (e.Detail == "announce-table-setup") return $"{a}, 해설 테이블을 치우고 {b}를 붙잡습니다! 테이블 공격을 준비합니다!";
            if (e.Detail == "announce-table-break") return $"{a}, 해설 테이블에 {WithObject(move ?? "파워밤")} 작렬시킵니다! {b} 아래에서 테이블이 부서집니다! 경기는 계속됩니다!";
            if (e.Detail == "announce-table-blocked") return $"{a}, {b}의 손을 풀어 해설 테이블 공격에서 빠져나옵니다!";
            if (e.Detail == "announce-table-failed") return $"{a}의 해설 테이블 {move}이 연결되지 않습니다! {b}가 위기를 넘깁니다!";
            if (e.Detail == "double-down") return $"{a}와 {b}, 쓰러진 채 숨을 고릅니다. 먼저 일어서려 서로를 바라봅니다.";
            if (e.Detail == "double-rise") return $"{a}와 {b}, 매트를 짚고 상체를 일으킵니다!";
            if (e.Detail == "boo-yay") return $"{a}의 펀치! {b}도 버티며 맞섭니다. 관중의 Boo-Yay가 교환에 맞춰 이어집니다!";
            if (e.Detail == "comeback-rally") return $"{a}, 기세를 끌어올리며 반격을 이어갑니다!";
            if (e.Detail == "comeback-cutoff") return $"{b}, {a}의 연속 반격을 끊어냅니다!";
            if (e.Detail == "clear-save") return $"{a}, {b}를 밀어내며 다른 선수의 세이브를 막습니다!";
            if (e.Detail == "object-retreat") return $"{a}, 구조물에서 내려와 상대를 확인하고 다음 시도를 준비합니다.";
            if (e.Detail == "comeback-strike") return $"{a}, {move ?? "타격"}으로 연속 반격! {b}가 흐름을 끊으려 합니다!";
            if (e.Type == MatchActionType.Finish || e.Type == MatchActionType.Fall)
            {
                var line = e.Result switch
                {
                    MatchActionResult.Submitted => $"{a}의 홀드에서 벗어나지 못한 {b}, 탭을 칩니다!",
                    MatchActionResult.PassedOut => $"{b}, 항복하지 않았지만 의식을 잃었습니다! 심판이 경기를 멈춥니다. {a}의 승리입니다!",
                    MatchActionResult.Disqualification => $"심판이 반칙을 적발했습니다! {a}의 실격승입니다.",
                    MatchActionResult.CountOut => (countLine.Length > 0 ? countLine + " " : "") + $"{b}, 카운트가 끝날 때까지 링으로 돌아오지 못합니다! {a}의 승리입니다.",
                    MatchActionResult.ObjectRetrieved => $"{a}, 마침내 걸린 물체를 확보합니다! 승부가 끝났습니다!",
                    MatchActionResult.TableBroken => $"{a}, {b}를 테이블에 내리꽂아 부숩니다! 승리 조건을 달성합니다!",
                    MatchActionResult.CageEscaped => $"{a}, 상대의 추격을 뿌리치고 철창을 벗어납니다!",
                    MatchActionResult.Draw => e.PerformerId == null ? "종이 울립니다. 승자를 가리지 못하고 무승부로 끝납니다." : $"시간이 끝났습니다! 점수에서 앞선 {a}의 승리입니다!",
                    MatchActionResult.RollUp => $"{a}, 몸을 말아 기습 커버! 하나, 둘, 셋!",
                    _ => $"{a}, 어깨를 누릅니다! 하나, 둘, 셋! {b}가 빠져나오지 못합니다!"
                };
                return e.Type == MatchActionType.Fall ? line.Replace("승부가 끝났습니다!", "폴을 가져갑니다!") + " 한 폴을 따냅니다. 경기는 이어집니다." : line;
            }
            if (e.Type == MatchActionType.Spot)
            {
                if (e.Detail == "illegal-strike") return $"{a}, 심판 앞에서 {b}에게 금지된 공격을 가합니다!";
                var spot = match.SpotResults?.FirstOrDefault(x => x.SpotId == e.Detail && x.ActorId == e.PerformerId);
                return spot?.ScriptedMoment ?? $"{a}의 개입으로 {b}가 흐름을 잃습니다!";
            }
            if (e.Detail == "rear-setup") return $"{a}, {b}의 뒤로 돌아 기술을 준비합니다.";
            if (e.Type == MatchActionType.PrepareAttack)
                return e.Result is MatchActionResult.Blocked or MatchActionResult.Interrupted
                    ? $"{a}의 {move ?? "결정타"} 준비를 막아내는 {b}! 잡힌 손을 풀며 다음 공격을 끊습니다!"
                    : $"{a}, {b}를 붙잡아 {move ?? "결정타"} 자세를 잡습니다! {b}가 빠져나오려 합니다!";
            if (e.Type == MatchActionType.ExitRing) return $"{a}, 링 밖으로 빠져나가 {b}의 공세를 끊고 숨을 고릅니다!";
            if (e.Type == MatchActionType.Pursue) return $"{a}, 링 밖의 {b}를 쫓아 내려갑니다! 공방이 링사이드로 이어집니다!";
            if (e.Detail == "aerial-setup-lost") return $"{a}, 상대의 자세와 위치를 보고 공중기 준비를 취소합니다! {b}가 반격할 틈을 노립니다!";
            if (e.Detail == "aerial-interrupted") return $"{a}, 코너를 따라 올라가 {b}의 공중기 준비를 끊습니다! 두 선수가 코너 위에서 맞붙습니다!";
            if (e.Detail == "retained-grip") return $"{a}, 손을 풀지 않고 쓰러진 {b}를 다시 일으킵니다! 다음 공격을 이어갈 자세입니다!";
            if (e.Detail == "crowd-pause") return $"방금 충돌에 관중의 함성이 터집니다! {a}도 숨을 고르며 다음 공격을 준비합니다.";
            if (e.Type is MatchActionType.IrishWhip or MatchActionType.CornerWhip)
            {
                var destination = e.Type == MatchActionType.CornerWhip ? "코너" : "로프";
                return e.Result == MatchActionResult.Countered
                    ? $"{a}의 휩을 뒤집는 {b}! 오히려 {a}가 {destination} 쪽으로 달려갑니다!"
                    : $"{a}, {b}의 팔을 잡아 {destination} 쪽으로 보냅니다!";
            }
            if (e.Type == MatchActionType.RopeRun)
                return e.Detail == "rebound" ? $"{a}, 로프의 반동을 타고 {b} 쪽으로 돌아옵니다!" :
                    $"{a}, 로프로 달려가 반동을 준비합니다! {b}에게 달려들려 합니다.";
            if (e.Type == MatchActionType.Evade)
                return e.Detail == "leapfrog" ? $"{a}, 달려오는 {b}를 뛰어넘습니다! {b}가 반대편 로프로 달립니다!" :
                    $"{a}, 몸을 낮춰 {b}를 흘려보냅니다! {b}가 반대편 로프로 달립니다!";
            if (e.Type == MatchActionType.Retreat)
                return e.Detail == "corner-run-up" ? $"{a}, 코너의 {b}에게 돌진하려고 거리를 확보합니다!" :
                    $"{a}, {b}와 거리를 벌리며 호흡을 고릅니다.";
            if (e.Type == MatchActionType.CornerAttack && e.Detail == "missed-charge")
                return $"{b}, 코너 돌진을 옆으로 피합니다! {a}가 턴버클 앞에서 멈칫하며 빈틈을 보입니다!";
            if (e.Type == MatchActionType.Probe) return $"{a}, {move}으로 {b}의 반응을 살핍니다.";
            if (e.Type == MatchActionType.TieUp) return $"{a}와 {b}, {move}으로 힘을 겨룹니다.";
            if (e.Type == MatchActionType.Chain && e.Result == MatchActionResult.Escaped) return $"{a}와 {b}, 손을 풀고 다시 거리를 벌립니다.";
            if (e.Type == MatchActionType.Recover) return e.Detail switch
            {
                "seated" => $"{a}, 상체를 일으켜 앉습니다. 아직 일어서지는 못합니다.",
                "kneeling" => $"{a}, 매트를 짚고 한쪽 무릎을 세웁니다.",
                _ => $"{a}, 몸을 추스르고 다시 일어섭니다."
            };
            if (e.Type == MatchActionType.TurnOver) return e.Detail == "pin-setup"
                ? $"{a}, {b}의 몸을 돌려 어깨가 매트에 닿도록 커버 자세를 만듭니다!"
                : $"{a}, {b}의 몸을 돌려 다음 기술의 자세를 만듭니다!";
            if (e.Type == MatchActionType.Lift) return $"{a}, 쓰러진 {b}를 붙잡아 일으킵니다!";
            if (e.Detail == "delayed-cover") return $"{a}, 곧바로 커버하지 못합니다. {b} 쪽으로 몸을 끌며 다가갑니다!";
            if (e.Detail == "roll-up") return $"{a}, {b}의 틈을 잡아 말아 넣고 바로 커버합니다!";
            if (e.Type == MatchActionType.Rest) return e.Detail == "referee-recovery" ? "심판이 몸을 추스르는 동안 두 선수가 간격을 벌립니다." : $"{a}, 호흡을 고르며 힘을 비축합니다.";
            if (e.Type == MatchActionType.Reposition) return e.Detail switch
            {
                "corner" => $"{a}, 동료가 기다리는 코너로 접근합니다!",
                "throw-out" => $"{a}, {b}를 링 밖으로 몰아냅니다! 심판이 카운트를 시작합니다.",
                _ => $"{a}, {b}와의 공방을 링 중앙으로 옮깁니다."
            };
            if (e.Type == MatchActionType.Tag) return $"{a}, {Name(e.PartnerId)}에게 태그합니다!" + (e.Detail == "hot-tag" ? " 기다리던 동료가 뛰어들며 반격에 나섭니다!" : " 새로운 선수가 공방을 이어갑니다.");
            if (e.Type == MatchActionType.Climb) return $"{a}, {b}의 위치를 확인하고 코너 위로 올라갑니다!";
            if (e.Type == MatchActionType.Taunt)
            {
                var gesture = e.KayfabeAction switch
                {
                    "showboat" => $"{a}, 팔을 펼쳐 자신감을 드러냅니다!",
                    "mock" => $"{a}, {b}를 가리키며 조롱합니다!",
                    "finisher-signal" => $"{a}, 결정타를 예고하는 몸짓을 합니다!",
                    "stall" => $"{a}, 손짓하며 공방을 늦추고 숨을 고릅니다.",
                    _ => $"{a}, {b}에게 다시 덤비라는 손짓을 합니다!"
                };
                return gesture + (e.Detail switch
                {
                    "taunt-recover" => $" 그 사이 {b}가 몸을 추스릅니다.",
                    "taunt-counter" => $" {b}가 틈을 보고 파고들어 반격을 준비합니다!",
                    "taunt-approach" => $" {b}가 거리를 좁히며 다음 공격을 노립니다.",
                    _ => ""
                });
            }
            if (e.Type == MatchActionType.ObjectSetup) return $"{a}, 승부에 사용할 구조물을 준비합니다.";
            if (e.Type == MatchActionType.ObjectClimb) return $"{a}, 구조물을 이용해 승리 조건에 접근합니다! {b}가 저지할 수 있을까요?";
            if (e.Type == MatchActionType.ObjectFinish) return $"{b}, 마지막 순간 {a}의 시도를 저지합니다!";
            if (e.Type == MatchActionType.BreakUp) return $"{a}, 뛰어들어 {b}의 시도를 끊습니다!";
            if (e.Type == MatchActionType.RopeBreak) return $"{a}, 로프를 붙잡습니다! 심판의 지시에 {b}가 풀어줍니다.";
            if (e.Type == MatchActionType.Cover) return $"{a}, 공격을 연결한 뒤 {b}의 어깨를 누르며 커버합니다!";
            if (e.Type == MatchActionType.Pin) return e.Result switch
            {
                MatchActionResult.NearFall => $"하나, 둘…! {b}, 셋 직전에 어깨를 듭니다!",
                MatchActionResult.TwoCount => $"하나, 둘! {b}가 어깨를 들어 카운트를 끊습니다.",
                MatchActionResult.Interrupted => "심판이 카운트할 수 없습니다! 커버가 무산됩니다.",
                _ => $"하나! {b}, 빠르게 어깨를 들어 올립니다."
            };
            if (e.Type == MatchActionType.Hold)
            {
                if (e.Detail == "count-out") return $"심판의 카운트가 이어집니다. {b}가 아직 링 밖에 있습니다!";
                if (e.Result == MatchActionResult.Escaped) return $"{a}, 몸을 비틀어 {b}의 {move ?? "홀드"}에서 빠져나옵니다!";
                return e.Detail == "reach-ropes" ? $"{b}, {move ?? "홀드"}에 갇힌 채 로프를 향해 움직입니다!" : $"{a}, {WithObject(move ?? "홀드")} 유지하며 압박합니다. {b}가 버팁니다!";
            }
            var action = move ?? (e.Type switch
            {
                MatchActionType.BasicStrike => "짧은 타격", MatchActionType.HeavyStrike => "강타", MatchActionType.Grapple => "수플렉스",
                MatchActionType.GroundAttack => "그라운드 공격", MatchActionType.LimbAttack => $"{region} 공략", MatchActionType.Submission => "서브미션",
                MatchActionType.Dive => "공중 공격", MatchActionType.Signature => "특기 기술", MatchActionType.Finisher => "결정타",
                MatchActionType.Counter => "반격", MatchActionType.DoubleTeam => "합동 공격", _ => "공격"
            });

            if (e.Result == MatchActionResult.Countered) return $"{a}의 {action}! {b}가 읽고 되받아칠 자세를 잡습니다!";
            if (e.Result == MatchActionResult.ExecutionFailure) return $"{a}, {WithObject(action)} 시도하지만 자세가 무너져 연결하지 못합니다!";
            if (e.Result == MatchActionResult.ReceptionFailure) return $"{a}, {WithObject(action)} 연결합니다. 완전히 힘이 실리지는 않았습니다.";
            if (e.Type == MatchActionType.DoubleTeam) return $"{a}와 {Name(e.PartnerId)}, {b}에게 {WithObject(action)} 연결합니다!";
            var down = e.Before?.Participants.FirstOrDefault(x => x.Id == e.ReceiverId)?.IsDowned == false &&
                e.After?.Participants.FirstOrDefault(x => x.Id == e.ReceiverId)?.IsDowned == true;
            var attackLine = e.Type == MatchActionType.StandingHold ? $"{a}, 서 있는 {b}를 {WithObject(action)} 붙잡아 압박합니다!" :
                e.Type == MatchActionType.RingsideAttack ? $"{a}, 링 밖의 {b}에게 {WithObject(action)} 연결합니다!" :
                e.Type == MatchActionType.TopRopeAttack ? $"코너 위에서 맞붙은 두 선수! {a}, {b}에게 {WithObject(action)} 연결하며 함께 매트로 떨어집니다!" :
                e.Detail == "transition" ? $"{a}, 앞선 공방에 이어 {b}에게 {WithObject(action)} 전환합니다!" :
                e.Detail == "grip-follow-up" ? $"{a}, 손을 풀지 않은 채 다시 {WithObject(action)} 연결합니다!" :
                e.Type == MatchActionType.RunningAttack
                ? e.Detail == "rebound-catch" ? $"{a}, 로프에서 돌아오는 {b}에게 {WithObject(action)} 연결합니다!" :
                    $"{a}, 반동을 타고 달려들어 {b}에게 {WithObject(action)} 연결합니다!"
                : e.Type == MatchActionType.CornerAttack
                    ? e.Detail == "corner-chops" ? $"{a}, 코너의 {b}에게 {WithObject(action)} 연결합니다!" :
                        $"{a}, 코너로 돌진해 {b}에게 {WithObject(action)} 연결합니다!"
                    : $"{a}, {b}에게 {WithObject(action)} 연결합니다!";
            return attackLine + (down ? $" {b}가 쓰러집니다!" : "");
        }
    }
}
