using System;
using System.Collections.Generic;
using System.Linq;

namespace PWManager.Domain.Services
{
    public enum MoveSituation
    {
        Automatic, Probe, TieUp, Chain, StandingStrike, StandingHeavyStrike, StandingGrapple,
        StandingLimb, GroundStrike, GroundLimb, GroundSubmission, StandingSubmission,
        DivingStanding, DivingGrounded, RunningStanding, RunningGrounded, CornerStanding,
        RingsideStanding, RingsideGrounded, TopRopeGrapple, DoubleTeamStanding, DoubleTeamGrounded,
        StandingRearStrike, StandingRearGrapple, StandingRearSubmission, GroundProne,
        GroundSeated, GroundKneeling, RunningSeated, RunningKneeling, RunningRebound,
        CornerRear, CornerSeated, CornerHanging, TopRopeRearGrapple, DivingOutsideStanding,
        DivingOutsideGrounded, RopeLeaning, RopeHung, SpringboardStanding, SpringboardGrounded,
        SpringboardOutsideStanding, SpringboardOutsideGrounded, RingsideDive,
        ApronBoth, ApronToRing, RingToApron, ApronToOutside, OutsideToApron, Carry,
        Ladder, Table, Cage, RunningRearStanding
    }

    // General use and personal assignment share the same technique identity.
    public sealed class MatchTechnique
    {
        public string Id { get; }
        public MatchActionType Action { get; }
        public bool GroundedTarget { get; }
        public MatchMoveRules Rules { get; }
        public string Key, EnglishName;
        public string[] AllowedStyleIds = Array.Empty<string>();
        public bool Enabled = true, CanUseGenerally = true, CanBeSignature, CanBeFinisher;
        public string RequiredStat = "None";
        public float RequiredStatValue;

        public bool AllowsStyle(string styleId) => AllowedStyleIds.Length == 0 ||
            string.IsNullOrEmpty(styleId) || AllowedStyleIds.Contains(styleId);

        public bool MeetsRequirement(Models.WrestlerAttributesState a) => RequiredStat switch
        {
            "Brawling" => a.Brawling >= RequiredStatValue, "Power" => a.Power >= RequiredStatValue,
            "Technical" => a.Technical >= RequiredStatValue, "HighFlying" => a.HighFlying >= RequiredStatValue,
            _ => true
        };

        public MatchTechnique(string id, string name, MatchActionType action, bool grounded, MoveKind kind, BodyRegion region, bool powerBased = false)
        {
            Id = id; Action = action; GroundedTarget = grounded;
            Rules = new MatchMoveRules { Name = name, Kind = kind, TargetRegion = region,
                ExecutionDifficulty = 6, SellingDifficulty = 7 };
            if (powerBased || action == MatchActionType.Finisher) Rules.PowerWeight = 1;
            else if (kind == MoveKind.Aerial) Rules.HighFlyingWeight = 1;
            else if (kind is MoveKind.Grapple or MoveKind.Submission || action == MatchActionType.LimbAttack) Rules.TechnicalWeight = 1;
            else Rules.BrawlingWeight = 1;
        }
    }

