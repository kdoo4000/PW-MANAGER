using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public sealed class DashboardFinanceView
    {
        public VisualElement Root { get; }
        public string SelectedTab { get; private set; } = "summary";
        private readonly VisualElement body, tabs;
        private GameSave save;
        private TransactionType? category;
        private string ledgerPeriod = "이번 시즌";
        private static readonly string[] Keys = { "summary", "income", "expense", "wages", "sponsors", "projection" };
        private static readonly string[] Titles = { "요약", "수입", "지출", "급여", "스폰서", "전망" };

        public DashboardFinanceView(VisualElement workspace)
        {
            workspace.Q("finance-content")?.RemoveFromHierarchy();
            Root = new VisualElement { name = "finance-content" }; Root.AddToClassList("finance-screen");
            Root.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/DashboardFinance")); workspace.Add(Root);
            Root.style.display = DisplayStyle.None;
            tabs = Element(Root, "finance-tabs");
            for (var i = 0; i < Keys.Length; i++)
            {
                var key = Keys[i]; var button = new Button(() => SelectTab(key)) { name = "finance-tab-" + key, text = Titles[i] };
                button.AddToClassList("finance-tab"); tabs.Add(button);
            }
            body = Element(Root, "finance-body"); body.name = "finance-body";
            Root.RegisterCallback<GeometryChangedEvent>(evt => Root.EnableInClassList("finance-narrow", evt.newRect.width < 1000));
        }

        public void Render(GameSave value) { save = value; if (Root.resolvedStyle.display != DisplayStyle.None) RenderBody(); }
        public void Open(GameSave value) { save = value; Root.style.display = DisplayStyle.Flex; RenderBody(); }
        public void SelectTab(string tab)
        {
            if (!Keys.Contains(tab)) throw new ArgumentException("Unknown finance tab.", nameof(tab));
            SelectedTab = tab; category = null; RenderBody();
        }

        private void RenderBody()
        {
            body.Clear(); if (save?.Promotion == null) return;
            foreach (var key in Keys) tabs.Q<Button>("finance-tab-" + key).EnableInClassList("active", key == SelectedTab);
            if (SelectedTab == "sponsors")
            {
                var season = save.SponsorSeasons?.LastOrDefault(x => x.PolicyId == save.SeasonPolicy?.Id && x.SelectionConfirmed);
                if (season == null) Text(body, "이번 시즌에 선택한 스폰서가 없습니다.", "finance-empty");
                else Sponsors(season);
                return;
            }
            var scroll = new ScrollView { name = "finance-scroll" }; scroll.AddToClassList("finance-scroll"); body.Add(scroll);
            if (SelectedTab == "summary") Summary(scroll);
            else if (SelectedTab == "income" || SelectedTab == "expense") Breakdown(scroll, SelectedTab == "income");
            else if (SelectedTab == "wages") Wages(scroll);
            else Projection(scroll);
        }

        private void Sponsors(SponsorSeasonState season)
        {
            var scroll = new ScrollView { name = "finance-sponsors-scroll" }; scroll.AddToClassList("finance-scroll"); body.Add(scroll);
            var columns = Element(scroll, "finance-sponsor-columns");
            foreach (var offer in season.Offers.Where(x => x.Selected))
            {
                var column = Element(columns, "finance-sponsor-column"); column.name = "finance-sponsor-" + offer.CompanyId;
                var info = Element(column, "finance-sponsor-info");
                var logo = new Image { image = Resources.Load<Texture2D>(offer.LogoResource), scaleMode = ScaleMode.ScaleToFit };
                logo.AddToClassList("finance-sponsor-logo"); info.Add(logo);
                Text(info, offer.CompanyName, "finance-sponsor-name");
                Text(info, offer.Description, "finance-sponsor-description");
                Pair(info, "난이도", offer.Difficulty == 1 ? "쉬움" : offer.Difficulty == 2 ? "보통" : "어려움");
                Pair(info, "기본금 수령", Money(offer.BasePayment));
                Pair(info, offer.RewardPaid ? "달성금 지급 완료" : "달성금", Money(offer.CompletionPayment));
                Text(info, $"{Date(season.StartDate)} ~ {Date(season.EndDate)}", "finance-sponsor-period");
                var heading = Element(column, "finance-sponsor-mission-heading");
                Text(heading, "임무", "finance-sponsor-section-title");
                var status = Text(heading, SponsorSelectionView.Status(offer), "finance-sponsor-status");
                status.EnableInClassList("failed", offer.Failed); status.EnableInClassList("paid", offer.RewardPaid);
                Text(column, $"완료 목표 {offer.Goals.Count(x => x.Progress >= x.Target)} / {offer.Goals.Count}", "finance-sponsor-count");
                for (var i = 0; i < offer.Goals.Count; i++)
                {
                    var goal = offer.Goals[i]; var block = Element(column, "finance-sponsor-goal");
                    Text(block, $"목표 {i + 1}", "finance-muted");
                    Text(block, SponsorSelectionView.GoalText(goal), "finance-sponsor-goal-text");
                    if (!string.IsNullOrEmpty(goal.WrestlerId))
                    {
                        var wrestler = save.Wrestlers.FirstOrDefault(x => x.Id == goal.WrestlerId);
                        if (wrestler != null) block.Add(WrestlerProfileController.CreateLink(wrestler));
                    }
                    var progress = new ProgressBar { name = "finance-sponsor-progress-" + offer.CompanyId + "-" + i,
                        lowValue = 0, highValue = 100, value = (float)Math.Clamp((double)goal.Progress / Math.Max(1L, goal.Target) * 100, 0, 100),
                        title = "", tooltip = $"{goal.Progress:N0} / {goal.Target:N0}" };
                    progress.AddToClassList("finance-sponsor-progress"); progress.EnableInClassList("failed", offer.Failed); block.Add(progress);
                    Text(block, $"{goal.Progress:N0} / {goal.Target:N0}" + (goal.Progress >= goal.Target ? " · 달성" : ""), "finance-sponsor-goal-value");
                }
            }
        }

        private void Summary(VisualElement host)
        {
            var ledger = FinanceReportService.Transactions(save); var month = FinanceReportService.MonthStart(save.CurrentDate);
            var current = ledger.Where(x => x.Date.CompareTo(month) >= 0).ToList();
            var income = current.Where(x => x.Amount > 0).Sum(x => x.Amount); var expense = -current.Where(x => x.Amount < 0).Sum(x => x.Amount);
            var metrics = Element(host, "finance-metrics");
            Metric(metrics, "현재 잔액", Money(save.Promotion.InitialCash + ledger.Sum(x => x.Amount)));
            Metric(metrics, "이번 달 손익", Signed(income - expense)); Metric(metrics, "이번 달 수입", Money(income)); Metric(metrics, "이번 달 지출", Money(expense));
            var columns = Element(host, "finance-columns"); var main = Element(columns, "finance-main"); var side = Element(columns, "finance-side");
            var history = FinanceReportService.History(save);
            Chart(Card(main, "잔액 추이"), history, false);
            var flow = Card(main, "월별 수입·지출");
            var legend = Element(flow, "finance-flow-legend"); Text(legend, "수입", "finance-income-key"); Text(legend, "지출", "finance-expense-key"); Chart(flow, history, true);
            var obligations = Card(side, "지급 예정·미지급금");
            var next = FinanceReportService.AddMonths(save.CurrentDate, 1);
            Pair(obligations, $"{next.Month}월 1일 기본급", Money(FinanceReportService.ContractsDue(save, next).Sum(x => x.MonthlySalary)));
            Pair(obligations, "미지급 급여", Money(SalaryPaymentService.Outstanding(save)));
            var sponsors = save.SponsorSeasons?.LastOrDefault(x => x.PolicyId == save.SeasonPolicy?.Id && x.SelectionConfirmed);
            Pair(obligations, "조건부 스폰서 보상", Money(sponsors?.Offers.Where(x => x.Selected && !x.Failed && !x.RewardPaid).Sum(x => x.CompletionPayment) ?? 0));
            var sponsorButton = new Button(() => SelectTab("sponsors")) { text = "스폰서 임무 확인" }; sponsorButton.AddToClassList("finance-action"); obligations.Add(sponsorButton);
            var goods = Card(side, "이번 달 굿즈 판매");
            var sales = (save.Merchandise?.Sales ?? new()).Where(x => x.Date.CompareTo(month) >= 0 && x.Date.CompareTo(save.CurrentDate) <= 0).ToList();
            Pair(goods, "온라인 판매", $"{sales.Where(x => x.Channel == MerchandiseChannel.Online).Sum(x => x.Units):N0}장");
            Pair(goods, "현장 판매", $"{sales.Where(x => x.Channel == MerchandiseChannel.Venue).Sum(x => x.Units):N0}장");
            Pair(goods, "단체 순수익", Money(sales.Sum(x => x.Net)));
            var goodsButton = new Button(() => { SelectedTab = "income"; category = TransactionType.MerchandiseRevenue; RenderBody(); }) { text = "굿즈 판매 내역" };
            goodsButton.AddToClassList("finance-action"); goods.Add(goodsButton);
            var composition = Card(side, "이번 달 수입 구성"); Composition(composition, current, true);
            composition = Card(side, "이번 달 지출 구성"); Composition(composition, current, false);
        }

        private void Composition(VisualElement host, IEnumerable<TransactionRecord> ledger, bool income)
        {
            var groups = ledger.Where(x => income ? x.Amount > 0 : x.Amount < 0).GroupBy(x => x.Type)
                .Select(g => new { Type = g.Key, Amount = g.Sum(x => Math.Abs(x.Amount)) }).OrderByDescending(x => x.Amount).ToList();
            if (groups.Count == 0) { Text(host, "거래 내역이 없습니다.", "finance-muted"); return; }
            foreach (var group in groups) Pair(host, FinanceReportService.Category(group.Type), Money(group.Amount));
        }

        private (GameDate Start, GameDate End)[] Periods()
        {
            var start = save.SeasonPolicy?.StartDate ?? FinanceReportService.MonthStart(save.CurrentDate);
            var end = save.SeasonPolicy?.EndDate.AddDays(1) ?? save.CurrentDate.AddDays(1);
            var prior = save.SeasonResults?.Where(x => x?.Policy != null && x.Policy.EndDate.CompareTo(start) < 0).OrderBy(x => x.Policy.EndDate).LastOrDefault();
            return new[] { (FinanceReportService.MonthStart(save.CurrentDate), FinanceReportService.AddMonths(save.CurrentDate, 1)),
                (FinanceReportService.AddMonths(save.CurrentDate, -1), FinanceReportService.MonthStart(save.CurrentDate)),
                (start, end), (prior?.Policy.StartDate ?? start, prior?.Policy.EndDate.AddDays(1) ?? start) };
        }

        private void Breakdown(VisualElement host, bool income)
        {
            var ledger = FinanceReportService.Transactions(save).Where(x => income ? x.Amount > 0 : x.Amount < 0).ToList();
            var periods = Periods(); var card = Card(host, income ? "수입 내역" : "지출 내역");
            TableRow(card, new[] { "항목", "이번 달", "지난달", "이번 시즌", "지난 시즌" }, true);
            foreach (var group in ledger.GroupBy(x => x.Type).OrderByDescending(g => g.Sum(x => Math.Abs(x.Amount))))
            {
                var key = group.Key;
                var row = new Button(() => { category = key; RenderBody(); }) { name = "finance-category-" + key };
                row.AddToClassList("finance-table-row"); row.AddToClassList("finance-category"); card.Add(row);
                Cell(row, FinanceReportService.Category(key), true);
                foreach (var period in periods) Cell(row, Money(group.Where(x => x.Date.CompareTo(period.Start) >= 0 && x.Date.CompareTo(period.End) < 0).Sum(x => Math.Abs(x.Amount))));
            }
            if (ledger.Count == 0) Text(card, "거래 내역이 없습니다.", "finance-empty");
            TableRow(card, new[] { "합계" }.Concat(periods.Select(p => Money(ledger.Where(x => x.Date.CompareTo(p.Start) >= 0 && x.Date.CompareTo(p.End) < 0).Sum(x => Math.Abs(x.Amount))))).ToArray(), true);
            var detail = Card(host, category.HasValue ? FinanceReportService.Category(category.Value) + " 거래 기록" : "거래 기록");
            var controls = Element(detail, "finance-controls");
            var periodField = new DropdownField(new List<string> { "이번 달", "지난달", "이번 시즌", "지난 시즌" }, ledgerPeriod) { name = "finance-ledger-period" };
            controls.Add(periodField); periodField.RegisterValueChangedCallback(evt => { ledgerPeriod = evt.newValue; RenderBody(); });
            if (category.HasValue) { var clear = new Button(() => { category = null; RenderBody(); }) { text = "전체 항목" }; clear.AddToClassList("finance-action"); controls.Add(clear); }
            var index = new[] { "이번 달", "지난달", "이번 시즌", "지난 시즌" }.ToList().IndexOf(ledgerPeriod); var range = periods[index];
            var entries = ledger.Where(x => (!category.HasValue || x.Type == category) && x.Date.CompareTo(range.Start) >= 0 && x.Date.CompareTo(range.End) < 0).OrderByDescending(x => x.Date).ThenBy(x => x.Id).ToList();
            TableRow(detail, new[] { "일자", "항목", "금액" }, true);
            foreach (var entry in entries) TableRow(detail, new[] { Date(entry.Date), FinanceReportService.Category(entry.Type), Money(Math.Abs(entry.Amount)) });
            if (entries.Count == 0) Text(detail, "선택한 기간에 거래가 없습니다.", "finance-empty");
            if (income && category == TransactionType.MerchandiseRevenue)
            {
                var goods = Card(host, "선수별 굿즈 판매"); TableRow(goods, new[] { "선수", "일자", "판매처", "수량", "매출", "단체 순수익" }, true);
                foreach (var sale in (save.Merchandise?.Sales ?? new()).Where(x => x.Date.CompareTo(range.Start) >= 0 && x.Date.CompareTo(range.End) < 0).OrderByDescending(x => x.Date))
                {
                    var row = Element(goods, "finance-table-row"); var name = Element(row, "finance-cell finance-first");
                    var wrestler = save.Wrestlers.FirstOrDefault(x => x.Id == sale.WrestlerId);
                    if (wrestler != null) name.Add(WrestlerProfileController.CreateLink(wrestler));
                    Cell(row, Date(sale.Date)); Cell(row, sale.Channel == MerchandiseChannel.Online ? "온라인" : "현장");
                    Cell(row, $"{sale.Units:N0}장"); Cell(row, Money(sale.Gross)); Cell(row, Money(sale.Net));
                }
            }
        }

        private void Wages(VisualElement host)
        {
            var next = FinanceReportService.AddMonths(save.CurrentDate, 1); var contracts = FinanceReportService.ContractsDue(save, next);
            var metrics = Element(host, "finance-metrics"); Metric(metrics, $"{next.Month}월 1일 예정 기본급", Money(contracts.Sum(x => x.MonthlySalary)));
            Metric(metrics, "미지급 급여", Money(SalaryPaymentService.Outstanding(save)));
            var card = Card(host, "계약 역할별 월 기본급");
            foreach (var group in contracts.Where(x => x.Type == ContractType.Wrestler).GroupBy(x => x.HasExpectedRole ? (int)x.ExpectedRole : -1).OrderBy(x => x.Key))
                Pair(card, group.Key == 0 ? "자버" : group.Key == 1 ? "미드카더" : group.Key == 2 ? "메인이벤터" : "역할 미지정", Money(group.Sum(x => x.MonthlySalary)));
            card = Card(host, "계약별 급여"); TableRow(card, new[] { "선수·매니저", "계약 종료", "월 기본급", "경기 수당", "프로모 수당", "미지급" }, true);
            foreach (var contract in save.OwnContracts.Where(x => x != null && (x.Covers(save.CurrentDate) || x.Covers(next) || save.SalaryPayments.Any(p => p.ContractId == x.Id && p.RemainingAmount > 0))).OrderByDescending(x => x.MonthlySalary))
            {
                var row = Element(card, "finance-table-row"); var person = Element(row, "finance-cell finance-first");
                var wrestler = save.Wrestlers.FirstOrDefault(x => x.Id == contract.PersonId);
                if (contract.Type == ContractType.Wrestler && wrestler != null) person.Add(WrestlerProfileController.CreateLink(wrestler));
                else Text(person, save.Managers.FirstOrDefault(x => x.Id == contract.PersonId)?.Name ?? "계약 대상", "finance-person");
                Cell(row, Date(contract.EndDate)); Cell(row, Money(contract.MonthlySalary));
                Cell(row, Money(contract.MatchAppearanceFee)); Cell(row, Money(contract.PromoAppearanceFee));
                Cell(row, Money(save.SalaryPayments.Where(x => x.ContractId == contract.Id).Sum(x => x.RemainingAmount)));
            }
        }

        private void Projection(VisualElement host)
        {
            var months = FinanceReportService.SalaryProjection(save);
            var card = Card(host, "기본급 기준 12개월 전망");
            Text(card, "미지급 급여와 월 기본급만 반영 · 출연 수당과 향후 수입·기타 지출 제외", "finance-legend");
            Chart(card, months, false);
            card = Card(host, "월별 예정 급여와 잔액"); TableRow(card, new[] { "월", "예정 급여", "급여 반영 후 잔액" }, true);
            foreach (var month in months) TableRow(card, new[] { $"{month.Date.Year}.{month.Date.Month:D2}", Money(month.Expense), Money(month.Balance) });
        }

        private static void Chart(VisualElement host, IReadOnlyList<FinanceReportService.Month> months, bool flow)
        {
            var plot = Element(host, "finance-plot"); var axis = Element(plot, "finance-axis");
            var maximum = Math.Max(1d, flow ? months.Select(x => (double)Math.Max(x.Income, x.Expense)).DefaultIfEmpty(0).Max() : months.Select(x => Math.Abs((double)x.Balance)).DefaultIfEmpty(0).Max());
            Text(axis, Money((long)maximum), "finance-axis-label");
            Text(axis, !flow && months.Any(x => x.Balance < 0) ? Money(-(long)maximum) : "$0", "finance-axis-label");
            var chart = new FinanceChart(months, flow) { name = flow ? "finance-flow-chart" : "finance-balance-chart" }; plot.Add(chart);
            var labels = Element(host, "finance-chart-labels");
            for (var i = 0; i < months.Count; i++) Text(labels, i == 0 || months[i].Date.Month == 1 ? $"{months[i].Date.Year % 100:D2}.{months[i].Date.Month}" : $"{months[i].Date.Month}월", "finance-month");
        }
        private static VisualElement Element(VisualElement host, string classes) { var element = new VisualElement(); foreach (var cls in classes.Split(' ')) element.AddToClassList(cls); host.Add(element); return element; }
        private static Label Text(VisualElement host, string value, string cls) { var label = new Label(value); label.AddToClassList(cls); host.Add(label); return label; }
        private static VisualElement Card(VisualElement host, string title) { var card = Element(host, "finance-card"); Text(card, title, "finance-card-title"); return card; }
        private static void Metric(VisualElement host, string title, string value) { var card = Element(host, "finance-metric"); Text(card, title, "finance-muted"); Text(card, value, "finance-value"); }
        private static void Pair(VisualElement host, string title, string value) { var row = Element(host, "finance-pair"); Text(row, title, "finance-muted"); Text(row, value, "finance-amount"); }
        private static void TableRow(VisualElement host, string[] values, bool heading = false) { var row = Element(host, "finance-table-row" + (heading ? " finance-table-heading" : "")); for (var i = 0; i < values.Length; i++) Cell(row, values[i], i == 0); }
        private static void Cell(VisualElement row, string value, bool first = false) => Text(row, value, "finance-cell").EnableInClassList("finance-first", first);
        private static string Money(long amount) => amount < 0 ? $"−${-(decimal)amount:N0}" : $"${amount:N0}";
        private static string Signed(long amount) => amount > 0 ? "+" + Money(amount) : Money(amount);
        private static string Date(GameDate date) => $"{date.Year}.{date.Month:D2}.{date.Day:D2}";
    }

    internal sealed class FinanceChart : VisualElement
    {
        private readonly IReadOnlyList<FinanceReportService.Month> months;
        private readonly bool flow;
        public FinanceChart(IReadOnlyList<FinanceReportService.Month> months, bool flow)
        {
            this.months = months; this.flow = flow; AddToClassList("finance-chart");
            tooltip = string.Join("\n", months.Select(m => $"{m.Date.Year}.{m.Date.Month:D2} · 잔액 ${m.Balance:N0} · 수입 ${m.Income:N0} · 지출 ${m.Expense:N0}"));
            generateVisualContent += Draw;
        }
        private void Draw(MeshGenerationContext context)
        {
            if (months.Count == 0 || contentRect.width <= 0) return;
            var p = context.painter2D; var width = contentRect.width; var height = contentRect.height - 8;
            var maximum = Math.Max(1d, flow ? months.Max(x => Math.Max(x.Income, x.Expense)) : months.Max(x => Math.Abs((double)x.Balance)));
            var minimum = !flow && months.Any(x => x.Balance < 0) ? -maximum : 0;
            Func<double, float> y = value => (float)(height - (value - minimum) / (maximum - minimum) * (height - 8));
            p.lineWidth = 1; p.strokeColor = new Color32(57, 57, 62, 255);
            for (var i = 0; i < 4; i++) { var lineY = 8 + i * (height - 8) / 3; p.BeginPath(); p.MoveTo(new Vector2(0, lineY)); p.LineTo(new Vector2(width, lineY)); p.Stroke(); }
            if (flow)
            {
                var step = width / months.Count;
                for (var i = 0; i < months.Count; i++)
                {
                    var x = step * (i + .5f); var bar = Math.Min(14f, step * .26f);
                    p.lineWidth = bar;
                    p.strokeColor = new Color32(124, 178, 139, 255); p.BeginPath(); p.MoveTo(new Vector2(x - bar * .6f, y(0))); p.LineTo(new Vector2(x - bar * .6f, y(months[i].Income))); p.Stroke();
                    p.strokeColor = new Color32(203, 139, 123, 255); p.BeginPath(); p.MoveTo(new Vector2(x + bar * .6f, y(0))); p.LineTo(new Vector2(x + bar * .6f, y(months[i].Expense))); p.Stroke();
                }
            }
            else
            {
                p.lineWidth = 2; p.strokeColor = new Color32(217, 180, 91, 255); p.BeginPath();
                for (var i = 0; i < months.Count; i++) { var point = new Vector2(width * (i + .5f) / months.Count, y(months[i].Balance)); if (i == 0) p.MoveTo(point); else p.LineTo(point); }
                if (months.Count == 1) p.LineTo(new Vector2(width * .5f + 2, y(months[0].Balance)));
                p.Stroke();
            }
        }
    }
}
