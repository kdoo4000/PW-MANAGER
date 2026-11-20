using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    internal sealed class DashboardInboxController
    {
        private readonly VisualElement root;
        private readonly Action showSchedule;
        private readonly Action<string> showPlanning;
        private readonly List<InboxMessageState> messages = new();
        private GameSave save;
        private int selectedIndex;
        private readonly DropdownField filter;

        public int UnreadCount => (save?.InboxMessages ?? new()).Count(x => NeedsResponse(save, x));

        internal static bool NeedsResponse(GameSave save, InboxMessageState message)
        {
            if (save == null || message == null || message.Status is InboxMessageStatus.Resolved or InboxMessageStatus.Expired) return false;
            if (message.SourceEventId?.StartsWith("wrestler-request:", StringComparison.Ordinal) == true)
                return save.WrestlerBehaviors.Any(s => s.PromotionId == save.Promotion.Id && s.WrestlerId == message.TargetId &&
                    s.Concerns.Any(c => c.Status == WrestlerConcernStatus.AwaitingResponse &&
                        message.SourceEventId == "wrestler-request:" + c.RequestId && c.ResponseDueDate.HasValue && save.CurrentDate.CompareTo(c.ResponseDueDate.Value) <= 0));
            if (message.SourceEventId?.StartsWith("contract-expiry-warning:", StringComparison.Ordinal) == true)
                return save.OwnContracts.Any(c => c.PersonId == message.TargetId && c.Covers(save.CurrentDate) && save.CurrentDate.DaysUntil(c.EndDate) <= 28);
            return message.Priority == InboxPriority.Required || message.Type == InboxMessageType.Decision &&
                (!message.HasDueDate || save.CurrentDate.CompareTo(message.DueDate) <= 0);
        }

        private bool ActivePromise(InboxMessageState message) => save.WrestlerBehaviors.Any(s => s.PromotionId == save.Promotion.Id && s.WrestlerId == message.TargetId &&
            s.Concerns.Any(c => c.Status == WrestlerConcernStatus.PromiseActive && message.SourceEventId == "wrestler-request:" + c.RequestId));

        public DashboardInboxController(VisualElement root, Action showSchedule, Action<string> showPlanning)
        {
            this.root = root;
            this.showSchedule = showSchedule;
            this.showPlanning = showPlanning;
            var open = root.Q<Button>("message-open-target");
            if (open != null) open.clicked += OpenSelectedTarget;
            filter = new DropdownField("표시", new List<string> { "대응할 일", "진행 중 약속", "전체 기록" }, 0) { name = "message-filter" };
            filter.AddToClassList("message-filter");
            var toolbar = root.Q<VisualElement>(className: "message-toolbar"); toolbar?.parent.Insert(1, filter);
            filter.RegisterValueChangedCallback(_ => { selectedIndex = 0; Bind(save); });
        }

        public void Bind(GameSave value)
        {
            save = value;
            messages.Clear();
            messages.AddRange((save?.InboxMessages ?? new List<InboxMessageState>()).Where(x => x != null &&
                (filter.index == 2 || (filter.index == 1 ? ActivePromise(x) : NeedsResponse(save, x))))
                .OrderByDescending(x => x.CreatedDate.Year).ThenByDescending(x => x.CreatedDate.Month).ThenByDescending(x => x.CreatedDate.Day));
            selectedIndex = Math.Min(selectedIndex, Math.Max(0, messages.Count - 1));
            Render();
        }

        public void Render()
        {
            var host = root.Q<VisualElement>("message-list");
            if (host == null) return;
            host.Clear();
            Set("message-count", $"{messages.Count}개");
            GameDate? renderedDate = null;
            var briefs = new System.Collections.Generic.Dictionary<string, Foldout>();
            for (var i = 0; i < messages.Count; i++)
            {
                var item = messages[i];
                VisualElement rowHost = host;
                var brief = filter.index == 2 && item.SourceEventId?.StartsWith("daily-brief:", StringComparison.Ordinal) == true;
                if (brief)
                {
                    var month = $"{item.CreatedDate.Year}.{item.CreatedDate.Month:D2}";
                    if (!briefs.TryGetValue(month, out var group))
                    {
                        var count = messages.Count(m => m.SourceEventId?.StartsWith("daily-brief:", StringComparison.Ordinal) == true && m.CreatedDate.Year == item.CreatedDate.Year && m.CreatedDate.Month == item.CreatedDate.Month);
                        group = new Foldout { text = $"{month} 운영 브리핑 · {count}건", value = false }; host.Add(group); briefs.Add(month, group);
                    }
                    rowHost = group;
                }
                else if (!renderedDate.HasValue || !renderedDate.Value.Equals(item.CreatedDate))
                {
                    var group = new VisualElement(); group.AddToClassList("message-date-group");
                    var date = new Label(DashboardText.Date(item.CreatedDate)); date.AddToClassList("message-date-label"); group.Add(date); host.Add(group);
                    renderedDate = item.CreatedDate;
                }
                var row = new Button(); row.AddToClassList("message-row");
                row.EnableInClassList("selected", i == selectedIndex);
                row.EnableInClassList("unread", item.Status == InboxMessageStatus.Unread);
                row.EnableInClassList("required", item.Priority == InboxPriority.Required && item.Status is InboxMessageStatus.Unread or InboxMessageStatus.Read);
                row.EnableInClassList("important", item.Priority == InboxPriority.Important);
                var avatar = new Label(string.IsNullOrWhiteSpace(item.Sender) ? "?" : item.Sender.Trim().Substring(0, 1)); avatar.AddToClassList("message-avatar"); row.Add(avatar);
                if (item.Status == InboxMessageStatus.Unread) { var dot = new VisualElement(); dot.AddToClassList("message-unread-dot"); row.Add(dot); }
                var copy = new VisualElement(); copy.AddToClassList("message-row-copy");
                var top = new VisualElement(); top.AddToClassList("message-row-top");
                var sender = WrestlerNameText.Create(item.Sender, save?.Wrestlers); sender.AddToClassList("message-sender"); top.Add(sender); copy.Add(top);
                var due = Deadline(item);
                if (due.HasValue) { var deadline = new Label("기한 " + DashboardText.Date(due.Value)); deadline.AddToClassList("message-deadline"); top.Add(deadline); }
                var subject = WrestlerNameText.Create(Subject(item), save?.Wrestlers); subject.AddToClassList("message-subject"); copy.Add(subject); row.Add(copy);
                var captured = i; row.clicked += () => Select(captured); rowHost.Add(row);
            }
            var detail = root.Q<VisualElement>("message-detail");
            var empty = root.Q<VisualElement>("message-detail-empty");
            var hasMessage = messages.Count > 0;
            if (detail != null) detail.style.display = hasMessage ? DisplayStyle.Flex : DisplayStyle.None;
            if (empty != null) empty.style.display = hasMessage ? DisplayStyle.None : DisplayStyle.Flex;
            if (!hasMessage) return;
            var selected = messages[selectedIndex];
            var selectedDue = Deadline(selected);
            Set("message-detail-sender", selected.Sender); Set("message-detail-time", DashboardText.Date(selected.CreatedDate) + (selectedDue.HasValue ? " · 기한 " + DashboardText.Date(selectedDue.Value) : ""));
            Set("message-detail-subject", Subject(selected)); Set("message-detail-body", selected.Body);
            var open = root.Q<Button>("message-open-target");
            if (open != null) open.style.display = selected.TargetType == InboxTargetType.None ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void Select(int index)
        {
            selectedIndex = index;
            var unread = messages[index].Status == InboxMessageStatus.Unread;
            new InboxService().MarkRead(messages[index]);
            Render();
            if (unread) DashboardSession.PersistActive();
        }

        private GameDate? Deadline(InboxMessageState message)
        {
            var concern = save.WrestlerBehaviors.Where(s => s.PromotionId == save.Promotion.Id && s.WrestlerId == message.TargetId)
                .SelectMany(s => s.Concerns).FirstOrDefault(c => message.SourceEventId == "wrestler-request:" + c.RequestId);
            if (concern?.Status == WrestlerConcernStatus.PromiseActive) return concern.Promises.FirstOrDefault(p => p.Status == WrestlerPromiseStatus.Active)?.DueDate;
            if (concern?.Status == WrestlerConcernStatus.AwaitingResponse && concern.ResponseDueDate.HasValue) return concern.ResponseDueDate.Value;
            if (message.SourceEventId?.StartsWith("contract-expiry-warning:", StringComparison.Ordinal) == true)
                return save.OwnContracts.FirstOrDefault(c => c.PersonId == message.TargetId && c.Covers(save.CurrentDate))?.EndDate;
            return message.HasDueDate ? message.DueDate : null;
        }

        private string Subject(InboxMessageState message)
        {
            if (filter.index == 0 && message.SourceEventId?.StartsWith("contract-expiry-warning:", StringComparison.Ordinal) == true)
            {
                var date = Deadline(message); var person = save.Wrestlers.FirstOrDefault(w => w.Id == message.TargetId);
                if (date.HasValue && person != null)
                {
                    var days = save.CurrentDate.DaysUntil(date.Value);
                    return $"{person.Identity.RingName}의 계약이 {(days == 0 ? "오늘" : days + "일 후")} 만료됩니다";
                }
            }
            return message.Subject;
        }

        private void OpenSelectedTarget()
        {
            if (selectedIndex < 0 || selectedIndex >= messages.Count) return;
            var message = messages[selectedIndex];
            if (message.TargetType == InboxTargetType.Show)
            {
                if (string.IsNullOrWhiteSpace(message.TargetId)) showSchedule(); else showPlanning(message.TargetId);
            }
            else if (message.TargetType == InboxTargetType.Wrestler)
            {
                var wrestler = save?.Wrestlers?.FirstOrDefault(x => x?.Id == message.TargetId);
                if (wrestler != null) WrestlerProfileController.Open(wrestler);
            }
            else if (message.TargetType == InboxTargetType.Season) SeasonTransitionView.Open(root);
            else if (message.TargetType == InboxTargetType.WrestlerReport)
                WrestlerBehaviorView.Open(root, message.TargetId, () => Bind(DashboardSession.ActiveSave));
            else if (message.TargetType == InboxTargetType.Promotion && message.SourceEventId.StartsWith("scouting-report:", StringComparison.Ordinal))
                DashboardController.ShowScoutingShell();
        }

        private void Set(string name, string value) => WrestlerNameText.Set(root.Q<Label>(name), value, save?.Wrestlers);
    }
}