    public static class MatchTechniques
    {
        private static readonly MatchTechnique[] techniques =
        {
            new("circling", "서클링", MatchActionType.Probe, false, MoveKind.Grapple, BodyRegion.Body),
            new("hand-fight", "손싸움", MatchActionType.Probe, false, MoveKind.Grapple, BodyRegion.Body),
            new("tie-up", "칼라 앤 엘보 타이업", MatchActionType.TieUp, false, MoveKind.Grapple, BodyRegion.Body),
            new("chain-reset", "거리 벌리기", MatchActionType.Chain, false, MoveKind.Grapple, BodyRegion.Body),
            new("chain-rollthrough", "롤스루 리스트록", MatchActionType.Chain, false, MoveKind.Grapple, BodyRegion.Body),
            new("chain-switch", "뒤잡기 전환", MatchActionType.Chain, false, MoveKind.Grapple, BodyRegion.Body),
            new("chain-headlock-takedown", "헤드록 테이크다운", MatchActionType.Chain, false, MoveKind.Grapple, BodyRegion.Body),
            new("chain-waistlock-takedown", "웨이스트록 테이크다운", MatchActionType.Chain, false, MoveKind.Grapple, BodyRegion.Body),
            new("chain-headlock", "사이드 헤드록", MatchActionType.Chain, false, MoveKind.Grapple, BodyRegion.Body),
            new("chain-wristlock", "리스트록", MatchActionType.Chain, false, MoveKind.Grapple, BodyRegion.Body),
            new("chain-hammerlock", "해머록", MatchActionType.Chain, false, MoveKind.Grapple, BodyRegion.Body),
            new("chain-waistlock", "웨이스트록", MatchActionType.Chain, false, MoveKind.Grapple, BodyRegion.Body),
            new("chop", "나이프에지 찹", MatchActionType.BasicStrike, false, MoveKind.Strike, BodyRegion.Body),
            new("forearm", "포어암 스매시", MatchActionType.BasicStrike, false, MoveKind.Strike, BodyRegion.Head),
            new("body-kick", "미들 킥", MatchActionType.BasicStrike, false, MoveKind.Strike, BodyRegion.Body),
            new("clothesline", "클로스라인", MatchActionType.HeavyStrike, false, MoveKind.Strike, BodyRegion.Head),
            new("big-boot", "빅 부트", MatchActionType.HeavyStrike, false, MoveKind.Strike, BodyRegion.Head),
            new("knee-lift", "니 리프트", MatchActionType.HeavyStrike, false, MoveKind.Strike, BodyRegion.Body),
            new("body-slam", "보디슬램", MatchActionType.Grapple, false, MoveKind.Grapple, BodyRegion.Body),
            new("snap-suplex", "스냅 수플렉스", MatchActionType.Grapple, false, MoveKind.Grapple, BodyRegion.Body),
            new("snapmare", "스냅메어", MatchActionType.Grapple, false, MoveKind.Grapple, BodyRegion.Body),
            new("stomp", "복부 스톰프", MatchActionType.GroundAttack, true, MoveKind.Strike, BodyRegion.Body),
            new("elbow-drop", "엘보드롭", MatchActionType.GroundAttack, true, MoveKind.Strike, BodyRegion.Body),
            new("knee-drop", "니 드롭", MatchActionType.GroundAttack, true, MoveKind.Strike, BodyRegion.Body),
            new("dragon-screw", "드래곤 스크루", MatchActionType.LimbAttack, false, MoveKind.Grapple, BodyRegion.Legs),
            new("low-kick", "로 킥", MatchActionType.LimbAttack, false, MoveKind.Strike, BodyRegion.Legs),
            new("leg-stomp", "무릎 스톰프", MatchActionType.LimbAttack, true, MoveKind.Strike, BodyRegion.Legs),
            new("leg-elbow", "무릎 엘보드롭", MatchActionType.LimbAttack, true, MoveKind.Strike, BodyRegion.Legs),
            new("arm-wringer", "암 링거", MatchActionType.LimbAttack, false, MoveKind.Strike, BodyRegion.Arms),
            new("arm-stomp", "팔 스톰프", MatchActionType.LimbAttack, true, MoveKind.Strike, BodyRegion.Arms),
            new("head-elbow", "백 엘보", MatchActionType.LimbAttack, false, MoveKind.Strike, BodyRegion.Head),
            new("head-knee", "머리 니 드롭", MatchActionType.LimbAttack, true, MoveKind.Strike, BodyRegion.Head),
            new("body-knee", "복부 니 스트라이크", MatchActionType.LimbAttack, false, MoveKind.Strike, BodyRegion.Body),
            new("body-elbow", "복부 엘보드롭", MatchActionType.LimbAttack, true, MoveKind.Strike, BodyRegion.Body),
            new("boston-crab", "보스턴 크랩", MatchActionType.Submission, true, MoveKind.Submission, BodyRegion.Legs),
            new("single-leg-crab", "싱글 레그 크랩", MatchActionType.Submission, true, MoveKind.Submission, BodyRegion.Legs),
            new("armbar", "암바", MatchActionType.Submission, true, MoveKind.Submission, BodyRegion.Arms),
            new("kimura", "기무라 록", MatchActionType.Submission, true, MoveKind.Submission, BodyRegion.Arms),
            new("chinlock", "리어 친록", MatchActionType.Submission, true, MoveKind.Submission, BodyRegion.Head),
            new("ground-sleeper", "그라운드 슬리퍼 홀드", MatchActionType.Submission, true, MoveKind.Submission, BodyRegion.Head),
            new("body-scissors", "보디 시저스", MatchActionType.Submission, true, MoveKind.Submission, BodyRegion.Body),
            new("diving-elbow", "다이빙 엘보드롭", MatchActionType.Dive, true, MoveKind.Aerial, BodyRegion.Body),
            new("diving-splash", "다이빙 스플래시", MatchActionType.Dive, true, MoveKind.Aerial, BodyRegion.Body),
            new("double-suplex", "더블 수플렉스", MatchActionType.DoubleTeam, false, MoveKind.Grapple, BodyRegion.Body),
            new("double-elbow", "더블 엘보드롭", MatchActionType.DoubleTeam, true, MoveKind.Strike, BodyRegion.Body),
            new("uppercut", "유러피언 어퍼컷", MatchActionType.BasicStrike, false, MoveKind.Strike, BodyRegion.Head),
            new("back-elbow", "백 엘보", MatchActionType.BasicStrike, false, MoveKind.Strike, BodyRegion.Head),
            new("body-punch", "복부 펀치", MatchActionType.BasicStrike, false, MoveKind.Strike, BodyRegion.Body),
            new("dropkick", "드롭킥", MatchActionType.HeavyStrike, false, MoveKind.Strike, BodyRegion.Body),
            new("superkick", "슈퍼킥", MatchActionType.HeavyStrike, false, MoveKind.Strike, BodyRegion.Head),
            new("discus-elbow", "디스커스 엘보", MatchActionType.HeavyStrike, false, MoveKind.Strike, BodyRegion.Head),
            new("hip-toss", "힙 토스", MatchActionType.Grapple, false, MoveKind.Grapple, BodyRegion.Body),
            new("arm-drag", "암 드래그", MatchActionType.Grapple, false, MoveKind.Grapple, BodyRegion.Arms),
            new("belly-to-belly", "벨리 투 벨리 수플렉스", MatchActionType.Grapple, false, MoveKind.Grapple, BodyRegion.Body, true),
            new("back-suplex", "백 수플렉스", MatchActionType.Grapple, false, MoveKind.Grapple, BodyRegion.Body),
            new("gutbuster", "것버스터", MatchActionType.Grapple, false, MoveKind.Grapple, BodyRegion.Body, true),
            new("neckbreaker", "넥브레이커", MatchActionType.Grapple, false, MoveKind.Grapple, BodyRegion.Head),
            new("sidewalk-slam", "사이드워크 슬램", MatchActionType.Grapple, false, MoveKind.Grapple, BodyRegion.Body, true),
            new("vertical-suplex", "버티컬 수플렉스", MatchActionType.Grapple, false, MoveKind.Grapple, BodyRegion.Body, true),
            new("senton", "센턴", MatchActionType.GroundAttack, true, MoveKind.Strike, BodyRegion.Body),
            new("leg-drop", "레그드롭", MatchActionType.GroundAttack, true, MoveKind.Strike, BodyRegion.Head),
            new("fist-drop", "피스트드롭", MatchActionType.GroundAttack, true, MoveKind.Strike, BodyRegion.Head),
            new("arm-breaker", "암브레이커", MatchActionType.LimbAttack, false, MoveKind.Grapple, BodyRegion.Arms),
            new("arm-knee", "팔 니 드롭", MatchActionType.LimbAttack, true, MoveKind.Strike, BodyRegion.Arms),
            new("shin-breaker", "신브레이커", MatchActionType.LimbAttack, false, MoveKind.Grapple, BodyRegion.Legs),
            new("leg-twist", "누운 다리 비틀기", MatchActionType.LimbAttack, true, MoveKind.Strike, BodyRegion.Legs),
            new("figure-four", "피겨 포 레그록", MatchActionType.Submission, true, MoveKind.Submission, BodyRegion.Legs),
            new("crossface", "크로스페이스", MatchActionType.Submission, true, MoveKind.Submission, BodyRegion.Head),
            new("abdominal-stretch", "앱도미널 스트레치", MatchActionType.StandingHold, false, MoveKind.Submission, BodyRegion.Body),
            new("bearhug", "베어허그", MatchActionType.StandingHold, false, MoveKind.Submission, BodyRegion.Body, true),
            new("standing-sleeper", "스탠딩 슬리퍼 홀드", MatchActionType.StandingHold, false, MoveKind.Submission, BodyRegion.Head),
            new("diving-crossbody", "다이빙 크로스보디", MatchActionType.Dive, false, MoveKind.Aerial, BodyRegion.Body),
            new("diving-double-axe", "다이빙 더블 액스 핸들", MatchActionType.Dive, false, MoveKind.Aerial, BodyRegion.Head),
            new("diving-leg-drop", "다이빙 레그드롭", MatchActionType.Dive, true, MoveKind.Aerial, BodyRegion.Head),
            new("running-clothesline", "러닝 클로스라인", MatchActionType.RunningAttack, false, MoveKind.Strike, BodyRegion.Head),
            new("running-forearm", "러닝 포어암", MatchActionType.RunningAttack, false, MoveKind.Strike, BodyRegion.Head),
            new("shoulder-block", "숄더 블록", MatchActionType.RunningAttack, false, MoveKind.Strike, BodyRegion.Body, true),
            new("running-dropkick", "러닝 드롭킥", MatchActionType.RunningAttack, false, MoveKind.Strike, BodyRegion.Body),
            new("running-powerslam", "스냅 파워슬램", MatchActionType.RunningAttack, false, MoveKind.Grapple, BodyRegion.Body, true),
            new("back-body-drop", "백 보디드롭", MatchActionType.RunningAttack, false, MoveKind.Grapple, BodyRegion.Body, true),
            new("running-elbow-drop", "러닝 엘보드롭", MatchActionType.RunningAttack, true, MoveKind.Strike, BodyRegion.Body),
            new("running-senton", "러닝 센턴", MatchActionType.RunningAttack, true, MoveKind.Strike, BodyRegion.Body),
            new("corner-clothesline", "코너 클로스라인", MatchActionType.CornerAttack, false, MoveKind.Strike, BodyRegion.Head),
            new("corner-splash", "코너 스플래시", MatchActionType.CornerAttack, false, MoveKind.Strike, BodyRegion.Body, true),
            new("corner-knee", "코너 니 스트라이크", MatchActionType.CornerAttack, false, MoveKind.Strike, BodyRegion.Body),
            new("ringside-forearm", "링사이드 포어암", MatchActionType.RingsideAttack, false, MoveKind.Strike, BodyRegion.Head),
            new("barricade-slam", "방호벽에 밀어붙이기", MatchActionType.RingsideAttack, false, MoveKind.Strike, BodyRegion.Body, true),
            new("ringside-stomp", "링사이드 스톰프", MatchActionType.RingsideAttack, true, MoveKind.Strike, BodyRegion.Body),
            new("ringside-elbow", "링사이드 엘보드롭", MatchActionType.RingsideAttack, true, MoveKind.Strike, BodyRegion.Body),
            new("superplex", "슈퍼플렉스", MatchActionType.TopRopeAttack, false, MoveKind.Grapple, BodyRegion.Body),
            new("avalanche-hip-toss", "코너 위 힙 토스", MatchActionType.TopRopeAttack, false, MoveKind.Grapple, BodyRegion.Body),
            new("finishing-lariat", "래리어트", MatchActionType.Finisher, false, MoveKind.Strike, BodyRegion.Head),
            new("finishing-powerslam", "파워슬램", MatchActionType.Finisher, false, MoveKind.Grapple, BodyRegion.Body),
            new("finishing-senton", "센턴", MatchActionType.Finisher, true, MoveKind.Strike, BodyRegion.Body),
            new("finishing-elbow", "점핑 엘보드롭", MatchActionType.Finisher, true, MoveKind.Strike, BodyRegion.Body)
        };

