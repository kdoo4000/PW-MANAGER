using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    internal sealed class DashboardScheduleController
    {
        private readonly VisualElement root;
        private readonly Func<ShowState, string> venueName;
        private readonly Action<string> openShow;
        private readonly Dictionary<int, TextField> ppvTitleFields = new();
        private readonly Dictionary<int, IntegerField> ppvDurationFields = new();
        private IntegerField regularDurationField;
        private GameSave save;
        private bool editingRegularTitles;

        public DashboardScheduleController(VisualElement root, Func<ShowState, string> venueName, Action<string> openShow)
        {
            this.root = root;
            this.venueName = venueName;
            this.openShow = openShow;
            root.Q<Button>("schedule-regular-title-edit").clicked += () => OpenTitleEditor(true);
            root.Q<Button>("schedule-ppv-title-edit").clicked += () => OpenTitleEditor(false);
            root.Q<Button>("schedule-title-close").clicked += CloseTitleEditor;
            root.Q<Button>("schedule-title-cancel").clicked += CloseTitleEditor;
            root.Q<Button>("schedule-title-save").clicked += SaveTitles;
        }

        public void Render(GameSave save)
        {
            this.save = save;
            var regularHost = root.Q<VisualElement>("show-schedule-regular-rows");
            var ppvHost = root.Q<VisualElement>("show-schedule-ppv-rows");
            if (regularHost == null || ppvHost == null) return;
            regularHost.Clear();
            ppvHost.Clear();
            var shows = save?.Shows?.Where(x => x != null).OrderBy(x => x.Date.Year).ThenBy(x => x.Date.Month).ThenBy(x => x.Date.Day).ToList() ?? new List<ShowState>();
            var upcoming = save == null ? 0 : shows.Count(x => x.Status != ShowStatus.Completed && x.Date.CompareTo(save.CurrentDate) >= 0);
            Set("show-schedule-caption", shows.Count == 0 ? "생성된 쇼 일정이 없습니다" : $"{DashboardText.Date(shows.First().Date)}부터 {DashboardText.Date(shows.Last().Date)}까지");
            Set("show-schedule-upcoming", $"예정 {upcoming}");
            Set("show-schedule-completed", $"완료 {shows.Count(x => x.Status == ShowStatus.Completed)}");
            foreach (var show in shows)
            {
                var host = show.ShowType == ScheduledShowType.Regular ? regularHost : ppvHost;
                var events = save.ShowEvents?.Where(x => x != null && x.ShowId == show.Id).ToList() ?? new List<ShowEventState>();
                var row = new Button { name = $"show-schedule-{show.Id}" }; row.AddToClassList("show-schedule-row");
                var top = new VisualElement(); top.AddToClassList("show-schedule-row-top");
                var date = new Label($"{show.Date.Year}. {show.Date.Month:D2}. {show.Date.Day:D2}."); date.AddToClassList("show-schedule-date"); top.Add(date);
                var status = new Label(DashboardText.ShowStatus(show.Status)); status.AddToClassList("show-schedule-status"); status.EnableInClassList("open", show.Status != ShowStatus.Completed);
                top.Add(status); row.Add(top);
                var name = new Label(string.IsNullOrWhiteSpace(show.Name) ? DashboardText.ShowName(show.ShowType) : show.Name); name.AddToClassList("show-schedule-name"); row.Add(name);
                var meta = new Label($"{venueName(show)} · {show.CalculatePlannedDuration(events)}/{show.DurationLimit}분"); meta.AddToClassList("show-schedule-meta"); row.Add(meta);
                var footer = new VisualElement(); footer.AddToClassList("show-schedule-row-footer");
                AddMetric(footer, "편성", $"{events.Count}개 세그먼트");
                AddMetric(footer, "예상 비용", DashboardText.Money(show.EstimatedCost));
                var open = new Label("상세 보기"); open.AddToClassList("show-schedule-open"); footer.Add(open); row.Add(footer);
                var id = show.Id; row.clicked += () => openShow(id);
                host.Add(row);
            }
            AddEmptyState(regularHost, "예정된 정규 쇼가 없습니다");
            AddEmptyState(ppvHost, "예정된 PPV가 없습니다");
        }

        private void Set(string name, string value) { var label = root.Q<Label>(name); if (label != null) label.text = value; }

        private void OpenTitleEditor(bool regular)
        {
            editingRegularTitles = regular;
            ppvTitleFields.Clear();
            ppvDurationFields.Clear();
            var fields = root.Q<VisualElement>("schedule-title-fields");
            fields.Clear();
            Set("schedule-title-message", string.Empty);
            Set("schedule-title-dialog-title", regular ? "정규 쇼 설정" : "PPV 설정");
            if (regular)
            {
                var row = new VisualElement(); row.AddToClassList("schedule-title-row");
                var field = new TextField("정규 쇼 제목") { name = "schedule-regular-title-field" };
                field.AddToClassList("schedule-title-field");
                field.SetValueWithoutNotify(string.IsNullOrWhiteSpace(save.SeasonPolicy?.RegularShowName) ? "정규 쇼" : save.SeasonPolicy.RegularShowName);
                row.Add(field);
                regularDurationField = AddDurationControl(row, save.SeasonPolicy.RegularShowDurationMinutes);
                fields.Add(row);
            }
            else
            {
                save.SeasonPolicy.PpvShowTitles ??= new List<PpvTitlePolicyState>();
                foreach (var month in save.Shows.Where(x => x != null && x.ShowType != ScheduledShowType.Regular)
                    .OrderBy(x => x.Date.Year).ThenBy(x => x.Date.Month).ThenBy(x => x.Date.Day)
                    .Select(x => x.Date.Month).Distinct())
                {
                    var row = new VisualElement(); row.AddToClassList("schedule-title-row");
                    var policy = save.SeasonPolicy.PpvShowTitles.FirstOrDefault(x => x.Month == month);
                    var field = new TextField($"{month}월");
                    field.AddToClassList("schedule-title-field");
                    field.SetValueWithoutNotify(policy?.Title ?? $"{month}월 PPV");
                    ppvTitleFields[month] = field;
                    row.Add(field);
                    var duration = AddDurationControl(row, policy?.DurationMinutes ?? 120);
                    ppvDurationFields[month] = duration;
                    fields.Add(row);
                }
            }
            var overlay = root.Q<VisualElement>("schedule-title-overlay");
            overlay.BringToFront();
            overlay.RemoveFromClassList("hidden");
        }

        private void SaveTitles()
        {
            if (editingRegularTitles)
            {
                var title = root.Q<TextField>("schedule-regular-title-field")?.value?.Trim();
                if (string.IsNullOrEmpty(title)) { Set("schedule-title-message", "제목을 입력하세요"); return; }
                if (!ValidDuration(regularDurationField.value)) { Set("schedule-title-message", "시간은 60분 단위로 입력하세요"); return; }
                save.SeasonPolicy.RegularShowName = title;
                save.SeasonPolicy.RegularShowDurationMinutes = regularDurationField.value;
            }
            else
            {
                var titles = ppvTitleFields.ToDictionary(x => x.Key, x => x.Value.value?.Trim());
                if (titles.Values.Any(string.IsNullOrEmpty)) { Set("schedule-title-message", "모든 PPV 제목을 입력하세요"); return; }
                if (titles.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != titles.Count)
                { Set("schedule-title-message", "PPV 제목은 같은 시즌 안에서 서로 달라야 합니다"); return; }
                if (ppvDurationFields.Values.Any(x => !ValidDuration(x.value))) { Set("schedule-title-message", "시간은 60분 단위로 입력하세요"); return; }
                save.SeasonPolicy.PpvShowTitles = titles.Select(x => new PpvTitlePolicyState
                    { Month = x.Key, Title = x.Value, DurationMinutes = ppvDurationFields[x.Key].value }).ToList();
            }
            var shows = save.Shows.Where(x => x != null && (x.ShowType == ScheduledShowType.Regular) == editingRegularTitles)
                .OrderBy(x => x.Date.Year).ThenBy(x => x.Date.Month).ThenBy(x => x.Date.Day).ToList();
            for (var index = 0; index < shows.Count; index++)
            {
                var title = editingRegularTitles
                    ? save.SeasonPolicy.RegularShowName
                    : save.SeasonPolicy.PpvShowTitles.Single(x => x.Month == shows[index].Date.Month).Title;
                shows[index].Name = ShowNamingRules.Compose(save.Promotion.Abbreviation, title, shows[index].ShowType,
                    editingRegularTitles ? index + 1 : 0, shows[index].Date.Year);
                shows[index].DurationLimit = editingRegularTitles
                    ? save.SeasonPolicy.RegularShowDurationMinutes
                    : save.SeasonPolicy.PpvShowTitles.Single(x => x.Month == shows[index].Date.Month).DurationMinutes;
                var venue = save.VenueContracts.Single(x => x.Id == shows[index].VenueContractId);
                shows[index].EstimatedCost = venue.CalculateProductionCost(shows[index].DurationLimit);
            }
            DashboardSession.PersistActive();
            Render(save);
            CloseTitleEditor();
        }

        private void CloseTitleEditor() => root.Q<VisualElement>("schedule-title-overlay")?.AddToClassList("hidden");
        private static bool ValidDuration(int value) => value > 0 && value % 60 == 0;
        private static IntegerField AddDurationControl(VisualElement row, int value)
        {
            var control = new VisualElement(); control.AddToClassList("schedule-duration-control");
            var caption = new Label("시간"); caption.AddToClassList("schedule-duration-caption"); control.Add(caption);
            var decrease = new Button { text = "−", tooltip = "60분 줄이기" }; decrease.AddToClassList("schedule-duration-step"); control.Add(decrease);
            var field = new IntegerField { isReadOnly = true };
            field.AddToClassList("schedule-duration-field");
            field.SetValueWithoutNotify(value > 0 ? value : 120);
            control.Add(field);
            var unit = new Label("분"); unit.AddToClassList("schedule-duration-unit"); control.Add(unit);
            var increase = new Button { text = "+", tooltip = "60분 늘리기" }; increase.AddToClassList("schedule-duration-step"); control.Add(increase);
            decrease.clicked += () => { field.SetValueWithoutNotify(Math.Max(60, field.value - 60)); decrease.SetEnabled(field.value > 60); };
            increase.clicked += () => { field.SetValueWithoutNotify(field.value + 60); decrease.SetEnabled(true); };
            decrease.SetEnabled(field.value > 60);
            row.Add(control);
            return field;
        }
        private static void AddMetric(VisualElement row, string label, string value)
        {
            var metric = new VisualElement(); metric.AddToClassList("show-schedule-metric");
            var caption = new Label(label); caption.AddToClassList("show-schedule-metric-label"); metric.Add(caption);
            var content = new Label(value); content.AddToClassList("show-schedule-metric-value"); metric.Add(content); row.Add(metric);
        }

        private static void AddEmptyState(VisualElement host, string text)
        {
            if (host.childCount != 0) return;
            var empty = new Label(text); empty.AddToClassList("show-schedule-column-empty"); host.Add(empty);
        }
    }
}
