using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public sealed class SponsorSelectionView
    {
        [Serializable] private sealed class Catalog { public List<SponsorCompany> Companies; }
        public static IReadOnlyList<SponsorCompany> Companies() => JsonUtility.FromJson<Catalog>(Resources.Load<TextAsset>("PWManagerSponsors/Catalog").text).Companies;
        public readonly HashSet<string> SelectedIds = new(StringComparer.Ordinal);
        public VisualElement Root { get; }
        private readonly GameSave save;
        private readonly SponsorSeasonState season;
        private readonly bool readOnly;
        private readonly Action changed;
        private readonly ScrollView list, detail;
        private readonly VisualElement detailActions;
        private readonly Label totals;
        private SponsorOfferState focused;

        public SponsorSelectionView(VisualElement host, GameSave save, SponsorSeasonState season, bool readOnly = false, Action changed = null, IEnumerable<string> selectedIds = null, bool showTitle = true)
        {
            this.save = save; this.season = season; this.readOnly = readOnly; this.changed = changed;
            foreach (var offer in season.Offers.Where(x => x.Selected)) SelectedIds.Add(offer.Id);
            foreach (var id in selectedIds ?? Array.Empty<string>()) SelectedIds.Add(id);
            Root = new VisualElement { name = "sponsor-selection" }; Root.AddToClassList("sponsor-screen");
            Root.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/SponsorSelection")); host.Add(Root);
            var heading = new VisualElement(); heading.AddToClassList("sponsor-heading"); Root.Add(heading);
            var title = new Label(readOnly ? "시즌 스폰서" : "스폰서 선택"); title.AddToClassList("sponsor-title"); heading.Add(title);
            if (!showTitle) title.style.display = DisplayStyle.None;
            totals = new Label { name = "sponsor-selection-total" }; heading.Add(totals);
            var columns = new VisualElement(); columns.AddToClassList("sponsor-columns"); Root.Add(columns);
            list = new ScrollView { name = "sponsor-offers" }; list.AddToClassList("sponsor-list"); columns.Add(list);
            list.contentContainer.AddToClassList("sponsor-card-grid");
            Root.RegisterCallback<GeometryChangedEvent>(evt => Root.EnableInClassList("sponsor-narrow", evt.newRect.width < 1300));
            var pane = new VisualElement(); pane.AddToClassList("sponsor-detail-pane"); columns.Add(pane);
            detail = new ScrollView { name = "sponsor-detail" }; detail.AddToClassList("sponsor-detail"); pane.Add(detail);
            detailActions = new VisualElement(); detailActions.AddToClassList("sponsor-detail-actions"); pane.Add(detailActions);
            focused = season.Offers.FirstOrDefault(x => !readOnly || x.Selected);
            Render();
        }

        private static string Difficulty(int value) => value == 1 ? "쉬움" : value == 2 ? "보통" : "어려움";
        private void Render()
        {
            totals.text = $"선택 {SelectedIds.Count} / 3 · 기본금 ${season.Offers.Where(x => SelectedIds.Contains(x.Id)).Sum(x => x.BasePayment):N0} · 달성금 최대 ${season.Offers.Where(x => SelectedIds.Contains(x.Id)).Sum(x => x.CompletionPayment):N0}";
            list.Clear();
            foreach (var offer in season.Offers.Where(x => !readOnly || x.Selected))
            {
                var row = new Button(() =>
                {
                    focused = offer;
                    if (!readOnly) Toggle(offer);
                    else Render();
                }) { name = "sponsor-offer-" + offer.CompanyId };
                row.AddToClassList("sponsor-row"); row.EnableInClassList("focused", focused == offer);
                row.EnableInClassList("selected", SelectedIds.Contains(offer.Id));
                var identity = new VisualElement(); identity.AddToClassList("sponsor-row-identity"); row.Add(identity);
                var logo = new Image { image = Resources.Load<Texture2D>(offer.LogoResource), scaleMode = ScaleMode.ScaleToFit }; logo.AddToClassList("sponsor-row-logo"); identity.Add(logo);
                identity.Add(Text(offer.CompanyName, "sponsor-company"));
                var copy = new VisualElement(); copy.AddToClassList("sponsor-row-copy"); row.Add(copy);
                copy.Add(Text(Difficulty(offer.Difficulty), "sponsor-muted"));
                copy.Add(Text($"기본금 ${offer.BasePayment:N0}", "sponsor-money"));
                copy.Add(Text($"달성금 ${offer.CompletionPayment:N0}", "sponsor-money"));
                list.Add(row);
            }
            detail.Clear(); detailActions.Clear(); if (focused == null) { detail.Add(Text("이번 시즌에 선택한 스폰서가 없습니다.", "sponsor-muted")); return; }
            var large = new Image { image = Resources.Load<Texture2D>(focused.LogoResource), scaleMode = ScaleMode.ScaleToFit }; large.AddToClassList("sponsor-detail-logo"); detail.Add(large);
            detail.Add(Text(focused.CompanyName, "sponsor-title"));
            detail.Add(Text(focused.Description, "sponsor-description"));
            detail.Add(Text($"{Difficulty(focused.Difficulty)} · {season.StartDate.Year}.{season.StartDate.Month:D2}.{season.StartDate.Day:D2} ~ {season.EndDate.Year}.{season.EndDate.Month:D2}.{season.EndDate.Day:D2}", "sponsor-muted"));
            detail.Add(Text($"기본금 ${focused.BasePayment:N0}", "sponsor-money"));
            detail.Add(Text($"모든 목표 달성 시 +${focused.CompletionPayment:N0}", "sponsor-money"));
            for (var i = 0; i < focused.Goals.Count; i++)
            {
                var goal = focused.Goals[i]; var goalRow = new VisualElement(); goalRow.AddToClassList("sponsor-goal"); detail.Add(goalRow);
                goalRow.Add(Text($"목표 {i + 1}", "sponsor-muted"));
                if (!string.IsNullOrEmpty(goal.WrestlerId))
                {
                    var wrestler = save.Wrestlers.FirstOrDefault(x => x.Id == goal.WrestlerId);
                    if (wrestler != null)
                    {
                        var link = WrestlerProfileController.CreateLink(wrestler);
                        link.clicked += () =>
                        {
                            if (DashboardSession.ActiveSave == null) return;
                            var tree = Root.panel?.visualTree;
                            tree?.Q("sponsor-overlay")?.RemoveFromHierarchy();
                            tree?.Q("season-transition-overlay")?.RemoveFromHierarchy();
                        };
                        goalRow.Add(link);
                    }
                }
                goalRow.Add(Text(GoalText(goal), "sponsor-goal-text"));
                goalRow.Add(Text($"{goal.Progress:N0} / {goal.Target:N0}" + (goal.Progress >= goal.Target ? " · 달성" : ""), "sponsor-money"));
            }
            if (!readOnly)
            {
                var toggle = new Button(() =>
                {
                    Toggle(focused);
                }) { text = SelectedIds.Contains(focused.Id) ? "선택 해제" : "이 스폰서 선택", name = "sponsor-toggle" };
                toggle.AddToClassList("sponsor-action"); toggle.SetEnabled(SelectedIds.Contains(focused.Id) || SelectedIds.Count < 3); detailActions.Add(toggle);
            }
            else detail.Add(Text(Status(focused), "sponsor-selected"));
        }

        private void Toggle(SponsorOfferState offer)
        {
            if (SelectedIds.Contains(offer.Id)) SelectedIds.Remove(offer.Id);
            else if (SelectedIds.Count < SponsorMissionService.SelectionCount) SelectedIds.Add(offer.Id);
            Render(); changed?.Invoke();
        }

        public static string GoalText(SponsorGoalState goal) => goal.Type switch
        {
            SponsorGoalType.Fans => $"단체 팬 {goal.Target:N0}명 달성",
            SponsorGoalType.Prestige => $"단체 명성 {goal.Target:N0} 달성",
            SponsorGoalType.Attendance => $"한 쇼에서 유료 관객 {goal.Target:N0}명 이상 기록",
            SponsorGoalType.ShowQuality => $"평가 {goal.MinimumQuality:0} 이상인 쇼를 {goal.Target:N0}회 개최",
            SponsorGoalType.WrestlerAppearance => $"지정 선수를 서로 다른 쇼 {goal.Target:N0}회에 경기 또는 프로모로 출연",
            SponsorGoalType.Advertisement => $"이 스폰서의 광고 프로모를 서로 다른 쇼 {goal.Target:N0}회에서 진행",
            SponsorGoalType.StyleChampion => "루차 리브레 스타일 선수가 이번 시즌 타이틀 획득",
            SponsorGoalType.GiantMatch => $"자이언트 vs 자이언트 경기를 서로 다른 쇼 {goal.Target:N0}회에서 개최",
            SponsorGoalType.WrestlerFame => $"지정 선수 개인 명성 {goal.Target:N0} 달성",
            _ => throw new ArgumentOutOfRangeException()
        };
        public static string Status(SponsorOfferState offer) => offer.RewardPaid ? "달성 · 보상 지급 완료" : offer.Failed ? "실패" : "진행 중";
        private static Label Text(string text, string className) { var label = new Label(text); label.AddToClassList(className); return label; }

        public static VisualElement OpenNextSeason(VisualElement host, GameSave save, SeasonSetupRequest settings, Action completed)
        {
            var before = JsonUtility.ToJson(save);
            var preview = JsonUtility.FromJson<GameSave>(before);
            new SeasonTransitionService().ConfirmNextSeason(preview, settings);
            var service = new SponsorMissionService(); var season = service.PrepareOffers(preview, Companies());
            host.Q("sponsor-overlay")?.RemoveFromHierarchy();
            var overlay = new VisualElement { name = "sponsor-overlay" }; overlay.AddToClassList("sponsor-overlay");
            overlay.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/SponsorSelection")); host.Add(overlay);
            var panel = new VisualElement(); panel.AddToClassList("sponsor-dialog"); overlay.Add(panel);
            Button confirm = null;
            SponsorSelectionView selection = null;
            selection = new SponsorSelectionView(panel, preview, season, changed: () => confirm.SetEnabled(selection.SelectedIds.Count == 3));
            var error = Text("", "sponsor-description"); panel.Add(error);
            var previous = host.focusController?.focusedElement;
            void Close() { overlay.RemoveFromHierarchy(); previous?.Focus(); }
            var actions = new VisualElement(); actions.style.flexDirection = FlexDirection.Row; panel.Add(actions);
            var back = new Button(Close) { text = "설정으로 돌아가기", name = "sponsor-back" }; back.AddToClassList("sponsor-action"); actions.Add(back);
            confirm = new Button(() =>
            {
                try
                {
                    if (DashboardSession.ActiveSave != save || JsonUtility.ToJson(save) != before) throw new InvalidOperationException("시즌 정보가 변경되었습니다. 설정으로 돌아가 다시 확인하세요.");
                    try
                    {
                        new SeasonTransitionService().ConfirmNextSeason(save, settings);
                        season.PolicyId = save.SeasonPolicy.Id;
                        save.SponsorSeasons.Add(season);
                        service.ConfirmSelection(save, season, selection.SelectedIds);
                        DashboardSession.PersistActive();
                    }
                    catch { JsonUtility.FromJsonOverwrite(before, save); throw; }
                    Close(); completed?.Invoke();
                }
                catch (Exception exception) { error.text = exception.Message; }
            }) { text = "다음 시즌 확정", name = "sponsor-confirm" };
            confirm.AddToClassList("sponsor-action"); confirm.SetEnabled(false); actions.Add(confirm);
            InstallModalKeys(overlay, Close); overlay.RegisterCallback<ClickEvent>(evt => evt.StopPropagation()); back.Focus(); return overlay;
        }

        private static void InstallModalKeys(VisualElement overlay, Action close)
        {
            overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape) { close(); evt.StopPropagation(); }
                else if (evt.keyCode == KeyCode.Tab)
                {
                    var controls = overlay.Query<VisualElement>().ToList().Where(x => x.focusable && x.tabIndex >= 0 && x.enabledInHierarchy).ToList();
                    if (controls.Count == 0) return;
                    var index = controls.FindIndex(x => x == overlay.focusController?.focusedElement);
                    controls[(index + (evt.shiftKey ? controls.Count - 1 : 1)) % controls.Count].Focus(); evt.StopPropagation(); evt.PreventDefault();
                }
            });
        }

        public static VisualElement OpenActive(VisualElement host)
        {
            var save = DashboardSession.ActiveSave;
            var season = save?.SponsorSeasons.LastOrDefault(x => x.PolicyId == save.SeasonPolicy?.Id && x.SelectionConfirmed);
            host.Q("sponsor-overlay")?.RemoveFromHierarchy();
            var overlay = new VisualElement { name = "sponsor-overlay" }; overlay.AddToClassList("sponsor-overlay");
            overlay.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/SponsorSelection")); host.Add(overlay);
            var panel = new VisualElement(); panel.AddToClassList("sponsor-dialog"); overlay.Add(panel);
            if (season != null) new SponsorSelectionView(panel, save, season, true);
            else panel.Add(Text("이번 시즌에 선택한 스폰서가 없습니다.", "sponsor-description"));
            var previous = host.focusController?.focusedElement;
            var close = new Button(() => { overlay.RemoveFromHierarchy(); previous?.Focus(); }) { text = "닫기", name = "sponsor-close" }; close.AddToClassList("sponsor-action"); panel.Add(close);
            InstallModalKeys(overlay, () => { overlay.RemoveFromHierarchy(); previous?.Focus(); });
            overlay.RegisterCallback<ClickEvent>(evt => evt.StopPropagation()); close.Focus(); return overlay;
        }
    }
}
