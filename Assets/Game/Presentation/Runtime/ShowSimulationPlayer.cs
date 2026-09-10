using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public sealed class ShowSimulationPlayer : IDisposable
    {
        private sealed class Segment
        {
            public ShowEventState Event;
            public List<string> Participants;
            public List<NarrativeLineState> Lines;
            public List<MatchSimulationBeatState> Beats;
            public string Title;
            public string Result;
        }

        private readonly VisualElement screen;
        private readonly VisualElement dashboard;
        private readonly GameSave save;
        private readonly ShowResultState result;
        private readonly List<Segment> segments = new();
        private readonly List<Label> cards = new();
        private readonly Dictionary<string, Image> sprites = new();
        private readonly IVisualElementScheduledItem timer;
        private readonly Action onComplete;
        private int segmentIndex;
        private int lineIndex;
        private float elapsed;
        private float lastTime;
        private int frame = -1;
        private float animationElapsed;
        private float matchClock;
        private int nextHighlight;
        private bool fastForwarding;
        private int speed = 1;
        private bool paused;
        private bool finished;
        private bool disposed;

        public ShowSimulationPlayer(VisualElement root, GameSave save, ShowResultState result, Action onComplete)
        {
            this.save = save;
            this.result = result;
            this.onComplete = onComplete;
            BuildSegments();
            if (segments.Count == 0) throw new InvalidOperationException("재생할 쇼 기록이 없습니다.");
            var template = Resources.Load<VisualTreeAsset>("PWManagerUI/ShowSimulation")
                ?? throw new InvalidOperationException("쇼 시뮬레이션 화면을 불러오지 못했습니다.");
            screen = template.CloneTree().Q("simulation-screen");
            // Styles live on the template container; retain them when mounting the dedicated screen.
            var container = screen.parent;
            for (var i = 0; i < container.styleSheets.count; i++) screen.styleSheets.Add(container.styleSheets[i]);
            screen.RemoveFromHierarchy();
            dashboard = root.Q("dashboard");
            dashboard?.AddToClassList("hidden");
            root.Add(screen);
            screen.Q("simulation-cast").RegisterCallback<GeometryChangedEvent>(_ => ResizeSprites());
            screen.Focus();
            Text("simulation-title", save.Shows.First(x => x.Id == result.ShowId).Name);
            Button("simulation-pause").clicked += () =>
            {
                paused = !paused;
                Button("simulation-pause").text = paused ? "계속 재생" : "일시정지";
            };
            Button("simulation-speed").clicked += () =>
            {
                speed = speed == 1 ? 2 : 1;
                Button("simulation-speed").text = $"{speed}배속";
            };
            Button("simulation-next").clicked += NextSegment;
            Button("simulation-end").clicked += Finish;
            Button("simulation-return").clicked += () => this.onComplete();
            foreach (var segment in segments)
            {
                var label = WrestlerNameText.Create(segment.Title, save.Wrestlers);
                label.AddToClassList("sim-card-row");
                screen.Q<ScrollView>("simulation-card").Add(label);
                cards.Add(label);
            }
            ShowSegment();
            lastTime = Time.realtimeSinceStartup;
            timer = screen.schedule.Execute(Tick).Every(33);
        }

        private void BuildSegments()
        {
            foreach (var id in result.TimelineResultIds ?? new List<string>())
            {
                var match = save.MatchResults.FirstOrDefault(x => x?.Id == id);
                var promo = save.PromoResults.FirstOrDefault(x => x?.Id == id);
                var showEvent = save.ShowEvents.FirstOrDefault(x => x?.Id == (match?.ShowEventId ?? promo?.ShowEventId));
                if (showEvent == null) throw new InvalidOperationException("쇼 기록의 세그먼트가 없습니다.");
                var segment = new Segment { Event = showEvent };
                if (match != null)
                {
                    var plan = save.MatchPlans.Single(x => x.Id == match.MatchPlanId);
                    segment.Participants = plan.Sides.Where(x => x?.MemberIds != null).SelectMany(x => x.MemberIds).ToList();
                    if (segment.Participants.Count == 0) segment.Participants.AddRange(plan.ParticipantIds);
                    var narration = new MatchNarrationService();
                    var timed = match.SimulationBeats?.Count > 0 && match.SimulationBeats.All(x => x != null && x.DurationSeconds > 0) &&
                        match.NarrativeLines?.Count == match.SimulationBeats.Count;
                    segment.Beats = timed ? match.SimulationBeats : narration.CreateBeats(plan, match, save);
                    segment.Lines = timed ? match.NarrativeLines : narration.Narrate(plan, match, Name, save);
                    segment.Title = string.Join(" / ", segment.Participants.Select(Name));
                    var winners = new[] { match.FinishPerformerId ?? match.WinnerId }.Concat(match.IndirectWinnerIds ?? new List<string>())
                        .Where(x => !string.IsNullOrEmpty(x)).Distinct().Select(Name);
                    segment.Result = (match.FinishType == MatchFinishType.Draw ? "무승부" : $"{string.Join(" & ", winners)} 승리") + $" · ★ {match.CriticReview?.DisplayedStars:0.##}";
                    foreach (var spot in match.SpotResults ?? new List<SpotResultState>())
                        segment.Result += $"\n특수 스팟 · {SpecialMatchSpotCatalog.Find(spot.SpotId)?.Name ?? "스팟"} · {MatchSpotEvaluator.ResultText(spot)}" +
                            (spot.Result >= SpotExecutionResult.Success && spot.FinalSpotScore < 0 ? " · 반복 역반응" : spot.RepeatCount > 0 ? " · 반복 사용" : string.Empty);
                }
                else
                {
                    var plan = save.PromoPlans.Single(x => x.Id == promo.PromoPlanId);
                    segment.Participants = plan.ParticipantIds;
                    segment.Lines = promo.Narrative?.Lines;
                    if (segment.Lines == null || segment.Lines.Count == 0)
                        segment.Lines = PromoFallbackNarration.Create(PromoContextBuilder.Build(save, promo)).Lines;
                    segment.Title = "프로모 · " + string.Join(" / ", segment.Participants.Select(Name));
                    segment.Result = $"프로모 종료 · {promo.PromoScore:0.0}점";
                }
                if (segment.Lines.Count == 0) throw new InvalidOperationException("세그먼트의 해설이 없습니다.");
                segments.Add(segment);
            }
        }

        private void ShowSegment()
        {
            lineIndex = 0;
            elapsed = 0;
            frame = -1;
            animationElapsed = 0;
            matchClock = 0;
            fastForwarding = false;
            sprites.Clear();
            var cast = screen.Q("simulation-cast");
            cast.Clear();
            var segment = segments[segmentIndex];
            Text("simulation-position", $"{segmentIndex + 1} / {segments.Count}");
            Text("simulation-match", segment.Title);
            cards[segmentIndex].AddToClassList("current");
            foreach (var id in segment.Participants.Distinct())
            {
                if (screen.Q<Image>("simulation-sheet").image == null)
                {
                    cast.Add(WrestlerProfileController.CreatePortraitDisplayWithNameLink(save.Wrestlers.First(w => w.Id == id), "sim-wrestler"));
                    continue;
                }
                var wrestler = new VisualElement();
                wrestler.AddToClassList("sim-wrestler");
                var sprite = new Image
                {
                    image = screen.Q<Image>("simulation-sheet").image,
                    scaleMode = ScaleMode.StretchToFill,
                    uv = new Rect(0, 0, .25f, 1),
                    pickingMode = PickingMode.Ignore
                };
                if (sprite.image != null) sprite.image.filterMode = FilterMode.Point;
                sprite.AddToClassList("sim-sprite");
                wrestler.Add(sprite);
                var name = WrestlerProfileController.CreateLink(save.Wrestlers.FirstOrDefault(w => w?.Id == id));
                name.AddToClassList("sim-wrestler-name");
                wrestler.Add(name);
                cast.Add(wrestler);
                sprites.Add(id, sprite);
            }
            ResizeSprites();
            ShowLine();
            if (CurrentBeat != null && !IsHighlight(CurrentBeat)) AdvanceToHighlight(lineIndex + 1);
        }

        private void ShowLine()
        {
            var segment = segments[segmentIndex];
            var line = segment.Lines[lineIndex];
            var speaker = line.Type switch
            {
                NarrativeLineType.Dialogue => Name(line.SpeakerId),
                NarrativeLineType.Crowd => "관중",
                NarrativeLineType.Action => "현장",
                _ => "해설"
            };
            Text("simulation-speaker", speaker);
            Text("simulation-commentary", line.Text);
            var log = screen.Q<ScrollView>("simulation-log");
            var entry = WrestlerNameText.Create($"{speaker} · {line.Text}", save.Wrestlers);
            entry.AddToClassList("sim-log-line");
            log.Add(entry);
            log.schedule.Execute(() => { if (entry.panel != null) log.ScrollTo(entry); });
            foreach (var pair in sprites)
            {
                pair.Value.parent.EnableInClassList("active", pair.Key == CurrentBeat?.ActorId || pair.Key == line.SpeakerId);
            }
            UpdateClock();
        }

        private MatchSimulationBeatState CurrentBeat => segments[segmentIndex].Beats?[lineIndex];
        private float Duration => CurrentBeat?.DurationSeconds ?? Math.Max(4f, segments[segmentIndex].Lines[lineIndex].Text.Length / 5f);

        private static bool IsHighlight(MatchSimulationBeatState beat) =>
            beat.BeatType is MatchBeatType.PlannedSpot or MatchBeatType.NearFall or MatchBeatType.Finish ||
            beat.BeatType == MatchBeatType.EngineAction && beat.Importance >= 4;

        private static float AdvanceMatchClock(float current, float target, float delta, bool fastForward) =>
            Math.Min(Math.Max(current, target), current + Math.Max(0, delta) * (fastForward ? 120f : 1f));

        private void AdvanceToHighlight(int from)
        {
            var beats = segments[segmentIndex].Beats;
            nextHighlight = from;
            while (nextHighlight < beats.Count && !IsHighlight(beats[nextHighlight])) nextHighlight++;
            elapsed = 0;
            fastForwarding = true;
            foreach (var sprite in sprites.Values) sprite.parent.RemoveFromClassList("active");
        }

        private void Tick()
        {
            var now = Time.realtimeSinceStartup;
            var delta = Mathf.Min(now - lastTime, .25f);
            lastTime = now;
            if (paused || finished || disposed || ViewingProfile) return;
            animationElapsed += delta;
            var segment = segments[segmentIndex];
            if (segment.Beats != null)
            {
                var total = segment.Event.PlannedDuration * 60f;
                if (fastForwarding)
                {
                    var target = nextHighlight < segment.Beats.Count ? segment.Beats[nextHighlight].MatchProgress * total : total;
                    matchClock = AdvanceMatchClock(matchClock, target, delta * speed, true);
                    // Routine commentary follows the accelerated match clock; it never holds playback.
                    while (lineIndex + 1 < nextHighlight && segment.Beats[lineIndex + 1].MatchProgress * total <= matchClock)
                    {
                        lineIndex++;
                        ShowLine();
                    }
                    if (matchClock >= target)
                    {
                        fastForwarding = false;
                        if (nextHighlight >= segment.Beats.Count) { NextSegment(); return; }
                        lineIndex = nextHighlight;
                        ShowLine();
                    }
                }
                else
                {
                    matchClock = AdvanceMatchClock(matchClock, total, delta * speed, false);
                    elapsed += delta * speed;
                    if (elapsed >= Duration) AdvanceToHighlight(lineIndex + 1);
                }
            }
            else
            {
                elapsed += delta * speed;
                if (elapsed >= Duration)
                {
                    elapsed = 0;
                    if (++lineIndex >= segment.Lines.Count) { NextSegment(); return; }
                    ShowLine();
                }
            }
            Animate();
            UpdateClock();
        }

        private void Animate()
        {
            var nextFrame = (int)(animationElapsed * 4f) % 4;
            if (frame != nextFrame)
            {
                foreach (var sprite in sprites.Values)
                    sprite.uv = new Rect(nextFrame * .25f, 0, .25f, 1);
                frame = nextFrame;
            }
        }

        private void ResizeSprites()
        {
            var area = screen.Q("simulation-cast").contentRect;
            if (sprites.Count == 0 || area.width <= 0 || area.height <= 0 || float.IsNaN(area.width)) return;
            // Integer scaling keeps the supplied 32px idle frames square and pixel-aligned.
            var scale = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(area.width / sprites.Count * .8f, area.height * .75f) / 32f), 1, 3);
            foreach (var sprite in sprites.Values)
            {
                sprite.style.width = 32 * scale;
                sprite.style.height = 32 * scale;
            }
        }

        private void UpdateClock()
        {
            var segment = segments[segmentIndex];
            if (segment.Beats != null)
            {
                var clock = (int)matchClock;
                Text("simulation-clock", $"{(fastForwarding ? "▶▶ " : string.Empty)}{clock / 60:00}:{clock % 60:00}");
                return;
            }
            var progress = (float)lineIndex / segment.Lines.Count;
            var nextProgress = (float)(lineIndex + 1) / segment.Lines.Count;
            var seconds = (int)(segment.Event.PlannedDuration * 60 * Mathf.Lerp(progress, nextProgress, Mathf.Clamp01(elapsed / Duration)));
            Text("simulation-clock", $"{seconds / 60:00}:{seconds % 60:00}");
        }

        private void NextSegment()
        {
            if (finished) return;
            CompleteCard(segmentIndex);
            if (segmentIndex + 1 >= segments.Count) { Finish(); return; }
            segmentIndex++;
            ShowSegment();
        }

        private void CompleteCard(int index)
        {
            cards[index].RemoveFromClassList("current");
            cards[index].AddToClassList("done");
            WrestlerNameText.Set(cards[index], $"{segments[index].Title}\n{segments[index].Result}", save.Wrestlers);
        }

        private void Finish()
        {
            if (finished) return;
            finished = true;
            timer?.Pause();
            for (var i = 0; i < segments.Count; i++) CompleteCard(i);
            foreach (var id in new[] { "simulation-pause", "simulation-speed", "simulation-next", "simulation-end" }) Button(id).SetEnabled(false);
            Text("simulation-position", "쇼 종료");
            Text("simulation-match", "쇼 요약");
            Text("simulation-clock", "");
            Text("simulation-speaker", "해설");
            Text("simulation-commentary", $"오늘 준비된 순서가 모두 끝났습니다! 쇼 평점 ★ {result.ShowEvaluation?.CriticReview?.DisplayedStars:0.##} · 순손익 ${result.FinancialSettlement.NetIncome:N0}");
            screen.Q("simulation-cast").Clear();
            Button("simulation-return").RemoveFromClassList("sim-hidden");
            Button("simulation-return").Focus();
        }

        public bool ViewingProfile { get; private set; }

        public void ViewProfile(bool visible)
        {
            ViewingProfile = visible;
            screen.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
            dashboard?.EnableInClassList("hidden", !visible);
            lastTime = Time.realtimeSinceStartup;
        }

        private string Name(string id)
        {
            var wrestler = save.Wrestlers.FirstOrDefault(x => x?.Id == id);
            return wrestler == null ? "선수" : string.IsNullOrWhiteSpace(wrestler.Identity.RingName) ? wrestler.Identity.LegalName : wrestler.Identity.RingName;
        }

        private Button Button(string name) => screen.Q<Button>(name);
        private void Text(string name, string value) => WrestlerNameText.Set(screen.Q<Label>(name), value, save.Wrestlers);

        public void ShowSaveError(string message)
        {
            Text("simulation-speaker", "저장 실패");
            Text("simulation-commentary", $"결과를 저장하지 못했습니다. 다시 쇼 요약 확인을 눌러주세요.\n{message}");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            timer?.Pause();
            screen.RemoveFromHierarchy();
            dashboard?.RemoveFromClassList("hidden");
        }
    }
}
