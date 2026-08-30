using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class WrestlerProfileController : MonoBehaviour
    {
        private static readonly string[] Palette = { "grade-ss", "grade-splus", "grade-s", "grade-aplus", "grade-a", "grade-bplus", "grade-b", "grade-c", "grade-d", "grade-e", "status-main", "status-mid", "status-jobber", "status-rookie", "role-face", "role-heel", "role-tweener", "state-good", "state-warn", "state-high", "state-danger", "momentum-low", "momentum-cool", "momentum-mid", "momentum-hot", "momentum-peak" };
        private static WrestlerProfileController instance;
        private VisualElement root;
        private WrestlerState boundWrestler;
        private string previewStyleId;
        private KayfabeAlignment previewAlignment;
        private PromoDisposition previewDisposition;
        private SliderInt roleSlider;
        private SliderInt dispositionSlider;
        private Button saveMatchStyleButton;
        private Button savePromoStyleButton;
        private bool canSaveStyles;

        private void OnEnable()
        {
            instance = this; root = GetComponent<UIDocument>().rootVisualElement;
            var back = root.Q<Button>("wrestler-profile-back"); if (back != null) back.clicked += Close;
            roleSlider = root.Q<SliderInt>("wp-role-slider");
            dispositionSlider = root.Q<SliderInt>("wp-disposition-slider");
            saveMatchStyleButton = root.Q<Button>("wp-save-match-style");
            savePromoStyleButton = root.Q<Button>("wp-save-promo-style");
            if (roleSlider != null) roleSlider.RegisterValueChangedCallback(OnRoleSliderChanged);
            if (dispositionSlider != null) dispositionSlider.RegisterValueChangedCallback(OnDispositionSliderChanged);
            if (saveMatchStyleButton != null) saveMatchStyleButton.clicked += SaveMatchStyle;
            if (savePromoStyleButton != null) savePromoStyleButton.clicked += SavePromoStyle;
            foreach (var key in new[] { "home", "roster", "teams", "locker", "scout", "show", "story", "title", "tournament", "schedule", "staff", "company", "finance", "report", "world", "history", "settings" })
            { var e = root.Q<VisualElement>($"wp-icon-{key}"); var t = Resources.Load<Texture2D>($"PWManagerUI/Icons/{key}"); if (e != null && t != null) e.style.backgroundImage = new StyleBackground(t); }
            if (Application.isPlaying) root.style.display = DisplayStyle.None;
        }
        private void OnDisable()
        {
            if (roleSlider != null) roleSlider.UnregisterValueChangedCallback(OnRoleSliderChanged);
            if (dispositionSlider != null) dispositionSlider.UnregisterValueChangedCallback(OnDispositionSliderChanged);
            if (saveMatchStyleButton != null) saveMatchStyleButton.clicked -= SaveMatchStyle;
            if (savePromoStyleButton != null) savePromoStyleButton.clicked -= SavePromoStyle;
            if (instance == this) instance = null;
        }
        private void Close() => root.style.display = DisplayStyle.None;

        public static void Open(WrestlerState wrestler)
        {
            if (wrestler == null || instance == null) return;
            instance.root.EnableInClassList("pre-game", DashboardSession.ActiveSave == null);
            instance.Bind(wrestler);
            instance.root.style.display = DisplayStyle.Flex;
        }
        public static Button CreateLink(WrestlerState wrestler, string className = null)
        {
            var button = new Button { text = wrestler?.Identity?.RingName ?? "—" }; button.AddToClassList("wrestler-link"); if (!string.IsNullOrEmpty(className)) button.AddToClassList(className);
            button.clicked += () => Open(wrestler); button.RegisterCallback<ClickEvent>(e => e.StopPropagation()); return button;
        }

        private void Bind(WrestlerState wrestler)
        {
            boundWrestler = wrestler;
            previewStyleId = wrestler.Presentation?.WrestlingStyleId ?? "style_007";
            previewAlignment = wrestler.Roster?.Alignment ?? KayfabeAlignment.Tweener;
            previewDisposition = wrestler.Presentation?.PromoDisposition ?? PromoDisposition.Balanced;
            var save = DashboardSession.ActiveSave; var id = wrestler.Identity ?? new WrestlerIdentityState(); var a = wrestler.Attributes ?? new WrestlerAttributesState();
            var c = wrestler.Condition ?? new WrestlerConditionState(); var g = wrestler.Growth ?? new WrestlerGrowthState(); var f = wrestler.FanReaction ?? new WrestlerFanReactionState();
            var roster = wrestler.Roster ?? new WrestlerRosterState(); var status = wrestler.Status ?? new WrestlerStatusState(); var date = save?.CurrentDate ?? id.CreatedDate;
            var card = CardStatus(status); var contract = save?.Contracts?.FirstOrDefault(x => x != null && x.PersonId == wrestler.Id && (x.Status == ContractStatus.Active || x.Status == ContractStatus.Expiring));
            canSaveStyles = contract != null;
            saveMatchStyleButton?.SetEnabled(canSaveStyles);
            savePromoStyleButton?.SetEnabled(canSaveStyles);
            var results = (save?.MatchResults ?? new List<MatchResultState>()).Where(x => IsParticipant(x, wrestler.Id)).ToList();
            var wins = results.Count(x => x.WinnerId == wrestler.Id || (x.IndirectWinnerIds?.Contains(wrestler.Id) ?? false)); var losses = results.Count(x => x.LoserTargetId == wrestler.Id || (x.IndirectLoserIds?.Contains(wrestler.Id) ?? false)); var draws = Math.Max(0, results.Count - wins - losses);
            var fatigue = Mathf.Clamp(100f - c.Condition, 0f, 100f); var momentum = wrestler.Momentum?.Momentum ?? 0f;

            Set("wp-promotion", save?.Promotion?.GetDisplayAbbreviation() ?? "PW"); Set("wp-breadcrumb", $"{id.RingName} / 개요"); Set("wp-date", Date(date));
            Set("wp-cash", save?.Promotion == null ? "—" : $"${save.Promotion.CalculateCurrentCash(save.Transactions):N0}"); var next = save?.Schedules?.Where(x => x != null && x.Date.CompareTo(date) >= 0).OrderBy(x => x.Date).FirstOrDefault(); Set("wp-next-show", next == null ? "예정 없음" : Date(next.Date));
            Set("wp-name", id.RingName); Set("wp-nickname", string.IsNullOrWhiteSpace(id.Nickname) ? id.LegalName : $"‘{id.Nickname}’"); Set("wp-initials", Initials(id.RingName)); Set("wp-status-title", card.Label);
            Set("wp-legal-name", id.LegalName); Set("wp-gender", id.Gender == WrestlerGender.Male ? "남성" : "여성"); Set("wp-height", $"{id.HeightCm} cm"); Set("wp-weight", $"{id.WeightKg} kg"); Set("wp-age", $"{Age(id.BirthDate, date)}세"); Set("wp-career", $"{id.CareerYears}년"); Set("wp-background", Background(id.Background)); Set("wp-current-style", StyleName(previewStyleId)); Set("wp-tag", string.IsNullOrWhiteSpace(roster.ActiveTagTeamId) ? "없음" : roster.ActiveTagTeamId); Set("wp-stable", string.IsNullOrWhiteSpace(roster.ActiveStableId) ? "없음" : roster.ActiveStableId);
            Set("wp-card-status", card.Label); Set("wp-role", Alignment(roster.Alignment)); Set("wp-contract-end", contract == null ? "계약 없음" : Date(contract.EndDate)); Set("wp-salary", contract == null ? "—" : $"${contract.MonthlySalary / 4:N0}"); Set("wp-last-match", roster.LastMatchDate.HasValue ? Date(roster.LastMatchDate.Value) : "기록 없음");
            Set("wp-availability", Availability(c.Availability)); Set("wp-fatigue", $"{fatigue:0}"); Set("wp-satisfaction", $"{c.Satisfaction:0}"); Set("wp-injury", Injury(c.InjuryStatus)); Set("wp-momentum", $"{momentum:0}");
            Set("wp-mania", $"{f.ManiaFanReaction:0}"); Set("wp-light", $"{f.LightFanReaction:0}"); Set("wp-family", $"{f.FamilyFanReaction:0}"); Set("wp-fan-total", $"{(f.ManiaFanReaction + f.LightFanReaction + f.FamilyFanReaction) / 3f:0} / 100");
            Set("wp-matches", Math.Max(status.OfficialMatchCount, results.Count).ToString()); Set("wp-wins", wins.ToString()); Set("wp-draws", draws.ToString()); Set("wp-losses", losses.ToString()); Set("wp-recent-result", results.Count == 0 ? "기록 없음" : wins > 0 ? "승리" : "패배");
            Class("wp-status-title", card.ClassName); Class("wp-card-status", card.ClassName); Class("wp-role", $"role-{roster.Alignment.ToString().ToLowerInvariant()}"); Class("wp-fatigue", StateClass(fatigue)); Class("wp-satisfaction", StateClass(100f - c.Satisfaction)); Class("wp-injury", c.InjuryStatus == InjuryStatus.None ? "state-good" : "state-danger"); Class("wp-momentum", MomentumClass(momentum)); GradeClass("wp-mania", f.ManiaFanReaction); GradeClass("wp-light", f.LightFanReaction); GradeClass("wp-family", f.FamilyFanReaction);
            Abilities("wp-match-abilities", new[] { ("경기 운영", a.RingPsychology), ("임기응변", a.RingImprovisation), ("브롤링", a.Brawling), ("파워", a.Power), ("테크니컬", a.Technical), ("하이플라잉", a.HighFlying), ("스팟 수행력", a.SpotWork), ("특수 경기", a.SpecialtyMatches), ("접수력", a.Selling), ("체력", a.Stamina) });
            Abilities("wp-promo-abilities", new[] { ("카리스마", a.Charisma), ("마이크", a.MicWork), ("즉흥성", a.Improvisation), ("캐릭터 표현력", a.Acting), ("페이스 연기", a.FaceWork), ("힐 연기", a.HeelWork), ("코미디", a.Comedy) });
            SetGrade("wp-match-potential", g.MatchPotentialCap); SetGrade("wp-promo-potential", g.PromoPotentialCap);
            roleSlider?.SetValueWithoutNotify(AlignmentIndex(previewAlignment));
            dispositionSlider?.SetValueWithoutNotify(DispositionIndex(previewDisposition));
            RenderStyleChoices();
            UpdatePreviewSummary();
        }

        private void RenderStyleChoices()
        {
            var match = root.Q<VisualElement>("wp-match-style-options");
            if (match != null)
            {
                match.Clear();
                foreach (var option in new[] { ("style_001", "브롤러"), ("style_007", "올라운더"), ("style_002", "파워하우스"), ("style_005", "루차 리브레"), ("style_003", "테크니션"), ("style_006", "자이언트"), ("style_004", "하이플라이어") })
                {
                    var captured = option.Item1;
                    var button = Choice(option.Item2, captured == previewStyleId, () => { previewStyleId = captured; RenderStyleChoices(); UpdatePreviewSummary(); });
                    if (captured == "style_006" && (boundWrestler?.Identity?.HeightCm ?? 0) < 195) button.SetEnabled(false);
                    match.Add(button);
                }
            }

        }

        private void OnRoleSliderChanged(ChangeEvent<int> evt)
        {
            previewAlignment = evt.newValue == 0 ? KayfabeAlignment.Face : evt.newValue == 2 ? KayfabeAlignment.Heel : KayfabeAlignment.Tweener;
            UpdatePreviewSummary();
        }

        private void OnDispositionSliderChanged(ChangeEvent<int> evt)
        {
            previewDisposition = evt.newValue == 0 ? PromoDisposition.Serious : evt.newValue == 2 ? PromoDisposition.Comic : PromoDisposition.Balanced;
            UpdatePreviewSummary();
        }

        private void SaveMatchStyle()
        {
            if (!canSaveStyles || boundWrestler == null) return;
            boundWrestler.Presentation ??= new WrestlerPresentationState();
            boundWrestler.Presentation.WrestlingStyleId = previewStyleId;
            Set("wp-current-style", StyleName(previewStyleId));
            DashboardSession.PersistActive();
        }

        private void SavePromoStyle()
        {
            if (!canSaveStyles || boundWrestler == null) return;
            boundWrestler.Roster ??= new WrestlerRosterState();
            boundWrestler.Presentation ??= new WrestlerPresentationState();
            boundWrestler.Roster.Alignment = previewAlignment;
            boundWrestler.Presentation.PromoDisposition = previewDisposition;
            Set("wp-role", Alignment(previewAlignment));
            Class("wp-role", $"role-{previewAlignment.ToString().ToLowerInvariant()}");
            DashboardSession.PersistActive();
        }

        private static Button Choice(string label, bool selected, Action clicked)
        {
            var button = new Button(clicked) { text = label };
            button.AddToClassList("wp-choice");
            button.EnableInClassList("selected", selected);
            return button;
        }

        private void UpdatePreviewSummary()
        {
            if (boundWrestler == null) return;
            SetGrade("wp-match-overall", WrestlerOverallCalculator.Match(boundWrestler, previewStyleId));
            SetGrade("wp-promo-overall", WrestlerOverallCalculator.Promo(boundWrestler, previewAlignment, previewDisposition));
            ApplyMatchStyleEmphasis();
        }

        private void Abilities(string targetId, (string Name, float Value)[] values)
        {
            var target = root.Q<VisualElement>(targetId); if (target == null) return; target.Clear(); for (var i = 0; i < values.Length; i++) AddAbility(target, values[i].Name, values[i].Value, i % 2 == 1 ? "alt" : null);
        }
        private static void AddAbility(VisualElement target, string title, float value, string extra)
        {
            var row = new VisualElement(); row.AddToClassList("wp-ability-row"); if (!string.IsNullOrEmpty(extra)) row.AddToClassList(extra); var name = new Label(title); name.AddToClassList("wp-ability-label"); var grade = new Label(Grade(value)); grade.AddToClassList("wp-ability-value"); grade.AddToClassList(ToGradeClass(value)); row.Add(name); row.Add(grade); target.Add(row);
        }
        private void Set(string key, string value) { var label = root.Q<Label>(key); if (label != null) label.text = value ?? "—"; }
        private void Class(string key, string value) { var e = root.Q<VisualElement>(key); if (e == null) return; foreach (var old in Palette) e.RemoveFromClassList(old); e.AddToClassList(value); }
        private void GradeClass(string key, float value) => Class(key, ToGradeClass(value));
        private void SetGrade(string key, float value) { Set(key, Grade(value)); GradeClass(key, value); }
        private void ApplyMatchStyleEmphasis()
        {
            var target = root.Q<VisualElement>("wp-match-abilities");
            if (target == null) return;
            foreach (var row in target.Children())
            {
                row.RemoveFromClassList("style-priority-1");
                row.RemoveFromClassList("style-priority-2");
            }

            var priorities = previewStyleId switch
            {
                "style_001" => (new[] { "브롤링" }, new[] { "파워" }),
                "style_002" => (new[] { "파워" }, new[] { "테크니컬" }),
                "style_003" => (new[] { "테크니컬" }, new[] { "하이플라잉" }),
                "style_004" => (new[] { "하이플라잉" }, new[] { "테크니컬" }),
                "style_005" => (new[] { "하이플라잉" }, new[] { "테크니컬" }),
                "style_006" => (new[] { "파워" }, new[] { "브롤링" }),
                _ => (Array.Empty<string>(), new[] { "브롤링", "파워", "하이플라잉", "테크니컬" })
            };
            foreach (var row in target.Children())
            {
                var label = row.Q<Label>(className: "wp-ability-label")?.text;
                if (priorities.Item1.Contains(label)) row.AddToClassList("style-priority-1");
                else if (priorities.Item2.Contains(label)) row.AddToClassList("style-priority-2");
            }
        }
        private static bool IsParticipant(MatchResultState r, string id) => r != null && (r.WrestlerPerformances?.Any(x => x.WrestlerId == id) ?? false);
        private static int Age(GameDate birth, GameDate current) => Math.Max(0, current.Year - birth.Year - (current.Month < birth.Month || current.Month == birth.Month && current.Day < birth.Day ? 1 : 0));
        private static string Date(GameDate d) => $"{d.Year}년 {d.Month}월 {d.Day}일";
        private static string Initials(string name) { if (string.IsNullOrWhiteSpace(name)) return "PW"; var p = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); return p.Length > 1 ? $"{p[0][0]}{p[^1][0]}".ToUpperInvariant() : name.Substring(0, Math.Min(2, name.Length)).ToUpperInvariant(); }
        private static string Grade(float v) => v >= 19 ? "SS" : v >= 18 ? "S+" : v >= 17 ? "S" : v >= 16 ? "A+" : v >= 15 ? "A" : v >= 14 ? "B+" : v >= 13 ? "B" : v >= 10 ? "C" : v >= 7 ? "D" : "E";
        private static string ToGradeClass(float v) => "grade-" + Grade(v).ToLowerInvariant().Replace("+", "plus");
        private static (string Label, string ClassName) CardStatus(WrestlerStatusState s) => s.IsRookie ? ("신인", "status-rookie") : s.StatusValue >= 80 ? ("메인이벤터", "status-main") : s.StatusValue >= 45 ? ("미드카더", "status-mid") : ("자버", "status-jobber");
        private static string Alignment(KayfabeAlignment v) => v == KayfabeAlignment.Face ? "페이스" : v == KayfabeAlignment.Heel ? "힐" : "트위너";
        private static string Availability(WrestlerAvailability v) => v == WrestlerAvailability.Available ? "정상" : v == WrestlerAvailability.Limited ? "제한" : v == WrestlerAvailability.MatchUnavailable ? "경기 불가" : "활동 불가";
        private static string Injury(InjuryStatus v) => v == InjuryStatus.None ? "없음" : v == InjuryStatus.Minor ? "경상" : v == InjuryStatus.Moderate ? "부상" : "중상";
        private static string Background(WrestlerBackground v) => v == WrestlerBackground.Rookie ? "신인" : v == WrestlerBackground.Athlete ? "스포츠" : v == WrestlerBackground.Entertainer ? "연예계" : "타 단체";
        private static int AlignmentIndex(KayfabeAlignment v) => v == KayfabeAlignment.Face ? 0 : v == KayfabeAlignment.Heel ? 2 : 1;
        private static int DispositionIndex(PromoDisposition v) => v == PromoDisposition.Serious ? 0 : v == PromoDisposition.Comic ? 2 : 1;
        private static string StyleName(string id) => id switch { "style_001" => "브롤러", "style_002" => "파워하우스", "style_003" => "테크니션", "style_004" => "하이플라이어", "style_005" => "루차 리브레", "style_006" => "자이언트", _ => "올라운더" };
        private static string StateClass(float v) => v < 30 ? "state-good" : v < 60 ? "state-warn" : v < 80 ? "state-high" : "state-danger";
        private static string MomentumClass(float v) => v < 20 ? "momentum-low" : v < 40 ? "momentum-cool" : v < 50 ? "momentum-mid" : v < 80 ? "momentum-hot" : "momentum-peak";
    }
}
