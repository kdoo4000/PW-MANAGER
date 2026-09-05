using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public enum SpotSkill { Technical, Brawling, Power, HighFlying, Psychology, Specialty, Comedy }
    public enum SpotActorScope { Participant, Outsider }
    public enum SpotRelationship { Any, Opponents, Teammates }

    public sealed class MatchSpotDefinition
    {
        public readonly string Id, Name, Category, ActorLabel, TargetLabel;
        public readonly float Difficulty, BasePoint;
        public readonly SpotSkill Skill;
        public readonly SpotActorScope ActorScope;
        public readonly SpotRelationship Relationship;
        public readonly SpotScriptedEffect Effect;
        public readonly IReadOnlyList<MatchSpotPhase> Phases;
        public readonly IReadOnlyList<string> Variants;
        public bool RequiresPartner => false;
        public string RequiredGimmickId => null;

        public MatchSpotDefinition(string id, string name, string category, float difficulty, float basePoint,
            SpotSkill skill, SpotActorScope actorScope, SpotRelationship relationship, SpotScriptedEffect effect,
            MatchSpotPhase[] phases, string actorLabel, string targetLabel, string first, string second)
        {
            Id = id; Name = name; Category = category; Difficulty = difficulty; BasePoint = basePoint; Skill = skill;
            ActorScope = actorScope; Relationship = relationship; Effect = effect; Phases = Array.AsReadOnly(phases);
            ActorLabel = actorLabel; TargetLabel = targetLabel; Variants = Array.AsReadOnly(new[] { first, second });
        }
    }

    public static class SpecialMatchSpotCatalog
    {
        private static readonly MatchSpotPhase[] Entrance = { MatchSpotPhase.Entrance };
        private static readonly MatchSpotPhase[] During = { MatchSpotPhase.Early, MatchSpotPhase.Middle, MatchSpotPhase.Late };
        private static readonly MatchSpotPhase[] MiddleLate = { MatchSpotPhase.Middle, MatchSpotPhase.Late };
        private static readonly MatchSpotPhase[] After = { MatchSpotPhase.PostMatch };

        public static readonly IReadOnlyList<MatchSpotDefinition> All = Array.AsReadOnly(new[]
        {
            D("spot_001", "입장 중 상대의 뒤치기", "입장", 9, .40f, SpotSkill.Brawling, SpotActorScope.Participant, SpotRelationship.Opponents, SpotScriptedEffect.None, Entrance, "습격자", "피습자", "{target}의 입장 도중 {actor}가 뒤에서 덮칩니다!", "{actor}, 링에 도착하기도 전인 {target}을 기습합니다!"),
            D("spot_002", "제3자의 입장로 습격", "입장", 11, .45f, SpotSkill.Brawling, SpotActorScope.Outsider, SpotRelationship.Any, SpotScriptedEffect.None, Entrance, "제3자", "피습자", "{target}의 입장 음악이 흐르는 순간 {actor}가 뒤에서 습격합니다!", "관중석에서 튀어나온 {actor}가 입장 중인 {target}을 공격합니다!"),
            D("spot_003", "백스테이지 습격으로 경기 무산", "입장", 12, .50f, SpotSkill.Psychology, SpotActorScope.Outsider, SpotRelationship.Any, SpotScriptedEffect.MatchStopped, Entrance, "습격자", "피습자", "경기 직전 {actor}가 백스테이지에서 {target}을 쓰러뜨립니다. 경기를 시작할 수 없습니다!", "{target}이 입장하지 못합니다! 뒤에서 공격한 {actor} 때문에 경기가 무산됩니다!"),
            D("spot_004", "제3자 난입으로 경기 중단", "난입", 13, .60f, SpotSkill.Psychology, SpotActorScope.Outsider, SpotRelationship.Any, SpotScriptedEffect.MatchStopped, During, "난입자", "피습자", "{actor}가 링에 난입해 {target}을 공격합니다! 심판이 경기를 중단시킵니다!", "경기와 관계없는 {actor}가 뛰어들어 {target}을 덮칩니다. 더는 경기를 진행할 수 없습니다!"),
            D("spot_005", "심판 오폭 후 반칙", "심판 사고", 12, .50f, SpotSkill.Psychology, SpotActorScope.Participant, SpotRelationship.Opponents, SpotScriptedEffect.None, MiddleLate, "반칙 선수", "피해 선수", "{actor}의 공격이 실수로 심판에게 적중합니다! 심판이 쓰러진 사이 {actor}가 {target}에게 반칙을 가합니다!", "심판과 충돌이 일어났습니다! 쓰러진 심판의 눈을 피해 {actor}가 {target}을 공격합니다!"),
            D("spot_006", "제3자의 심판 유인", "심판 사고", 10, .40f, SpotSkill.Psychology, SpotActorScope.Outsider, SpotRelationship.Any, SpotScriptedEffect.None, During, "유인자", "피해 선수", "{actor}가 심판의 시선을 끕니다! 그 사이 {target}에게 보이지 않는 공격이 이어집니다!", "링 밖의 {actor}가 심판과 실랑이를 벌입니다. {target}은 무방비로 공격당합니다!"),
            D("spot_007", "링 밖에서 발목 잡기", "난입", 8, .35f, SpotSkill.Psychology, SpotActorScope.Outsider, SpotRelationship.Any, SpotScriptedEffect.None, During, "방해자", "피해 선수", "링 밖의 {actor}가 몰래 {target}의 발목을 잡아 넘어뜨립니다!", "심판이 보지 못한 사이 {actor}가 에이프런 아래에서 {target}을 방해합니다!"),
            D("spot_008", "흉기 전달과 기습", "반칙", 14, .60f, SpotSkill.Specialty, SpotActorScope.Outsider, SpotRelationship.Any, SpotScriptedEffect.None, MiddleLate, "조력자", "피해 선수", "{actor}가 몰래 링 안으로 흉기를 건넵니다! {target}이 알아채기 전에 공격이 적중합니다!", "심판의 사각에서 {actor}가 흉기를 전달합니다. {target}에게 비열한 공격이 이어집니다!"),
            D("spot_009", "태그 파트너의 배신", "배신", 13, .60f, SpotSkill.Psychology, SpotActorScope.Participant, SpotRelationship.Teammates, SpotScriptedEffect.None, MiddleLate, "배신자", "배신당한 선수", "{actor}가 같은 팀 {target}을 갑자기 공격합니다! 태그 파트너의 배신입니다!", "태그를 기다리던 {target}에게 {actor}가 공격을 가합니다. 팀이 무너집니다!"),
            D("spot_010", "조력자의 몰래 공격", "난입", 11, .45f, SpotSkill.Brawling, SpotActorScope.Outsider, SpotRelationship.Any, SpotScriptedEffect.None, During, "조력자", "피해 선수", "심판이 돌아선 사이 {actor}가 {target}을 공격하고 링 아래로 숨습니다!", "{actor}가 재빨리 {target}을 가격합니다! 심판은 아무것도 보지 못했습니다!"),
            D("spot_011", "조명 소등 후 괴한 습격", "난입", 15, .65f, SpotSkill.Psychology, SpotActorScope.Outsider, SpotRelationship.Any, SpotScriptedEffect.None, MiddleLate, "습격자", "피습자", "경기장의 불이 꺼집니다! 조명이 돌아오자 {actor}가 쓰러진 {target} 곁에 서 있습니다!", "암전이 끝난 링 안, {actor}가 {target}을 공격하고 모습을 드러냅니다!"),
            D("spot_012", "난입 발각으로 실격", "반칙", 10, .45f, SpotSkill.Psychology, SpotActorScope.Outsider, SpotRelationship.Any, SpotScriptedEffect.Disqualification, During, "난입자", "피해 선수", "{actor}가 {target}을 공격하는 장면을 심판이 봤습니다! 즉시 실격이 선언됩니다!", "심판이 {actor}의 개입을 적발합니다. {target} 쪽의 실격승입니다!"),
            D("spot_013", "승자의 경기 후 추가 공격", "경기 후", 11, .45f, SpotSkill.Brawling, SpotActorScope.Participant, SpotRelationship.Opponents, SpotScriptedEffect.None, After, "공격자", "피습자", "승부가 끝났는데도 {actor}가 {target}을 계속 공격합니다!", "종이 울린 뒤 {actor}가 다시 {target}에게 달려듭니다!"),
            D("spot_014", "패자의 경기 후 보복", "경기 후", 12, .50f, SpotSkill.Brawling, SpotActorScope.Participant, SpotRelationship.Opponents, SpotScriptedEffect.None, After, "공격자", "피습자", "패배를 받아들이지 못한 {actor}가 뒤에서 {target}을 공격합니다!", "퇴장하던 {target}에게 {actor}가 달려들어 경기 후 보복을 가합니다!"),
            D("spot_015", "제3자의 경기 후 습격", "경기 후", 12, .55f, SpotSkill.Psychology, SpotActorScope.Outsider, SpotRelationship.Any, SpotScriptedEffect.None, After, "습격자", "피습자", "경기가 끝난 직후 {actor}가 나타나 {target}을 습격합니다!", "축하할 틈도 없습니다! {actor}가 링에 들어와 {target}을 공격합니다!"),
            D("spot_016", "악수 뒤 배신 공격", "경기 후", 10, .45f, SpotSkill.Psychology, SpotActorScope.Participant, SpotRelationship.Opponents, SpotScriptedEffect.None, After, "배신자", "피습자", "{actor}가 내민 악수를 {target}이 받아들이는 순간, 기습 공격이 이어집니다!", "경기 뒤 악수를 나누던 {actor}, 갑자기 {target}을 끌어당겨 공격합니다!"),
        });

        private static MatchSpotDefinition D(string id, string name, string category, float difficulty, float point,
            SpotSkill skill, SpotActorScope scope, SpotRelationship relationship, SpotScriptedEffect effect,
            MatchSpotPhase[] phases, string actorLabel, string targetLabel, string first, string second) =>
            new(id, name, category, difficulty, point, skill, scope, relationship, effect, phases, actorLabel, targetLabel, first, second);

        public static MatchSpotDefinition Find(string id) => All.FirstOrDefault(x => x.Id == id);
        public static string UnavailableReason(MatchSpotDefinition definition, MatchPlanState match)
        {
            if (definition == null) return "존재하지 않는 스팟입니다.";
            if (definition.Relationship == SpotRelationship.Teammates && !(match.Sides?.Any(x => x?.MemberIds?.Count >= 2) ?? false))
                return "같은 팀 선수 둘이 필요한 경기에서 사용할 수 있습니다.";
            return null;
        }
    }
}
