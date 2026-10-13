using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Identifiers;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public static class WrestlerGroupService
    {
        public static TagTeamState CreateTagTeam(GameSave save, string name, IEnumerable<string> memberIds, string id = null)
        {
            var members = Members(save, name, memberIds, 2, 2);
            if (save.TagTeams.Any(x => x.Status == TagTeamStatus.Active && x.MemberIds.Intersect(members).Any()))
                throw new InvalidOperationException("이미 태그팀에 소속된 선수입니다.");
            var stables = save.Stables.Where(x => x.Status == TagTeamStatus.Active && x.MemberIds.Intersect(members).Any()).ToList();
            if (stables.Any(x => !members.All(x.MemberIds.Contains)))
                throw new InvalidOperationException("태그팀 선수는 같은 스테이블에 소속되어야 합니다.");
            var team = new TagTeamState { Id = NewId(save, id), PromotionId = save.Promotion.Id, Name = name.Trim(), MemberIds = members };
            save.TagTeams.Add(team);
            foreach (var wrestler in save.Wrestlers.Where(x => members.Contains(x.Id))) wrestler.Roster.ActiveTagTeamId = team.Id;
            return team;
        }

        public static StableState CreateStable(GameSave save, string name, IEnumerable<string> memberIds, string leaderId, string id = null)
        {
            var members = Members(save, name, memberIds, 3, int.MaxValue);
            ValidateStableMembers(save, members, leaderId, null);
            var stable = new StableState { Id = NewId(save, id), PromotionId = save.Promotion.Id, Name = name.Trim(), MemberIds = members, LeaderId = leaderId };
            save.Stables.Add(stable);
            foreach (var wrestler in save.Wrestlers.Where(x => members.Contains(x.Id))) wrestler.Roster.ActiveStableId = stable.Id;
            return stable;
        }

        public static void SetTagTeamMembers(GameSave save, string teamId, IEnumerable<string> memberIds)
        {
            var team = save.TagTeams.Single(x => x.Id == teamId && x.Status == TagTeamStatus.Active);
            var members = Members(save, string.IsNullOrWhiteSpace(team.Name) ? "태그팀" : team.Name, memberIds, 2, 2);
            if (save.TagTeams.Any(x => x.Id != teamId && x.Status == TagTeamStatus.Active && x.MemberIds.Intersect(members).Any()))
                throw new InvalidOperationException("이미 다른 태그팀에 소속된 선수입니다.");
            if (save.Stables.Any(x => x.Status == TagTeamStatus.Active && x.MemberIds.Intersect(members).Any() && !members.All(x.MemberIds.Contains)))
                throw new InvalidOperationException("태그팀 선수는 같은 스테이블에 소속되어야 합니다.");
            foreach (var wrestler in save.Wrestlers)
            {
                if (wrestler.Roster.ActiveTagTeamId == teamId) wrestler.Roster.ActiveTagTeamId = null;
                if (members.Contains(wrestler.Id)) wrestler.Roster.ActiveTagTeamId = teamId;
            }
            if (!team.MemberIds.ToHashSet().SetEquals(members)) team.Chemistry = 50f;
            team.MemberIds = members;
        }

        public static void SetStableMembers(GameSave save, string stableId, IEnumerable<string> memberIds, string leaderId)
        {
            var stable = save.Stables.Single(x => x.Id == stableId && x.Status == TagTeamStatus.Active);
            var members = Members(save, stable.Name, memberIds, 3, int.MaxValue);
            ValidateStableMembers(save, members, leaderId, stableId);
            foreach (var wrestler in save.Wrestlers)
            {
                if (wrestler.Roster.ActiveStableId == stableId) wrestler.Roster.ActiveStableId = null;
                if (members.Contains(wrestler.Id)) wrestler.Roster.ActiveStableId = stableId;
            }
            stable.MemberIds = members;
            stable.LeaderId = leaderId;
        }

        public static void Disband(GameSave save, string groupId, bool isStable)
        {
            if (isStable)
            {
                var stable = save.Stables.Single(x => x.Id == groupId && x.Status == TagTeamStatus.Active);
                stable.Status = TagTeamStatus.Disbanded;
                foreach (var wrestler in save.Wrestlers.Where(x => x.Roster.ActiveStableId == groupId)) wrestler.Roster.ActiveStableId = null;
            }
            else
            {
                var team = save.TagTeams.Single(x => x.Id == groupId && x.Status == TagTeamStatus.Active);
                team.Status = TagTeamStatus.Disbanded;
                foreach (var wrestler in save.Wrestlers.Where(x => x.Roster.ActiveTagTeamId == groupId)) wrestler.Roster.ActiveTagTeamId = null;
            }
        }

        private static string NewId(GameSave save, string id)
        {
            id ??= EntityId.CreateRuntimeId();
            if (!EntityId.IsValidRuntimeId(id) || save.TagTeams.Any(x => x.Id == id) || save.Stables.Any(x => x.Id == id))
                throw new InvalidOperationException("Group ID must be unique.");
            return id;
        }

        public static GroupResultChangeState FromPromo(GameSave save, PromoPlanState plan, string sourceResultId)
        {
            ValidatePromo(save, plan);
            if (plan.Purpose is not (PromoPurpose.TeamFormation or PromoPurpose.TeamBreakup)) return null;
            // Legacy promos without an explicit group selection remain narrative-only.
            if (plan.GroupType == StoryPartyType.Wrestler) return null;
            if (plan.GroupType is not (StoryPartyType.TagTeam or StoryPartyType.Stable)) throw new InvalidOperationException("Invalid promo group type.");
            var change = new GroupResultChangeState
            {
                SourceResultId = sourceResultId, GroupType = plan.GroupType, Disband = plan.Purpose == PromoPurpose.TeamBreakup,
                GroupId = plan.Purpose == PromoPurpose.TeamFormation ? EntityId.CreateRuntimeId() : plan.GroupId,
                Name = plan.GroupName, LeaderId = plan.GroupLeaderId, MemberIds = new(plan.ParticipantIds)
            };
            if (change.Disband)
            {
                var members = change.GroupType == StoryPartyType.TagTeam ? save.TagTeams.Single(x => x.Id == change.GroupId).MemberIds : save.Stables.Single(x => x.Id == change.GroupId).MemberIds;
                if (!members.Intersect(plan.ParticipantIds).Any()) throw new InvalidOperationException("해체 프로모에는 해당 그룹 선수가 출연해야 합니다.");
            }
            return change;
        }

        public static void ValidatePromo(GameSave save, PromoPlanState plan)
        {
            if (plan.GroupType == StoryPartyType.Wrestler) return;
            if (plan.GroupType is not (StoryPartyType.TagTeam or StoryPartyType.Stable) ||
                plan.Purpose is not (PromoPurpose.TeamFormation or PromoPurpose.TeamBreakup))
                throw new InvalidOperationException("그룹 반영은 결성·해체 프로모에서만 지정할 수 있습니다.");
            if (plan.Purpose == PromoPurpose.TeamFormation)
            {
                var members = Members(save, plan.GroupName, plan.ParticipantIds, plan.GroupType == StoryPartyType.TagTeam ? 2 : 3,
                    plan.GroupType == StoryPartyType.TagTeam ? 2 : int.MaxValue);
                if (plan.GroupType == StoryPartyType.Stable && !members.Contains(plan.GroupLeaderId))
                    throw new InvalidOperationException("스테이블 리더는 출연 선수 중에서 선택해야 합니다.");
            }
            else
            {
                var members = plan.GroupType == StoryPartyType.TagTeam ?
                    (save.TagTeams ?? new()).SingleOrDefault(x => x?.Id == plan.GroupId && x.Status == TagTeamStatus.Active)?.MemberIds :
                    (save.Stables ?? new()).SingleOrDefault(x => x?.Id == plan.GroupId && x.Status == TagTeamStatus.Active)?.MemberIds;
                if (members == null || plan.ParticipantIds == null || !members.Intersect(plan.ParticipantIds).Any())
                    throw new InvalidOperationException("해체할 활동 중인 그룹의 선수가 출연해야 합니다.");
            }
        }

        public static void ApplyPromoChange(GameSave save, GroupResultChangeState change)
        {
            if (change == null || string.IsNullOrWhiteSpace(change.SourceResultId) || change.GroupType is not (StoryPartyType.TagTeam or StoryPartyType.Stable))
                throw new InvalidOperationException("Invalid group result.");
            if (change.Disband)
            {
                var members = change.GroupType == StoryPartyType.TagTeam ?
                    save.TagTeams.SingleOrDefault(x => x.Id == change.GroupId)?.MemberIds :
                    save.Stables.SingleOrDefault(x => x.Id == change.GroupId)?.MemberIds;
                if (members == null || change.MemberIds == null || !members.Intersect(change.MemberIds).Any())
                    throw new InvalidOperationException("해체 결과에 해당 그룹 출연 선수가 없습니다.");
                Disband(save, change.GroupId, change.GroupType == StoryPartyType.Stable);
            }
            else if (change.GroupType == StoryPartyType.TagTeam) CreateTagTeam(save, change.Name, change.MemberIds, change.GroupId);
            else CreateStable(save, change.Name, change.MemberIds, change.LeaderId, change.GroupId);
        }

        private static List<string> Members(GameSave save, string name, IEnumerable<string> ids, int min, int max)
        {
            if (save?.Promotion == null || string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("단체와 이름이 필요합니다.");
            var members = ids?.ToList() ?? new List<string>();
            if (members.Count < min || members.Count > max || members.Distinct().Count() != members.Count)
                throw new InvalidOperationException("태그팀은 2명, 스테이블은 3명 이상의 서로 다른 선수가 필요합니다.");
            if (members.Any(id => !save.Wrestlers.Any(x => x.Id == id && x.PromotionId == save.Promotion.Id)))
                throw new InvalidOperationException("현재 단체의 선수만 소속시킬 수 있습니다.");
            return members;
        }

        private static void ValidateStableMembers(GameSave save, List<string> members, string leaderId, string ownId)
        {
            if (!members.Contains(leaderId)) throw new InvalidOperationException("리더는 스테이블 소속 선수여야 합니다.");
            if (save.Stables.Any(x => x.Id != ownId && x.Status == TagTeamStatus.Active && x.MemberIds.Intersect(members).Any()))
                throw new InvalidOperationException("이미 다른 스테이블에 소속된 선수입니다.");
            if (save.TagTeams.Any(x => x.Status == TagTeamStatus.Active && x.MemberIds.Intersect(members).Any() && !x.MemberIds.All(members.Contains)))
                throw new InvalidOperationException("활동 중인 태그팀은 두 선수를 함께 소속시키거나 제외해야 합니다.");
        }
    }
}
