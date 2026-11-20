using System;
using System.Collections.Generic;
using PWManager.Domain.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    internal static class OperationsView
    {
        internal static readonly string[] AbilityNames = { "경기 운영", "링 임기응변", "테크니컬", "브롤링", "파워", "하이플라잉", "스팟 수행력", "특수 경기", "접수력", "스태미나",
            "카리스마", "마이크", "즉흥성", "연기", "페이스 연기", "힐 연기", "코미디" };

        internal static VisualElement Page(VisualElement host, string name)
        {
            var root = new VisualElement { name = name }; root.AddToClassList("ops-content");
            root.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/OperationsScreens"));
            root.RegisterCallback<GeometryChangedEvent>(evt => root.EnableInClassList("ops-narrow", evt.newRect.width < 900));
            host.Add(root); return root;
        }

        internal static Button Action(VisualElement parent, string title, string name, Action clicked, bool primary = false)
        {
            var button = new Button(clicked) { text = title, name = name }; button.AddToClassList("ops-action");
            if (primary) button.AddToClassList("ops-primary"); parent.Add(button); return button;
        }

        internal static void AddValue(VisualElement parent, string title, string value, string name = null, bool ability = false)
        {
            var row = new VisualElement(); row.AddToClassList("ops-value-row");
            var label = new Label(title); label.AddToClassList("ops-value-name"); row.Add(label);
            var result = new Label(value) { name = name }; result.AddToClassList("ops-value"); row.Add(result); parent.Add(row);
            if (ability) WrestlerProfileController.SetScoutGrade(result, value);
        }

        internal static void Heading(VisualElement parent, string title)
        {
            var heading = new Label(title); heading.AddToClassList("ops-heading"); parent.Add(heading);
        }

        internal static VisualElement Identity(VisualElement host, WrestlerState wrestler, string description)
        {
            var identity = new VisualElement(); identity.AddToClassList("ops-identity"); host.Add(identity);
            var portrait = new GroupPortraitView(new[] { wrestler }); portrait.AddToClassList("ops-identity-portrait"); identity.Add(portrait);
            var copy = new VisualElement(); copy.AddToClassList("ops-identity-copy"); identity.Add(copy);
            copy.Add(WrestlerProfileController.CreateLink(wrestler)); var meta = new Label(description); meta.AddToClassList("ops-meta"); copy.Add(meta); return identity;
        }

        internal static ProfileMetricGauge Condition(float value)
        {
            var gauge = new ProfileMetricGauge();
            var description = value < 40 ? "위험" : value < 60 ? "나쁨" : value < 80 ? "보통" : "좋음";
            ColorUtility.TryParseHtmlString(value < 40 ? "#f87171" : value < 60 ? "#fb923c" : value < 80 ? "#facc15" : "#4ade80", out var color);
            gauge.SetValue("체력", value, description, color); return gauge;
        }

        internal static void Selectable(VisualElement row, Action selected)
        {
            row.focusable = true;
            row.RegisterCallback<ClickEvent>(_ => selected());
            row.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.target != row || evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.Space) return;
                selected(); evt.StopPropagation(); evt.PreventDefault();
            });
        }

        internal static void AddAbilities(VisualElement parent, Func<ScoutValueType, string> value)
        {
            var columns = new VisualElement(); columns.AddToClassList("ops-detail-columns"); parent.Add(columns);
            foreach (var match in new[] { true, false })
            {
                var column = new VisualElement(); column.AddToClassList("ops-ability-column"); columns.Add(column);
                Heading(column, match ? "경기 능력" : "프로모 능력");
                for (var i = match ? 0 : 10; i < (match ? 10 : 17); i++)
                    AddValue(column, AbilityNames[i], value((ScoutValueType)i), "ops-ability-" + (ScoutValueType)i, true);
            }
        }

        internal static Label Cell(VisualElement parent, string text, string extra = null)
        {
            var cell = new Label(text); cell.AddToClassList("ops-cell"); if (extra != null) cell.AddToClassList(extra); parent.Add(cell); return cell;
        }

        internal static void NameCell(VisualElement row, WrestlerState wrestler)
        {
            var cell = new VisualElement(); cell.AddToClassList("ops-cell"); cell.AddToClassList("ops-name-cell");
            var link = WrestlerProfileController.CreateLink(wrestler); link.tooltip = WrestlerNameText.DisplayName(wrestler); cell.Add(link); row.Add(cell);
        }

        internal static void ShowTab(VisualElement root, string prefix, string selected, IEnumerable<string> keys)
        {
            foreach (var key in keys)
            {
                root.Q(prefix + "-" + key + "-panel").style.display = key == selected ? DisplayStyle.Flex : DisplayStyle.None;
                root.Q<Button>(prefix + "-tab-" + key).EnableInClassList("ops-active", key == selected);
            }
        }
    }
}
