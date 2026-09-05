using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public sealed class MatchSpotEditor
    {
        private readonly VisualElement host;
        private readonly GameSave save;
        private readonly MatchPlanState match;
        private readonly GameDate date;
        private readonly List<PlannedSpotState> spots;

        public MatchSpotEditor(VisualElement host, GameSave save, MatchPlanState match, GameDate date)
        {
            this.host = host; this.save = save; this.match = match; this.date = date;
            spots = (match.Spots ?? new List<PlannedSpotState>()).Where(x => x != null).Select(Clone).ToList();
            Render();
        }

        public List<PlannedSpotState> Read()
        {
            var candidate = new MatchPlanState { Sides = match.Sides, MatchGimmickId = match.MatchGimmickId, Spots = spots };
            var errors = MatchSpotEvaluator.Validate(save, candidate).ToList();
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            return spots.Select(Clone).ToList();
        }

        private static PlannedSpotState Clone(PlannedSpotState spot) => new()
        { SpotId = spot.SpotId, Phase = spot.Phase, ActorId = spot.ActorId, TargetId = spot.TargetId, PartnerId = spot.PartnerId };

        private void Render()
        {
            host.Clear();
            var heading = new VisualElement(); heading.AddToClassList("spot-heading");
            heading.Add(new Label("특수 스팟") { name = "match-spots-title" });
            var add = new Button(() =>
            {
                var sides = match.Sides?.Where(x => x?.MemberIds?.Any(id => !string.IsNullOrEmpty(id)) == true).ToList();
                spots.Add(new PlannedSpotState { SpotId = "spot_001", Phase = MatchSpotPhase.Entrance,
                    ActorId = sides?.FirstOrDefault()?.MemberIds.FirstOrDefault(), TargetId = sides?.Skip(1).FirstOrDefault()?.MemberIds.FirstOrDefault() });
                Render();
            }) { name = "match-spot-add", text = "+ 특수 스팟 추가" };
            add.AddToClassList("small-button"); heading.Add(add); host.Add(heading);
            var scroll = new ScrollView { name = "match-spot-list" }; scroll.AddToClassList("spot-list"); host.Add(scroll);
            if (spots.Count == 0)
            {
                var empty = new Label("난입, 습격, 배신 같은 특수 상황을 추가할 수 있습니다.");
                empty.AddToClassList("spot-empty"); scroll.Add(empty);
            }
            foreach (var spot in spots)
            {
                var index = spots.IndexOf(spot);
                var row = new VisualElement { name = $"match-spot-{index}" }; row.AddToClassList("spot-row"); scroll.Add(row);
                var types = SpecialMatchSpotCatalog.All.ToList();
                var labels = types.Select(x => $"{x.Category} · {x.Name}" +
                    (SpecialMatchSpotCatalog.UnavailableReason(x, match) is string why ? $" ({why})" : string.Empty)).ToList();
                var choice = new DropdownField("스팟 종류", labels, Math.Max(0, types.FindIndex(x => x.Id == spot.SpotId))) { name = $"spot-type-{index}" };
                choice.AddToClassList("show-editor-field"); row.Add(choice);
                choice.RegisterValueChangedCallback(_ =>
                {
                    spot.SpotId = types[choice.index].Id;
                    var selected = types[choice.index];
                    spot.Phase = selected.Phases[0]; spot.PartnerId = null;
                    var participants = ParticipantIds();
                    spot.ActorId = selected.ActorScope == SpotActorScope.Outsider
                        ? save.Wrestlers.FirstOrDefault(x => x != null && !participants.Contains(x.Id))?.Id
                        : participants.FirstOrDefault();
                    spot.TargetId = TargetIds(selected, spot.ActorId).FirstOrDefault();
                    Render();
                });
                var definition = SpecialMatchSpotCatalog.Find(spot.SpotId);
                string RoleName(string id, string fallback) => save.Wrestlers.FirstOrDefault(x => x.Id == id)?.Identity.RingName ?? fallback;
                var preview = new Label("연출 예시 · " + definition.Variants[0]
                    .Replace("{actor}", RoleName(spot.ActorId, "주체"))
                    .Replace("{target}", RoleName(spot.TargetId, "상대"))
                    .Replace("{partner}", RoleName(spot.PartnerId, "동료"))) { name = $"spot-preview-{index}" };
                WrestlerNameText.Set(preview, preview.text, save.Wrestlers);
                preview.AddToClassList("spot-preview"); row.Add(preview);
                var roles = new VisualElement(); roles.AddToClassList("spot-roles"); row.Add(roles);
                var phaseValues = definition.Phases.ToList();
                var phase = new DropdownField("구간", phaseValues.Select(PhaseName).ToList(), Math.Max(0, phaseValues.IndexOf(spot.Phase))) { name = $"spot-phase-{index}" };
                phase.AddToClassList("show-editor-field"); roles.Add(phase);
                phase.RegisterValueChangedCallback(_ => spot.Phase = phaseValues[phase.index]);
                var participantIds = ParticipantIds();
                var actorIds = definition.ActorScope == SpotActorScope.Outsider
                    ? save.Wrestlers.Where(x => x != null && !participantIds.Contains(x.Id)).Select(x => x.Id).ToList()
                    : participantIds;
                AddRole(roles, definition.ActorLabel, $"spot-actor-{index}", actorIds, spot.ActorId, id => { spot.ActorId = id; Render(); });
                AddRole(roles, definition.TargetLabel, $"spot-target-{index}", TargetIds(definition, spot.ActorId), spot.TargetId,
                    id => { spot.TargetId = id; Render(); });
                var repeats = MatchSpotEvaluator.RecentUses(save, spot.SpotId, date) + spots.Take(index).Count(x => x.SpotId == spot.SpotId);
                var difficulty = definition.Difficulty >= 15 ? "매우 어려움" : definition.Difficulty >= 12 ? "어려움" : definition.Difficulty >= 10 ? "보통" : "쉬움";
                var value = definition.BasePoint >= .55f ? "큼" : definition.BasePoint >= .4f ? "보통" : "작음";
                var info = new Label($"난도 {difficulty} · 기대 효과 {value} · 최근 8주 {repeats}회" + (repeats >= 6 ? " · 성공해도 역반응" : string.Empty));
                info.AddToClassList("spot-info"); row.Add(info);
                var candidate = new MatchPlanState { Sides = match.Sides, MatchGimmickId = match.MatchGimmickId, Spots = new List<PlannedSpotState> { spot } };
                var problem = MatchSpotEvaluator.Validate(save, candidate).FirstOrDefault();
                var risky = new[] { spot.ActorId, spot.TargetId, spot.PartnerId }.Where(x => !string.IsNullOrEmpty(x))
                    .Select(id => save.Wrestlers.FirstOrDefault(x => x.Id == id))
                    .Any(x => x != null && (x.Condition.Condition < 50 || x.Condition.InjuryStatus != InjuryStatus.None));
                if (problem != null || risky)
                {
                    var warning = new Label(problem ?? "낮은 컨디션 또는 부상으로 수행 위험 증가");
                    warning.AddToClassList("spot-warning"); row.Add(warning);
                }
                var remove = new Button(() => { spots.Remove(spot); Render(); }) { text = "삭제", name = $"spot-remove-{index}" };
                remove.AddToClassList("small-button"); row.Add(remove);
            }
        }

        private void AddRole(VisualElement row, string label, string name, List<string> ids, string selected, Action<string> changed)
        {
            var choices = new List<string> { "선택" };
            choices.AddRange(ids.Select((id, index) => $"{index + 1}. {save.Wrestlers.FirstOrDefault(x => x.Id == id)?.Identity.RingName ?? "선수"}"));
            var field = new DropdownField(label, choices, ids.IndexOf(selected) + 1) { name = name };
            field.AddToClassList("show-editor-field"); row.Add(field);
            field.RegisterValueChangedCallback(_ => changed(field.index > 0 ? ids[field.index - 1] : null));
        }

        private List<string> ParticipantIds() => (match.Sides ?? new List<MatchSideState>()).Where(x => x?.MemberIds != null)
            .SelectMany(x => x.MemberIds).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();

        private List<string> TargetIds(MatchSpotDefinition definition, string actorId)
        {
            var sides = (match.Sides ?? new List<MatchSideState>()).Where(x => x?.MemberIds != null).ToList();
            var actorSide = sides.FirstOrDefault(x => x.MemberIds.Contains(actorId));
            return sides.SelectMany(x => x.MemberIds).Where(id => !string.IsNullOrEmpty(id) && id != actorId &&
                (definition.Relationship == SpotRelationship.Any || actorSide == null ||
                 definition.Relationship == SpotRelationship.Opponents && !actorSide.MemberIds.Contains(id) ||
                 definition.Relationship == SpotRelationship.Teammates && actorSide.MemberIds.Contains(id))).Distinct().ToList();
        }

        private static string PhaseName(MatchSpotPhase phase) => phase switch
        {
            MatchSpotPhase.Entrance => "입장 중", MatchSpotPhase.Early => "경기 초반", MatchSpotPhase.Middle => "경기 중반",
            MatchSpotPhase.Late => "경기 후반", _ => "경기 후"
        };
    }
}
