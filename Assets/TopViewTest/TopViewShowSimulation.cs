using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class TopViewArenaTest
{
    private GameSave showSimulationSave;
    private ShowResultState showSimulationResult;
    private int showSegment, loggedEvent = -1;
    private bool showSummary, showPromo;
    private float showPromoElapsed;
    private int showPromoLine;
    private VisualElement showSimulationUI, showStage, showCastLeft, showCastRight, showResultOverlay;
    private ScrollView showLog, showCard;
    private Label showHeading, showPhase, showHeat, showOutcome;
    private ProgressBar showHeatBar;
    private Button showPause, showNext;
    private Slider showTimeline;
    private readonly List<VisualElement> showCardRows = new();
    private ProfileMetricGauge[] showConditionGauges;
    private TopViewFanCrowdFrame[] showFanFrames;
    private VisualElement[] showFanBlue, showFanRed;
    private Label[] showFanCaptions;
    private Label[][] showStatCaptions;
    private int[] showFanGroups, showFanRanks;
    private readonly int[] showFanCounts = new int[3];

    private void StartShowSimulation()
    {
        try
        {
            showSimulationSave = ShowTestSaveFactory.Create(catalog, 20261019);
            var content = new StaticContentRegistry(catalog);
            var bookedIds = showSimulationSave.Wrestlers.ToDictionary(w => w.Identity.RingName, w => w.Id);
            var generator = new WrestlerGenerator(content, 20261019);
            var names = new HashSet<string>();
            int appearanceIndex = 0;
            foreach (var wrestler in showSimulationSave.Wrestlers)
            {
                string name;
                do { name = generator.GenerateCandidate(wrestler.Identity.Gender, showSimulationSave.CurrentDate).Identity.LegalName; }
                while (!names.Add(name));
                wrestler.Identity.RingName = wrestler.Identity.LegalName = name;
                var appearance = new PortraitAppearanceState { BodyType = PortraitAppearanceRules.BodyType(wrestler.Identity), SkinBrightness = 98 + appearanceIndex % 4 * 4 };
                appearance.SetPart("hair", 2 + appearanceIndex % 3);
                appearance.SetPart("head", 1 + appearanceIndex % 4);
                appearance.SetPart("eyes", 1 + appearanceIndex % 3);
                appearance.SetPart("brows", 1 + appearanceIndex % 3);
                appearance.SetPart("nose", 1 + appearanceIndex % 3);
                appearance.SetPart("mouth", 1 + appearanceIndex % 3);
                appearance.SetPart("clothes", wrestler.Identity.Gender == WrestlerGender.Female ? 2 : 1);
                appearance.SetColor("hair", appearanceIndex % 2 == 0 ? "#242424" : "#513525");
                appearance.SetColor("eyes", "#513525");
                PortraitAppearanceRules.Normalize(appearance, wrestler.Identity.Gender);
                wrestler.Presentation.Appearance = appearance;
                appearanceIndex++;
            }
            var planning = new ShowPlanningService(content, randomSeed: 73);
            var show = showSimulationSave.Shows[0];
            show.Name = "MONDAY NIGHT WRESTLING";
            string Id(string name) => bookedIds[name];
            void Match(string a, string b, int minutes)
            {
                planning.AddMatch(showSimulationSave, show.Id, new MatchPlanState
                {
                    MatchTypeId = "matchtype_001", MatchGimmickId = "gimmick_000",
                    ParticipantIds = new() { Id(a), Id(b) }, WinnerId = Id(a), LoserTargetId = Id(b), FinishType = MatchFinishType.Pinfall
                }, minutes);
            }
            Match("Gunther", "Seth Rollins", 5);
            planning.AddPromo(showSimulationSave, show.Id, new PromoPlanState
            {
                Purpose = PromoPurpose.MatchBuild, Presentation = PromoPresentation.InRingMic,
                ParticipantIds = new() { Id("Cody Rhodes"), Id("Roman Reigns") }
            }, 5);
            Match("Cody Rhodes", "Roman Reigns", 10);
            show.DurationLimit = show.CalculatePlannedDuration(showSimulationSave.ShowEvents);
            planning.Confirm(showSimulationSave, show.Id);
            show.Status = ShowStatus.InProgress;
            var venue = content.Venues[showSimulationSave.VenueContracts[0].VenueId];
            showSimulationResult = new ShowExecutionService(planning,
                new MatchEvaluator(content, content, findMove: content.GetMoveRules, accidentsEnabled: false), new PromoEvaluator())
                .Execute(showSimulationSave, show.Id, 73, venue.Capacity, venue.BaseTicketPrice);
            foreach (var promo in showSimulationSave.PromoResults)
                if (promo.Narrative?.Lines?.Count is null or 0)
                    promo.Narrative = PromoFallbackNarration.Create(PromoContextBuilder.Build(showSimulationSave, promo));
            LoadShowMatch(showSimulationSave.MatchResults[0]);
            CreateProfile();
            previewSave = showSimulationSave;
            DashboardSession.SetActiveSave(previewSave, transient: true);
            BuildShowSimulationUI();
            ShowSimulationSegment(0);
        }
        catch (Exception exception)
        {
            paused = true;
            Debug.LogException(exception);
            var root = GetComponent<UIDocument>().rootVisualElement;
            var error = new Label("쇼를 불러오지 못했습니다. 게임 데이터를 확인한 뒤 다시 실행해주세요.");
            error.style.whiteSpace = WhiteSpace.Normal;
            error.style.color = Color.white;
            error.style.backgroundColor = new Color(.12f, .06f, .06f);
            error.style.paddingTop = error.style.paddingLeft = 30;
            root.Add(error);
        }
    }

    private void LoadShowMatch(MatchResultState result)
    {
        showFanFrames = null;
        statesRow = null;
        Wrestlers = result.EngineState.Participants.Select(p => showSimulationSave.Wrestlers.Single(w => w.Id == p.Id)).ToArray();
        LoadMatch(result.EngineState);
        showFanFrames = TopViewFanCrowd.Build(Engine, Wrestlers);
        if (showFanGroups == null)
        {
            showFanGroups = new int[crowdPanels.Length]; showFanRanks = new int[crowdPanels.Length];
            var audience = showSimulationSave.Promotion.Audience;
            double total = Enumerable.Range(0, 3).Sum(g => (double)FanAudienceService.Group(audience, g).Followers);
            int start = 0;
            for (int group = 0; group < 3; group++)
            {
                int count = group == 2 ? crowdPanels.Length - start : Mathf.RoundToInt((float)(FanAudienceService.Group(audience, group).Followers / Math.Max(1, total) * crowdPanels.Length));
                showFanCounts[group] = count;
                for (int i = 0; i < count; i++) { showFanGroups[start + i] = group; showFanRanks[start + i] = i; }
                start += count;
            }
        }
        narrationMatch = result;
        narratedIndex = -1;
        CommentaryText = null;
        loggedEvent = -1;
        target = ringCenter;
        targetSize = ringsideViewSize;
    }

    private static VisualElement ShowBox(VisualElement parent, string className, string name = null)
    {
        var box = new VisualElement { name = name }; foreach (var item in className.Split(' ')) box.AddToClassList(item); parent.Add(box); return box;
    }
    private static Label ShowText(VisualElement parent, string text, string className, string name = null)
    {
        var label = new Label(text) { name = name }; label.AddToClassList(className); parent.Add(label); return label;
    }
    private static Button ShowButton(VisualElement parent, string text, string name, Action clicked)
    {
        var button = new Button(clicked) { name = name, text = text }; button.AddToClassList("show-button"); parent.Add(button); return button;
    }

    private void BuildShowSimulationUI()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;
        root.Clear();
        showSimulationUI = ShowBox(root, "show-screen", "topview-show-screen");
        showSimulationUI.style.unityFont = commentaryFont;
        showSimulationUI.styleSheets.Add(Resources.Load<StyleSheet>("TopViewShowSimulation"));
        var header = ShowBox(showSimulationUI, "show-header");
        var brand = ShowBox(header, "show-brand");
        ShowText(brand, showSimulationSave.Shows[0].Name, "show-title");
        ShowText(header, "쇼 중계", "show-live");
        ShowText(header, $"{showSimulationSave.CurrentDate.Year}.{showSimulationSave.CurrentDate.Month:D2}.{showSimulationSave.CurrentDate.Day:D2}", "show-muted");
        ShowButton(header, "결과까지 진행", "show-finish", FinishShowSimulation);
        var body = ShowBox(showSimulationUI, "show-body");
        var left = ShowBox(body, "show-side");
        showCastLeft = ShowBox(left, "show-wrestler", "show-left-wrestler");
        var crowd = ShowBox(left, "show-section");
        ShowText(crowd, "관중 반응", "show-section-title");
        showHeat = ShowText(crowd, "0%", "show-heat", "show-crowd-value");
        showHeatBar = new ProgressBar { name = "show-crowd-bar", lowValue = 0, highValue = 100 };
        showHeatBar.AddToClassList("show-meter"); crowd.Add(showHeatBar);
        showFanBlue = new VisualElement[3]; showFanRed = new VisualElement[3]; showFanCaptions = new Label[3];
        for (int group = 0; group < 3; group++)
        {
            var meter = ShowBox(crowd, "show-fan-meter");
            showFanCaptions[group] = ShowText(meter, "", "show-meter-caption");
            var track = ShowBox(meter, "show-fan-track", "show-fans-" + group);
            showFanBlue[group] = ShowBox(track, "show-fan-blue");
            showFanRed[group] = ShowBox(track, "show-fan-red");
        }
        var center = ShowBox(body, "show-center");
        var heading = ShowBox(center, "show-match-heading");
        showHeading = ShowText(heading, "", "show-match-title", "show-match-title");
        matchTime = ShowText(heading, "00:00", "show-clock", "show-clock");
        showStage = ShowBox(center, "show-stage", "show-arena-stage");
        showPhase = ShowText(showStage, "", "show-phase", "show-phase");
        showResultOverlay = ShowBox(showStage, "show-result-overlay", "show-segment-result");
        showOutcome = ShowText(showResultOverlay, "", "show-result-title", "show-result-title");
        var caption = ShowBox(center, "show-caption");
        ShowText(caption, "해설", "show-kicker");
        commentary = ShowText(caption, "", "show-commentary", "show-commentary");
        var right = ShowBox(body, "show-side");
        showCastRight = ShowBox(right, "show-wrestler", "show-right-wrestler");
        var card = ShowBox(right, "show-section show-card-section");
        ShowText(card, "쇼 카드", "show-section-title");
        showCard = new ScrollView { name = "show-card" }; card.Add(showCard);
        for (int i = 0; i < showSimulationResult.TimelineResultIds.Count; i++)
        {
            var row = ShowBox(showCard, "show-card-row");
            ShowText(row, $"{i + 1:D2}", "show-card-number");
            var item = ShowBox(row, "show-card-copy");
            ShowText(item, i == 1 ? "프로모" : i == 0 ? "오프닝 매치" : "메인 이벤트", "show-card-type");
            var text = WrestlerNameText.Create(ShowSegmentTitle(i), showSimulationSave.Wrestlers);
            text.AddToClassList("show-card-name"); item.Add(text); showCardRows.Add(row);
        }
        var bottom = ShowBox(showSimulationUI, "show-bottom");
        var transport = ShowBox(bottom, "show-transport");
        showPause = ShowButton(transport, "일시정지", "show-pause", () => paused = !paused);
        var speed = new DropdownField(new List<string> { "1배속", "2배속", "4배속" }, 0) { name = "show-speed" };
        speed.AddToClassList("show-speed"); speed.RegisterValueChangedCallback(_ => playbackSpeed = speed.index == 2 ? 4 : speed.index + 1); transport.Add(speed);
        showTimeline = new Slider { name = "show-timeline", lowValue = 0, highValue = Engine.ElapsedSeconds };
        showTimeline.AddToClassList("show-timeline");
        showTimeline.RegisterValueChangedCallback(e => { paused = true; Seek(e.newValue); }); transport.Add(showTimeline);
        ShowButton(transport, "다음 주요 장면", "show-highlight", NextShowHighlight);
        showNext = ShowButton(transport, "다음 세그먼트", "show-next", () => ShowSimulationSegment(showSegment + 1));
        showLog = new ScrollView(ScrollViewMode.Horizontal) { name = "show-log" }; showLog.AddToClassList("show-log"); bottom.Add(showLog);
        showStage.RegisterCallback<GeometryChangedEvent>(_ => FitShowCamera());
        root.RegisterCallback<GeometryChangedEvent>(_ => FitShowCamera());
        showSimulationUI.RegisterCallback<KeyDownEvent>(e =>
        {
            if (e.keyCode == KeyCode.Space) { paused = !paused; e.StopPropagation(); }
        });
        showSimulationUI.focusable = true;
        showSimulationUI.Focus();
    }

    private string ShowSegmentTitle(int index)
    {
        var id = showSimulationResult.TimelineResultIds[index];
        var match = showSimulationSave.MatchResults.FirstOrDefault(m => m.Id == id);
        var ids = match != null ? match.EngineState.Participants.Select(p => p.Id) :
            showSimulationSave.PromoPlans.Single(p => p.Id == showSimulationSave.PromoResults.Single(r => r.Id == id).PromoPlanId).ParticipantIds;
        return string.Join(match != null ? " vs " : " · ", ids.Select(id => WrestlerNameText.DisplayName(showSimulationSave.Wrestlers.Single(w => w.Id == id))));
    }

    public void ShowSimulationSegment(int index)
    {
        if (index >= showSimulationResult.TimelineResultIds.Count) { FinishShowSimulation(); return; }
        showSummary = false;
        showPause.SetEnabled(true); showNext.SetEnabled(true);
        showSimulationUI.Q<Button>("show-highlight").SetEnabled(true);
        showSimulationUI.Q<Button>("show-finish").SetEnabled(true);
        showSimulationUI.Q<DropdownField>("show-speed").SetEnabled(true);
        showResultOverlay.Clear();
        showOutcome = ShowText(showResultOverlay, "", "show-result-title", "show-result-title");
        showPromoElapsed = 0; showPromoLine = 0;
        showSegment = index;
        var id = showSimulationResult.TimelineResultIds[index];
        var result = showSimulationSave.MatchResults.FirstOrDefault(r => r.Id == id);
        showPromo = result == null;
        if (result != null) LoadShowMatch(result);
        else
        {
            var promo = showSimulationSave.PromoResults.Single(r => r.Id == id);
            var plan = showSimulationSave.PromoPlans.Single(p => p.Id == promo.PromoPlanId);
            Wrestlers = plan.ParticipantIds.Select(id => showSimulationSave.Wrestlers.Single(w => w.Id == id)).ToArray();
        }
        foreach (var marker in markers) marker.gameObject.SetActive(!showPromo);
        BuildShowWrestlers();
        WrestlerNameText.Set(showHeading, ShowSegmentTitle(index), Wrestlers);
        showLog.Clear();
        showResultOverlay.style.display = result == null ? DisplayStyle.Flex : DisplayStyle.None;
        showPhase.text = result == null ? "프로모 · 링 위의 대화" : index == 0 ? "오프닝 매치 · 싱글 경기" : "메인 이벤트 · 싱글 경기";
        if (result == null)
        {
            var promo = showSimulationSave.PromoResults.Single(r => r.Id == id);
            var lines = promo.Narrative?.Lines ?? new List<NarrativeLineState>();
            WrestlerNameText.Set(showOutcome, "링 위의 대화", Wrestlers);
            var cast = ShowBox(showResultOverlay, "show-promo-cast");
            foreach (var wrestler in Wrestlers)
            {
                var person = ShowBox(cast, "show-promo-person");
                var image = new Image { image = PortraitRenderer.Get(wrestler), scaleMode = ScaleMode.ScaleToFit };
                image.AddToClassList("show-promo-portrait"); person.Add(image);
                var link = WrestlerProfileController.CreateLink(wrestler); link.AddToClassList("show-wrestler-name"); person.Add(link);
            }
            WrestlerNameText.Set(commentary, lines.Count > 0 ? lines[0].Text : ShowSegmentTitle(index), Wrestlers);
            foreach (var line in lines) AddShowLog(line.Text);
            paused = false;
        }
        else { Seek(0); paused = false; }
        playbackSpeed = 1;
        showSimulationUI.Q<DropdownField>("show-speed").SetValueWithoutNotify("1배속");
        showTimeline.highValue = showPromo ? 300 : Engine.ElapsedSeconds;
        showTimeline.SetValueWithoutNotify(showPromo ? 0 : clock);
        showTimeline.SetEnabled(result != null);
        showNext.text = index == 2 ? "쇼 요약 확인" : "다음 세그먼트";
        for (int i = 0; i < showCardRows.Count; i++) { showCardRows[i].EnableInClassList("current", i == index); showCardRows[i].EnableInClassList("done", i < index); }
        statesRow = showSimulationUI;
        FitShowCamera();
    }

    private void BuildShowWrestlers()
    {
        showCastLeft.Clear(); showCastRight.Clear();
        stateLabels = new Label[2]; stateBars = new ProgressBar[2][]; damageIndicators = new LimbDamageIndicator[2];
        showConditionGauges = new ProfileMetricGauge[2];
        showStatCaptions = new Label[2][];
        for (int i = 0; i < 2; i++)
        {
            var host = i == 0 ? showCastLeft : showCastRight;
            host.EnableInClassList("blue", i == 0); host.EnableInClassList("red", i == 1);
            var wrestler = Wrestlers[i];
            var portrait = new Image { image = PortraitRenderer.Get(wrestler), scaleMode = ScaleMode.ScaleToFit, name = "show-portrait-" + i };
            portrait.AddToClassList("show-portrait"); host.Add(portrait);
            var name = WrestlerProfileController.CreateLink(wrestler); name.name = "show-name-" + i; name.AddToClassList("show-wrestler-name"); host.Add(name);
            var detail = ShowBox(host, "show-wrestler-detail");
            stateLabels[i] = ShowText(detail, "", "show-position");
            stateBars[i] = new ProgressBar[3];
            showStatCaptions[i] = new Label[3];
            for (int j = 0; j < 3; j++)
            {
                showStatCaptions[i][j] = ShowText(detail, "", "show-meter-caption");
                var meter = new ProgressBar { lowValue = 0, highValue = 100 }; meter.AddToClassList("show-meter"); detail.Add(meter); stateBars[i][j] = meter;
                meter.Q<VisualElement>(className: "unity-progress-bar__progress").style.backgroundColor = j == 0 ? new Color(.65f, .52f, .3f) :
                    j == 1 ? new Color(.7f, .34f, .37f) : new Color(.32f, .52f, .72f);
            }
            var damage = ShowBox(detail, "show-damage");
            damageIndicators[i] = new LimbDamageIndicator(); damageIndicators[i].AddToClassList("show-body-damage"); damage.Add(damageIndicators[i]);
            var condition = ShowBox(damage, "show-condition-column");
            showConditionGauges[i] = new ProfileMetricGauge { name = "show-condition-" + i };
            showConditionGauges[i].AddToClassList("show-condition"); condition.Add(showConditionGauges[i]);
            ShowText(condition, "컨디션", "show-meter-caption");
            if (showPromo)
            {
                stateLabels[i].text = "프로모 · 마이크";
                foreach (var meter in stateBars[i]) meter.style.display = DisplayStyle.None;
                foreach (var caption in showStatCaptions[i]) caption.style.display = DisplayStyle.None;
                damage.style.display = DisplayStyle.None;
            }
        }
    }

    private void AddShowLog(string text)
    {
        var label = WrestlerNameText.Create(text, showSimulationSave.Wrestlers);
        label.AddToClassList("show-log-entry"); showLog.Add(label);
        while (showLog.childCount > 40) showLog[0].RemoveFromHierarchy();
        showLog.schedule.Execute(() => { if (label.panel != null) showLog.ScrollTo(label); });
    }

    private void UpdateShowSimulation()
    {
        showSimulationUI.style.display = ViewingProfile ? DisplayStyle.None : DisplayStyle.Flex;
        if (ViewingProfile)
        {
            if (UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame == true) WrestlerProfileController.CloseOpenProfile(false);
            return;
        }
        if (showSummary) return;
        var id = showSimulationResult.TimelineResultIds[showSegment];
        showPause.text = paused ? "계속 재생" : "일시정지";
        if (showPromo)
        {
            var promo = showSimulationSave.PromoResults.Single(r => r.Id == id);
            var lines = promo.Narrative?.Lines;
            if (!paused) showPromoElapsed += Time.deltaTime * playbackSpeed;
            if (lines != null && lines.Count > 0)
            {
                int line = Mathf.Min(lines.Count - 1, (int)(showPromoElapsed / 5));
                crowdAccent = lines[line].Intensity >= 3 ? Mathf.Clamp01(1 - showPromoElapsed % 5 / 1.2f) : 0;
                float seconds = Mathf.Clamp01(showPromoElapsed / (lines.Count * 5)) * 300;
                matchTime.text = $"{(int)seconds / 60:00}:{(int)seconds % 60:00}";
                showTimeline.SetValueWithoutNotify(seconds);
                int speaker = Array.FindIndex(Wrestlers, w => w.Id == lines[line].SpeakerId);
                var reaction = new TopViewFanCrowdFrame();
                for (int group = 0; group < 3; group++)
                {
                    reaction.Heat[group] = Mathf.Clamp(FanAudienceService.Get(promo.FanReaction, group), 0, 100);
                    reaction.Focus[group] = Mathf.Max(0, speaker);
                    var first = TopViewFanCrowd.Response(Wrestlers[0], group);
                    var second = TopViewFanCrowd.Response(Wrestlers[1], group);
                    float blue = .1f + first.Interest / 100 + first.Preference / 100;
                    float red = .1f + second.Interest / 100 + second.Preference / 100;
                    if (speaker == 0) blue *= 1.3f; else if (speaker == 1) red *= 1.3f;
                    reaction.Support[group, 0] = blue / (blue + red);
                    reaction.Support[group, 1] = 1 - reaction.Support[group, 0];
                }
                AnimateShowFanCrowd(reaction);
                if (line != showPromoLine) { showPromoLine = line; WrestlerNameText.Set(commentary, lines[line].Text, Wrestlers); }
                if (showPromoElapsed >= lines.Count * 5) ShowSimulationSegment(showSegment + 1);
            }
            return;
        }
        showTimeline.SetValueWithoutNotify(clock);
        var e = CurrentEvent;
        var snapshot = clock >= e.MatchTimeSeconds + e.DurationSeconds * .65f ? e.After : e.Before;
        if (loggedEvent != EventIndex)
        {
            loggedEvent = EventIndex;
            if (!string.IsNullOrWhiteSpace(CommentaryText)) AddShowLog(CommentaryText);
        }
        if (Finished)
        {
            paused = true;
            showResultOverlay.style.display = DisplayStyle.Flex;
            var result = showSimulationSave.MatchResults.Single(r => r.Id == id);
            var winner = showSimulationSave.Wrestlers.FirstOrDefault(w => w.Id == result.WinnerId);
            WrestlerNameText.Set(showOutcome, winner == null ? "경기 종료" : WrestlerNameText.DisplayName(winner) + " 승리", showSimulationSave.Wrestlers);
        }
    }

    public void NextShowHighlight()
    {
        if (showSummary) return;
        if (showPromo) { ShowSimulationSegment(showSegment + 1); return; }
        var e = Engine.Events.FirstOrDefault(e => e.MatchTimeSeconds > clock + 1 && (e.IsMajorMoment || e.Type is MatchActionType.Finisher or MatchActionType.Finish));
        Seek(e?.MatchTimeSeconds ?? Engine.ElapsedSeconds); paused = true;
    }

    public void FinishShowSimulation()
    {
        paused = true; showSummary = true;
        showResultOverlay.style.display = DisplayStyle.Flex; showResultOverlay.Clear();
        ShowText(showResultOverlay, "오늘의 쇼", "show-kicker");
        ShowText(showResultOverlay, "쇼 결과", "show-result-title");
        foreach (var match in showSimulationSave.MatchResults)
        {
            var label = WrestlerNameText.Create(string.Join(" vs ", match.EngineState.Participants.Select(p =>
                WrestlerNameText.DisplayName(showSimulationSave.Wrestlers.Single(w => w.Id == p.Id)))) + "\n" +
                WrestlerNameText.DisplayName(showSimulationSave.Wrestlers.Single(w => w.Id == match.WinnerId)) + $" 승리 · ★ {match.CriticReview?.DisplayedStars:0.##}", showSimulationSave.Wrestlers);
            label.AddToClassList("show-summary-match"); showResultOverlay.Add(label);
        }
        var finance = showSimulationResult.FinancialSettlement;
        ShowText(showResultOverlay, $"쇼 평점 ★ {showSimulationResult.ShowEvaluation.CriticReview?.DisplayedStars:0.##}    관중 {finance.Attendance:N0}명", "show-summary-line");
        ShowText(showResultOverlay, $"수입 ${finance.Revenue:N0}    비용 ${finance.Cost:N0}    순손익 ${finance.NetIncome:N0}", "show-summary-line");
        var apply = ShowButton(showResultOverlay, "결과 반영", "show-apply", () =>
        {
            try
            {
                showSimulationSave.Shows[0].Status = ShowStatus.ResultsReviewed;
                new ShowResultApplicationService().Apply(showSimulationSave, showSimulationResult.Id);
                var button = showResultOverlay.Q<Button>("show-apply"); button.text = "결과 반영 완료"; button.SetEnabled(false);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                ShowText(showResultOverlay, "결과를 반영하지 못했습니다. 다시 시도해주세요.", "show-summary-line");
            }
        });
        if (showSimulationSave.Shows[0].Status == ShowStatus.Completed) { apply.text = "결과 반영 완료"; apply.SetEnabled(false); }
        for (int i = 0; i < showCardRows.Count; i++) showCardRows[i].AddToClassList("done");
        showNext.SetEnabled(false); showPause.SetEnabled(false); showTimeline.SetEnabled(false);
        showSimulationUI.Q<Button>("show-highlight").SetEnabled(false);
        showSimulationUI.Q<Button>("show-finish").SetEnabled(false);
        showSimulationUI.Q<DropdownField>("show-speed").SetEnabled(false);
        ShowButton(showResultOverlay, "다시 보기", "show-replay", () => ShowSimulationSegment(0));
        apply.Focus();
    }

    private void FitShowCamera()
    {
        if (showStage?.panel == null) return;
        var root = GetComponent<UIDocument>().rootVisualElement.worldBound;
        var bounds = showStage.worldBound;
        if (bounds.width <= 0 || bounds.height <= 0 || root.width <= 0 || root.height <= 0) return;
        viewCamera.rect = new Rect(bounds.x / root.width, 1 - bounds.yMax / root.height, bounds.width / root.width, bounds.height / root.height);
    }

    private void AnimateShowFanCrowd(TopViewFanCrowdFrame frame)
    {
        var lit = new int[3];
        float totalHeat = 0;
        var names = new[] { "몰입형", "캐주얼", "매니아" };
        for (int group = 0; group < 3; group++)
        {
            lit[group] = frame.Focus[group] < 0 ? 0 : Mathf.RoundToInt(showFanCounts[group] * frame.Heat[group] / 100);
            totalHeat += frame.Heat[group] * showFanCounts[group] / Mathf.Max(1f, crowdPanels.Length);
            if (showFanCaptions != null)
            {
                showFanCaptions[group].text = $"{names[group]}  {frame.Heat[group]:0}%";
                showFanBlue[group].style.width = Length.Percent(frame.Heat[group] * frame.Support[group, 0]);
                showFanRed[group].style.width = Length.Percent(frame.Heat[group] * frame.Support[group, 1]);
            }
        }
        if (showHeat != null) { showHeat.text = $"{totalHeat:0}%"; showHeatBar.value = totalHeat; }
        AnimateCrowdPanels(i => showFanRanks[i] < lit[showFanGroups[i]], i =>
        {
            int group = showFanGroups[i];
            float preference = Mathf.Repeat(showFanRanks[i] * .61803399f + group * .17f, 1);
            return markerColors[preference < frame.Support[group, 0] ? 0 : 1];
        });
    }
}
