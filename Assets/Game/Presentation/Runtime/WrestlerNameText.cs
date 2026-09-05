using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PWManager.Domain.Models;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace PWManager.Presentation
{
    public static class WrestlerNameText
    {
        public static string DisplayName(WrestlerState wrestler) =>
            string.IsNullOrWhiteSpace(wrestler?.Identity?.RingName) ? wrestler?.Identity?.LegalName ?? "—" : wrestler.Identity.RingName;

        public static string Format(string text, IEnumerable<WrestlerState> wrestlers)
        {
            if (string.IsNullOrEmpty(text) || wrestlers == null) return text ?? "—";
            // Names alone cannot identify namesakes; callers should supply the relevant participants.
            var names = wrestlers.Where(w => !string.IsNullOrEmpty(w?.Id))
                .SelectMany(w => new[] { w.Identity?.RingName, w.Identity?.LegalName }
                    .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().Select(n => (Name: n, Wrestler: w)))
                .GroupBy(x => x.Name, StringComparer.Ordinal).Where(g => g.Select(x => x.Wrestler.Id).Distinct().Count() == 1)
                .ToDictionary(g => g.Key, g => g.First().Wrestler.Id, StringComparer.Ordinal);
            if (names.Count == 0) return text;
            var pattern = @"<[^>]*>|(?<![\p{L}\p{N}_])(?:" + string.Join("|", names.Keys.OrderByDescending(n => n.Length).Select(Regex.Escape)) + @")(?![A-Za-z0-9_])";
            return Regex.Replace(text, pattern, match => names.TryGetValue(match.Value, out var id)
                ? $"<link=\"wrestler:{Uri.EscapeDataString(id)}\"><u>{match.Value}</u></link>" : match.Value);
        }

        public static Label Create(string text, IEnumerable<WrestlerState> wrestlers)
        {
            var label = new Label();
            Set(label, text, wrestlers);
            return label;
        }

        public static void Set(Label label, string text, IEnumerable<WrestlerState> wrestlers)
        {
            if (label == null) return;
            label.enableRichText = true;
            label.text = Format(text, wrestlers);
            label.UnregisterCallback<PointerUpLinkTagEvent>(OpenLink);
            label.RegisterCallback<PointerUpLinkTagEvent>(OpenLink);
            label.UnregisterCallback<PointerDownEvent>(StopParentAction);
            label.UnregisterCallback<PointerUpEvent>(StopParentAction);
            label.UnregisterCallback<ClickEvent>(StopParentAction);
            if (!label.text.Contains("<link=\"wrestler:")) return;
            label.RegisterCallback<PointerDownEvent>(StopParentAction);
            label.RegisterCallback<PointerUpEvent>(StopParentAction);
            label.RegisterCallback<ClickEvent>(StopParentAction);
        }

        private static void StopParentAction(EventBase evt) => evt.StopPropagation();

        private static void OpenLink(PointerUpLinkTagEvent evt)
        {
            if (evt.button != 0 || !evt.linkID.StartsWith("wrestler:", StringComparison.Ordinal)) return;
            var id = Uri.UnescapeDataString(evt.linkID.Substring("wrestler:".Length));
            WrestlerProfileController.Open(DashboardSession.ActiveSave?.Wrestlers?.FirstOrDefault(w => w?.Id == id));
            evt.StopPropagation();
        }
    }
}
