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

        public int UnreadCount => messages.Count(x => x.Status == InboxMessageStatus.Unread);

        public DashboardInboxController(VisualElement root, Action showSchedule, Action<string> showPlanning)
        {
            this.root = root;
            this.showSchedule = showSchedule;
            this.showPlanning = showPlanning;
            var open = root.Q<Button>("message-open-target");
            if (open != null) open.clicked += OpenSelectedTarget;
        }

        public void Bind(GameSave value)
        {
            save = value;
            messages.Clear();
            messages.AddRange((save?.InboxMessages ?? new List<InboxMessageState>()).Where(x => x != null)
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
            for (var i = 0; i < messages.Count; i++)
            {
                var item = messages[i];
                if (!renderedDate.HasValue || !renderedDate.Value.Equals(item.CreatedDate))
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
                var top = new VisualElement(); top.AddToClassList("message-row-top");
                var sender = WrestlerNameText.Create(item.Sender, save?.Wrestlers); sender.AddToClassList("message-sender"); top.Add(sender); row.Add(top);
                var subject = WrestlerNameText.Create(item.Subject, save?.Wrestlers); subject.AddToClassList("message-subject"); row.Add(subject);
                var captured = i; row.clicked += () => Select(captured); host.Add(row);
            }
            var detail = root.Q<VisualElement>("message-detail");
            var empty = root.Q<VisualElement>("message-detail-empty");
            var hasMessage = messages.Count > 0;
            if (detail != null) detail.style.display = hasMessage ? DisplayStyle.Flex : DisplayStyle.None;
            if (empty != null) empty.style.display = hasMessage ? DisplayStyle.None : DisplayStyle.Flex;
            if (!hasMessage) return;
            var selected = messages[selectedIndex];
            Set("message-detail-sender", selected.Sender); Set("message-detail-time", DashboardText.Date(selected.CreatedDate));
            Set("message-detail-subject", selected.Subject); Set("message-detail-body", selected.Body);
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
        }

        private void Set(string name, string value) => WrestlerNameText.Set(root.Q<Label>(name), value, save?.Wrestlers);
    }
}