        // ponytail: preserve the legacy pool for standalone callers until every host supplies the table.
        private static IReadOnlyList<MatchTechnique> configured;
        public static IReadOnlyList<MatchTechnique> All => configured ?? techniques;

        public static void Configure(IReadOnlyList<MatchTechnique> values) => configured = values ?? throw new ArgumentNullException(nameof(values));

        public static bool IsSupported(MoveSituation situation) => situation > MoveSituation.Automatic && situation <= MoveSituation.GroundKneeling ||
            IsOutsideDive(situation) || situation is MoveSituation.Table or MoveSituation.RunningRearStanding;

        public static bool IsRear(MoveSituation situation) => situation is MoveSituation.StandingRearStrike or
            MoveSituation.StandingRearGrapple or MoveSituation.StandingRearSubmission or MoveSituation.RunningRearStanding;

        public static bool IsRopeDive(MoveSituation situation) => situation is MoveSituation.RingsideDive or
            MoveSituation.SpringboardOutsideStanding or MoveSituation.SpringboardOutsideGrounded;

        public static bool IsOutsideDive(MoveSituation situation) => IsRopeDive(situation) ||
            situation is MoveSituation.DivingOutsideStanding or MoveSituation.DivingOutsideGrounded;

        public static bool IsGrounded(MoveSituation situation) => situation is MoveSituation.GroundStrike or MoveSituation.GroundLimb or
            MoveSituation.GroundSubmission or MoveSituation.DivingGrounded or MoveSituation.RunningGrounded or
            MoveSituation.RingsideGrounded or MoveSituation.DoubleTeamGrounded or MoveSituation.GroundProne or MoveSituation.GroundSeated or MoveSituation.GroundKneeling or
            MoveSituation.DivingOutsideGrounded or MoveSituation.SpringboardOutsideGrounded;

