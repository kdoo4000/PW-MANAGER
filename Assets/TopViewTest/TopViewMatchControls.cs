using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class TopViewArenaTest
{
    private readonly List<TopViewMatchRecording> recordings = new();
    private List<TopViewMatchRecording> visibleRecordings = new();
    private VisualElement reviewControls;
    private DropdownField reviewGroup, reviewSeed, reviewMatch, reviewEngine;
    private Slider reviewTimeline;
    private Label reviewPosition;
    private Button reviewPlay, previousMatch, nextMatch, previousExchange, nextExchange, playExchange;
    private Toggle reviewCamera;
    private bool changingReview;
    private float exchangeStop = -1;
    private string recordingError;

    private void InitializeMatchReview()
    {
        if (gameObject.scene.name != "TopView Arena Test" || Engine != null) return;
        ringsideViewSize = 4.5f;
        try
        {
            var content = new StaticContentRegistry(catalog);
            recordings.AddRange(TopViewMatchLibrary.Discover(
                Path.GetFullPath(Path.Combine(Application.dataPath, "../artifacts/cooperative-engine-comparison")),
                id => content.Styles.TryGetValue(id, out var style) ? style.DisplayName : id));
            if (recordings.Count == 0) { recordingError = "저장된 경기 기록을 찾을 수 없습니다."; return; }
            var initial = recordings.FirstOrDefault(r => r.Key == "styles/20261017/01-vs-04") ?? recordings[0];
            LoadRecording(initial, initial.Versions.ContainsKey(TopViewMatchLibrary.Improved) ? TopViewMatchLibrary.Improved : initial.Versions.Keys.First());
        }
        catch (Exception exception)
        {
            recordingError = "경기 기록을 불러오지 못했습니다.";
            Debug.LogException(exception);
        }
    }

    private void LoadRecording(TopViewMatchRecording recording, string version)
    {
        var (save, result) = recording.Load(version);
        WrestlerProfileController.CloseOpenProfile(false);
        Wrestlers = result.EngineState.Participants.Select(p => save.Wrestlers.Single(w => w.Id == p.Id)).ToArray();
        paused = true;
        exchangeStop = -1;
        LoadMatch(result.EngineState);
        narrationMatch = result;
        narratedIndex = -1;
        CommentaryText = null;
        if (statesRow != null) CreateCommentary();
        Seek(0);
    }

    private void CreateMatchReviewControls()
    {
        if (gameObject.scene.name != "TopView Arena Test") return;
        var root = GetComponent<UIDocument>().rootVisualElement;
        root.Q("match-review-controls")?.RemoveFromHierarchy();
        reviewControls = new VisualElement { name = "match-review-controls" };
        reviewControls.style.unityFont = commentaryFont;
        var sheet = Resources.Load<StyleSheet>("TopViewMatchReview");
        if (sheet) reviewControls.styleSheets.Add(sheet);
        root.Add(reviewControls);
        if (recordingError != null)
        {
            reviewControls.Add(new Label(recordingError));
            return;
        }

        var selectors = ReviewRow();
        reviewControls.Add(selectors);
        reviewGroup = ReviewDropdown(selectors, "review-group", "경기 모음", new() { TopViewMatchLibrary.Styles, TopViewMatchLibrary.Fixtures });
        reviewSeed = ReviewDropdown(selectors, "review-seed", "시드", recordings.Where(r => r.Group == TopViewMatchLibrary.Styles).Select(r => r.Seed.ToString()).Distinct().ToList());
        reviewMatch = ReviewDropdown(selectors, "review-match", "경기", new());
        reviewMatch.parent.style.flexGrow = 3;
        reviewEngine = ReviewDropdown(selectors, "review-engine", "엔진", new());
        reviewGroup.RegisterValueChangedCallback(_ => RefreshReviewMatches());
        reviewSeed.RegisterValueChangedCallback(_ => RefreshReviewMatches());
        reviewMatch.RegisterValueChangedCallback(_ => RefreshReviewEngines());
        reviewEngine.RegisterValueChangedCallback(_ => SelectReviewRecording());

        var transport = ReviewRow();
        reviewControls.Add(transport);
        previousMatch = ReviewButton(transport, "review-previous-match", "이전 경기", () => ChangeReviewMatch(-1));
        nextMatch = ReviewButton(transport, "review-next-match", "다음 경기", () => ChangeReviewMatch(1));
        ReviewButton(transport, "review-restart", "처음부터", () => PauseReviewAt(0));
        previousExchange = ReviewButton(transport, "review-previous-exchange", "이전 공방", () => ChangeReviewExchange(-1));
        reviewPlay = ReviewButton(transport, "review-play", "재생", ToggleReviewPlayback);
        reviewPlay.AddToClassList("primary");
        nextExchange = ReviewButton(transport, "review-next-exchange", "다음 공방", () => ChangeReviewExchange(1));
        playExchange = ReviewButton(transport, "review-play-exchange", "공방 하나 재생", PlayReviewExchange);
        var speed = ReviewDropdown(transport, "review-speed", "배속", new() { "0.5배", "1배", "2배", "4배", "8배" });
        speed.SetValueWithoutNotify("1배");
        playbackSpeed = 1;
        speed.RegisterValueChangedCallback(e => playbackSpeed = e.newValue switch { "0.5배" => .5f, "2배" => 2, "4배" => 4, "8배" => 8, _ => 1 });
        reviewCamera = new Toggle("자동 카메라") { name = "review-camera", value = automaticCamera };
        transport.Add(reviewCamera);
        reviewCamera.RegisterValueChangedCallback(e => automaticCamera = e.newValue);
        var timelineRow = ReviewRow();
        reviewControls.Add(timelineRow);
        reviewPosition = new Label { name = "review-position" };
        timelineRow.Add(reviewPosition);
        reviewTimeline = new Slider(0, Engine.ElapsedSeconds) { name = "review-timeline" };
        reviewTimeline.style.flexGrow = 1;
        reviewTimeline.RegisterValueChangedCallback(e => PauseReviewAt(e.newValue));
        timelineRow.Add(reviewTimeline);

        changingReview = true;
        reviewGroup.SetValueWithoutNotify(TopViewMatchLibrary.Styles);
        reviewSeed.SetValueWithoutNotify("20261017");
        changingReview = false;
        RefreshReviewMatches("styles/20261017/01-vs-04");
    }

    private static VisualElement ReviewRow()
    {
        var row = new VisualElement();
        row.AddToClassList("review-row");
        return row;
    }

    private static DropdownField ReviewDropdown(VisualElement row, string name, string label, List<string> choices)
    {
        var field = new VisualElement();
        field.AddToClassList("review-field");
        field.Add(new Label(label));
        var dropdown = new DropdownField { name = name, choices = choices };
        if (choices.Count > 0) dropdown.SetValueWithoutNotify(choices[0]);
        field.Add(dropdown);
        row.Add(field);
        return dropdown;
    }

    private static Button ReviewButton(VisualElement row, string name, string text, Action clicked)
    {
        var button = new Button(clicked) { name = name, text = text };
        row.Add(button);
        return button;
    }

    private void RefreshReviewMatches(string preferredKey = null)
    {
        if (changingReview) return;
        changingReview = true;
        string previousTitle = reviewMatch.value;
        visibleRecordings = recordings.Where(r => r.Group == reviewGroup.value &&
            (r.Group != TopViewMatchLibrary.Styles || r.Seed.ToString() == reviewSeed.value)).ToList();
        reviewSeed.parent.style.display = reviewGroup.value == TopViewMatchLibrary.Styles ? DisplayStyle.Flex : DisplayStyle.None;
        reviewMatch.choices = visibleRecordings.Select(r => r.Title).ToList();
        var preferred = visibleRecordings.FindIndex(r => r.Key == preferredKey);
        if (preferred < 0) preferred = Math.Max(0, reviewMatch.choices.IndexOf(previousTitle));
        reviewMatch.SetValueWithoutNotify(reviewMatch.choices[preferred]);
        changingReview = false;
        RefreshReviewEngines();
    }

    private void RefreshReviewEngines()
    {
        if (changingReview) return;
        changingReview = true;
        var record = visibleRecordings[reviewMatch.index];
        var previous = reviewEngine.value;
        reviewEngine.choices = new[] { TopViewMatchLibrary.Legacy, TopViewMatchLibrary.Previous, TopViewMatchLibrary.Improved }
            .Where(record.Versions.ContainsKey).ToList();
        reviewEngine.SetValueWithoutNotify(reviewEngine.choices.Contains(previous) ? previous :
            reviewEngine.choices.Contains(TopViewMatchLibrary.Improved) ? TopViewMatchLibrary.Improved : reviewEngine.choices[0]);
        changingReview = false;
        SelectReviewRecording();
    }

    private void SelectReviewRecording()
    {
        if (changingReview) return;
        LoadRecording(visibleRecordings[reviewMatch.index], reviewEngine.value);
        reviewTimeline.highValue = Engine.ElapsedSeconds;
        UpdateReviewPosition();
    }

    private void ChangeReviewMatch(int direction)
    {
        reviewMatch.index = Mathf.Clamp(reviewMatch.index + direction, 0, visibleRecordings.Count - 1);
    }

    private void PauseReviewAt(float time)
    {
        paused = true;
        exchangeStop = -1;
        Seek(time);
        UpdateReviewPosition();
    }

    private void ToggleReviewPlayback()
    {
        exchangeStop = -1;
        if (paused && Finished) Seek(0);
        paused = !paused;
        UpdateReviewPosition();
    }

    private void ChangeReviewExchange(int direction)
    {
        var range = TopViewMatchLibrary.ExchangeRange(Engine, EventIndex);
        int index = direction > 0 ? Math.Min(Engine.Events.Count - 1, range.end + 1) :
            clock > Engine.Events[range.start].MatchTimeSeconds + .01f ? range.start : Math.Max(0, range.start - 1);
        var destination = TopViewMatchLibrary.ExchangeRange(Engine, index);
        PauseReviewAt(Engine.Events[destination.start].MatchTimeSeconds);
    }

    private void PlayReviewExchange()
    {
        var range = TopViewMatchLibrary.ExchangeRange(Engine, EventIndex);
        var last = Engine.Events[range.end];
        PauseReviewAt(Engine.Events[range.start].MatchTimeSeconds);
        exchangeStop = range.end + 1 < Engine.Events.Count ? Engine.Events[range.end + 1].MatchTimeSeconds - .001f :
            Math.Min(Engine.ElapsedSeconds, last.MatchTimeSeconds + last.DurationSeconds);
        paused = false;
        UpdateReviewPosition();
    }

    public void AdvancePlayback(float realSeconds)
    {
        if (paused || Engine == null) return;
        float destination = clock + Math.Max(0, realSeconds) * PlaybackRate(CurrentEvent);
        float limit = exchangeStop >= 0 ? exchangeStop : Engine.ElapsedSeconds;
        Seek(Math.Min(destination, limit));
        if (clock >= limit) { paused = true; exchangeStop = -1; }
    }

    private void UpdateReviewPosition()
    {
        if (reviewTimeline == null) return;
        reviewTimeline.SetValueWithoutNotify(clock);
        static string TimeLabel(float seconds) => $"{(int)seconds / 60:00}:{(int)seconds % 60:00}";
        reviewPosition.text = $"{TimeLabel(clock)} / {TimeLabel(Engine.ElapsedSeconds)}";
        reviewPlay.text = paused ? "재생" : "일시정지";
        previousMatch.SetEnabled(reviewMatch.index > 0);
        nextMatch.SetEnabled(reviewMatch.index + 1 < visibleRecordings.Count);
        var range = TopViewMatchLibrary.ExchangeRange(Engine, EventIndex);
        previousExchange.SetEnabled(clock > 0);
        nextExchange.SetEnabled(range.end + 1 < Engine.Events.Count);
        playExchange.SetEnabled(!Finished);
        reviewCamera.SetValueWithoutNotify(automaticCamera);
    }

    private void UpdateMatchReview(DisplayStyle display)
    {
        if (reviewControls == null) return;
        reviewControls.style.display = display;
        if (display == DisplayStyle.None) return;
        UpdateReviewPosition();
        var root = GetComponent<UIDocument>().rootVisualElement;
        if (reviewControls.layout.height > 0) commentaryBar.style.bottom = reviewControls.layout.height + 32;
        float height = root.worldBound.height;
        float top = statesRow.worldBound.yMax + 12, bottom = commentaryBar.worldBound.yMin - 12;
        if (height > 0 && bottom > top && viewCamera)
            viewCamera.rect = new Rect(0, 1 - bottom / height, 1, (bottom - top) / height);
    }

    private bool PointerOverReviewUI(Vector2 screenPosition)
    {
        if (reviewControls == null && showSimulationUI == null) return false;
        var root = GetComponent<UIDocument>().rootVisualElement;
        if (root.panel == null) return false;
        var picked = root.panel.Pick(RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y)));
        if (showSimulationUI != null)
            return picked != null && picked != root && picked != showStage && !showStage.Contains(picked);
        return picked != null && picked != root;
    }
}
