using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public static class FinanceReportService
    {
        public sealed class Month
        {
            public GameDate Date;
            public long Income, Expense, Balance;
            public long Net => Income - Expense;
        }

        public static IReadOnlyList<TransactionRecord> Transactions(GameSave save) =>
            (save.Transactions ?? new()).Where(x => x != null && !x.IsVoided && x.PromotionId == save.Promotion.Id &&
                x.Date.CompareTo(save.CurrentDate) <= 0).OrderBy(x => x.Date).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();

        public static GameDate MonthStart(GameDate date) => new(date.Year, date.Month, 1);
        public static GameDate AddMonths(GameDate date, int count)
        {
            var next = new DateTime(date.Year, date.Month, 1).AddMonths(count);
            return new GameDate(next.Year, next.Month, 1);
        }

        public static IReadOnlyList<Month> History(GameSave save)
        {
            var ledger = Transactions(save);
            var first = AddMonths(MonthStart(save.CurrentDate), -11);
            var result = new List<Month>();
            for (var i = 0; i < 12; i++)
            {
                var start = AddMonths(first, i); var end = AddMonths(start, 1);
                // Do not invent cash history before the campaign existed.
                var campaignStart = (save.SeasonResults ?? new()).Where(x => x?.Policy != null).Select(x => x.Policy.StartDate).Append(save.SeasonPolicy?.StartDate ?? save.CurrentDate).Min();
                if (end.CompareTo(campaignStart) <= 0) continue;
                var entries = ledger.Where(x => x.Date.CompareTo(start) >= 0 && x.Date.CompareTo(end) < 0).ToList();
                result.Add(new Month { Date = start, Income = entries.Where(x => x.Amount > 0).Sum(x => x.Amount),
                    Expense = -entries.Where(x => x.Amount < 0).Sum(x => x.Amount),
                    Balance = checked(save.Promotion.InitialCash + ledger.Where(x => x.Date.CompareTo(end) < 0).Sum(x => x.Amount)) });
            }
            return result;
        }

        public static IReadOnlyList<ContractState> ContractsDue(GameSave save, GameDate date) =>
            save.OwnContracts.Where(x => x != null && x.Covers(date) && x.MonthlySalary > 0).ToList();

        public static IReadOnlyList<Month> SalaryProjection(GameSave save)
        {
            var cash = checked(save.Promotion.InitialCash + Transactions(save).Sum(x => x.Amount) - SalaryPaymentService.Outstanding(save));
            var result = new List<Month>();
            for (var i = 1; i <= 12; i++)
            {
                var date = AddMonths(save.CurrentDate, i);
                var expense = ContractsDue(save, date).Sum(x => x.MonthlySalary);
                cash = checked(cash - expense);
                result.Add(new Month { Date = date, Expense = expense, Balance = cash });
            }
            return result;
        }

        public static string Category(TransactionType type) => type switch
        {
            TransactionType.SigningFee => "계약금", TransactionType.Salary => "기본급", TransactionType.LatePayment => "연체 급여 상환",
            TransactionType.MatchAppearanceFee => "경기 수당", TransactionType.PromoAppearanceFee => "프로모 수당",
            TransactionType.StaffDepartmentUpgrade => "스태프 투자", TransactionType.VenueUnlock => "경기장 해금",
            TransactionType.FacilityRent => "시설 이용", TransactionType.VenueCost => "쇼 개최비",
            TransactionType.TicketRevenue => "티켓", TransactionType.BroadcastRevenue => "방송", TransactionType.SponsorRevenue => "스폰서",
            TransactionType.MerchandiseRevenue => "굿즈", TransactionType.MarketingCost => "홍보", TransactionType.MedicalCost => "의료",
            TransactionType.PerformanceBonus => "출전 보너스", TransactionType.TerminationFee => "해지 보상금", TransactionType.Refund => "환불",
            TransactionType.TrainingFacilityUpgrade => "훈련 시설 투자", TransactionType.TrainerUpgrade => "트레이너 투자",
            TransactionType.MerchandiseProductionCost => "굿즈 제작비", TransactionType.MerchandiseSalesCost => "굿즈 판매 비용",
            TransactionType.MerchandiseRoyalty => "굿즈 선수 배분금", _ => type.ToString()
        };
    }
}
