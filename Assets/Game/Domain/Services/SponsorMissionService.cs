using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Identifiers;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public sealed class SponsorMissionService
    {
        public const int OfferCount = 10, SelectionCount = 3;
        private readonly Func<string> createId;
        public SponsorMissionService(Func<string> createId = null) => this.createId = createId ?? EntityId.CreateRuntimeId;

        public SponsorSeasonState PrepareOffers(GameSave save, IReadOnlyList<SponsorCompany> companies)
        {
            if (save?.SeasonPolicy == null || companies == null || companies.Count < OfferCount || companies.Select(x => x.Id).Distinct().Count() != companies.Count)
                throw new InvalidOperationException("시즌과 스폰서 기업 정보를 확인하세요.");
            var existing = save.SponsorSeasons.FirstOrDefault(x => x.PolicyId == save.SeasonPolicy.Id);
            if (existing != null) return existing;
            var random = new Random(unchecked(save.WorldSeed * 397 ^ save.SeasonPolicy.StartDate.Year));
            var pool = companies.OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
            for (var i = pool.Count - 1; i > 0; i--) { var j = random.Next(i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }
            var season = new SponsorSeasonState { PolicyId = save.SeasonPolicy.Id, StartDate = save.SeasonPolicy.StartDate, EndDate = save.SeasonPolicy.EndDate };
            var roster = save.Wrestlers.Where(x => x.Roster.PromotionId == save.Promotion.Id && x.Roster.ActivityState == RosterActivityState.Active).OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
            var shows = Math.Max(1, save.Shows.Count(x => x.Date.CompareTo(season.StartDate) >= 0 && x.Date.CompareTo(season.EndDate) <= 0 && !save.IsCancelledShow(x)));
            var fans = Math.Max(0, save.Promotion.Audience?.TotalFollowers ?? 0);
            var prestige = Math.Max(0, save.Promotion.PromotionPrestige);
            var basePay = checked(5000L + Math.Min(1000000L, prestige * 2));
            for (var i = 0; i < OfferCount; i++)
            {
                var company = pool[i]; var difficulty = i % 3 + 1;
                var offer = new SponsorOfferState { Id = createId(), CompanyId = company.Id, CompanyName = company.Name,
                    Industry = company.Industry, Description = company.Description, LogoResource = company.LogoResource,
                    Difficulty = difficulty, BasePayment = RandomPayment(random, basePay, 95, 105),
                    CompletionPayment = RandomPayment(random, checked(basePay * (difficulty == 1 ? 2 : difficulty == 2 ? 4 : 7)), 90, 110) };
                var useFans = i % 2 == 0;
                offer.Goals.Add(new SponsorGoalState { Type = useFans ? SponsorGoalType.Fans : SponsorGoalType.Prestige,
                    Progress = useFans ? fans : prestige,
                    Target = useFans ? checked(fans + Math.Max(100L * difficulty, (long)Math.Ceiling(fans * new[] { .20, .40, .70 }[difficulty - 1]))) :
                        checked(prestige + Math.Max(25L * difficulty, (long)Math.Ceiling(prestige * new[] { .25, .50, .80 }[difficulty - 1]))) });
                if (difficulty >= 2)
                {
                    var type = company.PreferredGoal;
                    if (type is SponsorGoalType.Fans or SponsorGoalType.Prestige) type = SponsorGoalType.ShowQuality;
                    if ((type is SponsorGoalType.WrestlerAppearance or SponsorGoalType.WrestlerFame) && roster.Count == 0) type = SponsorGoalType.Advertisement;
                    var wrestler = roster.Count == 0 ? null : roster[random.Next(roster.Count)];
                    var goal = new SponsorGoalState { Type = type, Target = Math.Max(2, (long)Math.Ceiling(shows * .12 * difficulty)), MinimumQuality = 60 + 5 * difficulty };
                    if (type == SponsorGoalType.WrestlerAppearance) goal.WrestlerId = wrestler.Id;
                    if (type == SponsorGoalType.WrestlerFame) { goal.WrestlerId = wrestler.Id; goal.Progress = wrestler.Fame.Value; goal.Target = checked(wrestler.Fame.Value + Math.Max(100L, wrestler.Fame.Value / 3) * difficulty); }
                    if (type == SponsorGoalType.Attendance) goal.Target = Math.Max(100L, (long)Math.Ceiling(Math.Max(fans * .12, 75) * (1 + difficulty * .25)));
                    if (type == SponsorGoalType.StyleChampion) { goal.StyleId = "style_005"; goal.Target = 1; }
                    offer.Goals.Add(goal);
                }
                if (difficulty == 3)
                    offer.Goals.Add(new SponsorGoalState { Type = offer.Goals[1].Type == SponsorGoalType.Advertisement ? SponsorGoalType.ShowQuality : SponsorGoalType.Advertisement,
                        Target = Math.Max(3, (long)Math.Ceiling(shows * .2)), MinimumQuality = 75 });
                season.Offers.Add(offer);
            }
            save.SponsorSeasons.Add(season);
            return season;
        }

        private static long RandomPayment(Random random, long reference, int minimumPercent, int maximumPercent)
        {
            // Sample in $10 units so rounding never pushes a payment outside its band.
            var minimum = checked((reference * minimumPercent + 999) / 1000);
            var maximum = checked(reference * maximumPercent / 1000);
            return checked(random.Next((int)minimum, (int)maximum + 1) * 10L);
        }

        public void ConfirmSelection(GameSave save, SponsorSeasonState season, IEnumerable<string> selectedIds)
        {
            if (season == null || !save.SponsorSeasons.Contains(season) || season.PolicyId != save.SeasonPolicy.Id)
                throw new InvalidOperationException("현재 시즌의 스폰서 제안을 확인하세요.");
            var ids = (selectedIds ?? Array.Empty<string>()).ToList();
            if (ids.Count != SelectionCount || ids.Distinct().Count() != SelectionCount || ids.Any(id => !season.Offers.Any(x => x.Id == id)))
                throw new InvalidOperationException("스폰서 제안 3개를 선택하세요.");
            if (season.SelectionConfirmed)
            {
                if (!season.Offers.Where(x => x.Selected).Select(x => x.Id).ToHashSet().SetEquals(ids)) throw new InvalidOperationException("확정된 스폰서는 변경할 수 없습니다.");
                return;
            }
            var payments = season.Offers.Where(x => ids.Contains(x.Id)).Select(x => Payment(save, season, x, false)).ToList();
            foreach (var offer in season.Offers) offer.Selected = ids.Contains(offer.Id);
            save.Transactions.AddRange(payments); season.SelectionConfirmed = true;
        }

        public void ProcessShow(GameSave save, ShowResultState result)
        {
            var show = save.Shows.Single(x => x.Id == result.ShowId);
            if (show.Status != ShowStatus.Completed || show.ShowVersion != result.ShowVersion || save.IsCancelledShow(show)) return;
            var matches = save.MatchResults.Where(x => result.MatchResultIds.Contains(x.Id) && string.IsNullOrEmpty(x.CancellationReason)).ToList();
            var promos = save.PromoResults.Where(x => result.PromoResultIds.Contains(x.Id) && string.IsNullOrEmpty(x.CancellationReason)).ToList();
            foreach (var season in save.SponsorSeasons.Where(x => x.SelectionConfirmed && show.Date.CompareTo(x.StartDate) >= 0 && show.Date.CompareTo(x.EndDate) <= 0))
            foreach (var offer in season.Offers.Where(x => x.Selected && !x.RewardPaid && !x.Failed))
            foreach (var goal in offer.Goals)
            {
                if (goal.CountedShowIds.Contains(show.Id)) continue;
                var met = goal.Type switch
                {
                    SponsorGoalType.Attendance => result.FinancialSettlement.Attendance >= goal.Target,
                    SponsorGoalType.ShowQuality => result.ShowEvaluation.Score >= goal.MinimumQuality,
                    SponsorGoalType.WrestlerAppearance => matches.Any(x => x.ParticipantProfiles.Any(p => p.MemberIds.Contains(goal.WrestlerId))) || promos.Any(x => x.ParticipantUsage.Any(p => p.WrestlerId == goal.WrestlerId)),
                    SponsorGoalType.Advertisement => promos.Any(x => save.PromoPlans.Any(p => p.Id == x.PromoPlanId && p.Purpose == PromoPurpose.SponsorAdvertisement && p.SponsorRequirementId == offer.Id)),
                    SponsorGoalType.GiantMatch => matches.Any(x => x.ParticipantProfiles.Count(p => p.MemberIds.Any(id => x.WrestlerPerformances.Any(w => w.WrestlerId == id && w.WrestlingStyleId == "style_006"))) >= 2),
                    SponsorGoalType.StyleChampion => save.Championships.Any(x => x.Reigns.Any(r => r.WonDate.Equals(show.Date) && result.MatchResultIds.Contains(r.SourceResultId) &&
                        r.MemberIds.Any(id => matches.Any(m => m.Id == r.SourceResultId && m.WrestlerPerformances.Any(w => w.WrestlerId == id && w.WrestlingStyleId == goal.StyleId))))),
                    _ => false
                };
                goal.CountedShowIds.Add(show.Id);
                if (met) goal.Progress = goal.Type is SponsorGoalType.Attendance or SponsorGoalType.StyleChampion ? goal.Target : Math.Min(goal.Target, goal.Progress + 1);
            }
            ProcessDay(save);
        }

        public void ProcessDay(GameSave save)
        {
            foreach (var season in save.SponsorSeasons.Where(x => x.SelectionConfirmed && save.CurrentDate.CompareTo(x.StartDate) >= 0))
            foreach (var offer in season.Offers.Where(x => x.Selected && !x.RewardPaid && !x.Failed))
            {
                // The deadline's shows still count; failure is final on the following day.
                if (save.CurrentDate.CompareTo(season.EndDate) > 0) { offer.Failed = true; continue; }
                foreach (var goal in offer.Goals)
                {
                    var observed = goal.Type switch
                    {
                        SponsorGoalType.Fans => save.Promotion.Audience?.TotalFollowers ?? 0,
                        SponsorGoalType.Prestige => save.Promotion.PromotionPrestige,
                        SponsorGoalType.WrestlerFame => save.Wrestlers.FirstOrDefault(x => x.Id == goal.WrestlerId)?.Fame.Value ?? 0,
                        _ => goal.Progress
                    };
                    goal.Progress = Math.Min(goal.Target, Math.Max(goal.Progress, observed));
                }
                if (offer.Goals.Any(goal => goal.Progress < goal.Target && !string.IsNullOrEmpty(goal.WrestlerId) &&
                    save.Wrestlers.Any(w => w.Id == goal.WrestlerId && (w.Condition.InjuryStatus != InjuryStatus.None || w.Roster.ActivityState == RosterActivityState.Released || w.Roster.PromotionId != save.Promotion.Id))))
                    offer.Failed = true;
                else if (offer.Goals.All(x => x.Progress >= x.Target))
                {
                    save.Transactions.Add(Payment(save, season, offer, true)); offer.RewardPaid = true;
                }
            }
        }

        private TransactionRecord Payment(GameSave save, SponsorSeasonState season, SponsorOfferState offer, bool completion) => new()
        {
            Id = createId(), PromotionId = save.Promotion.Id, Date = completion ? save.CurrentDate : season.StartDate,
            Type = TransactionType.SponsorRevenue, Amount = completion ? offer.CompletionPayment : offer.BasePayment,
            ReasonId = $"sponsor:{offer.Id}:{(completion ? "completion" : "base")}"
        };

        public static void ValidateState(GameSave save)
        {
            var policies = new HashSet<string>(); var ids = new HashSet<string>();
            foreach (var season in save.SponsorSeasons ?? new())
            {
                if (season == null || !policies.Add(season.PolicyId) || season.StartDate.CompareTo(season.EndDate) > 0 || season.Offers == null || season.Offers.Count != OfferCount ||
                    season.Offers.Where(x => x != null).Select(x => x.CompanyId).Distinct().Count() != OfferCount)
                    throw new InvalidOperationException("스폰서 시즌 제안 정보가 올바르지 않습니다.");
                if (season.SelectionConfirmed && season.Offers.Count(x => x.Selected) != SelectionCount || !season.SelectionConfirmed && season.Offers.Any(x => x.Selected))
                    throw new InvalidOperationException("스폰서 선택은 시즌당 3개여야 합니다.");
                foreach (var offer in season.Offers)
                {
                    if (offer == null || !EntityId.IsValidRuntimeId(offer.Id) || !ids.Add(offer.Id) || string.IsNullOrWhiteSpace(offer.CompanyId) || string.IsNullOrWhiteSpace(offer.CompanyName) ||
                        offer.Difficulty < 1 || offer.Difficulty > 3 || offer.Goals == null || offer.Goals.Count != offer.Difficulty || offer.BasePayment < 0 || offer.CompletionPayment <= 0 ||
                        !offer.Goals.Any(x => x?.Type is SponsorGoalType.Fans or SponsorGoalType.Prestige) || (offer.RewardPaid || offer.Failed) && !offer.Selected || offer.RewardPaid && offer.Failed)
                        throw new InvalidOperationException("스폰서 임무 정보가 올바르지 않습니다.");
                    foreach (var goal in offer.Goals)
                        if (goal == null || !Enum.IsDefined(typeof(SponsorGoalType), goal.Type) || goal.Target <= 0 || goal.Progress < 0 || goal.Progress > goal.Target || goal.CountedShowIds == null || goal.CountedShowIds.Distinct().Count() != goal.CountedShowIds.Count ||
                            (goal.Type is SponsorGoalType.WrestlerAppearance or SponsorGoalType.WrestlerFame) && !save.Wrestlers.Any(x => x.Id == goal.WrestlerId))
                            throw new InvalidOperationException("스폰서 목표 정보가 올바르지 않습니다.");
                    foreach (var stage in new[] { "base", "completion" })
                    {
                        var payments = save.Transactions.Where(x => !x.IsVoided && x.ReasonId == $"sponsor:{offer.Id}:{stage}").ToList();
                        var due = stage == "base" ? offer.Selected : offer.RewardPaid;
                        if (payments.Count != (due ? 1 : 0) || payments.Any(x => x.Type != TransactionType.SponsorRevenue || x.Amount != (stage == "base" ? offer.BasePayment : offer.CompletionPayment)))
                            throw new InvalidOperationException("스폰서 지급 기록이 올바르지 않습니다.");
                    }
                }
            }
        }
    }
}