        public static bool FitsSituation(MoveSituation situation, MatchEngineParticipant actor, MatchEngineParticipant target)
        {
            if (situation == MoveSituation.Automatic) return true;
            if (IsGrounded(situation) != target.IsDowned) return false;
            if (IsOutsideDive(situation)) return MatchPhysics.InRing(actor) && target.Position == MatchPosition.Outside &&
                (!IsRopeDive(situation) || actor.Position != MatchPosition.TopRope);
            if (situation is MoveSituation.RingsideStanding or MoveSituation.RingsideGrounded or MoveSituation.Table)
                return actor.Position == MatchPosition.Outside && target.Position == MatchPosition.Outside;
            if (!MatchPhysics.InRing(actor) || !MatchPhysics.InRing(target)) return false;
            if (IsRear(situation)) return MatchMovement.IsBehind(actor, target);
            return situation switch
            {
                MoveSituation.GroundProne => target.Posture == MatchPosture.Prone,
                MoveSituation.GroundSeated => target.Posture == MatchPosture.Seated,
                MoveSituation.GroundKneeling => target.Posture == MatchPosture.Kneeling,
                _ => true
            };
        }

        public static bool FitsSituation(MatchMoveRules move, MatchEngineParticipant actor, MatchEngineParticipant target)
        {
            if (!FitsSituation(move.Situation, actor, target)) return false;
            bool flatTarget = move.MotionProfile == MoveMotionProfile.GroundDrop || move.Situation is
                MoveSituation.RunningGrounded or MoveSituation.DivingGrounded or MoveSituation.DoubleTeamGrounded;
            // Legacy callers set IsDowned before movement normalizes Standing to Supine.
            if (flatTarget && target.Posture is MatchPosture.Seated or MatchPosture.Kneeling) return false;
            return move.MotionProfile != MoveMotionProfile.GroundDrop && move.Situation != MoveSituation.RunningGrounded ||
                !MatchMovement.CanReachRopes(target);
        }

