using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    internal sealed class DashboardScheduleController
    {
        private readonly VisualElement root;
        private readonly Func<ShowState, string> venueName;
        private readonly Action<string> openShow;

        public DashboardScheduleController(VisualElement root, Func<ShowState, string> venueName, Action<string> openShow)
        {
            this.root = root;
            this.venueName = venueName;
            this.openShow = openShow;
        }

        public void Render(GameSave save)
        {
            var host = root.Q<VisualElement>("show-schedule-rows");
            if (host == null) return;
            host.Clear();
            var shows = save?.Shows?.Where(x => x != null).OrderBy(x => x.Date.Year).ThenBy(x => x.Date.Month).ThenBy(x => x.Date.Day).ToList() ?? new List<ShowState>();
            var upcoming = save == null ? 0 : shows.Count(x => x.Status != ShowStatus.Completed && x.Date.CompareTo(save.CurrentDate) >= 0);
            Set("show-schedule-caption", shows.Count == 0 ? "생성된 쇼 일정이 없습니다" : $"{DashboardText.Date(shows.First().Date)}부터 {DashboardText.Date(shows.Last().Date)}까지");
            Set("show-schedule-upcoming", $"예정 {upcoming}");
            Set("show-schedule-completed", $"완료 {shows.Count(x => x.Status == ShowStatus.Completed)}");
            foreach (var show in shows)
            {
                var events = save.ShowEvents?.Where(x => x != null && x.ShowId == show.Id).ToList() ?? new List<ShowEventState>();
                var row = new Button { name = $"show-schedule-{show.Id}" }; row.AddToClassList("show-schedule-row");
                var date = new Label($"{show.Date.Month:D2}월 {show.Date.Day:D2}일\n{show.Date.Year}"); date.AddToClassList("show-schedule-date"); row.Add(date);
                var copy = new VisualElement(); copy.AddToClassList("show-schedule-copy");
                var name = new Label(string.IsNullOrWhiteSpace(show.Name) ? DashboardText.ShowName(show.ShowType) : show.Name); name.AddToClassList("show-schedule-name"); copy.Add(name);
                var meta = new Label($"{DashboardText.ShowType(show.ShowType)} · {venueName(show)} · {show.CalculatePlannedDuration(events)}/{show.DurationLimit}분"); meta.AddToClassList("show-schedule-meta"); copy.Add(meta); row.Add(copy);
                AddMetric(row, "편성", $"{events.Count}개 세그먼트");
                AddMetric(row, "예상 비용", DashboardText.Money(show.EstimatedCost));
                var status = new Label(DashboardText.ShowStatus(show.Status)); status.AddToClassList("show-schedule-status"); status.EnableInClassList("open", show.Status != ShowStatus.Completed); row.Add(status);
                var open = new Label("상세 보기"); open.AddToClassList("show-schedule-open"); row.Add(open);
                var id = show.Id; row.clicked += () => openShow(id);
                host.Add(row);
            }
            if (shows.Count == 0) { var empty = new Label("표시할 쇼 일정이 없습니다"); empty.AddToClassList("show-empty-state"); host.Add(empty); }
        }

        private void Set(string name, string value) { var label = root.Q<Label>(name); if (label != null) label.text = value; }
        private static void AddMetric(VisualElement row, string label, string value)
        {
            var metric = new VisualElement(); metric.AddToClassList("show-schedule-metric");
            var caption = new Label(label); caption.AddToClassList("show-schedule-metric-label"); metric.Add(caption);
            var content = new Label(value); content.AddToClassList("show-schedule-metric-value"); metric.Add(content); row.Add(metric);
        }
    }
}
