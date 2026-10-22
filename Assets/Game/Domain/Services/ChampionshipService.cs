using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Identifiers;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public static class ChampionshipService
    {
        public static ChampionshipState Create(GameSave save, string name, ChampionshipType type,
            bool world = false, string holderId = null)
        {
            if (save?.Promotion == null) throw new InvalidOperationException("단체를 찾을 수 없습니다.");
            name = name?.Trim();
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("챔피언십 이름을 입력하세요.");
            if (!Enum.IsDefined(typeof(ChampionshipType), type) || type == ChampionshipType.TagTeam && world)
                throw new InvalidOperationException("월드 챔피언십은 개인 타이틀만 선택할 수 있습니다.");
            name = $"{save.Promotion.Name?.Trim()} {name}{(type == ChampionshipType.TagTeam ? " Tag Team" : "")} Championship".Trim();
            if ((save.Championships ?? new()).Any(x => x.PromotionId == save.Promotion.Id &&
                string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("같은 이름의 챔피언십이 있습니다.");
            var title = new ChampionshipState { Id = EntityId.CreateRuntimeId(), PromotionId = save.Promotion.Id,
                Name = name, Type = type, IsWorldChampionship = world, HasPrestige = true, Prestige = world ? 60f : 30f };
            SetHolder(save, title, holderId);
            (save.Championships ??= new()).Add(title);
            return title;
        }

        public static void SetHolder(GameSave save, ChampionshipState title, string holderId)
        {
            var members = string.IsNullOrEmpty(holderId) ? new List<string>() : HolderMembers(save, title, holderId, true);
            if (title.HolderId == holderId || string.IsNullOrEmpty(title.HolderId) && string.IsNullOrEmpty(holderId)) return;
            title.Prestige = Prestige(title); title.HasPrestige = true;
            var current = title.Reigns.LastOrDefault();
            if (current != null && members.Count > 0)
                title.Prestige = Clamp(Prestige(title) + Clamp((Standing(save, members) - Standing(save, current.MemberIds)) * .04f, -3, 3), 0, 100);
            if (current != null && !current.LostDate.HasValue) current.LostDate = new OptionalGameDate(save.CurrentDate);
            title.HolderId = string.IsNullOrEmpty(holderId) ? null : holderId;
            if (members.Count > 0) title.Reigns.Add(new ChampionshipReignState
                { HolderId = holderId, MemberIds = members, WonDate = save.CurrentDate });
        }

        public static float Prestige(ChampionshipState title) => title.HasPrestige ? title.Prestige : title.IsWorldChampionship ? 60f : 30f;

        public static void Upgrade(GameSave save)
        {
            foreach (var title in save.Championships ?? new())
            {
                if (title == null) continue;
                if (!title.HasPrestige) { title.Prestige = Prestige(title); title.HasPrestige = true; }
                title.MatchHistory ??= new();
            }
        }

        private static float Clamp(float value, float minimum, float maximum) => Math.Max(minimum, Math.Min(maximum, value));

        private static float Standing(GameSave save, IEnumerable<string> ids)
        {
            var wrestlers = ids.Select(id => save.Wrestlers.FirstOrDefault(x => x.Id == id)).Where(x => x != null).ToList();
            return wrestlers.Count == 0 ? 50 : wrestlers.Average(x =>
                x.Status.StatusValue * .5f + (x.FanReaction.MarkResponse.Interest + x.FanReaction.CasualResponse.Interest +
                    x.FanReaction.HardcoreResponse.Interest) / 6f);
        }

        private static List<string> HolderMembers(GameSave save, ChampionshipState title, string id, bool active)
        {
            if (title.Type == ChampionshipType.Individual)
            {
                var wrestler = save.Wrestlers.FirstOrDefault(x => x.Id == id && x.PromotionId == title.PromotionId);
                if (wrestler == null || active && wrestler.Roster.ActivityState != RosterActivityState.Active)
                    throw new InvalidOperationException("챔피언은 소속 로스터의 활동 중인 선수여야 합니다.");
                return new List<string> { id };
            }
            var team = save.TagTeams.FirstOrDefault(x => x.Id == id && x.PromotionId == title.PromotionId);
            if (team == null || team.MemberIds == null || team.MemberIds.Count < 2 ||
                team.MemberIds.Distinct().Count() != team.MemberIds.Count ||
                active && team.Status != TagTeamStatus.Active ||
                team.MemberIds.Any(member => !save.Wrestlers.Any(x => x.Id == member &&
                    x.PromotionId == title.PromotionId && (!active || x.Roster.ActivityState == RosterActivityState.Active))))
                throw new InvalidOperationException("챔피언은 소속 선수로 구성된 활동 중인 태그팀이어야 합니다.");
            return new List<string>(team.MemberIds);
        }

        public static void ValidateState(GameSave save)
        {
            var ids = new HashSet<string>();
            foreach (var title in save.Championships ?? new())
            {
                if (title == null || !EntityId.IsValidRuntimeId(title.Id) || !ids.Add(title.Id) ||
                    title.PromotionId != save.Promotion.Id || string.IsNullOrWhiteSpace(title.Name) ||
                    !Enum.IsDefined(typeof(ChampionshipType), title.Type) ||
                    title.Type == ChampionshipType.TagTeam && title.IsWorldChampionship || title.Reigns == null ||
                    float.IsNaN(Prestige(title)) || float.IsInfinity(Prestige(title)) || Prestige(title) < 0 || Prestige(title) > 100)
                    throw new InvalidOperationException("챔피언십 정보가 올바르지 않습니다.");
                if (!string.IsNullOrEmpty(title.HolderId)) HolderMembers(save, title, title.HolderId, false);
                var open = title.Reigns.Where(x => x != null && !x.LostDate.HasValue).ToList();
                if (title.Reigns.Any(x => x == null || string.IsNullOrEmpty(x.HolderId) || x.MemberIds == null ||
                    x.MemberIds.Count == 0 || x.Defenses < 0 || x.MemberIds.Any(id => !save.Wrestlers.Any(w => w.Id == id))) ||
                    (string.IsNullOrEmpty(title.HolderId) ? open.Count != 0 :
                        open.Count != 1 || open[0] != title.Reigns.Last() || open[0].HolderId != title.HolderId))
                    throw new InvalidOperationException("챔피언십 재위 기록이 올바르지 않습니다.");
                var resultIds = new HashSet<string>();
                foreach (var match in title.MatchHistory ?? new())
                    if (match == null || string.IsNullOrEmpty(match.SourceResultId) || !resultIds.Add(match.SourceResultId) ||
                        match.ParticipantIds == null || match.ParticipantIds.Count < 2 ||
                        match.ParticipantIds.Distinct().Count() != match.ParticipantIds.Count ||
                        match.ChallengerIds == null || match.ChallengerIds.Any(id => !match.ParticipantIds.Contains(id)) ||
                        match.WinnerMemberIds == null || match.WinnerMemberIds.Any(id => !match.ParticipantIds.Contains(id)) ||
                        !Enum.IsDefined(typeof(MatchFinishType), match.FinishType) ||
                        new[] { match.MatchQuality, match.FanSatisfaction, match.ChampionStanding, match.ChallengerStanding,
                            match.PrestigeBefore, match.PrestigeAfter }.Any(x => float.IsNaN(x) || float.IsInfinity(x) || x < 0 || x > 100))
                        throw new InvalidOperationException("챔피언십 경기 기록이 올바르지 않습니다.");
            }
        }

        public static void ValidateMatch(GameSave save, MatchPlanState match)
        {
            if (string.IsNullOrEmpty(match.ChampionshipId)) return;
            var title = (save.Championships ?? new()).FirstOrDefault(x => x.Id == match.ChampionshipId)
                ?? throw new InvalidOperationException("챔피언십을 찾을 수 없습니다.");
            var sides = match.Sides ?? new();
            if (sides.Count < 2 || sides.Any(x => x == null || x.MemberIds == null || x.MemberIds.Count == 0))
                throw new InvalidOperationException("타이틀전 참가자를 지정하세요.");
            var holders = new List<string>();
            foreach (var side in sides)
            {
                var holder = title.Type == ChampionshipType.Individual ? side.MemberIds[0] : MatchEvaluator.ResolveOfficialTagTeam(save, side)?.Id;
                var members = HolderMembers(save, title, holder, true);
                if (members.Count != side.MemberIds.Count || !members.ToHashSet().SetEquals(side.MemberIds))
                    throw new InvalidOperationException("개인 타이틀은 개인전, 태그팀 타이틀은 등록된 태그팀 간 경기로 편성하세요.");
                holders.Add(holder);
            }
            if (holders.Distinct().Count() != holders.Count ||
                !string.IsNullOrEmpty(title.HolderId) && !holders.Contains(title.HolderId))
                throw new InvalidOperationException("방어전에는 현재 챔피언이 참가해야 합니다.");
        }

        public static List<ChampionshipState> PrepareResults(GameSave save, IEnumerable<MatchResultState> results, GameDate date)
        {
            // Stage title changes alongside the other result validation, before touching the save.
            var staged = new GameSave { Promotion = save.Promotion, Wrestlers = save.Wrestlers, TagTeams = save.TagTeams, CurrentDate = date };
            staged.Championships = (save.Championships ?? new()).Select(x => new ChampionshipState
            {
                Id = x.Id, PromotionId = x.PromotionId, Name = x.Name, Type = x.Type,
                IsWorldChampionship = x.IsWorldChampionship, HolderId = x.HolderId,
                HasPrestige = true, Prestige = Prestige(x), MatchHistory = new(x.MatchHistory ?? new()),
                Reigns = x.Reigns.Select(r => new ChampionshipReignState { HolderId = r.HolderId, MemberIds = new(r.MemberIds),
                    WonDate = r.WonDate, LostDate = r.LostDate, SourceResultId = r.SourceResultId, Defenses = r.Defenses }).ToList()
            }).ToList();
            foreach (var result in results)
            {
                var plan = save.MatchPlans.FirstOrDefault(x => x.Id == result.MatchPlanId);
                if (plan == null || string.IsNullOrEmpty(plan.ChampionshipId) || !string.IsNullOrEmpty(result.CancellationReason)) continue;
                var actual = new MatchPlanState { ChampionshipId = plan.ChampionshipId,
                    Sides = result.ParticipantProfiles.Select(x => new MatchSideState
                        { Id = x.SideId, MemberIds = x.MemberIds, RepresentedTagTeamId = x.RepresentedTagTeamId }).ToList() };
                ValidateMatch(staged, actual);
                var title = staged.Championships.First(x => x.Id == plan.ChampionshipId);
                if (title.MatchHistory.Any(x => x.SourceResultId == result.Id)) continue;
                var before = title.HolderId;
                var beforePrestige = title.Prestige;
                var defendingMembers = string.IsNullOrEmpty(before) ? new List<string>() : HolderMembers(staged, title, before, false);
                var challengers = actual.Sides.SelectMany(x => x.MemberIds).Except(defendingMembers).ToList();
                var stopped = result.WasStoppedByInjury || result.WasStoppedBySpot || result.WasStoppedByExecution;
                var protectedFinish = result.FinishType is MatchFinishType.Draw or MatchFinishType.Disqualification or MatchFinishType.CountOut;
                var winning = stopped || result.FinishType == MatchFinishType.Draw ? null : actual.Sides.FirstOrDefault(x => x.Id == result.WinningSideId);
                if (!protectedFinish && !stopped)
                {
                    if (winning == null) throw new InvalidOperationException("타이틀전 승리 팀을 찾을 수 없습니다.");
                    var holder = title.Type == ChampionshipType.Individual ? winning.MemberIds[0] : MatchEvaluator.ResolveOfficialTagTeam(staged, winning)?.Id;
                    if (title.HolderId == holder) title.Reigns.Last().Defenses++;
                    else { SetHolder(staged, title, holder); title.Reigns.Last().SourceResultId = result.Id; }
                }
                var quality = Clamp(result.FinalMatchQuality * 5, 0, 100);
                if (float.IsNaN(result.FinalMatchQuality) || float.IsInfinity(result.FinalMatchQuality) ||
                    result.FinalMatchQuality < 0 || result.FinalMatchQuality > 20 ||
                    result.FanReaction?.IsCalculated == true && new[] { result.FanReaction.Family, result.FanReaction.Light, result.FanReaction.Mania }
                        .Any(x => float.IsNaN(x) || float.IsInfinity(x) || x < 0 || x > 100))
                    throw new InvalidOperationException("위상을 계산할 경기·팬 수치가 올바르지 않습니다.");
                var fans = result.FanReaction?.IsCalculated == true
                    ? (result.FanReaction.Family + result.FanReaction.Light + result.FanReaction.Mania) / 3f : 50;
                var championStanding = string.IsNullOrEmpty(title.HolderId) ? 50 : Standing(staged, HolderMembers(staged, title, title.HolderId, false));
                var challengerStanding = Standing(staged, challengers);
                if (float.IsNaN(quality) || float.IsInfinity(quality) || float.IsNaN(fans) || float.IsInfinity(fans) || fans < 0 || fans > 100 ||
                    float.IsNaN(championStanding) || float.IsInfinity(championStanding) || float.IsNaN(challengerStanding) || float.IsInfinity(challengerStanding))
                    throw new InvalidOperationException("위상을 계산할 경기·팬·선수 수치가 올바르지 않습니다.");
                var target = quality * .4f + fans * .3f + championStanding * .2f + challengerStanding * .1f;
                var penalty = stopped ? 2 : protectedFinish ? 1 : 0;
                title.Prestige = Clamp(beforePrestige + Clamp((target - beforePrestige) * .1f - penalty, -5, 5), 0, 100);
                title.MatchHistory.Add(new ChampionshipMatchState
                {
                    SourceResultId = result.Id, ShowId = result.ShowId, Date = date, ChampionBeforeId = before,
                    ChampionAfterId = title.HolderId, ParticipantIds = actual.Sides.SelectMany(x => x.MemberIds).ToList(),
                    ChallengerIds = challengers, WinnerMemberIds = winning == null ? new() : new(winning.MemberIds),
                    FinishType = result.FinishType, WasStopped = stopped, SuccessfulDefense = !string.IsNullOrEmpty(before) && before == title.HolderId && !protectedFinish && !stopped,
                    MatchQuality = quality, FanSatisfaction = fans, ChampionStanding = championStanding,
                    ChallengerStanding = challengerStanding, PrestigeBefore = beforePrestige, PrestigeAfter = title.Prestige
                });
            }
            return staged.Championships;
        }

        public static int ReignCount(GameSave save, string wrestlerId, bool worldOnly = false) =>
            (save?.Championships ?? new()).Where(x => !worldOnly || x.IsWorldChampionship)
                .Sum(x => x.Reigns.Count(r => r.MemberIds.Contains(wrestlerId)));
    }
}