        public static MatchTechnique Find(string id) => string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(x => x.Id == id || x.Key == id);

        public static MatchTechnique Find(MatchEngineEvent e) => e == null ? null :
            Find(string.IsNullOrEmpty(e.TechniqueId) ? e.MoveId : e.TechniqueId);

        public static bool CanExecute(MatchMoveRules move, MatchEngineParticipant actor) => MatchHealth.CanUse(move, actor) &&
            (move == null || !IsOutsideDive(move.Situation) || actor.Attributes.HighFlying >= 10 && actor.Fatigue < 75);

        public static List<MatchTechnique> Candidates(MatchActionType action, MatchEngineParticipant actor, MatchEngineParticipant target) =>
            All.Where(x => x.Enabled && x.CanUseGenerally && (x.Rules.Situation == MoveSituation.Automatic || IsSupported(x.Rules.Situation)) &&
                x.Action == action && x.GroundedTarget == target.IsDowned && FitsSituation(x.Rules, actor, target) && x.AllowsStyle(actor.WrestlingStyleId) && x.MeetsRequirement(actor.Attributes) &&
                (action is not (MatchActionType.LimbAttack or MatchActionType.Submission) || x.Rules.TargetRegion == actor.TargetRegion) &&
                CanExecute(x.Rules, actor) && MatchHealth.CanReceive(x.Rules, target)).ToList();

        public static MatchTechnique Select(MatchEngineState state, MatchActionType action, MatchEngineParticipant actor,
            MatchEngineParticipant target, Random random)
        {
            var candidates = Candidates(action, actor, target);
            if (candidates.Count == 0) throw new InvalidOperationException($"No general technique fits {action} and the target posture.");
            var last = state.Events.LastOrDefault(x => x.PerformerId == actor.Id && !string.IsNullOrEmpty(x.TechniqueId))?.TechniqueId;
            // Attribute weights give the shared pool a style without duplicating wrestler catalogs.
            var weights = candidates.Select(x => Math.Pow(2 + actor.Attributes.Brawling * x.Rules.BrawlingWeight +
                actor.Attributes.Power * x.Rules.PowerWeight + actor.Attributes.Technical * x.Rules.TechnicalWeight +
                actor.Attributes.HighFlying * x.Rules.HighFlyingWeight, 2) *
                (IsRear(x.Rules.Situation)
                    ? 1 + actor.Attributes.RingPsychology * .15f : 1) *
                (candidates.Count > 1 && (x.Id == last || x.Key == last) && state.Exchange?.Kind is not (ExchangeKind.GripCombo or ExchangeKind.Hold or ExchangeKind.Chain)
                    ? MatchPhysics.Damage(target, actor.TargetRegion) >= 60 ? .25 : .9 : 1)).ToArray();
            return candidates[PerformerSelector.WeightedIndex(weights, random)];
        }
    }
}
