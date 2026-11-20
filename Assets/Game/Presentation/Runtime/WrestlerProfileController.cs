using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Data.Catalogs;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class WrestlerProfileController : MonoBehaviour
    {
        private static readonly string[] Palette = { "grade-ss", "grade-splus", "grade-s", "grade-aplus", "grade-a", "grade-bplus", "grade-b", "grade-cplus", "grade-c", "grade-dplus", "grade-d", "grade-eplus", "grade-e", "grade-fplus", "grade-f", "grade-gplus", "grade-g", "status-main", "status-mid", "status-jobber", "status-rookie", "role-face", "role-heel", "role-tweener", "state-good", "state-warn", "state-high", "state-danger", "momentum-low", "momentum-cool", "momentum-mid", "momentum-hot", "momentum-peak" };
        private static WrestlerProfileController instance;
        private VisualElement root;
        private VisualElement standaloneRoot;
        private VisualElement embeddedRoot;
        private Button backButton;
        private WrestlerState boundWrestler;
        private string previewStyleId;
        private KayfabeAlignment previewAlignment;
        private PromoDisposition previewDisposition;
        private Button saveMatchStyleButton;
        private Button savePromoStyleButton;
        private Button editMovesButton;
        private VisualElement movesOverlay;
        private Button editAppearanceButton;
        private VisualElement appearanceOverlay;
        private bool canSaveStyles;
        private bool canEditAppearance;

        private void OnEnable()
        {
            instance = this;
            standaloneRoot = GetComponent<UIDocument>().rootVisualElement.Q<VisualElement>("wrestler-profile");
            embeddedRoot = GameObject.Find("Dashboard UI")?.GetComponent<UIDocument>()?.rootVisualElement.Q<VisualElement>("wrestler-profile");
            BindInteractiveRoot(standaloneRoot);
            if (Application.isPlaying) { if (standaloneRoot != null) standaloneRoot.style.display = DisplayStyle.None; if (embeddedRoot != null) embeddedRoot.style.display = DisplayStyle.None; }
        }

        private void BindInteractiveRoot(VisualElement target)
        {
            root?.Q("wrestler-relationships")?.RemoveFromHierarchy();
            root?.Q("career-history-overlay")?.RemoveFromHierarchy();
            var previousCareer = root?.Q<Button>("wp-open-career");
            var previousReport = root?.Q<Button>("wp-open-report");
            if (previousReport != null) previousReport.clicked -= OpenBehaviorReport;
            root?.Q("wrestler-report-overlay")?.RemoveFromHierarchy();
            if (previousCareer != null) previousCareer.clicked -= OpenCareer;
            root?.Q("growth-history-overlay")?.RemoveFromHierarchy();
            var previousGrowth = root?.Q<Button>("wp-open-growth");
            if (previousGrowth != null) previousGrowth.clicked -= OpenGrowth;
            root?.Q("contract-negotiation-overlay")?.RemoveFromHierarchy();
            var previousContract = root?.Q<Button>("wp-open-contract");
            if (previousContract != null) previousContract.clicked -= OpenContract;
            CloseAppearanceEditor();
            var previousRelations = root?.Q<Button>("wp-open-relationships");
            if (previousRelations != null) previousRelations.clicked -= OpenRelationships;
            var previousOverview = root?.Q<Button>("wp-open-overview");
            if (previousOverview != null) previousOverview.clicked -= ShowOverview;
            if (backButton != null) backButton.clicked -= Close;
            if (saveMatchStyleButton != null) saveMatchStyleButton.clicked -= SaveMatchStyle;
            if (savePromoStyleButton != null) savePromoStyleButton.clicked -= SavePromoStyle;
            if (editMovesButton != null) editMovesButton.clicked -= OpenMovesEditor;
            if (editAppearanceButton != null) editAppearanceButton.clicked -= OpenAppearanceEditor;
            root = target;
            if (root == null) return;
            var careerButton = root.Q<Button>("wp-open-career");
            var reportButton = root.Q<Button>("wp-open-report");
            if (reportButton == null)
            {
                reportButton = new Button { name = "wp-open-report", text = "개인 보고서" }; reportButton.AddToClassList("wp-tab");
                root.Q(className: "wp-tabs")?.Add(reportButton);
            }
            reportButton.clicked += OpenBehaviorReport;
            if (careerButton != null) careerButton.clicked += OpenCareer;
            var growthButton = root.Q<Button>("wp-open-growth");
            if (growthButton != null) growthButton.clicked += OpenGrowth;
            var contractButton = root.Q<Button>("wp-open-contract");
            if (contractButton != null) contractButton.clicked += OpenContract;
            var relations = root.Q<Button>("wp-open-relationships");
            if (relations != null) relations.clicked += OpenRelationships;
            var overview = root.Q<Button>("wp-open-overview");
            if (overview != null) overview.clicked += ShowOverview;
            backButton = root.Q<Button>("wrestler-profile-back"); if (backButton != null) backButton.clicked += Close;
            saveMatchStyleButton = root.Q<Button>("wp-save-match-style");
            savePromoStyleButton = root.Q<Button>("wp-save-promo-style");
            editMovesButton = root.Q<Button>("wp-edit-moves");
            editAppearanceButton = root.Q<Button>("wp-edit-appearance");
            if (saveMatchStyleButton != null) saveMatchStyleButton.clicked += SaveMatchStyle;
            if (savePromoStyleButton != null) savePromoStyleButton.clicked += SavePromoStyle;
            if (editMovesButton != null) editMovesButton.clicked += OpenMovesEditor;
            if (editAppearanceButton != null) editAppearanceButton.clicked += OpenAppearanceEditor;
            foreach (var key in new[] { "home", "roster", "teams", "locker", "scout", "training", "show", "story", "title", "tournament", "schedule", "staff", "company", "finance", "report", "world", "history", "settings" })
            {
                var icon = root.Q<VisualElement>($"wp-icon-{key}");
                if (icon == null) continue;
                icon.Clear();
                icon.AddToClassList($"nav-icon-{key}");
            }
        }

        private void OnDisable()
        {
            root?.Q("wrestler-report-overlay")?.RemoveFromHierarchy();
            var reportButton = root?.Q<Button>("wp-open-report");
            if (reportButton != null) reportButton.clicked -= OpenBehaviorReport;
            root?.Q("career-history-overlay")?.RemoveFromHierarchy();
            var careerButton = root?.Q<Button>("wp-open-career");
            if (careerButton != null) careerButton.clicked -= OpenCareer;
            root?.Q("growth-history-overlay")?.RemoveFromHierarchy();
            var growthButton = root?.Q<Button>("wp-open-growth");
            if (growthButton != null) growthButton.clicked -= OpenGrowth;
            root?.Q("contract-negotiation-overlay")?.RemoveFromHierarchy();
            var contractButton = root?.Q<Button>("wp-open-contract");
            if (contractButton != null) contractButton.clicked -= OpenContract;
            CloseAppearanceEditor();
            var relations = root?.Q<Button>("wp-open-relationships");
            if (relations != null) relations.clicked -= OpenRelationships;
            var overview = root?.Q<Button>("wp-open-overview");
            if (overview != null) overview.clicked -= ShowOverview;
            if (saveMatchStyleButton != null) saveMatchStyleButton.clicked -= SaveMatchStyle;
            if (savePromoStyleButton != null) savePromoStyleButton.clicked -= SavePromoStyle;
            if (editMovesButton != null) editMovesButton.clicked -= OpenMovesEditor;
            if (editAppearanceButton != null) editAppearanceButton.clicked -= OpenAppearanceEditor;
            if (backButton != null) backButton.clicked -= Close;
            if (instance == this) instance = null;
        }
        private void Close()
        {
            root?.Q("wrestler-report-overlay")?.RemoveFromHierarchy();
            root.Q("career-history-overlay")?.RemoveFromHierarchy();
            root.Q("growth-history-overlay")?.RemoveFromHierarchy();
            root.Q("contract-negotiation-overlay")?.RemoveFromHierarchy();
            CloseAppearanceEditor();
            root.style.display = DisplayStyle.None;
            DashboardController.ShowRosterShell();
        }

        public static bool CloseOpenProfile(bool navigateToRoster = true)
        {
            if (instance?.root == null || instance.root.resolvedStyle.display == DisplayStyle.None) return false;
            instance.CloseAppearanceEditor();
            if (navigateToRoster) instance.Close();
            else instance.root.style.display = DisplayStyle.None;
            return true;
        }

        public static void Open(WrestlerState wrestler)
        {
            if (wrestler == null || instance == null) return;
            if (DashboardSession.ActiveSave?.IsWrestlerDiscovered(wrestler.Id) == false) return;
            var inGame = DashboardSession.ActiveSave != null;
            if (inGame && instance.embeddedRoot == null) instance.embeddedRoot = GameObject.Find("Dashboard UI")?.GetComponent<UIDocument>()?.rootVisualElement.Q<VisualElement>("wrestler-profile");
            instance.BindInteractiveRoot(inGame && instance.embeddedRoot != null ? instance.embeddedRoot : instance.standaloneRoot);
            instance.root.EnableInClassList("pre-game", !inGame);
            instance.root.EnableInClassList("in-game", inGame);
            instance.Bind(wrestler);
            instance.ShowOverview();
            if (inGame) DashboardController.ShowWrestlerShell(WrestlerNameText.DisplayName(wrestler), wrestler.Id);
            instance.root.style.display = DisplayStyle.Flex;
        }
        public static Button CreateLink(WrestlerState wrestler, string className = null)
        {
            var button = new Button { text = WrestlerNameText.DisplayName(wrestler) }; button.AddToClassList("wrestler-link"); if (!string.IsNullOrEmpty(className)) button.AddToClassList(className);
            button.clicked += () => Open(wrestler); button.RegisterCallback<PointerDownEvent>(e => e.StopPropagation()); button.RegisterCallback<PointerUpEvent>(e => e.StopPropagation()); button.RegisterCallback<ClickEvent>(e => e.StopPropagation()); return button;
        }

        public static Button CreatePortraitLink(WrestlerState wrestler, string className = null)
        {
            var button = new Button();
            button.AddToClassList("wrestler-portrait-link");
            if (!string.IsNullOrEmpty(className)) button.AddToClassList(className);

            var portraitFrame = new VisualElement();
            portraitFrame.AddToClassList("wrestler-portrait-frame");
            var portraitTexture = PortraitRenderer.Get(wrestler);
            if (portraitTexture != null)
            {
                var portrait = new Image { image = portraitTexture, scaleMode = ScaleMode.ScaleToFit };
                portrait.AddToClassList("wrestler-portrait-image");
                portraitFrame.Add(portrait);
            }
            else
            {
                var initials = new Label(Initials(wrestler?.Identity?.RingName));
                initials.AddToClassList("wrestler-portrait-initials");
                portraitFrame.Add(initials);
            }

            var name = new Label(WrestlerNameText.DisplayName(wrestler));
            name.AddToClassList("wrestler-portrait-name");
            button.Add(portraitFrame);
            button.Add(name);
            button.clicked += () => Open(wrestler);
            button.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            return button;
        }

        public static VisualElement CreatePortraitDisplayWithNameLink(WrestlerState wrestler, string className = null)
        {
            var container = new VisualElement();
            container.AddToClassList("wrestler-portrait-link");
            if (!string.IsNullOrEmpty(className)) container.AddToClassList(className);

            var portraitFrame = new VisualElement();
            portraitFrame.AddToClassList("wrestler-portrait-frame");
            portraitFrame.pickingMode = PickingMode.Ignore;
            var portraitTexture = PortraitRenderer.Get(wrestler);
            if (portraitTexture != null)
            {
                var portrait = new Image { image = portraitTexture, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                portrait.AddToClassList("wrestler-portrait-image");
                portraitFrame.Add(portrait);
            }
            else
            {
                var initials = new Label(Initials(wrestler?.Identity?.RingName)) { pickingMode = PickingMode.Ignore };
                initials.AddToClassList("wrestler-portrait-initials");
                portraitFrame.Add(initials);
            }

            var name = new Button { text = WrestlerNameText.DisplayName(wrestler) };
            name.AddToClassList("wrestler-portrait-name");
            name.AddToClassList("wrestler-portrait-name-link");
            name.clicked += () => Open(wrestler);
            name.RegisterCallback<PointerUpEvent>(evt => evt.StopPropagation());
            name.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            container.Add(portraitFrame);
            container.Add(name);
            return container;
        }

        private void Bind(WrestlerState wrestler)
        {
            if (DashboardSession.ActiveSave?.IsWrestlerDiscovered(wrestler.Id) == false) return;
            CloseAppearanceEditor();
            boundWrestler = wrestler;
            previewStyleId = wrestler.Presentation?.WrestlingStyleId ?? "style_007";
            previewAlignment = wrestler.Roster?.Alignment ?? KayfabeAlignment.Tweener;
            previewDisposition = wrestler.Presentation?.PromoDisposition ?? PromoDisposition.Balanced;
            var save = DashboardSession.ActiveSave; var id = wrestler.Identity ?? new WrestlerIdentityState(); var a = wrestler.Attributes ?? new WrestlerAttributesState();
            var c = wrestler.Condition ?? new WrestlerConditionState(); var g = wrestler.Growth ?? new WrestlerGrowthState(); var f = wrestler.FanReaction ?? new WrestlerFanReactionState();
            var roster = wrestler.Roster ?? new WrestlerRosterState(); var status = wrestler.Status ?? new WrestlerStatusState(); var date = save?.CurrentDate ?? id.CreatedDate;
            var card = CardStatus(status); var contract = save?.OwnContracts?.FirstOrDefault(x => x != null && x.PersonId == wrestler.Id && x.IsInForce);
            var futureContract = save?.OwnContracts.FirstOrDefault(x => x.PersonId == wrestler.Id && x.Type == ContractType.Wrestler &&
                x.Status == ContractStatus.Scheduled && x.EndDate.CompareTo(date) >= 0);
            var hasOwnAgreement = contract?.Covers(date) == true || futureContract != null;
            root.Q<Button>("wp-open-report")?.SetEnabled(save != null && save.IsWrestlerDiscovered(wrestler.Id) &&
                (wrestler.PromotionId == save.Promotion.Id || save.WrestlerBehaviors.Any(s => s.PromotionId == save.Promotion.Id && s.WrestlerId == wrestler.Id)));
            root.Q<Button>("wp-open-contract")?.SetEnabled(save != null && !DashboardSession.IsShowRunning &&
                (string.IsNullOrEmpty(wrestler.PromotionId) || wrestler.PromotionId == save.Promotion.Id || hasOwnAgreement) &&
                save.IsWrestlerDiscovered(wrestler.Id) &&
                (hasOwnAgreement || ContractNegotiationService.HasCurrentRecruitmentInformation(save, wrestler)));
            root.Q<Button>("wp-open-growth")?.SetEnabled(save != null && ScoutReport == null);
            root.Q<Button>("wp-open-career")?.SetEnabled(save != null && ScoutReport == null);
            root.Q<Button>("wp-open-relationships")?.SetEnabled(save != null && ScoutReport == null);
            var homeBadge = root.Q<Label>("wp-home-badge"); if (homeBadge != null) { var count = DashboardController.HomeBadgeCount; homeBadge.text = count.ToString(); homeBadge.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None; }
            canSaveStyles = contract != null && !DashboardSession.IsShowRunning;
            canEditAppearance = save != null && !DashboardSession.IsShowRunning && (contract != null || save.Player?.WrestlerId == wrestler.Id);
            saveMatchStyleButton?.SetEnabled(canSaveStyles);
            savePromoStyleButton?.SetEnabled(canSaveStyles);
            editMovesButton?.SetEnabled(canSaveStyles);
            editAppearanceButton?.SetEnabled(canEditAppearance);
            var avatar = root.Q<VisualElement>(className: "wp-avatar");
            if (avatar != null)
            {
                var portrait = avatar.Q<Image>("wp-portrait");
                if (portrait == null)
                {
                    portrait = new Image { name = "wp-portrait", scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                    portrait.style.width = Length.Percent(100); portrait.style.height = Length.Percent(100); avatar.Add(portrait);
                    var image = portrait;
                    avatar.RegisterCallback<GeometryChangedEvent>(_ => FitPortraitFrame(avatar, image));
                    avatar.parent.RegisterCallback<GeometryChangedEvent>(_ => FitPortraitFrame(avatar, image));
                }
                portrait.image = PortraitRenderer.Get(wrestler);
                FitPortraitFrame(avatar, portrait);
                avatar.Q<Label>("wp-initials").style.display = portrait.image == null ? DisplayStyle.Flex : DisplayStyle.None;
            }
            var results = (save?.MatchResults ?? new List<MatchResultState>()).Where(x => IsParticipant(x, wrestler.Id)).ToList();
            var wins = results.Count(x => x.WinnerId == wrestler.Id || (x.IndirectWinnerIds?.Contains(wrestler.Id) ?? false)); var losses = results.Count(x => x.LoserTargetId == wrestler.Id || (x.IndirectLoserIds?.Contains(wrestler.Id) ?? false)); var draws = Math.Max(0, results.Count - wins - losses);
            var winStreak = 0;
            foreach (var result in results.AsEnumerable().Reverse())
            {
                if (result.WinnerId != wrestler.Id && !(result.IndirectWinnerIds?.Contains(wrestler.Id) ?? false)) break;
                winStreak++;
            }
            var condition = Mathf.Clamp(c.Condition, 0f, 100f); var momentum = wrestler.Momentum?.Momentum ?? 0f;

            Set("wp-promotion", save?.Promotion?.GetDisplayAbbreviation() ?? "PW"); Set("wp-breadcrumb", $"{id.RingName} / 개요"); Set("wp-date", Date(date));
            Set("wp-cash", save?.Promotion == null ? "—" : $"${save.Promotion.CalculateCurrentCash(save.Transactions):N0}"); var next = save?.Schedules?.Where(x => x != null && x.Date.CompareTo(date) >= 0).OrderBy(x => x.Date).FirstOrDefault(); Set("wp-next-show", next == null ? "예정 없음" : Date(next.Date));
            root.Q<Label>("wp-name").text = id.RingName; Set("wp-nickname", string.IsNullOrWhiteSpace(id.Nickname) ? id.LegalName : $"‘{id.Nickname}’"); Set("wp-initials", Initials(id.RingName)); Set("wp-status-title", card.Label);
            Set("wp-legal-name", id.LegalName); Set("wp-gender", id.Gender == WrestlerGender.Male ? "남성" : "여성"); Set("wp-height", $"{id.HeightCm} cm"); Set("wp-weight", $"{id.WeightKg} kg"); Set("wp-age", $"{Age(id.BirthDate, date)}세"); Set("wp-career", $"{id.CareerYears}년"); Set("wp-background", Background(id.Background)); Set("wp-current-style", StyleName(previewStyleId));
            var activeTag = save?.TagTeams?.FirstOrDefault(x => x.Id == roster.ActiveTagTeamId);
            var activeStable = save?.Stables?.FirstOrDefault(x => x.Id == roster.ActiveStableId);
            Set("wp-tag", activeTag == null ? "없음" : string.IsNullOrWhiteSpace(activeTag.Name) ? "태그팀" : activeTag.Name);
            Set("wp-stable", activeStable?.Name ?? "없음");
            Set("wp-card-status", card.Label); Set("wp-role", Alignment(roster.Alignment)); Set("wp-contract-end", contract == null ? "계약 없음" : Date(contract.EndDate)); Set("wp-salary", contract == null ? "—" : $"${contract.MonthlySalary:N0}"); Set("wp-last-match", roster.LastMatchDate.HasValue ? Date(roster.LastMatchDate.Value) : "기록 없음");
            Set("wp-fame-tier", WrestlerFameRules.Label(wrestler.Fame.Value));
            var moves = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog")?.Moves;
            string MoveName(IReadOnlyList<string> ids, int index)
            {
                if (ids == null || index >= ids.Count) return "—";
                return moves?.FirstOrDefault(x => x != null && x.Id == ids[index])?.DisplayName ?? "—";
            }
            Set("wp-signature-1", MoveName(wrestler.Presentation?.SignatureMoveIds, 0)); Set("wp-signature-2", MoveName(wrestler.Presentation?.SignatureMoveIds, 1));
            Set("wp-finisher-1", MoveName(wrestler.Presentation?.FinisherMoveIds, 0)); Set("wp-finisher-2", MoveName(wrestler.Presentation?.FinisherMoveIds, 1));
            var canCompete = c.Availability is WrestlerAvailability.Available or WrestlerAvailability.Limited &&
                !(wrestler.KayfabeInjury?.Absent == true && wrestler.KayfabeInjury.Severity != InjuryStatus.None);
            Set("wp-availability", canCompete ? "출전 가능" : "출전 불가");
            Class("wp-availability", canCompete ? "state-good" : "state-danger");
            Metric("wp-condition", "컨디션", condition, condition < 40 ? "위험" : condition < 60 ? "나쁨" : condition < 80 ? "보통" : "좋음", ConditionClass(condition));
            Metric("wp-satisfaction", "만족도", c.Satisfaction, c.Satisfaction > 70 ? "좋음" : c.Satisfaction > 40 ? "보통" : c.Satisfaction > 20 ? "낮음" : "위험", StateClass(100f - c.Satisfaction));
            Metric("wp-momentum", "모멘텀", momentum, momentum < 20 ? "냉각" : momentum < 40 ? "낮음" : momentum < 50 ? "보통" : momentum < 80 ? "뜨거움" : "최고", MomentumClass(momentum));
            Set("wp-real-injury", Injury(c.InjuryStatus));
            var storyInjury = wrestler.KayfabeInjury?.Severity ?? InjuryStatus.None;
            Set("wp-story-injury", Injury(storyInjury));
            Class("wp-real-injury", c.InjuryStatus == InjuryStatus.None ? "state-good" : "state-danger");
            Class("wp-story-injury", storyInjury == InjuryStatus.None ? "state-good" : "state-warn");
            var injuryLabel = root.Q<Label>("wp-real-injury");
            if (injuryLabel != null)
            {
                injuryLabel.tooltip = $"실제 부위: {Region(c.InjuryRegion)}" + (c.RecoveryDate.HasValue ? $" · 회복 {c.RecoveryDate.Value.Month}/{c.RecoveryDate.Value.Day}" : "") +
                    $"\n각본 부위: {Region(wrestler.KayfabeInjury?.Region ?? BodyRegion.Body)}" +
                    (wrestler.KayfabeInjury?.ReturnDate.HasValue == true ? $" · 복귀 {wrestler.KayfabeInjury.ReturnDate.Value.Month}/{wrestler.KayfabeInjury.ReturnDate.Value.Day}" : "");
                root.Q<Label>("wp-story-injury").tooltip = injuryLabel.tooltip;
            }
            FanResponse("wp-mania", "매니아", f.HardcoreResponse);
            FanResponse("wp-light", "캐주얼", f.CasualResponse);
            FanResponse("wp-family", "몰입형", f.MarkResponse);
            Set("wp-matches", Math.Max(status.OfficialMatchCount, results.Count).ToString()); Set("wp-wins", wins.ToString()); Set("wp-draws", draws.ToString()); Set("wp-losses", losses.ToString()); Set("wp-recent-result", results.Count == 0 ? "기록 없음" : wins > 0 ? "승리" : "패배");
            Set("wp-championships", ChampionshipService.ReignCount(save, wrestler.Id).ToString());
            Set("wp-world-championships", ChampionshipService.ReignCount(save, wrestler.Id, true).ToString());
            var heldTitles = root.Q("wp-current-titles");
            if (heldTitles != null)
            {
                heldTitles.Clear(); var currentTitles = ChampionshipService.HeldBy(save, wrestler.Id);
                heldTitles.style.display = currentTitles.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
                if (currentTitles.Count > 0) { var heading = new Label("보유 챔피언십"); heading.AddToClassList("wp-card-title"); heldTitles.Add(heading); }
                foreach (var title in currentTitles) heldTitles.Add(ChampionshipProfileView.CreateLink(title));
            }
            Set("wp-win-streak", winStreak.ToString());
            Class("wp-status-title", card.ClassName); Class("wp-card-status", card.ClassName); Class("wp-role", $"role-{roster.Alignment.ToString().ToLowerInvariant()}"); Class("wp-condition", ConditionClass(condition)); Class("wp-satisfaction", StateClass(100f - c.Satisfaction)); Class("wp-momentum", MomentumClass(momentum));
            var matchAbilities = MatchAbilities(a); var promoAbilities = PromoAbilities(a); var abilityRows = Math.Max(matchAbilities.Length, promoAbilities.Length);
            Abilities("wp-match-abilities", matchAbilities, abilityRows);
            Abilities("wp-promo-abilities", promoAbilities, abilityRows);
            SetGrade("wp-match-potential", g.MatchPotentialCap);
            SetGrade("wp-promo-potential", g.PromoPotentialCap);
            RenderStyleChoices();
            RenderPromoChoices();
            UpdatePreviewSummary();
            ApplyScoutingReport();
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

        private void RenderPromoChoices()
        {
            var roles = root.Q<VisualElement>("wp-role-options");
            if (roles != null)
            {
                roles.Clear();
                foreach (var option in new[] { (KayfabeAlignment.Face, "페이스"), (KayfabeAlignment.Tweener, "트위너"), (KayfabeAlignment.Heel, "힐") })
                {
                    var captured = option.Item1;
                    var button = Choice(option.Item2, previewAlignment == captured, () => { previewAlignment = captured; RenderPromoChoices(); UpdatePreviewSummary(); });
                    button.AddToClassList("wp-promo-choice");
                    roles.Add(button);
                }
            }
            var dispositions = root.Q<VisualElement>("wp-disposition-options");
            if (dispositions != null)
            {
                dispositions.Clear();
                foreach (var option in new[] { (PromoDisposition.Serious, "진지함"), (PromoDisposition.Balanced, "균형"), (PromoDisposition.Comic, "코믹") })
                {
                    var captured = option.Item1;
                    var button = Choice(option.Item2, previewDisposition == captured, () => { previewDisposition = captured; RenderPromoChoices(); UpdatePreviewSummary(); });
                    button.AddToClassList("wp-promo-choice");
                    dispositions.Add(button);
                }
            }
        }

        private static void FitPortraitFrame(VisualElement frame, Image image)
        {
            if (frame.contentRect.width <= 0 || image.image == null) return;
            var hero = frame.parent;
            var size = hero.contentRect.width;
            if (hero.contentRect.height > 0)
            {
                var name = hero.Q<Label>("wp-name");
                var physical = hero.Q<VisualElement>(className: "wp-profile-physical");
                size = Mathf.Min(size, Mathf.Max(0, hero.contentRect.height - name.resolvedStyle.height - name.resolvedStyle.marginTop - physical.resolvedStyle.height - physical.resolvedStyle.marginTop));
            }
            if (Mathf.Abs(frame.resolvedStyle.width - size) > .5f) frame.style.width = size;
            if (Mathf.Abs(frame.resolvedStyle.height - size) > .5f) frame.style.height = size;
        }

        private void Metric(string key, string title, float value, string description, string stateClass)
        {
            var host = root.Q<VisualElement>(key);
            if (host == null) return;
            var gauge = host.Q<ProfileMetricGauge>();
            if (gauge == null) { gauge = new ProfileMetricGauge(); host.Add(gauge); }
            var hex = stateClass switch
            {
                "state-good" => "#4ade80", "state-warn" => "#facc15", "state-high" => "#fb923c", "state-danger" => "#f87171",
                "momentum-low" => "#94a3b8", "momentum-cool" => "#60a5fa", "momentum-mid" => "#22d3ee", "momentum-hot" => "#fb923c", _ => "#f5c451"
            };
            ColorUtility.TryParseHtmlString(hex, out var color);
            gauge.SetValue(title, value, description, color);
        }

        private void FanResponse(string key, string audience, FanResponseState response)
        {
            foreach (var axis in new[] { ("preference", "선호", response.Preference), ("interest", "관심", response.Interest) })
            {
                var host = root.Q<VisualElement>(key + "-" + axis.Item1);
                if (host == null) continue;
                var bar = host.Q<ProgressBar>();
                if (bar == null)
                {
                    bar = new ProgressBar { lowValue = 0, highValue = 100, pickingMode = PickingMode.Position, focusable = true, tabIndex = 0 };
                    bar.AddToClassList("wp-fan-bar"); bar.AddToClassList(axis.Item1); host.Add(bar);
                }
                bar.value = float.IsNaN(axis.Item3) || float.IsInfinity(axis.Item3) ? 0 : Mathf.Clamp(axis.Item3, 0, 100);
                bar.title = bar.value.ToString("0");
                bar.tooltip = $"{audience} {axis.Item2}: {bar.value:0} / 100";
            }
        }

        private void CloseAppearanceEditor()
        {
            appearanceOverlay?.RemoveFromHierarchy(); appearanceOverlay = null;
        }

        private void OpenAppearanceEditor()
        {
            if (!canEditAppearance || boundWrestler == null || DashboardSession.IsShowRunning || appearanceOverlay != null) return;
            var wrestler = boundWrestler;
            var appearance = wrestler.Presentation?.Appearance ?? PortraitAppearanceRules.Generate(wrestler.Identity,
                wrestler.Presentation?.WrestlingStyleId, new System.Random(PortraitAppearanceRules.StableSeed(wrestler.Id)));
            var editor = new PortraitEditorView(appearance, wrestler.Identity.Gender, true);
            var overlay = new VisualElement(); overlay.AddToClassList("wp-moves-overlay");
            overlay.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/PortraitEditor"));
            var dialog = new VisualElement(); dialog.AddToClassList("portrait-dialog"); overlay.Add(dialog);
            var title = new Label("복장 변경"); title.AddToClassList("portrait-dialog-title"); dialog.Add(title); dialog.Add(editor);
            var actions = new VisualElement(); actions.AddToClassList("portrait-dialog-actions"); dialog.Add(actions);
            void CloseEditor() { overlay.RemoveFromHierarchy(); appearanceOverlay = null; editAppearanceButton?.Focus(); }
            actions.Add(new Button(CloseEditor) { text = "취소" });
            var saveButton = new Button(() =>
            {
                if (!canEditAppearance || DashboardSession.IsShowRunning || appearanceOverlay != overlay || boundWrestler != wrestler ||
                    DashboardSession.ActiveSave?.Wrestlers.Contains(wrestler) != true) return;
                wrestler.Presentation ??= new WrestlerPresentationState();
                wrestler.Presentation.Appearance ??= appearance.Clone();
                PortraitAppearanceRules.ApplyWardrobe(wrestler.Presentation.Appearance, editor.Appearance, wrestler.Identity.Gender);
                var player = DashboardSession.ActiveSave?.Player;
                if (player?.WrestlerId == wrestler.Id) player.Appearance = wrestler.Presentation.Appearance.Clone();
                PortraitRenderer.Get(wrestler);
                DashboardSession.PersistActive(); CloseEditor(); Bind(wrestler);
            }) { text = "저장", name = "portrait-save" };
            actions.Add(saveButton);
            overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape) { CloseEditor(); evt.StopPropagation(); }
                else if (evt.keyCode == KeyCode.Tab)
                {
                    var focusable = dialog.Query<VisualElement>().ToList().Where(x => x.focusable && x.tabIndex >= 0 && x.enabledInHierarchy && x.resolvedStyle.display != DisplayStyle.None).ToList();
                    if (focusable.Count == 0) return;
                    var index = focusable.IndexOf(dialog.focusController?.focusedElement as VisualElement);
                    focusable[(index + (evt.shiftKey ? -1 : 1) + focusable.Count) % focusable.Count].Focus();
                    evt.StopPropagation(); dialog.focusController?.IgnoreEvent(evt);
                }
            });
            appearanceOverlay = overlay; root.Add(overlay); saveButton.Focus();
        }

        private void OpenCareer()
        {
            if (boundWrestler != null) WrestlerCareerView.Open(root, boundWrestler.Id);
        }

        private void OpenGrowth()
        {
            if (boundWrestler != null) WrestlerGrowthView.Open(root, boundWrestler.Id);
        }

        private void OpenBehaviorReport()
        {
            if (boundWrestler != null) WrestlerBehaviorView.Open(root, boundWrestler.Id);
        }

        private void OpenContract()
        {
            var save = DashboardSession.ActiveSave;
            var id = boundWrestler?.Id;
            if (save == null || id == null || DashboardSession.IsShowRunning) return;
            ContractNegotiationView.Open(root, id, ContractType.Wrestler, () =>
            {
                var wrestler = save.Wrestlers.FirstOrDefault(x => x.Id == id);
                if (wrestler != null) Bind(wrestler);
            });
        }

        private void SaveMatchStyle()
        {
            if (!canSaveStyles || boundWrestler == null || DashboardSession.IsShowRunning) return;
            boundWrestler.Presentation ??= new WrestlerPresentationState();
            boundWrestler.Presentation.WrestlingStyleId = previewStyleId;
            Set("wp-current-style", StyleName(previewStyleId));
            DashboardSession.PersistActive();
        }

        private void SavePromoStyle()
        {
            if (!canSaveStyles || boundWrestler == null || DashboardSession.IsShowRunning) return;
            boundWrestler.Roster ??= new WrestlerRosterState();
            boundWrestler.Presentation ??= new WrestlerPresentationState();
            boundWrestler.Roster.Alignment = previewAlignment;
            boundWrestler.Presentation.PromoDisposition = previewDisposition;
            Set("wp-role", Alignment(previewAlignment));
            Class("wp-role", $"role-{previewAlignment.ToString().ToLowerInvariant()}");
            DashboardSession.PersistActive();
        }

        private void OpenRelationships()
        {
            if (boundWrestler == null || DashboardSession.ActiveSave == null) return;
            var overview = root.Q<VisualElement>(className: "wp-scroll");
            if (overview != null) overview.style.display = DisplayStyle.None;
            root.Q<Button>("wp-open-overview")?.EnableInClassList("active", false);
            root.Q<Button>("wp-open-relationships")?.EnableInClassList("active", true);
            root.Q("story-management")?.RemoveFromHierarchy();
            WrestlerRelationshipsView.Open(root.Q<VisualElement>(className: "wp-content"), DashboardSession.ActiveSave, boundWrestler.Id);
        }

        private void ShowOverview()
        {
            root?.Q("wrestler-relationships")?.RemoveFromHierarchy();
            root?.Q("story-management")?.RemoveFromHierarchy();
            var overview = root?.Q<VisualElement>(className: "wp-scroll");
            if (overview != null) overview.style.display = DisplayStyle.Flex;
            root?.Q<Button>("wp-open-overview")?.EnableInClassList("active", true);
            root?.Q<Button>("wp-open-relationships")?.EnableInClassList("active", false);
        }

        private void OpenMovesEditor()
        {
            if (!canSaveStyles || boundWrestler == null || DashboardSession.IsShowRunning) return;
            movesOverlay?.RemoveFromHierarchy();
            var moves = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog")?.Moves?
                .Where(x => x != null).OrderBy(x => x.DisplayName).ToList();
            if (moves == null || moves.Count == 0) return;

            var current = boundWrestler.Presentation ?? new WrestlerPresentationState();
            DropdownField Field(string label, IReadOnlyList<string> selected, int index, bool finisher)
            {
                var id = selected != null && index < selected.Count ? selected[index] : null;
                var choices = moves.Where(x => x.CanAssign(boundWrestler, finisher) || x.Id == id).ToList();
                var labels = new List<string> { "없음" };
                labels.AddRange(choices.Select(x => x.DisplayName));
                var ids = new List<string> { null };
                ids.AddRange(choices.Select(x => x.Id));
                var field = new DropdownField(label, labels, Math.Max(0, ids.IndexOf(id)));
                field.userData = ids;
                field.AddToClassList("wp-moves-field");
                return field;
            }

            var signature1 = Field("시그니처 1", current.SignatureMoveIds, 0, false);
            var signature2 = Field("시그니처 2", current.SignatureMoveIds, 1, false);
            var finisher1 = Field("피니셔 1", current.FinisherMoveIds, 0, true);
            var finisher2 = Field("피니셔 2", current.FinisherMoveIds, 1, true);
            movesOverlay = new VisualElement(); movesOverlay.AddToClassList("wp-moves-overlay");
            var dialog = new VisualElement(); dialog.AddToClassList("wp-moves-dialog");
            var title = new Label("무브셋 변경"); title.AddToClassList("wp-moves-dialog-title"); dialog.Add(title);
            dialog.Add(signature1); dialog.Add(signature2); dialog.Add(finisher1); dialog.Add(finisher2);
            var actions = new VisualElement(); actions.AddToClassList("wp-moves-actions");
            actions.Add(new Button(() => { movesOverlay.RemoveFromHierarchy(); movesOverlay = null; }) { text = "취소" });
            var save = new Button(() =>
            {
                boundWrestler.Presentation ??= new WrestlerPresentationState();
                string Selected(DropdownField field) => ((List<string>)field.userData)[field.index];
                boundWrestler.Presentation.SignatureMoveIds = new[] { Selected(signature1), Selected(signature2) }.Where(x => x != null).Distinct().ToList();
                boundWrestler.Presentation.FinisherMoveIds = new[] { Selected(finisher1), Selected(finisher2) }.Where(x => x != null).Distinct().ToList();
                DashboardSession.PersistActive();
                Bind(boundWrestler);
                movesOverlay.RemoveFromHierarchy(); movesOverlay = null;
            }) { text = "저장" };
            save.AddToClassList("wp-moves-save"); actions.Add(save); dialog.Add(actions); movesOverlay.Add(dialog); root.Add(movesOverlay);
        }

        private static Button Choice(string label, bool selected, Action clicked)
        {
            var button = new Button(clicked) { text = label };
            button.AddToClassList("wp-choice");
            button.EnableInClassList("selected", selected);
            return button;
        }

        private ScoutCandidateState ScoutReport => DashboardSession.ActiveSave?.ScoutCandidates?.FirstOrDefault(x => x.WrestlerId == boundWrestler?.Id);

        private void ApplyScoutingReport()
        {
            var report = ScoutReport; if (report == null) return;
            var match = new[] { ScoutValueType.RingPsychology, ScoutValueType.RingImprovisation, ScoutValueType.Brawling, ScoutValueType.Power,
                ScoutValueType.Technical, ScoutValueType.HighFlying, ScoutValueType.SpotWork, ScoutValueType.SpecialtyMatches, ScoutValueType.Selling, ScoutValueType.Stamina };
            var promo = new[] { ScoutValueType.Charisma, ScoutValueType.MicWork, ScoutValueType.Improvisation, ScoutValueType.Acting,
                ScoutValueType.FaceWork, ScoutValueType.HeelWork, ScoutValueType.Comedy };
            foreach (var (target, types) in new[] { ("wp-match-abilities", match), ("wp-promo-abilities", promo) })
            {
                var values = root.Q(target)?.Query<Label>(className: "wp-ability-value").ToList();
                if (values == null) continue;
                for (var i = 0; i < values.Count && i < types.Length; i++)
                    SetScoutGrade(values[i], DashboardScoutingController.Estimate(report, types[i]));
            }
            SetScoutGrade(root.Q<Label>("wp-match-potential"), DashboardScoutingController.Estimate(report, ScoutValueType.MatchPotential));
            SetScoutGrade(root.Q<Label>("wp-promo-potential"), DashboardScoutingController.Estimate(report, ScoutValueType.PromoPotential));
        }

        internal static void SetScoutGrade(Label label, string text)
        {
            if (label == null) return;
            foreach (var old in Palette) label.RemoveFromClassList(old);
            label.RemoveFromClassList("scout-unconfirmed"); label.text = text;
            if (text == "미확인" || text.Contains("~")) label.AddToClassList("scout-unconfirmed");
            else
            {
                var gradeClass = text switch
                {
                    "매우 낮음" => "grade-e", "낮음" => "grade-eplus", "보통" => "grade-dplus",
                    "높음" => "grade-a", "매우 높음" => "grade-ss",
                    _ => "grade-" + text.ToLowerInvariant().Replace("+", "plus")
                };
                if (Palette.Contains(gradeClass)) label.AddToClassList(gradeClass);
            }
        }

        private void UpdatePreviewSummary()
        {
            if (boundWrestler == null) return;
            var report = ScoutReport;
            if (report != null)
            {
                SetScoutGrade(root.Q<Label>("wp-match-overall"), DashboardScoutingController.Estimate(report, ScoutValueType.MatchOverall));
                SetScoutGrade(root.Q<Label>("wp-promo-overall"), DashboardScoutingController.Estimate(report, ScoutValueType.PromoOverall));
                return;
            }
            SetGrade("wp-match-overall", WrestlerOverallCalculator.Match(boundWrestler, previewStyleId));
            SetGrade("wp-promo-overall", WrestlerOverallCalculator.Promo(boundWrestler, previewAlignment, previewDisposition));
            ApplyMatchStyleEmphasis();
            ApplyPromoStyleEmphasis();
        }

        private void Abilities(string targetId, (string Name, float Value)[] values, int rowCount)
        {
            var target = root.Q<VisualElement>(targetId); if (target == null) return; target.Clear(); for (var i = 0; i < values.Length; i++) AddAbility(target, values[i].Name, values[i].Value, i % 2 == 1 ? "alt" : null);
            while (target.childCount < rowCount) { var spacer = new VisualElement(); spacer.AddToClassList("wp-ability-row"); spacer.AddToClassList("wp-ability-spacer"); target.Add(spacer); }
        }
        internal static void AddAbility(VisualElement target, string title, float value, string extra)
        {
            var row = new VisualElement(); row.AddToClassList("wp-ability-row"); if (!string.IsNullOrEmpty(extra)) row.AddToClassList(extra); var name = new Label(title); name.AddToClassList("wp-ability-label"); var grade = new Label(Grade(value)); grade.AddToClassList("wp-ability-value"); grade.AddToClassList(ToGradeClass(value)); row.Add(name); row.Add(grade); target.Add(row);
        }
        internal static (string Name, float Value)[] MatchAbilities(WrestlerAttributesState a) => new[]
        {
            ("경기 운영", a.RingPsychology), ("임기응변", a.RingImprovisation), ("브롤링", a.Brawling),
            ("파워", a.Power), ("테크니컬", a.Technical), ("하이플라잉", a.HighFlying),
            ("스팟 수행력", a.SpotWork), ("특수 경기", a.SpecialtyMatches), ("접수력", a.Selling), ("스태미나", a.Stamina)
        };
        internal static (string Name, float Value)[] PromoAbilities(WrestlerAttributesState a) => new[]
        {
            ("카리스마", a.Charisma), ("마이크", a.MicWork), ("즉흥성", a.Improvisation),
            ("캐릭터 표현력", a.Acting), ("페이스 연기", a.FaceWork), ("힐 연기", a.HeelWork), ("코미디", a.Comedy)
        };
        private void Set(string key, string value) { var label = root.Q<Label>(key); if (label != null) label.text = value ?? "—"; }
        private void Class(string key, string value) { var e = root.Q<VisualElement>(key); if (e == null) return; foreach (var old in Palette) e.RemoveFromClassList(old); e.AddToClassList(value); }
        private void GradeClass(string key, float value) => Class(key, ToGradeClass(value));
        private void SetGrade(string key, float value) { root.Q(key)?.RemoveFromClassList("scout-unconfirmed"); Set(key, Grade(value)); GradeClass(key, value); }
        private void ApplyMatchStyleEmphasis()
        {
            var target = root.Q<VisualElement>("wp-match-abilities");
            if (target == null) return;
            foreach (var row in target.Children())
            {
                row.RemoveFromClassList("style-priority-1");
                row.RemoveFromClassList("style-priority-2");
            }

            var priorities = WrestlerOverallCalculator.MatchStylePriorities(previewStyleId);
            foreach (var row in target.Children())
            {
                var label = row.Q<Label>(className: "wp-ability-label")?.text;
                if (priorities.Primary.Contains(label)) row.AddToClassList("style-priority-1");
                else if (priorities.Secondary.Contains(label)) row.AddToClassList("style-priority-2");
            }
        }
        private void ApplyPromoStyleEmphasis()
        {
            var target = root.Q<VisualElement>("wp-promo-abilities");
            if (target == null) return;
            foreach (var row in target.Children())
            {
                row.RemoveFromClassList("style-priority-1");
                row.RemoveFromClassList("style-priority-2");
                var label = row.Q<Label>(className: "wp-ability-label")?.text;
                if (previewAlignment == KayfabeAlignment.Face && label == "페이스 연기" || previewAlignment == KayfabeAlignment.Heel && label == "힐 연기" || previewDisposition == PromoDisposition.Comic && label == "코미디")
                    row.AddToClassList("style-priority-1");
                else if (previewAlignment == KayfabeAlignment.Tweener && (label == "페이스 연기" || label == "힐 연기") || previewDisposition == PromoDisposition.Balanced && label == "코미디")
                    row.AddToClassList("style-priority-2");
            }
        }
        private static bool IsParticipant(MatchResultState r, string id) => r != null && (r.WrestlerPerformances?.Any(x => x.WrestlerId == id) ?? false);
        private static int Age(GameDate birth, GameDate current) => birth.AgeOn(current);
        private static string Date(GameDate d) => $"{d.Year}년 {d.Month}월 {d.Day}일";
        private static string Initials(string name) { if (string.IsNullOrWhiteSpace(name)) return "PW"; var p = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); return p.Length > 1 ? $"{p[0][0]}{p[^1][0]}".ToUpperInvariant() : name.Substring(0, Math.Min(2, name.Length)).ToUpperInvariant(); }
        private static string Grade(float v) => WrestlerOverallCalculator.Grade(v);
        private static string ToGradeClass(float v) => "grade-" + WrestlerOverallCalculator.Grade(v).ToLowerInvariant().Replace("+", "plus");
        private static (string Label, string ClassName) CardStatus(WrestlerStatusState s) => s.IsRookie ? ("신인", "status-rookie") : s.StatusValue >= 80 ? ("메인이벤터", "status-main") : s.StatusValue >= 45 ? ("미드카더", "status-mid") : ("자버", "status-jobber");
        private static string Alignment(KayfabeAlignment v) => v == KayfabeAlignment.Face ? "페이스" : v == KayfabeAlignment.Heel ? "힐" : "트위너";
        private static string Injury(InjuryStatus v) => v == InjuryStatus.None ? "없음" : v == InjuryStatus.Minor ? "경상" : v == InjuryStatus.Moderate ? "부상" : "중상";
        private static string Region(BodyRegion region) => region switch { BodyRegion.Head => "머리", BodyRegion.Arms => "팔", BodyRegion.Legs => "다리", _ => "몸통" };
        private static string Background(WrestlerBackground v) => v == WrestlerBackground.Rookie ? "신인" : v == WrestlerBackground.Athlete ? "스포츠" : v == WrestlerBackground.Entertainer ? "연예계" : "베테랑";
        private static string StyleName(string id) => id switch { "style_001" => "브롤러", "style_002" => "파워하우스", "style_003" => "테크니션", "style_004" => "하이플라이어", "style_005" => "루차 리브레", "style_006" => "자이언트", _ => "올라운더" };
        private static string StateClass(float v) => v < 30 ? "state-good" : v < 60 ? "state-warn" : v < 80 ? "state-high" : "state-danger";
        private static string ConditionClass(float v) => v < 40 ? "state-danger" : v < 60 ? "state-high" : v < 80 ? "state-warn" : "state-good";
        private static string MomentumClass(float v) => v < 20 ? "momentum-low" : v < 40 ? "momentum-cool" : v < 50 ? "momentum-mid" : v < 80 ? "momentum-hot" : "momentum-peak";
    }
}
