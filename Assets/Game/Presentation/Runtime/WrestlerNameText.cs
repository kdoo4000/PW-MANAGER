using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using PWManager.Domain.Models;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace PWManager.Presentation
{
    public static class WrestlerNameText
    {
        private sealed class KeyboardLinks
        {
            public string Text;
            public Match[] Links;
            public int Selected;
        }

        private static readonly ConditionalWeakTable<Label, KeyboardLinks> keyboardLinks = new();
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
            label.UnregisterCallback<FocusInEvent>(FocusLink);
            label.UnregisterCallback<FocusOutEvent>(BlurLink);
            label.UnregisterCallback<KeyDownEvent>(UseLink, TrickleDown.TrickleDown);
            label.UnregisterCallback<NavigationMoveEvent>(MoveLink, TrickleDown.TrickleDown);
            keyboardLinks.Remove(label);
            label.UnregisterCallback<PointerUpLinkTagEvent>(OpenLink);
            label.RegisterCallback<PointerUpLinkTagEvent>(OpenLink);
            label.UnregisterCallback<PointerDownEvent>(StopParentAction);
            label.UnregisterCallback<PointerUpEvent>(StopParentAction);
            label.UnregisterCallback<ClickEvent>(StopParentAction);
            var links = Regex.Matches(label.text, "<link=\"wrestler:([^\"]+)\">(.*?)</link>", RegexOptions.Singleline).Cast<Match>().ToArray();
            label.focusable = links.Length > 0;
            label.tabIndex = links.Length > 0 ? 0 : -1;
            if (links.Length == 0) return;
            var navigation = new KeyboardLinks { Text = label.text, Links = links };
            keyboardLinks.Add(label, navigation);
            label.RegisterCallback<FocusInEvent>(FocusLink);
            label.RegisterCallback<FocusOutEvent>(BlurLink);
            label.RegisterCallback<KeyDownEvent>(UseLink, TrickleDown.TrickleDown);
            label.RegisterCallback<NavigationMoveEvent>(MoveLink, TrickleDown.TrickleDown);
            label.RegisterCallback<PointerDownEvent>(StopParentAction);
            label.RegisterCallback<PointerUpEvent>(StopParentAction);
            label.RegisterCallback<ClickEvent>(StopParentAction);
            if (label.focusController?.focusedElement == label)
                HighlightLink(label, navigation);
        }

        private static void StopParentAction(EventBase evt) => evt.StopPropagation();

        private static void FocusLink(FocusInEvent evt)
        {
            var label = (Label)evt.currentTarget;
            if (!keyboardLinks.TryGetValue(label, out var links)) return;
            links.Selected = evt.direction == VisualElementFocusChangeDirection.left ? links.Links.Length - 1 : 0;
            HighlightLink(label, links);
        }

        private static void BlurLink(FocusOutEvent evt)
        {
            var label = (Label)evt.currentTarget;
            if (keyboardLinks.TryGetValue(label, out var links)) label.text = links.Text;
        }

        private static void UseLink(KeyDownEvent evt)
        {
            var label = (Label)evt.currentTarget;
            if (!keyboardLinks.TryGetValue(label, out var links)) return;
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter || evt.keyCode == KeyCode.Space)
                OpenWrestler(links.Links[links.Selected].Groups[1].Value);
            else return;
            evt.StopPropagation();
            label.focusController?.IgnoreEvent(evt);
        }

        private static void MoveLink(NavigationMoveEvent evt)
        {
            if (evt.direction != NavigationMoveEvent.Direction.Next && evt.direction != NavigationMoveEvent.Direction.Previous) return;
            var label = (Label)evt.currentTarget;
            if (!keyboardLinks.TryGetValue(label, out var links)) return;
            var next = links.Selected + (evt.direction == NavigationMoveEvent.Direction.Previous ? -1 : 1);
            if (next < 0 || next >= links.Links.Length) return;
            links.Selected = next;
            HighlightLink(label, links);
            evt.StopPropagation();
            label.focusController?.IgnoreEvent(evt);
        }

        private static void HighlightLink(Label label, KeyboardLinks links)
        {
            var match = links.Links[links.Selected];
            var content = match.Groups[2];
            label.text = links.Text.Substring(0, content.Index) + "<mark=#d9b45b><color=#090a0c>" + content.Value
                + "</color></mark>" + links.Text.Substring(content.Index + content.Length);
        }

        private static void OpenWrestler(string escapedId)
        {
            var id = Uri.UnescapeDataString(escapedId);
            WrestlerProfileController.Open(DashboardSession.ActiveSave?.Wrestlers?.FirstOrDefault(w => w?.Id == id));
        }

        private static void OpenLink(PointerUpLinkTagEvent evt)
        {
            if (evt.button != 0 || !evt.linkID.StartsWith("wrestler:", StringComparison.Ordinal)) return;
            OpenWrestler(evt.linkID.Substring("wrestler:".Length));
            evt.StopPropagation();
        }
    }
}
