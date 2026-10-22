using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEditor;
using UnityEngine;

namespace PWManager.Editor
{
    // Invoked through Unity CLI -executeMethod. Never mutates the player's save or static assets.
    public static class MatchEngineAudit
    {
        [Serializable]
        private sealed class Report
        {
            public int GenerationSeed = 9122026, MatchSeed = 731;
            public List<WrestlerState> Wrestlers;
            public MatchResultState Match;
            public List<string> Commentary;
        }

        public static void Run()
        {
            var content = new StaticContentRegistry(AssetDatabase.LoadAssetAtPath<StaticContentCatalog>("Assets/Game/Data/Static/GameStaticContentCatalog.asset"));
            var date = new GameDate(2026, 9, 12);
            var id = 0;
            var generator = new WrestlerGenerator(content, 9122026, () => "audit-wrestler-" + ++id);
            var save = new GameSave { CurrentDate = date, WorldSeed = 9122026 };
            for (var i = 0; i < 2; i++) save.Wrestlers.Add(generator.GenerateCandidate(WrestlerGender.Male, date));
            var plan = new MatchPlanState { Id = "audit-match", MatchTypeId = "matchtype_001", FinishType = MatchFinishType.Pinfall,
                FinishPerformerId = save.Wrestlers[0].Id, LoserTargetId = save.Wrestlers[1].Id, WinningSideId = "a",
                Sides = new() { new() { Id = "a", MemberIds = new() { save.Wrestlers[0].Id } }, new() { Id = "b", MemberIds = new() { save.Wrestlers[1].Id } } } };
            save.MatchPlans.Add(plan);
            save.Shows.Add(new ShowState { Id = "audit-show", ShowVersion = 1, Status = ShowStatus.Confirmed, Date = date });
            save.ShowEvents.Add(new ShowEventState { Id = "audit-event", ShowId = "audit-show", DetailId = plan.Id, EventType = ShowEventType.Match, PlannedDuration = 12 });
            var result = new MatchEvaluator(content, content, () => "audit-result", content.GetMoveRules).EvaluateTechnical(save, "audit-event", 0, 731);
            string Name(string wrestlerId) => save.Wrestlers.FirstOrDefault(x => x.Id == wrestlerId)?.Identity.RingName ?? "선수";
            var commentary = result.EngineState.Events.Select((_, i) => MatchNarrationService.NarrateEngineEvent(result, i, Name)).ToList();
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Reports/MatchEngineV2"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "match.json"), JsonUtility.ToJson(new Report { Wrestlers = save.Wrestlers, Match = result, Commentary = commentary }, true), new UTF8Encoding(false));
            var log = new StringBuilder();
            log.AppendLine("# 임의 생성 선수 경기 로그");
            log.AppendLine("생성 시드 9122026 / 경기 시드 731 / 예약 12분, 첫 선수 핀승");
            foreach (var p in save.Wrestlers)
                log.AppendLine($"- {p.Identity.RingName} ({p.Id}): 타격 {p.Attributes.Brawling:F1}, 힘 {p.Attributes.Power:F1}, 테크닉 {p.Attributes.Technical:F1}, 공중기 {p.Attributes.HighFlying:F1}, 체력 {p.Attributes.Stamina:F1}");
            log.AppendLine();
            for (var i = 0; i < result.EngineState.Events.Count; i++)
            {
                var e = result.EngineState.Events[i];
                if (string.IsNullOrEmpty(commentary[i])) continue;
                log.AppendLine($"{i + 1:000} | {e.MatchTimeSeconds / 60:00}:{e.MatchTimeSeconds % 60:00} | 공방 {e.ExchangeId} | {e.Type}/{e.Result} | {commentary[i]}");
            }
            log.AppendLine($"\n결과: {Name(result.WinnerId)}, {result.FinishType}, {result.EngineState.ElapsedSeconds}초, 평가 {result.FinalMatchQuality:F2}/20");
            foreach (var p in result.EngineState.Participants)
                log.AppendLine($"{Name(p.Id)}: 피로 {p.Fatigue:F1}, 머리 {p.HeadDamage:F1}, 몸통 {p.BodyDamage:F1}, 팔 {p.ArmDamage:F1}, 다리 {p.LegDamage:F1}");
            File.WriteAllText(Path.Combine(directory, "match-log.md"), log.ToString(), new UTF8Encoding(false));
            Debug.Log("Match audit written: " + directory);
        }
    }
}
