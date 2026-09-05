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
        private static readonly string[] StepTitles = { "단체 설정", "선수 영입", "시즌 운영", "경기장 계약", "최종 검토" };
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
            BindButton("settings", () => ShowStatus("설정 화면은 다음 개발 단계에서 연결됩니다."));
            BindButton("credits", () => ShowStatus("PW MANAGER · 개발 중인 플레이 버전"));
            BindButton("exit-game", Application.Quit);
            promotionName.RegisterValueChangedCallback(_ => RefreshSummary());
            promotionAbbreviation.RegisterValueChangedCallback(_ => RefreshSummary());
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
                if (string.IsNullOrWhiteSpace(promotionName.value)) return Fail("단체 이름을 입력해 주세요.");
                var abbreviation = promotionAbbreviation.value?.Trim();
                if (string.IsNullOrWhiteSpace(abbreviation) || abbreviation.Length < 2 || abbreviation.Length > 8) return Fail("단체 약자는 2~8자로 입력해 주세요.");
            }
            if (step == 1 && (selectedWrestlerIds.Count < 8 || selectedWrestlerIds.Count > 20)) return Fail("선수는 8~20명 선택해야 합니다.");
            if (step == 1 && candidates.Where(x => selectedWrestlerIds.Contains(x.Id)).Sum(x => ContractOfferFor(x).SigningBonus) > InitialCash)
                return Fail("선수 계약금이 시작 자금을 초과했습니다. 선택 인원을 조정해 주세요.");
            if (step == 3 && selectedVenue == null) return Fail("정규 시즌 경기장을 선택해 주세요.");
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
            Ui<Label>("summary-roster").text = $"{selectedWrestlerIds.Count}명";
            Ui<Label>("summary-signing").text = Money(signing);
            Ui<Label>("summary-salary").text = Money(monthly);
            Ui<Label>("summary-season-fixed").text = Money(monthly * 12);
            Ui<Label>("summary-show-cost").text = selectedVenue == null ? "—" : Money(showCost);
            Ui<Label>("summary-remaining").text = Money(InitialCash - signing);
            var count = Ui<Label>("roster-count");
            if (count != null) count.text = $"{selectedWrestlerIds.Count}명 선택";
        }

        private void RenderFinalReview()
        {
            var review = Ui<VisualElement>("final-review");
            review.Clear();
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
            var birth = wrestler.Identity.BirthDate;
            return 2026 - birth.Year - (birth.Month > 6 || birth.Month == 6 && birth.Day > 1 ? 1 : 0);
        }

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
