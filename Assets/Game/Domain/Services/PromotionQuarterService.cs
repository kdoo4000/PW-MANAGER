using System;
using System.Linq;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public static class PromotionQuarterService
    {
        public static GameDate EndOfQuarter(GameDate date)
        {
            var month = date.Month <= 2 ? 2 : date.Month <= 5 ? 5 : date.Month <= 8 ? 8 : 11;
            var year = date.Month == 12 ? date.Year + 1 : date.Year;
            if (date.Month == 12) month = 2;
            return new(year, month, DateTime.DaysInMonth(year, month));
        }
        public static void Initialize(GameSave save, bool legacy = false)
        {
            if (save.Promotion.QuarterlyGrowth?.IsInitialized == true) return;
            FanAudienceService.Initialize(save);
            save.Promotion.UnlockedIds ??= new();
            var tier = legacy ? PromotionProgressionRules.LegacyTier(save) : PromotionProgressionRules.Tier(save.Promotion.PromotionPrestige);
            for (var i = 1; i <= tier; i++)
                if (!save.Promotion.UnlockedIds.Contains(PromotionProgressionRules.TierId(i))) save.Promotion.UnlockedIds.Add(PromotionProgressionRules.TierId(i));
            var opening = save.CurrentDate; var end = EndOfQuarter(opening);
            if (save.LastCareerQuarterEnd.HasValue && save.LastCareerQuarterEnd.Value.CompareTo(end) >= 0) opening = end.AddDays(1);
            save.Promotion.QuarterlyGrowth = new() { IsInitialized = true, OpeningDate = opening,
                OpeningFollowers = save.Promotion.Audience.TotalFollowers,
                BaselineResultIds = save.ShowResults.Where(r => save.Shows.Any(s => s.Id == r.ShowId && s.Status == ShowStatus.Completed)).Select(r => r.Id).ToList() };
        }
        public static long Round(double value)
        {
            if (!double.IsFinite(value) || value < 0 || value > long.MaxValue) throw new InvalidOperationException("분기 명성 점수가 올바르지 않습니다.");
            return checked((long)Math.Round(value, MidpointRounding.AwayFromZero));
        }
        public static double FanScore(long start, long end)
        {
            if (start < 0 || end < 0) throw new InvalidOperationException("팬 수가 올바르지 않습니다.");
            var gain = Math.Max(0, end - start);
            return 6 * Math.Sqrt(end / 1000d) + 8 * Math.Sqrt(gain / 1000d) + Math.Min(20, 40d * gain / Math.Max(1, start));
        }
        public static double AttendanceScore(long total, double average, double occupancy) =>
            10 * Math.Sqrt(total / 1000d) + 8 * Math.Sqrt(average / 500d) + 20 * Math.Clamp((occupancy - .5) / .4, 0, 1.25);
        private static double Interest(GameSave save, WrestlerState w)
        {
            var a = save.Promotion.Audience; var total = a?.TotalFollowers ?? 0;
            var responses = new[] { w.FanReaction.MarkResponse.Interest, w.FanReaction.CasualResponse.Interest, w.FanReaction.HardcoreResponse.Interest };
            return total > 0 ? Enumerable.Range(0, 3).Sum(i => responses[i] * (double)FanAudienceService.Group(a, i).Followers / total) : responses.Average();
        }
        private static PromotionQuarterRecord Calculate(GameSave save, GameDate end, string applyingShowId, long milestone, long ppv, long before)
        {
            var state = save.Promotion.QuarterlyGrowth;
            var rows = save.Shows.Where(s => s.Date.CompareTo(state.OpeningDate) >= 0 && s.Date.CompareTo(end) <= 0 &&
                !save.IsCancelledShow(s) && (s.Status == ShowStatus.Completed || s.Id == applyingShowId))
                .Select(s => save.ShowResults.SingleOrDefault(r => r.ShowId == s.Id && r.ShowVersion == s.ShowVersion && r.HasPrestigeSnapshot))
                .Where(r => r != null && !state.BaselineResultIds.Contains(r.Id)).ToList();
            var weight = rows.Sum(r => (double)TicketPricingRules.GetShowTypeMultiplier(r.PrestigeShowType));
            var total = rows.Sum(r => r.FinancialSettlement.Attendance);
            var average = weight > 0 ? rows.Sum(r => r.FinancialSettlement.Attendance * (double)TicketPricingRules.GetShowTypeMultiplier(r.PrestigeShowType)) / weight : 0;
            var occupancy = weight > 0 ? rows.Sum(r => (double)r.FinancialSettlement.Attendance / r.PrestigeVenueCapacity * (double)TicketPricingRules.GetShowTypeMultiplier(r.PrestigeShowType)) / weight : 0;
            var star = save.Wrestlers.Where(w => w.PromotionId == save.Promotion.Id && w.Roster.ActivityState is RosterActivityState.Active or RosterActivityState.Suspended)
                .Sum(w => Math.Max(0, Interest(save, w) - 50));
            var fans = save.Promotion.Audience?.TotalFollowers ?? 0;
            var record = new PromotionQuarterRecord { StartDate = state.OpeningDate, EndDate = end,
                OpeningFollowers = state.OpeningFollowers, ClosingFollowers = fans, CompletedShows = rows.Count, TotalAttendance = total,
                WeightedAttendance = average, WeightedOccupancy = occupancy, StarPower = star,
                BaseScore = rows.Count > 0 ? 10 : 0, FanScore = Round(FanScore(state.OpeningFollowers, fans)),
                AttendanceScore = rows.Count > 0 ? Round(AttendanceScore(total, average, occupancy)) : 0,
                RosterScore = Round(3 * Math.Sqrt(star)), MilestoneBonus = milestone, PpvBonus = ppv, PrestigeBefore = before };
            record.PrestigeAfter = checked(before + record.TotalGain); return record;
        }
        public static PromotionQuarterRecord Preview(GameSave save)
        {
            if (save?.Promotion?.QuarterlyGrowth?.IsInitialized != true) return null;
            var state = save.Promotion.QuarterlyGrowth;
            if (save.CurrentDate.CompareTo(state.OpeningDate) < 0) return null;
            var paid = save.Promotion.AppliedPrestigeReasonIds;
            var milestone = save.CareerEvents.Where(x => x.Kind == CareerEventKind.Fame && x.FamePromotionBonus > 0 &&
                x.Date.CompareTo(save.CurrentDate) <= 0 && !paid.Contains(x.Id)).Sum(x => (long)x.FamePromotionBonus);
            var ppv = save.Shows.Where(x => x.Status == ShowStatus.Completed && !save.IsCancelledShow(x) && x.Date.CompareTo(save.CurrentDate) <= 0 &&
                !paid.Contains(PromotionPrestigeRules.PpvReason(x.Id)))
                .Select(x => save.ShowResults.SingleOrDefault(r => r.ShowId == x.Id && r.ShowVersion == x.ShowVersion && r.HasPrestigeSnapshot))
                .Where(x => x != null).Sum(x => (long)x.PpvPrestigeBonus);
            return Calculate(save, save.CurrentDate, null, milestone, ppv, save.Promotion.PromotionPrestige);
        }
        public static void Settle(GameSave save, GameDate end, string applyingShowId, long before, long milestone, long ppv)
        {
            var state = save.Promotion.QuarterlyGrowth;
            if (state?.IsInitialized != true || end.CompareTo(state.OpeningDate) < 0) return;
            if (state.History.Any(r => r.EndDate.Equals(end))) throw new InvalidOperationException("분기 명성은 이미 정산되었습니다.");
            var record = Calculate(save, end, applyingShowId, milestone, ppv, before);
            save.Promotion.PromotionPrestige = record.PrestigeAfter; state.History.Add(record);
            state.OpeningDate = end.AddDays(1); state.OpeningFollowers = record.ClosingFollowers; state.BaselineResultIds.Clear();
        }
        public static void Validate(GameSave save)
        {
            var state = save.Promotion.QuarterlyGrowth; if (state?.IsInitialized != true) return;
            if (!ValidDate(state.OpeningDate) || state.OpeningFollowers < 0 || state.History == null || state.BaselineResultIds == null ||
                state.History.Any(x => x == null) || state.History.Select(x => x.EndDate).Distinct().Count() != state.History.Count)
                throw new InvalidOperationException("분기 명성 집계 상태가 올바르지 않습니다.");
            try
            {
                PromotionQuarterRecord previous = null;
                foreach (var r in state.History)
                {
                    if (!ValidDate(r.StartDate) || !ValidDate(r.EndDate) || r.StartDate.CompareTo(r.EndDate) > 0 || r.EndDate.CompareTo(save.CurrentDate) > 0 || !EndOfQuarter(r.EndDate).Equals(r.EndDate) ||
                        r.OpeningFollowers < 0 || r.ClosingFollowers < 0 || r.TotalAttendance < 0 || r.CompletedShows < 0 ||
                        r.BaseScore < 0 || r.FanScore < 0 || r.AttendanceScore < 0 || r.RosterScore < 0 || r.MilestoneBonus < 0 || r.PpvBonus < 0 ||
                        r.PrestigeBefore < 0 || r.PrestigeAfter < r.PrestigeBefore || r.PrestigeAfter - r.PrestigeBefore != r.TotalGain ||
                        !double.IsFinite(r.WeightedAttendance) || r.WeightedAttendance < 0 || !double.IsFinite(r.WeightedOccupancy) || r.WeightedOccupancy < 0 ||
                        !double.IsFinite(r.StarPower) || r.StarPower < 0)
                        throw new InvalidOperationException("분기 명성 결산 기록이 올바르지 않습니다.");
                    if (r.BaseScore != (r.CompletedShows > 0 ? 10 : 0) || r.FanScore != Round(FanScore(r.OpeningFollowers, r.ClosingFollowers)) ||
                        r.AttendanceScore != (r.CompletedShows > 0 ? Round(AttendanceScore(r.TotalAttendance, r.WeightedAttendance, r.WeightedOccupancy)) : 0) ||
                        r.RosterScore != Round(3 * Math.Sqrt(r.StarPower)) ||
                        previous != null && (!r.StartDate.Equals(previous.EndDate.AddDays(1)) || r.PrestigeBefore != previous.PrestigeAfter || r.OpeningFollowers != previous.ClosingFollowers))
                        throw new InvalidOperationException("분기 명성의 계산 근거나 연속 기록이 일치하지 않습니다.");
                    previous = r;
                }
                if (previous != null && (!state.OpeningDate.Equals(previous.EndDate.AddDays(1)) || state.OpeningFollowers != previous.ClosingFollowers || save.Promotion.PromotionPrestige < previous.PrestigeAfter))
                    throw new InvalidOperationException("분기 명성의 다음 집계 기준이 일치하지 않습니다.");
            }
            catch (OverflowException exception) { throw new InvalidOperationException("분기 명성 결산 수치가 너무 큽니다.", exception); }
        }
        private static bool ValidDate(GameDate date)
        { try { _ = new DateTime(date.Year, date.Month, date.Day); return true; } catch (ArgumentOutOfRangeException) { return false; } }
    }
}
