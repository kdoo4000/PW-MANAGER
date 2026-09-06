using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PWManager.Data.Catalogs;
using PWManager.Data.Definitions;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Infrastructure.Save;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public static class DashboardBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ValidateSceneUi()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Main Scene") return;
            if (GameObject.Find("Title UI")?.GetComponent<UIDocument>() == null)
                Debug.LogError("Main Scene에 Title UI UIDocument가 없습니다.");
            if (GameObject.Find("New Game UI")?.GetComponent<UIDocument>() == null)
                Debug.LogError("Main Scene에 New Game UI UIDocument가 없습니다.");
            if (GameObject.Find("Dashboard UI")?.GetComponent<UIDocument>() == null)
                Debug.LogError("Main Scene에 Dashboard UI UIDocument가 없습니다.");
        }
    }

    [RequireComponent(typeof(UIDocument))]
    public sealed class TitleScreenController : MonoBehaviour
    {
        private const string SaveSlot = "autosave";
        private const long InitialCash = 100000;
        private static readonly string[] StepTitles = { "기본 정보", "능력 설정", "단체 설정", "선수 영입", "시즌 운영", "경기장 계약", "최종 검토" };
        private static readonly string[] PlayerRoleChoices = { "선수 겸 단장", "전문 단장" };
        private static readonly string[] GenderChoices = { "남성", "여성" };
        private static readonly string[] ReputationChoices = { "지역 무명", "지역 스타", "전국구", "스타", "전설" };
        private static readonly string[] WrestlingTypeChoices = { "워커", "균형", "쇼맨" };
        private static readonly string[] WrestlingStyleChoices = { "브롤러", "파워하우스", "테크니션", "하이플라이어", "루차 리브레", "자이언트", "올라운더" };
        private static readonly string[] WrestlingStyleIds = { "style_001", "style_002", "style_003", "style_004", "style_005", "style_006", "style_007" };
        private static readonly string[] RegularChoices = { "월 1회", "격주 1회", "주 1회", "주 2회", "주 3회" };
        private static readonly string[] PpvChoices = { "4개월마다", "분기마다", "2개월마다", "매월" };

        private VisualElement root;
        private VisualElement dashboard;
        private UIDocument dashboardDocument;
        private VisualElement newGameDocumentRoot;
        private VisualElement newGameRoot;
        private VisualElement menu;
        private VisualElement wizard;
        private Label status;
        private Label wizardTitle;
        private Label wizardMessage;
        private TextField promotionName;
        private TextField promotionAbbreviation;
        private TextField playerName;
        private DropdownField playerRole;
        private DropdownField playerGender;
        private DropdownField playerReputation;
        private DropdownField playerWrestlingType;
        private DropdownField playerWrestlingStyle;
        private TextField playerBirthDate;
        private IntegerField playerHeight;
        private IntegerField playerWeight;
        private VisualElement birthDateCalendar;
        private VisualElement calendarDays;
        private DropdownField calendarYear;
        private DropdownField calendarMonth;
        private DateTime visibleCalendarMonth = new(1994, 6, 1);
        private VisualElement wrestlerAbilitySettings;
        private readonly List<string> availableWrestlingStyleIds = new();
        private DropdownField regularFrequency;
        private DropdownField ppvFrequency;
        private Button backButton;
        private Button nextButton;
        private VisualElement loadOverlay;
        private VisualElement loadRows;
        private SaveService saveService;
        private StaticContentCatalog catalog;
        private List<WrestlerState> candidates = new();
        private HashSet<string> selectedWrestlerIds = new(StringComparer.Ordinal);
        private VenueDefinition selectedVenue;
        private readonly List<RosterSortCriterion> rosterSorts = new() { new RosterSortCriterion("name", true) };
        private int seed;
        private int step;

        private string SaveDirectory => Path.Combine(Application.persistentDataPath, "Saves");

        private void Start()
        {
            var documentRoot = GetComponent<UIDocument>().rootVisualElement;
            root = documentRoot.Q<VisualElement>("title-screen");
            newGameDocumentRoot = GameObject.Find("New Game UI")?.GetComponent<UIDocument>()?.rootVisualElement;
            newGameRoot = newGameDocumentRoot?.Q<VisualElement>("new-game-screen");
            dashboardDocument = GameObject.Find("Dashboard UI")?.GetComponent<UIDocument>();
            dashboard = dashboardDocument?.rootVisualElement;
            if (root == null || newGameRoot == null || dashboard == null)
            {
                Debug.LogError("Title UI와 Dashboard UI의 UXML 연결을 확인하세요.");
                return;
            }
            menu = documentRoot.Q<VisualElement>("title-menu");
            wizard = Ui<VisualElement>("new-game-wizard");
            status = documentRoot.Q<Label>("title-status");
            wizardTitle = Ui<Label>("wizard-title");
            wizardMessage = Ui<Label>("wizard-message");
            promotionName = Ui<TextField>("promotion-name-input");
            promotionAbbreviation = Ui<TextField>("promotion-abbreviation-input");
            playerName = Ui<TextField>("player-name-input");
            playerRole = Ui<DropdownField>("player-role");
            playerGender = Ui<DropdownField>("player-gender");
            playerReputation = Ui<DropdownField>("player-reputation");
            playerWrestlingType = Ui<DropdownField>("player-wrestling-type");
            playerWrestlingStyle = Ui<DropdownField>("player-wrestling-style");
            playerBirthDate = Ui<TextField>("player-birth-date");
            playerHeight = Ui<IntegerField>("player-height");
            playerWeight = Ui<IntegerField>("player-weight");
            birthDateCalendar = Ui<VisualElement>("birth-date-calendar");
            calendarDays = Ui<VisualElement>("calendar-days");
            calendarYear = Ui<DropdownField>("calendar-year");
            calendarMonth = Ui<DropdownField>("calendar-month");
            wrestlerAbilitySettings = Ui<VisualElement>("wrestler-ability-settings");
            regularFrequency = Ui<DropdownField>("regular-frequency");
            ppvFrequency = Ui<DropdownField>("ppv-frequency");
            backButton = Ui<Button>("wizard-back");
            nextButton = Ui<Button>("wizard-next");
            loadOverlay = root.Q<VisualElement>("title-load-overlay");
            loadRows = root.Q<VisualElement>("title-load-rows");
            saveService = new SaveService(SaveDirectory);
            catalog = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog");

            BindButton("new-game", OpenWizard);
            BindButton("continue-game", ContinueGame);
            BindButton("load-game", OpenLoadMenu);
            BindButton("title-load-close", CloseLoadMenu);
            BindButton("cancel-new-game", CloseWizard);
            BindButton("wizard-back", PreviousStep);
            BindButton("wizard-next", NextStep);
            BindButton("birth-date-button", ToggleBirthDateCalendar);
            BindButton("calendar-previous", () => ShiftCalendarMonth(-1));
            BindButton("calendar-next", () => ShiftCalendarMonth(1));
            BindButton("settings", () => ShowStatus("설정 화면은 다음 개발 단계에서 연결됩니다."));
            BindButton("credits", () => ShowStatus("PW MANAGER · 개발 중인 플레이 버전"));
            BindButton("exit-game", Application.Quit);
            promotionName.RegisterValueChangedCallback(_ => RefreshSummary());
            promotionAbbreviation.RegisterValueChangedCallback(_ => RefreshSummary());
            playerName.RegisterValueChangedCallback(_ => RefreshSummary());
            playerRole.RegisterValueChangedCallback(_ => { RefreshStyleChoices(); RefreshPlayerAbilities(); RefreshSummary(); });
            playerGender.RegisterValueChangedCallback(_ => ApplyPhysicalLimits());
            playerReputation.RegisterValueChangedCallback(_ => RefreshSummary());
            playerReputation.RegisterValueChangedCallback(_ => RefreshPlayerAbilities());
            playerWrestlingType.RegisterValueChangedCallback(_ => { RefreshPlayerAbilities(); RefreshSummary(); });
            playerWrestlingStyle.RegisterValueChangedCallback(_ => RefreshPlayerAbilities());
            playerBirthDate.isReadOnly = true;
            playerBirthDate.RegisterCallback<ClickEvent>(_ => ToggleBirthDateCalendar());
            playerHeight.RegisterValueChangedCallback(evt =>
            {
                var range = PlayerCharacterRules.HeightRange(SelectedPlayerGender());
                playerHeight.SetValueWithoutNotify(Mathf.Clamp(evt.newValue, range.Min, range.Max));
                RefreshStyleChoices(); RefreshPlayerAbilities();
            });
            playerWeight.RegisterValueChangedCallback(evt =>
            {
                var range = PlayerCharacterRules.WeightRange(SelectedPlayerGender());
                playerWeight.SetValueWithoutNotify(Mathf.Clamp(evt.newValue, range.Min, range.Max));
            });
            calendarYear.RegisterValueChangedCallback(_ => ChangeCalendarPeriod());
            calendarMonth.RegisterValueChangedCallback(_ => ChangeCalendarPeriod());
            regularFrequency.RegisterValueChangedCallback(_ => RefreshSummary());

            dashboard.style.display = DisplayStyle.None;
            root.style.display = DisplayStyle.Flex;
            newGameDocumentRoot.style.display = DisplayStyle.None;
            RefreshContinueButton();
        }

        private void BindButton(string name, Action action)
        {
            var button = Ui<Button>(name);
            if (button != null) button.clicked += action;
        }

        private T Ui<T>(string name) where T : VisualElement => newGameRoot?.Q<T>(name) ?? root?.Q<T>(name);

        private void OpenWizard()
        {
            if (catalog == null) { ShowStatus("게임 기본 데이터를 불러오지 못했습니다.", true); return; }
            try
            {
                DashboardSession.Clear();
                seed = unchecked((int)DateTime.UtcNow.Ticks);
                candidates = new WrestlerGenerator(new StaticContentRegistry(catalog), seed)
                    .GenerateInitialCandidates(new GameDate(2026, 6, 1)).Take(20).ToList();
                selectedWrestlerIds ??= new HashSet<string>(StringComparer.Ordinal);
                selectedWrestlerIds.Clear();
                selectedVenue = catalog.Venues.FirstOrDefault(x => x.RequiredPrestige <= 0);
                playerRole.choices = PlayerRoleChoices.ToList(); playerRole.index = 0;
                playerGender.choices = GenderChoices.ToList(); playerGender.index = 0;
                playerReputation.choices = ReputationChoices.ToList(); playerReputation.index = 2;
                playerWrestlingType.choices = WrestlingTypeChoices.ToList(); playerWrestlingType.index = 1;
                playerBirthDate.value = "1994-06-01"; playerHeight.value = 180; playerWeight.value = 90;
                birthDateCalendar.AddToClassList("hidden");
                RefreshStyleChoices("style_007");
                RefreshPlayerAbilities();
                regularFrequency.choices = RegularChoices.ToList();
                regularFrequency.index = 0;
                ppvFrequency.choices = PpvChoices.ToList();
                ppvFrequency.index = 0;
                BuildRosterHeader();
                RenderRoster();
                RenderVenues();
                BuildProgress();
                dashboard.style.display = DisplayStyle.None;
                root.style.display = DisplayStyle.None;
                newGameDocumentRoot.style.display = DisplayStyle.Flex;
                newGameRoot.style.display = DisplayStyle.Flex;
                wizard.style.display = DisplayStyle.Flex;
                ShowStep(0);
            }
            catch (Exception exception) { Debug.LogException(exception); ShowStatus($"새 게임 준비에 실패했습니다: {exception.Message}", true); }
        }

        private void CloseWizard()
        {
            newGameDocumentRoot.style.display = DisplayStyle.None;
            root.style.display = DisplayStyle.Flex;
            status.text = string.Empty;
        }

        private void BuildProgress()
        {
            var progress = Ui<VisualElement>("wizard-progress");
            progress.Clear();
            for (var i = 0; i < StepTitles.Length; i++)
            {
                var item = new Label($"{i + 1}.  {StepTitles[i]}");
                item.AddToClassList("progress-item");
                item.name = $"progress-{i}";
                progress.Add(item);
            }
        }

        private void ShowStep(int value)
        {
            step = Mathf.Clamp(value, 0, StepTitles.Length - 1);
            wizardTitle.text = StepTitles[step];
            wizardMessage.text = string.Empty;
            for (var i = 0; i < StepTitles.Length; i++)
            {
                Ui<VisualElement>($"wizard-step-{i}").EnableInClassList("hidden", i != step);
                var progress = Ui<Label>($"progress-{i}");
                progress.EnableInClassList("active", i == step);
                progress.EnableInClassList("done", i < step);
            }
            backButton.text = step == 0 ? "취소" : "이전";
            nextButton.text = step == StepTitles.Length - 1 ? "게임 시작" : "다음";
            if (step == 1) RefreshStyleChoices();
            if (step == StepTitles.Length - 1) RenderFinalReview();
            RefreshSummary();
        }

        private void PreviousStep()
        {
            if (step == 0) CloseWizard(); else ShowStep(step - 1);
        }

        private void NextStep()
        {
            if (!ValidateStep()) return;
            if (step < StepTitles.Length - 1) ShowStep(step + 1); else StartNewGame();
        }

        private bool ValidateStep()
        {
            if (step == 0)
            {
                if (string.IsNullOrWhiteSpace(playerName.value)) return Fail("플레이어 이름을 입력해 주세요.");
                if (!TryPlayerBirthDate(out var birthDate)) return Fail("생년월일을 YYYY-MM-DD 형식으로 입력해 주세요.");
                var age = birthDate.AgeOn(new GameDate(2026, 6, 1));
                if (age < 18 || age > 70) return Fail("게임 시작일 기준 만 18~70세만 설정할 수 있습니다.");
                if (!PlayerCharacterRules.IsPhysicalProfileValid(SelectedPlayerGender(), playerHeight.value, playerWeight.value))
                    return Fail("성별에 맞는 키와 몸무게 범위를 확인해 주세요.");
            }
            if (step == 1)
            {
                if (playerRole.index == 0 && SelectedPlayerStyleId() == "style_006" && !PlayerCharacterRules.IsStyleAvailable("style_006", playerHeight.value, SelectedPlayerGender()))
                    return Fail($"자이언트는 신장 {(SelectedPlayerGender() == WrestlerGender.Male ? 195 : 180)}cm 이상인 선수만 선택할 수 있습니다.");
            }
            if (step == 2)
            {
                if (string.IsNullOrWhiteSpace(promotionName.value)) return Fail("단체 이름을 입력해 주세요.");
                var abbreviation = promotionAbbreviation.value?.Trim();
                if (string.IsNullOrWhiteSpace(abbreviation) || abbreviation.Length < 2 || abbreviation.Length > 8) return Fail("단체 약자는 2~8자로 입력해 주세요.");
            }
            if (step == 3 && (selectedWrestlerIds.Count < 8 || selectedWrestlerIds.Count > 20)) return Fail("선수는 8~20명 선택해야 합니다.");
            if (step == 3 && candidates.Where(x => selectedWrestlerIds.Contains(x.Id)).Sum(x => ContractOfferFor(x).SigningBonus) > InitialCash)
                return Fail("선수 계약금이 시작 자금을 초과했습니다. 선택 인원을 조정해 주세요.");
            if (step == 5 && selectedVenue == null) return Fail("정규 시즌 경기장을 선택해 주세요.");
            wizardMessage.text = string.Empty;
            return true;
        }

        private bool Fail(string message) { wizardMessage.text = message; return false; }

        private void RenderRoster()
        {
            var list = Ui<ScrollView>("roster-list");
            list.Clear();
            foreach (var wrestler in SortedRoster())
            {
                var current = wrestler;
                var row = new Button();
                row.AddToClassList("selection-row");
                row.AddToClassList("roster-row");
                row.EnableInClassList("selected", selectedWrestlerIds.Contains(current.Id));
                AddRosterCell(row, selectedWrestlerIds.Contains(current.Id) ? "✓" : "○", "col-check");
                row.Add(WrestlerProfileController.CreateLink(current, "col-name"));
                AddRosterCell(row, GenderText(current.Identity.Gender), "col-gender");
                AddRosterCell(row, AgeAtSeasonStart(current).ToString(), "col-age");
                AddRosterCell(row, $"{current.Identity.HeightCm}cm", "col-height");
                AddRosterCell(row, $"{current.Identity.WeightKg}kg", "col-weight");
                AddRosterCell(row, StyleText(current.Presentation.WrestlingStyleId), "col-style");
                AddRatingCell(row, WrestlerOverallCalculator.Match(current));
                AddRatingCell(row, WrestlerOverallCalculator.Promo(current));
                var offer = ContractOfferFor(current);
                AddRosterCell(row, Money(offer.SigningBonus), "col-money", "roster-money");
                AddRosterCell(row, Money(offer.MonthlySalary), "col-money", "roster-money");
                row.clicked += () =>
                {
                    if (!selectedWrestlerIds.Remove(current.Id)) selectedWrestlerIds.Add(current.Id);
                    RenderRoster();
                    RefreshSummary();
                };
                list.Add(row);
            }
        }

        private IEnumerable<WrestlerState> SortedRoster()
        {
            IOrderedEnumerable<WrestlerState> ordered = null;
            foreach (var criterion in rosterSorts)
            {
                var selector = RosterSortSelector(criterion.Key);
                ordered = ordered == null
                    ? criterion.Ascending ? candidates.OrderBy(selector) : candidates.OrderByDescending(selector)
                    : criterion.Ascending ? ordered.ThenBy(selector) : ordered.ThenByDescending(selector);
            }
            return (ordered ?? candidates.OrderBy(x => x.Identity.RingName)).ThenBy(x => x.Identity.RingName);
        }

        private Func<WrestlerState, IComparable> RosterSortSelector(string key)
        {
            return key switch
            {
                "gender" => x => x.Identity.Gender, "age" => x => AgeAtSeasonStart(x),
                "height" => x => x.Identity.HeightCm, "weight" => x => x.Identity.WeightKg,
                "style" => x => StyleText(x.Presentation.WrestlingStyleId),
                "match" => x => WrestlerOverallCalculator.Match(x), "promo" => x => WrestlerOverallCalculator.Promo(x),
                "signing" => x => ContractOfferFor(x).SigningBonus, "salary" => x => ContractOfferFor(x).MonthlySalary,
                _ => x => x.Identity.RingName
            };
        }

        private void BuildRosterHeader()
        {
            var header = Ui<VisualElement>("roster-table-header");
            header.Clear();
            AddHeaderSpacer(header, "col-check");
            AddSortHeader(header, "이름", "name", "col-name");
            AddSortHeader(header, "성별", "gender", "col-gender");
            AddSortHeader(header, "나이", "age", "col-age");
            AddSortHeader(header, "신장", "height", "col-height");
            AddSortHeader(header, "체중", "weight", "col-weight");
            AddSortHeader(header, "경기 스타일", "style", "col-style");
            AddSortHeader(header, "경기", "match", "col-rating");
            AddSortHeader(header, "프로모", "promo", "col-rating");
            AddSortHeader(header, "계약금", "signing", "col-money");
            AddSortHeader(header, "월급", "salary", "col-money");
        }

        private static void AddRosterCell(VisualElement row, string text, string widthClass, string extraClass = null)
        {
            var label = new Label(text);
            label.AddToClassList("roster-cell");
            label.AddToClassList(widthClass);
            if (!string.IsNullOrEmpty(extraClass)) label.AddToClassList(extraClass);
            row.Add(label);
        }

        private static void AddRatingCell(VisualElement row, float value)
        {
            var grade = RatingGrade(value);
            var label = new Label(grade);
            label.AddToClassList("roster-cell");
            label.AddToClassList("col-rating");
            label.AddToClassList("roster-grade");
            label.AddToClassList(grade switch
            {
                "SS" => "grade-ss", "S+" => "grade-splus", "S" => "grade-s", "A+" => "grade-aplus",
                "A" => "grade-a", "B+" => "grade-bplus", "B" => "grade-b", "C" => "grade-c",
                "D" => "grade-d", _ => "grade-e"
            });
            row.Add(label);
        }

        private void RenderVenues()
        {
            var grid = Ui<VisualElement>("venue-grid");
            grid.Clear();
            foreach (var venue in catalog.Venues.OrderBy(x => x.RequiredPrestige).ThenBy(x => x.Capacity))
            {
                var current = venue;
                var available = current.RequiredPrestige <= 0;
                var selected = selectedVenue == current;
                var card = new Button();
                card.AddToClassList("venue-card");
                card.EnableInClassList("selected", selected);
                card.EnableInClassList("locked", !available);

                var heading = new VisualElement();
                heading.AddToClassList("venue-card-heading");
                var title = new Label(current.DisplayName);
                title.AddToClassList("venue-card-title");
                var state = new Label(selected ? "선택됨" : available ? "선택 가능" : "잠금");
                state.AddToClassList("venue-state");
                state.EnableInClassList("available", available && !selected);
                state.EnableInClassList("selected", selected);
                state.EnableInClassList("locked", !available);
                heading.Add(title);
                heading.Add(state);
                card.Add(heading);

                var meta = new Label($"{VenueScaleText(current.Scale)}  ·  수용 {current.Capacity:N0}명");
                meta.AddToClassList("venue-card-meta");
                card.Add(meta);

                var stats = new VisualElement();
                stats.AddToClassList("venue-card-stats");
                AddVenueStat(stats, "필요 위상", current.RequiredPrestige.ToString("N0"));
                AddVenueStat(stats, "해금 비용", Money(current.UnlockCost));
                AddVenueStat(stats, "쇼당 제작비", Money(current.ProductionCost));
                AddVenueStat(stats, "기준 티켓", Money(current.BaseTicketPrice));
                card.Add(stats);

                if (available) card.clicked += () => { selectedVenue = current; RenderVenues(); RefreshSummary(); };
                grid.Add(card);
            }
        }

        private static void AddVenueStat(VisualElement parent, string labelText, string valueText)
        {
            var stat = new VisualElement();
            stat.AddToClassList("venue-stat");
            var label = new Label(labelText);
            label.AddToClassList("venue-stat-label");
            var value = new Label(valueText);
            value.AddToClassList("venue-stat-value");
            stat.Add(label);
            stat.Add(value);
            parent.Add(stat);
        }

        private static void AddHeaderSpacer(VisualElement header, string widthClass)
        {
            var spacer = new Label();
            spacer.AddToClassList(widthClass);
            header.Add(spacer);
        }

        private void AddSortHeader(VisualElement header, string label, string key, string widthClass)
        {
            var priority = rosterSorts.FindIndex(x => x.Key == key);
            var active = priority == 0;
            var ascending = active && rosterSorts[0].Ascending;
            var marker = active ? ascending ? "  ↑" : "  ↓" : string.Empty;
            var button = new Button { text = label + marker };
            button.AddToClassList("table-header-button");
            button.AddToClassList(widthClass);
            button.EnableInClassList("active", active);
            button.clicked += () =>
            {
                var existing = rosterSorts.FindIndex(x => x.Key == key);
                if (existing == 0) rosterSorts[0].Ascending = !rosterSorts[0].Ascending;
                else
                {
                    if (existing > 0) rosterSorts.RemoveAt(existing);
                    rosterSorts.Insert(0, new RosterSortCriterion(key, true));
                }
                BuildRosterHeader(); RenderRoster();
            };
            header.Add(button);
        }

        private sealed class RosterSortCriterion
        {
            public readonly string Key;
            public bool Ascending;

            public RosterSortCriterion(string key, bool ascending)
            {
                Key = key;
                Ascending = ascending;
            }
        }

        private void RefreshSummary()
        {
            if (root == null) return;
            var selected = candidates.Where(x => selectedWrestlerIds.Contains(x.Id)).ToList();
            var signing = selected.Sum(x => ContractOfferFor(x).SigningBonus);
            var monthly = selected.Sum(x => ContractOfferFor(x).MonthlySalary);
            var showCost = selectedVenue == null ? 0 : selectedVenue.ProductionCost * RegularShowCount();
            Ui<Label>("summary-promotion").text = $"{promotionName.value?.Trim()?.ToUpperInvariant()} · {promotionAbbreviation.value?.Trim()?.ToUpperInvariant()}";
            Ui<Label>("summary-player").text = $"{playerName.value?.Trim()} · {ReputationChoices[Mathf.Max(0, playerReputation.index)]}";
            Ui<Label>("summary-roster").text = $"{selectedWrestlerIds.Count}명";
            Ui<Label>("summary-signing").text = Money(signing);
            Ui<Label>("summary-salary").text = Money(monthly);
            Ui<Label>("summary-season-fixed").text = Money(monthly * 12);
            Ui<Label>("summary-show-cost").text = selectedVenue == null ? "—" : Money(showCost);
            Ui<Label>("summary-remaining").text = Money(InitialCash - signing);
            var count = Ui<Label>("roster-count");
            if (count != null) count.text = $"{selectedWrestlerIds.Count}명 선택";
        }

        private void RefreshPlayerAbilities()
        {
            var box = Ui<VisualElement>("player-ability-columns");
            if (box == null || playerRole.index < 0 || playerReputation.index < 0 || playerWrestlingType.index < 0) return;
            box.Clear();
            var styleId = SelectedPlayerStyleId();
            var build = PlayerCharacterRules.Build((PlayerCareerRole)playerRole.index, (PlayerReputation)playerReputation.index, (PlayerWrestlingType)playerWrestlingType.index, styleId);
            if (playerRole.index == 0)
            {
                AddAbilityColumn(box, WrestlerProfileController.MatchAbilities(build.Attributes));
            }
            AddAbilityColumn(box, WrestlerProfileController.PromoAbilities(build.Attributes));
            var summary = Ui<VisualElement>("player-ability-summary"); summary.Clear(); summary.EnableInClassList("manager-only", playerRole.index != 0);
            if (playerRole.index == 0) AddAbilitySummary(summary, "종합 경기 능력", WrestlerOverallCalculator.Match(build.Attributes, styleId));
            AddAbilitySummary(summary, "종합 프로모 능력", WrestlerOverallCalculator.Promo(build.Attributes, KayfabeAlignment.Tweener, PromoDisposition.Balanced));
        }

        private void RefreshStyleChoices(string preferredStyleId = null)
        {
            if (playerWrestlingStyle == null || playerRole == null || playerHeight == null) return;
            var currentStyleId = preferredStyleId ?? SelectedPlayerStyleId();
            availableWrestlingStyleIds.Clear();
            var choices = new List<string>();
            for (var i = 0; i < WrestlingStyleIds.Length; i++)
            {
                if (!PlayerCharacterRules.IsStyleAvailable(WrestlingStyleIds[i], playerHeight.value, SelectedPlayerGender())) continue;
                availableWrestlingStyleIds.Add(WrestlingStyleIds[i]);
                choices.Add(WrestlingStyleChoices[i]);
            }
            playerWrestlingStyle.choices = choices;
            var selectedIndex = availableWrestlingStyleIds.IndexOf(currentStyleId);
            playerWrestlingStyle.index = selectedIndex >= 0 ? selectedIndex : availableWrestlingStyleIds.IndexOf("style_007");
            var wrestler = playerRole.index == 0;
            wrestlerAbilitySettings?.EnableInClassList("hidden", !wrestler);
        }

        private WrestlerGender SelectedPlayerGender() => playerGender?.index == 1 ? WrestlerGender.Female : WrestlerGender.Male;

        private void ApplyPhysicalLimits()
        {
            var height = PlayerCharacterRules.HeightRange(SelectedPlayerGender());
            var weight = PlayerCharacterRules.WeightRange(SelectedPlayerGender());
            playerHeight.SetValueWithoutNotify(Mathf.Clamp(playerHeight.value, height.Min, height.Max));
            playerWeight.SetValueWithoutNotify(Mathf.Clamp(playerWeight.value, weight.Min, weight.Max));
            Ui<Label>("player-height-label").text = $"키(cm, {height.Min}~{height.Max})";
            Ui<Label>("player-weight-label").text = $"몸무게(kg, {weight.Min}~{weight.Max})";
            RefreshStyleChoices();
            RefreshPlayerAbilities();
        }

        private string SelectedPlayerStyleId() => playerWrestlingStyle != null && playerWrestlingStyle.index >= 0 && playerWrestlingStyle.index < availableWrestlingStyleIds.Count
            ? availableWrestlingStyleIds[playerWrestlingStyle.index]
            : "style_007";

        private static void AddAbilityColumn(VisualElement parent, IEnumerable<(string Name, float Value)> abilities)
        {
            var column = new VisualElement(); column.AddToClassList("wp-ability-column");
            var index = 0;
            foreach (var ability in abilities)
            {
                WrestlerProfileController.AddAbility(column, ability.Name, ability.Value, index++ % 2 == 1 ? "alt" : null);
            }
            parent.Add(column);
        }

        private static void AddAbilitySummary(VisualElement parent, string labelText, float value)
        {
            var cell = new VisualElement(); cell.AddToClassList("wp-summary-cell");
            var label = new Label(labelText); label.AddToClassList("wp-ability-label"); cell.Add(label);
            var grade = new Label(WrestlerOverallCalculator.Grade(value)); grade.AddToClassList("wp-ability-value");
            grade.AddToClassList("grade-" + WrestlerOverallCalculator.Grade(value).ToLowerInvariant().Replace("+", "plus"));
            cell.Add(grade); parent.Add(cell);
        }

        private void RenderFinalReview()
        {
            var review = Ui<VisualElement>("final-review");
            review.Clear();
            AddReviewRow(review, "플레이어", $"{playerName.value.Trim()} · {GenderChoices[playerGender.index]} · {PlayerRoleChoices[playerRole.index]} · {ReputationChoices[playerReputation.index]}");
            AddReviewRow(review, "능력 유형", playerRole.index == 0 ? $"{WrestlingTypeChoices[playerWrestlingType.index]} · {playerWrestlingStyle.value}" : "프로모 전문");
            TryPlayerBirthDate(out var birthDate);
            AddReviewRow(review, "신체", $"{FormatDate(birthDate)} · 만 {birthDate.AgeOn(new GameDate(2026, 6, 1))}세 · {playerHeight.value}cm · {playerWeight.value}kg");
            AddReviewRow(review, "단체", $"{promotionName.value.Trim()} ({promotionAbbreviation.value.Trim().ToUpperInvariant()})");
            var selected = candidates.Where(x => selectedWrestlerIds.Contains(x.Id)).ToList();
            var signing = selected.Sum(x => ContractOfferFor(x).SigningBonus);
            var monthly = selected.Sum(x => ContractOfferFor(x).MonthlySalary);
            AddReviewRow(review, "시작 로스터", $"{selectedWrestlerIds.Count}명");
            AddReviewRow(review, "즉시 지급액", $"선수 계약금 {Money(signing)}");
            AddReviewRow(review, "월간 / 시즌 고정비", $"{Money(monthly)} / {Money(monthly * 12)}");
            AddReviewRow(review, "시즌 운영", $"정규 쇼 {regularFrequency.value} · PPV {ppvFrequency.value}");
            AddReviewRow(review, "홈 경기장", $"{selectedVenue.DisplayName} · 회당 {Money(selectedVenue.ProductionCost)} · 정규 쇼 예상 {Money(selectedVenue.ProductionCost * RegularShowCount())}");
            AddReviewRow(review, "계약 후 잔여 현금", Money(InitialCash - signing));
        }

        private static void AddReviewRow(VisualElement parent, string labelText, string valueText)
        {
            var row = new VisualElement(); row.AddToClassList("review-row");
            var label = new Label(labelText); label.AddToClassList("review-label"); row.Add(label);
            var value = new Label(valueText); value.AddToClassList("review-value"); row.Add(value);
            parent.Add(row);
        }

        private void StartNewGame()
        {
            try
            {
                var request = new GameStartRequest
                {
                    HasPlayerCharacter = true,
                    PlayerName = playerName.value.Trim(),
                    PlayerRole = (PlayerCareerRole)Mathf.Max(0, playerRole.index),
                    PlayerGender = (WrestlerGender)Mathf.Max(0, playerGender.index),
                    PlayerReputation = (PlayerReputation)Mathf.Max(0, playerReputation.index),
                    PlayerWrestlingType = (PlayerWrestlingType)Mathf.Max(0, playerWrestlingType.index),
                    PlayerWrestlingStyleId = SelectedPlayerStyleId(),
                    PlayerBirthDate = ParsePlayerBirthDate(), PlayerHeightCm = playerHeight.value, PlayerWeightKg = playerWeight.value,
                    PromotionName = promotionName.value.Trim(),
                    PromotionAbbreviation = promotionAbbreviation.value.Trim().ToUpperInvariant(),
                    InitialCash = InitialCash,
                    InitialPrestige = 0,
                    WorldSeed = seed,
                    UtcNow = DateTime.UtcNow,
                    WrestlerContracts = candidates.Where(x => selectedWrestlerIds.Contains(x.Id)).Select(wrestler =>
                    {
                        var offer = ContractOfferFor(wrestler);
                        return new InitialWrestlerContractInput
                        {
                            Wrestler = wrestler, ContractEndYear = 2027, SigningBonus = offer.SigningBonus,
                            MonthlySalary = offer.MonthlySalary, TerminationCost = offer.TerminationCost
                        };
                    }).ToList(),
                    RegularVenueId = selectedVenue.Id,
                    RegularVenueProductionCost = selectedVenue.ProductionCost,
                    RegularShowFrequency = (RegularShowFrequency)Mathf.Max(0, regularFrequency.index),
                    PpvFrequency = (PpvFrequency)Mathf.Max(0, ppvFrequency.index)
                };
                EnterDashboard(new GameStartPersistenceService(saveService).CreateSaveAndReload(SaveSlot, request));
            }
            catch (Exception exception) { Debug.LogException(exception); Fail($"게임을 시작하지 못했습니다: {exception.Message}"); }
        }

        private void ContinueGame()
        {
            try
            {
                var slot = saveService.ListSlots().FirstOrDefault();
                if (slot == null) throw new FileNotFoundException("저장 파일이 없습니다.");
                LoadSlot(slot);
            }
            catch (Exception exception) { ShowStatus($"저장 파일을 불러오지 못했습니다: {exception.Message}", true); }
        }

        private void OpenLoadMenu()
        {
            loadRows.Clear();
            var slots = saveService.ListSlots();
            foreach (var slot in slots)
            {
                var row = new Button();
                row.AddToClassList("save-load-row");
                var copy = new VisualElement(); copy.AddToClassList("save-load-row-copy");
                var name = new Label(slot); name.AddToClassList("save-load-row-name"); copy.Add(name);
                var date = new Label(saveService.GetLastWriteTime(slot).ToString("yyyy.MM.dd  HH:mm")); date.AddToClassList("save-load-row-date"); copy.Add(date);
                row.Add(copy);
                var action = new Label("불러오기"); action.AddToClassList("save-load-row-action"); row.Add(action);
                var captured = slot;
                row.clicked += () => LoadSlot(captured);
                loadRows.Add(row);
                if (saveService.HasBackup(slot))
                {
                    var backup = new Button(() => LoadSlot(captured, true)) { text = $"{slot} · 이전 백업 불러오기" };
                    backup.AddToClassList("save-load-row");
                    backup.AddToClassList("save-load-row-name");
                    loadRows.Add(backup);
                }
            }
            if (slots.Count == 0) { var empty = new Label("저장된 게임이 없습니다"); empty.AddToClassList("save-load-empty"); loadRows.Add(empty); }
            loadOverlay.RemoveFromClassList("hidden");
        }

        private void CloseLoadMenu() => loadOverlay.AddToClassList("hidden");

        private void LoadSlot(string slotName, bool backup = false)
        {
            try
            {
                var save = backup ? saveService.LoadBackup(slotName) : saveService.Load(slotName);
                EnterDashboard(save);
                CloseLoadMenu();
            }
            catch (Exception exception)
            {
                OpenLoadMenu();
                var error = new Label($"불러오기 실패: {exception.Message}");
                error.AddToClassList("save-load-empty");
                loadRows.Insert(0, error);
            }
        }

        private void EnterDashboard(GameSave save)
        {
            DashboardSession.SetActiveSave(save);
            root.style.display = DisplayStyle.None;
            newGameDocumentRoot.style.display = DisplayStyle.None;
            dashboard.style.display = DisplayStyle.Flex;
        }

        private void RefreshContinueButton()
        {
            var hasSave = saveService.ListSlots().Count > 0;
            Ui<Button>("continue-game")?.SetEnabled(hasSave);
            Ui<Button>("load-game")?.SetEnabled(hasSave);
        }

        private void ShowStatus(string message, bool isError = false)
        {
            status.text = message;
            status.EnableInClassList("error", isError);
        }

        private static string GenderText(WrestlerGender value) => value == WrestlerGender.Male ? "남성" : "여성";
        private static int AgeAtSeasonStart(WrestlerState wrestler)
        {
            return wrestler.Identity.BirthDate.AgeOn(new GameDate(2026, 6, 1));
        }

        private void ToggleBirthDateCalendar()
        {
            if (!birthDateCalendar.ClassListContains("hidden"))
            {
                birthDateCalendar.AddToClassList("hidden");
                return;
            }

            if (TryPlayerBirthDate(out var selected)) visibleCalendarMonth = new DateTime(selected.Year, selected.Month, 1);
            calendarYear.choices = Enumerable.Range(1955, 54).Reverse().Select(x => $"{x}년").ToList();
            calendarMonth.choices = Enumerable.Range(1, 12).Select(x => $"{x}월").ToList();
            birthDateCalendar.RemoveFromClassList("hidden");
            RenderCalendar();
        }

        private void ShiftCalendarMonth(int months)
        {
            visibleCalendarMonth = visibleCalendarMonth.AddMonths(months);
            if (visibleCalendarMonth < new DateTime(1955, 6, 1)) visibleCalendarMonth = new DateTime(1955, 6, 1);
            if (visibleCalendarMonth > new DateTime(2008, 6, 1)) visibleCalendarMonth = new DateTime(2008, 6, 1);
            RenderCalendar();
        }

        private void ChangeCalendarPeriod()
        {
            if (calendarYear.index < 0 || calendarMonth.index < 0) return;
            visibleCalendarMonth = new DateTime(2008 - calendarYear.index, calendarMonth.index + 1, 1);
            ShiftCalendarMonth(0);
        }

        private void RenderCalendar()
        {
            calendarYear.SetValueWithoutNotify($"{visibleCalendarMonth.Year}년");
            calendarMonth.SetValueWithoutNotify($"{visibleCalendarMonth.Month}월");
            calendarDays.Clear();
            var firstCell = visibleCalendarMonth.AddDays(-(int)visibleCalendarMonth.DayOfWeek);
            var minimum = new DateTime(1955, 6, 2);
            var maximum = new DateTime(2008, 6, 1);
            TryPlayerBirthDate(out var selected);
            for (var i = 0; i < 42; i++)
            {
                var date = firstCell.AddDays(i);
                var button = new Button { text = date.Day.ToString() };
                button.AddToClassList("calendar-day");
                if (date.Month != visibleCalendarMonth.Month) button.AddToClassList("outside-month");
                if (selected.Year == date.Year && selected.Month == date.Month && selected.Day == date.Day) button.AddToClassList("selected");
                button.SetEnabled(date >= minimum && date <= maximum);
                button.clicked += () =>
                {
                    playerBirthDate.value = date.ToString("yyyy-MM-dd");
                    birthDateCalendar.AddToClassList("hidden");
                };
                calendarDays.Add(button);
            }
        }

        private bool TryPlayerBirthDate(out GameDate birthDate)
        {
            if (DateTime.TryParseExact(playerBirthDate?.value?.Trim(), "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var value))
            {
                birthDate = new GameDate(value.Year, value.Month, value.Day);
                return true;
            }
            birthDate = default;
            return false;
        }

        private GameDate ParsePlayerBirthDate() => TryPlayerBirthDate(out var value) ? value : default;
        private static string FormatDate(GameDate value) => $"{value.Year:D4}-{value.Month:D2}-{value.Day:D2}";

        private static InitialContractOffer ContractOfferFor(WrestlerState wrestler) => InitialContractOfferRules.Calculate(wrestler);
        private static string Money(long value) => $"${value:N0}";
        private int RegularShowCount() => regularFrequency == null ? 12 : regularFrequency.index switch
        {
            1 => 26, 2 => 52, 3 => 104, 4 => 156, _ => 12
        };

        private static string RatingGrade(float value) => WrestlerOverallCalculator.Grade(value);

        private static string StyleText(string id) => id switch
        {
            "style_001" => "브롤러", "style_002" => "파워하우스", "style_003" => "테크니션",
            "style_004" => "하이플라이어", "style_005" => "루차 리브레", "style_006" => "자이언트",
            "style_007" => "올라운더", _ => "미확인"
        };
        private static string VenueScaleText(VenueScale value) => value switch
        {
            VenueScale.Studio => "스튜디오", VenueScale.CommunityGym => "체육관", VenueScale.SmallArena => "소형 아레나",
            VenueScale.MediumArena => "중형 아레나", VenueScale.LargeArena => "대형 아레나", _ => "스타디움"
        };
    }
}
