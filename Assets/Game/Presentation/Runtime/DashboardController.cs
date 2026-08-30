using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using PWManager.Domain.Models;
using PWManager.Infrastructure.Save;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class DashboardController : MonoBehaviour
    {
        private UIDocument document;

        private void OnEnable()
        {
            document = GetComponent<UIDocument>();
            ApplyIcons();
            document.rootVisualElement.RegisterCallback<ClickEvent>(OnClick);
            DashboardSession.SaveChanged += Bind;
            Bind(DashboardSession.ActiveSave);
        }

        private void OnDisable()
        {
            DashboardSession.SaveChanged -= Bind;
            if (document != null && document.rootVisualElement != null)
                document.rootVisualElement.UnregisterCallback<ClickEvent>(OnClick);
        }

        public void Bind(GameSave save)
        {
            if (save?.Promotion == null) { ShowEmpty(); return; }
            var wrestlers = save.Wrestlers?.Where(x => x != null).ToList() ?? new List<WrestlerState>();
            var active = wrestlers.Where(x => x.Roster?.ActivityState == RosterActivityState.Active).ToList();
            var transactions = save.Transactions?.Where(x => x != null && !x.IsVoided).ToList() ?? new List<TransactionRecord>();
            var cash = save.Promotion.CalculateCurrentCash(transactions);
            var seasonStart = save.SeasonPolicy?.StartDate ?? save.CurrentDate;

            Set("current-date", Date(save.CurrentDate)); Set("top-cash", Money(cash)); Set("cash-value", Compact(cash));
            Set("cash-detail", $"시즌 순변동 {SignedMoney(transactions.Where(x => x.Date.CompareTo(seasonStart) >= 0).Sum(x => x.Amount))}");
            Set("prestige-value", save.Promotion.PromotionPrestige.ToString("N0")); Set("promotion-name", save.Promotion.Name); Set("promotion-abbreviation", save.Promotion.GetDisplayAbbreviation());
            Set("roster-value", active.Count.ToString()); Set("roster-detail", $"전체 {wrestlers.Count}명");
            var injuries = active.Count(x => x.Condition?.InjuryStatus != InjuryStatus.None);
            var conditionRisks = active.Count(x => x.Condition == null || x.Condition.Condition < 40f || x.Condition.Satisfaction < 30f);
            Set("risk-value", (injuries + conditionRisks).ToString()); Set("risk-detail", $"부상 {injuries} · 컨디션/만족도 {conditionRisks}");
            var staff = save.StaffDepartments?.Where(x => x != null).ToList() ?? new List<StaffDepartmentState>();
            Set("staff-value", staff.Count == 0 ? "—" : $"Lv {staff.Average(x => x.CurrentLevel):0.0}");
            Set("staff-detail", staff.Count == 0 ? "부서 데이터 없음" : $"{staff.Count}개 부서 · 총 {staff.Sum(x => x.CurrentLevel)}레벨");

            var schedule = save.Schedules?.Where(x => x != null && x.Status != ScheduleStatus.Cancelled && x.Date.CompareTo(save.CurrentDate) >= 0)
                .OrderBy(x => x.Date.Year).ThenBy(x => x.Date.Month).ThenBy(x => x.Date.Day).FirstOrDefault();
            var show = schedule == null ? null : save.Shows?.FirstOrDefault(x => x?.ScheduleId == schedule.Id);
            BindNextShow(save, schedule, show);
            var tasks = BuildTasks(save, schedule, show, active);
            BindTasks(tasks); BindFanReaction(active); BindStories(save); BindResult(save);
            Badge("home-badge", tasks.Count); Badge("show-badge", save.Shows?.Count(x => x != null && x.Status != ShowStatus.Completed) ?? 0); Badge("report-badge", save.ShowResults?.Count ?? 0);
            var notifications = document.rootVisualElement.Q<Button>("notifications"); if (notifications != null) notifications.text = $"알림  {tasks.Count}";
        }

        private void BindNextShow(GameSave save, ScheduleState schedule, ShowState show)
        {
            if (schedule == null)
            {
                Set("next-show-caption", "다음 쇼 일정"); Set("top-next-show", "예정된 쇼 없음"); Set("next-show-date", "—");
                Set("next-show-name", "예정된 쇼가 없습니다"); Set("next-show-detail", "시즌 일정을 확인하세요"); Set("next-show-type", "—"); Set("next-show-status", "일정 없음");
                SetProgress(0); Set("card-count", "카드 0개"); Set("participant-count", "출연 0명"); Set("estimated-cost", "예상 비용 —"); return;
            }
            Set("next-show-caption", $"다음 쇼 일정 · {save.CurrentDate.DaysUntil(schedule.Date)}일 남음");
            Set("top-next-show", show?.Name ?? ShowName(schedule.ShowType)); Set("next-show-date", $"{schedule.Date.Month:D2}\n{schedule.Date.Day:D2}");
            Set("next-show-name", show?.Name ?? ShowName(schedule.ShowType)); Set("next-show-detail", $"{Date(schedule.Date)} · 부킹 마감 {Date(schedule.BookingDeadline)}");
            Set("next-show-type", ShowType(schedule.ShowType)); Set("next-show-status", show == null ? "카드 미생성" : ShowStatusText(show.Status));
            var events = show == null ? new List<ShowEventState>() : save.ShowEvents?.Where(x => x != null && x.ShowId == show.Id).ToList() ?? new List<ShowEventState>();
            var planned = show?.CalculatePlannedDuration(events) ?? 0; var limit = Math.Max(show?.DurationLimit ?? 0, 1);
            SetProgress(show == null ? 0 : Math.Min(100, Mathf.RoundToInt(planned * 100f / limit)));
            Set("card-count", $"카드 {events.Count}개 · {planned}/{show?.DurationLimit ?? 0}분"); Set("participant-count", $"출연 {ParticipantCount(save, events)}명");
            Set("estimated-cost", show == null ? "예상 비용 —" : $"예상 비용 {Compact(show.EstimatedCost)}");
        }

        private static List<TaskItem> BuildTasks(GameSave save, ScheduleState schedule, ShowState show, List<WrestlerState> wrestlers)
        {
            var items = new List<TaskItem>();
            if (schedule != null && show == null) items.Add(new TaskItem("긴급", "다음 쇼 카드가 생성되지 않았습니다", "쇼 기획"));
            else if (show != null && string.IsNullOrEmpty(show.MainEventId)) items.Add(new TaskItem("긴급", "메인 이벤트가 지정되지 않았습니다", "쇼 기획"));
            var contract = save.Contracts?.Where(x => x != null && (x.Status == ContractStatus.Active || x.Status == ContractStatus.Expiring))
                .Select(x => new { Value = x, Days = save.CurrentDate.DaysUntil(x.EndDate) }).Where(x => x.Days >= 0 && x.Days <= 30).OrderBy(x => x.Days).FirstOrDefault();
            if (contract != null) { var wrestler = wrestlers.FirstOrDefault(x => x.Id == contract.Value.PersonId); items.Add(new TaskItem("높음", "계약 만료 임박", $"{contract.Days}일 남음", wrestler)); }
            var injured = wrestlers.FirstOrDefault(x => x.Condition?.InjuryStatus != InjuryStatus.None);
            if (injured != null) items.Add(new TaskItem("높음", "부상 상태", injured.Condition.InjuryStatus.ToString(), injured));
            var risk = wrestlers.Where(x => x.Condition != null).OrderBy(x => x.Condition.Condition).FirstOrDefault(x => x.Condition.Condition < 40f);
            if (risk != null) items.Add(new TaskItem("관심", "컨디션 저하", $"{risk.Condition.Condition:0}", risk));
            if (items.Count == 0) items.Add(new TaskItem("정상", "현재 우선 처리할 업무가 없습니다", ""));
            return items.Take(4).ToList();
        }

        private void BindTasks(List<TaskItem> items)
        {
            for (var i = 0; i < 4; i++)
            {
                var row = document.rootVisualElement.Q<VisualElement>($"task-{i}"); if (row == null) continue;
                row.style.display = i < items.Count ? DisplayStyle.Flex : DisplayStyle.None; if (i >= items.Count) continue;
                row.Q<Button>("task-wrestler-link")?.RemoveFromHierarchy();
                if (items[i].Wrestler != null) { var link = WrestlerProfileController.CreateLink(items[i].Wrestler); link.name = "task-wrestler-link"; row.Insert(1, link); }
                Set($"task-{i}-type", items[i].Severity); Set($"task-{i}-text", items[i].Text); Set($"task-{i}-link", items[i].Context);
            }
        }

        private void BindFanReaction(List<WrestlerState> wrestlers)
        {
            var values = wrestlers.Where(x => x.FanReaction != null).ToList();
            if (values.Count == 0) { Set("fan-caption", "데이터 없음"); Set("mania-value", "—"); Set("light-value", "—"); Set("family-value", "—"); FanBars(0, 0, 0); return; }
            var mania = values.Average(x => x.FanReaction.ManiaFanReaction); var light = values.Average(x => x.FanReaction.LightFanReaction); var family = values.Average(x => x.FanReaction.FamilyFanReaction);
            Set("fan-caption", $"{values.Count}명 기준"); Set("mania-value", Signed(mania)); Set("light-value", Signed(light)); Set("family-value", Signed(family)); FanBars(mania, light, family);
        }

        private void BindStories(GameSave save)
        {
            var changes = (save.ShowResults ?? new List<ShowResultState>()).Where(x => x != null).SelectMany(x => x.StoryChanges ?? new List<ResultChangeState>()).TakeLast(3).Reverse().ToList();
            for (var i = 0; i < 3; i++)
            {
                var row = document.rootVisualElement.Q<VisualElement>($"story-{i}"); if (row == null) continue;
                row.style.display = i < changes.Count || (i == 0 && changes.Count == 0) ? DisplayStyle.Flex : DisplayStyle.None;
                if (i < changes.Count) { Set($"story-{i}-title", string.IsNullOrWhiteSpace(changes[i].ValueKey) ? "스토리 변화" : changes[i].ValueKey); Set($"story-{i}-detail", $"{Signed(changes[i].Amount)} · {changes[i].Reason}"); }
                else if (i == 0) { Set("story-0-title", "데이터 없음"); Set("story-0-detail", "스토리 결과가 생성되면 표시됩니다"); }
            }
        }

        private void BindResult(GameSave save)
        {
            var result = save.ShowResults?.LastOrDefault(x => x != null);
            if (result == null) { Set("result-name", "결과 데이터 없음"); Set("result-finance", "완료된 쇼 결과가 생성되면 표시됩니다"); SetResultEmpty(); return; }
            var show = save.Shows?.FirstOrDefault(x => x?.Id == result.ShowId);
            var matches = (save.MatchResults ?? new List<MatchResultState>()).Where(x => x != null && result.MatchResultIds.Contains(x.Id)).ToList();
            var promos = (save.PromoResults ?? new List<PromoResultState>()).Where(x => x != null && result.PromoResultIds.Contains(x.Id)).ToList();
            var reactions = matches.Select(x => x.FanReaction).Concat(promos.Select(x => x.FanReaction)).Where(x => x != null).ToList();
            Set("result-name", show?.Name ?? "완료된 쇼"); Set("result-finance", $"수익 {Compact(result.FinancialSettlement.Revenue)} · 비용 {Compact(result.FinancialSettlement.Cost)} · 순손익 {SignedMoney(result.FinancialSettlement.NetIncome)}");
            Set("show-score", $"{result.ShowEvaluation.Score:0.0}"); Set("match-score", matches.Count == 0 ? "—" : $"{matches.Average(x => x.FinalMatchQuality):0.00}");
            Set("result-mania", reactions.Count == 0 ? "—" : Signed(reactions.Average(x => x.Mania))); Set("result-light", reactions.Count == 0 ? "—" : Signed(reactions.Average(x => x.Light))); Set("result-family", reactions.Count == 0 ? "—" : Signed(reactions.Average(x => x.Family)));
            var change = result.WrestlerChanges?.Concat(result.StoryChanges ?? new List<ResultChangeState>()).FirstOrDefault(); Set("result-change", change == null ? "기록된 변화 없음" : $"{change.ValueKey} {Signed(change.Amount)} · {change.Reason}");
        }

        private void ShowEmpty()
        {
            foreach (var id in new[] { "current-date", "top-cash", "top-next-show", "cash-value", "prestige-value", "roster-value", "risk-value", "staff-value" }) Set(id, "—");
            Set("next-show-caption", "다음 쇼 일정"); Set("next-show-date", "—"); Set("next-show-name", "활성 저장 데이터가 없습니다"); Set("next-show-detail", "새 게임 또는 저장 데이터를 연결하세요"); Set("next-show-type", "—"); Set("next-show-status", "저장 없음");
            Set("cash-detail", "활성 저장 없음"); Set("promotion-name", "—"); Set("promotion-abbreviation", "—"); Set("roster-detail", "전체 —"); Set("risk-detail", "부상 — · 컨디션 —"); Set("staff-detail", "부서 데이터 없음");
            SetProgress(0); Set("card-count", "카드 —"); Set("participant-count", "출연 —"); Set("estimated-cost", "예상 비용 —");
            BindTasks(new List<TaskItem> { new("—", "활성 저장 데이터 없음", "") }); BindFanReaction(new List<WrestlerState>()); BindStories(new GameSave());
            Set("result-name", "결과 데이터 없음"); Set("result-finance", "완료된 쇼 결과가 생성되면 표시됩니다"); SetResultEmpty();
            Badge("home-badge", 0); Badge("show-badge", 0); Badge("report-badge", 0); var button = document.rootVisualElement.Q<Button>("notifications"); if (button != null) button.text = "알림  0";
        }

        private void SetResultEmpty() { foreach (var id in new[] { "show-score", "match-score", "result-mania", "result-light", "result-family" }) Set(id, "—"); Set("result-change", "데이터 없음"); }
        private void SetProgress(int value) { Set("preparation-value", $"준비도 {value}%"); var bar = document.rootVisualElement.Q<VisualElement>(className: "progress-fill"); if (bar != null) bar.style.width = Length.Percent(value); }
        private void FanBars(float mania, float light, float family) { var v = new[] { Math.Max(0, mania + 100), Math.Max(0, light + 100), Math.Max(0, family + 100) }; var total = Math.Max(v.Sum(), 1); Width("mania-bar", (float)(v[0] / total * 100)); Width("light-bar", (float)(v[1] / total * 100)); Width("family-bar", (float)(v[2] / total * 100)); }
        private void Width(string id, float value) { var e = document.rootVisualElement.Q<VisualElement>(id); if (e != null) e.style.width = Length.Percent(value); }
        private void Badge(string id, int count) { var e = document.rootVisualElement.Q<Label>(id); if (e == null) return; e.text = count.ToString(); e.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None; }
        private void Set(string id, string value) { var label = document.rootVisualElement.Q<Label>(id); if (label != null) label.text = value ?? "—"; }
        private void ApplyIcons() { foreach (var name in new[] { "home", "roster", "teams", "locker", "scout", "show", "story", "title", "tournament", "schedule", "staff", "company", "finance", "report", "world", "history", "settings" }) { var icon = document.rootVisualElement.Q<VisualElement>($"icon-{name}"); var texture = Resources.Load<Texture2D>($"PWManagerUI/Icons/{name}"); if (icon != null && texture != null) icon.style.backgroundImage = new StyleBackground(texture); } }

        private static int ParticipantCount(GameSave save, List<ShowEventState> events) { var ids = new HashSet<string>(StringComparer.Ordinal); foreach (var e in events) if (e.EventType == ShowEventType.Match) { var m = save.MatchPlans?.FirstOrDefault(x => x?.Id == e.DetailId); if (m != null) { foreach (var s in m.Sides ?? new List<MatchSideState>()) foreach (var id in s.MemberIds ?? new List<string>()) ids.Add(id); foreach (var id in m.ParticipantIds ?? new List<string>()) ids.Add(id); } } else { var p = save.PromoPlans?.FirstOrDefault(x => x?.Id == e.DetailId); if (p != null) foreach (var id in p.ParticipantIds ?? new List<string>()) ids.Add(id); } return ids.Count; }
        private static string Date(GameDate d) => $"{d.Year}년 {d.Month}월 {d.Day}일";
        private static string Money(long v) => $"${v:N0}";
        private static string Compact(long v) => Math.Abs(v) >= 1_000_000 ? $"${v / 1_000_000f:0.0}M" : $"${v:N0}";
        private static string SignedMoney(long v) => $"{(v >= 0 ? "+" : "−")}{Compact(Math.Abs(v))}";
        private static string Signed(float v) => $"{(v >= 0 ? "+" : "")}{v:0.0}";
        private static string ShowName(ScheduledShowType v) => v switch { ScheduledShowType.Regular => "다음 정규 쇼", ScheduledShowType.PpvRegular => "다음 PPV", ScheduledShowType.PpvMajor => "다음 메이저 PPV", ScheduledShowType.PpvSignature => "다음 시그니처 PPV", _ => "다음 쇼" };
        private static string ShowType(ScheduledShowType v) => v switch { ScheduledShowType.Regular => "정규 쇼", ScheduledShowType.PpvRegular => "PPV", ScheduledShowType.PpvMajor => "메이저 PPV", ScheduledShowType.PpvSignature => "시그니처 PPV", _ => v.ToString() };
        private static string ShowStatusText(ShowStatus v) => v switch { ShowStatus.Draft => "초안", ShowStatus.Preparing => "준비 중", ShowStatus.Review => "검토 중", ShowStatus.Confirmed => "확정", ShowStatus.InProgress => "진행 중", ShowStatus.Completed => "완료", _ => v.ToString() };
        private static void OnClick(ClickEvent evt) { if (evt.target is Button b) Debug.Log($"Dashboard action selected: {b.name}"); }
        private readonly struct TaskItem { public readonly string Severity, Text, Context; public readonly WrestlerState Wrestler; public TaskItem(string severity, string text, string context, WrestlerState wrestler = null) { Severity = severity; Text = text; Context = context; Wrestler = wrestler; } }
    }

    public static class DashboardSession
    {
        public static GameSave ActiveSave { get; private set; }
        public static event Action<GameSave> SaveChanged;
        public static void SetActiveSave(GameSave save) { ActiveSave = save; SaveChanged?.Invoke(save); }
        public static void PersistActive()
        {
            if (ActiveSave == null) return;
            new SaveService(Path.Combine(Application.persistentDataPath, "Saves")).Save("autosave", ActiveSave);
            SaveChanged?.Invoke(ActiveSave);
        }
        public static void Clear() => SetActiveSave(null);
    }
}
