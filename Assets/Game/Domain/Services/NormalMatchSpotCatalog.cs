using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public sealed class NormalMatchSpotDefinition
    {
        public readonly string Id, Name, Text, RequiredGimmickId;
        public readonly MatchSpotPhase Phase;
        public readonly bool TeamOnly;

        public NormalMatchSpotDefinition(string id, string name, MatchSpotPhase phase, string text,
            bool teamOnly = false, string gimmick = null)
        { Id = id; Name = name; Phase = phase; Text = text; TeamOnly = teamOnly; RequiredGimmickId = gimmick; }
    }

    public static class NormalMatchSpotCatalog
    {
        public static readonly IReadOnlyList<NormalMatchSpotDefinition> All = Array.AsReadOnly(new[]
        {
            D("normal_001", "열세에서 대반격", MatchSpotPhase.Middle, "{actor}, {target}의 공세를 버텨내고 연속 반격으로 흐름을 뒤집습니다!"),
            D("normal_002", "끝나지 않는 승부", MatchSpotPhase.Late, "{actor}, {target}의 결정적인 공격을 맞고도 셋 직전에 어깨를 듭니다!"),
            D("normal_003", "피니셔 역이용", MatchSpotPhase.Late, "{target}의 피니셔가 나오는 순간 {actor}가 자세를 뒤집어 반격합니다!"),
            D("normal_004", "링 밖으로 몸을 던지다", MatchSpotPhase.Middle, "{actor}, 로프 사이로 몸을 날려 링 밖의 {target}에게 뛰어듭니다!"),
            D("normal_005", "코너 위의 대격돌", MatchSpotPhase.Late, "코너 위에서 버티는 두 선수! {actor}가 {target}을 붙잡고 함께 매트로 떨어집니다!"),
            D("normal_006", "쓰러져도 다시 일어서다", MatchSpotPhase.Middle, "{target}의 강타에도 {actor}가 다시 일어나 정면으로 맞섭니다!"),
            D("normal_007", "로프까지 마지막 한 뼘", MatchSpotPhase.Late, "{target}의 서브미션에 갇힌 {actor}! 온 힘을 짜내 로프를 붙잡습니다!"),
            D("normal_008", "집요한 약점 공략", MatchSpotPhase.Early, "{actor}, {target}의 한 부위를 집요하게 공략하며 움직임을 둔하게 만듭니다!"),
            D("normal_009", "압도적인 힘의 과시", MatchSpotPhase.Middle, "{actor}, 버티는 {target}을 들어 올려 한순간에 매트로 내리꽂습니다!"),
            D("normal_010", "도발이 부른 역습", MatchSpotPhase.Early, "{target}이 관중에게 도발하는 사이 {actor}가 빈틈을 놓치지 않습니다!"),
            D("normal_011", "극적인 핫 태그", MatchSpotPhase.Middle, "고립됐던 {actor}, 마지막 순간 동료에게 태그합니다! 반격이 시작됩니다!", true),
            D("normal_012", "호흡을 맞춘 합동 공격", MatchSpotPhase.Middle, "{actor}와 동료가 타이밍을 맞춰 {target}에게 합동 공격을 터뜨립니다!", true),
            D("normal_013", "동료의 패배를 막다", MatchSpotPhase.Late, "{actor}가 패배할 순간 동료가 뛰어들어 {target}의 커버를 끊습니다!", true),
            D("normal_014", "사다리 정상의 공방", MatchSpotPhase.Late, "사다리를 오른 {actor}! {target}이 반대편으로 올라와 정상을 두고 다툽니다!", gimmick: "gimmick_002"),
            D("normal_015", "테이블 파괴 직전의 탈출", MatchSpotPhase.Late, "{target}이 {actor}를 테이블로 내리꽂으려 하지만 마지막 순간 빠져나옵니다!", gimmick: "gimmick_003"),
            D("normal_016", "철창 위 탈출 저지", MatchSpotPhase.Late, "철창을 오르는 {actor}! {target}이 쫓아 올라와 탈출을 막습니다!", gimmick: "gimmick_001")
        });

        private static NormalMatchSpotDefinition D(string id, string name, MatchSpotPhase phase, string text,
            bool teamOnly = false, string gimmick = null) => new(id, name, phase, text, teamOnly, gimmick);

        public static List<NormalMatchSpotDefinition> Available(MatchPlanState match, MatchSpotPhase phase, bool teamMatch) =>
            All.Where(x => x.Phase == phase && (!x.TeamOnly || teamMatch) &&
                (x.RequiredGimmickId == null || x.RequiredGimmickId == match.MatchGimmickId) &&
                (!(x.Id == "normal_002" || x.Id == "normal_007" || x.Id == "normal_013") ||
                 !(match.MatchGimmickId == "gimmick_002" || match.MatchGimmickId == "gimmick_003"))).ToList();
    }
}
