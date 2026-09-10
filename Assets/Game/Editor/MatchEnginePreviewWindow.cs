using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Globalization;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Editor
{
    public sealed class MatchEnginePreviewWindow : EditorWindow
    {
        private StaticContentRegistry content;
        private GameSave save;
        private MatchResultState result;
        private IntegerField generationSeed, matchSeed, minutes;
        private DropdownField gender, winner, finish;
        private VisualElement cards;
        private ScrollView log;
        private Label status, summary;
        private Button run, play, next, end, export;
        private HelpBox error;
        private int eventIndex;
        private bool playing;
        private IVisualElementScheduledItem playback;
        private static readonly string[] FinishNames = { "핀폴", "서브미션", "롤업", "반칙", "카운트아웃", "무승부" };

        [MenuItem("PW Manager/Match Engine Preview")]
        public static void Open() => GetWindow<MatchEnginePreviewWindow>("Match Engine Preview");

        public void CreateGUI()
        {
            minSize = new Vector2(760, 600);
            playing = false;
            result = null;
            save = null;
            content = null;
            var root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = root.style.paddingRight = 12;
            root.style.paddingTop = root.style.paddingBottom = 12;
            root.style.unityFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Game/Presentation/Resources/PWManagerUI/Fonts/SEBANGGothic-Regular.ttf");
            var heading = new Label("싱글 매치 프리뷰");
            heading.style.fontSize = 20;
            heading.style.unityFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Game/Presentation/Resources/PWManagerUI/Fonts/SEBANGGothic-Bold.ttf");
            heading.style.marginBottom = 12;
            root.Add(heading);
            var generation = Row(root);
            generationSeed = new IntegerField("선수 시드") { value = 12345, name = "generation-seed" };
            gender = new DropdownField("성별", new List<string> { "남성", "여성" }, 0);
            generation.Add(generationSeed);
            generation.Add(gender);
            generation.Add(new Button(() => Attempt(GenerateWrestlers)) { text = "선수 두 명 생성", name = "generate" });
            cards = Row(root);
            cards.name = "wrestlers";
            cards.style.marginTop = cards.style.marginBottom = 8;
            var booking = Row(root);
            matchSeed = new IntegerField("경기 시드") { value = 73, name = "match-seed" };
            minutes = new IntegerField("시간(분)") { value = 10, name = "minutes" };
            booking.Add(matchSeed);
            booking.Add(minutes);
            var ending = Row(root);
            winner = new DropdownField("예정 승자", new List<string> { "선수 1", "선수 2" }, 0) { name = "winner" };
            finish = new DropdownField("피니시", FinishNames.ToList(), 0) { name = "finish" };
            finish.RegisterValueChangedCallback(_ => winner.SetEnabled(finish.index != (int)MatchFinishType.Draw));
            ending.Add(winner);
            ending.Add(finish);
            var controls = Row(root);
            run = new Button(() => Attempt(RunMatch)) { text = "경기 실행", name = "run" };
            play = new Button(() => { playing = !playing; UpdateControls(); }) { text = "재생", name = "play" };
            next = new Button(() => { playing = false; Advance(); }) { text = "다음 공방", name = "next" };
            end = new Button(() => { playing = false; while (result != null && eventIndex < result.EngineState.Events.Count) Advance(); })
                { text = "끝까지 보기", name = "end" };
            controls.Add(run);
            controls.Add(play);
            controls.Add(next);
            controls.Add(end);
            export = new Button(() => Attempt(() =>
            {
                playing = false;
                UpdateControls();
                var folder = EditorUtility.SaveFolderPanel("경기 로그 저장", Path.GetFullPath("artifacts"), string.Empty);
                if (string.IsNullOrEmpty(folder)) return;
                var saved = ExportLogs(folder);
                ShowNotification(new GUIContent("CSV · JSON 로그를 저장했습니다."));
                EditorUtility.RevealInFinder(saved);
            })) { text = "로그 저장", name = "export" };
            controls.Add(export);
            status = new Label("선수 두 명을 생성하면 경기를 실행할 수 있습니다.") { name = "status" };
            status.style.marginTop = status.style.marginBottom = 8;
            root.Add(status);
            error = new HelpBox(string.Empty, HelpBoxMessageType.Error);
            error.style.display = DisplayStyle.None;
            root.Add(error);
            log = new ScrollView { name = "match-log" };
            log.style.flexGrow = 1;
            log.style.minHeight = 100;
            root.Add(log);
            summary = new Label { name = "summary" };
            summary.style.whiteSpace = WhiteSpace.Normal;
            summary.style.marginTop = 8;
            root.Add(summary);
            playback?.Pause();
            playback = root.schedule.Execute(() => { if (playing) Advance(); }).Every(900);
            Attempt(() =>
            {
                var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>("Assets/Game/Data/Static/GameStaticContentCatalog.asset");
                if (catalog == null) throw new InvalidOperationException("선수 생성에 필요한 정적 데이터가 없습니다.");
                content = new StaticContentRegistry(catalog);
            });
            UpdateControls();
        }

        private static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 5;
            parent.Add(row);
            return row;
        }

        private void GenerateWrestlers()
        {
            if (content == null) throw new InvalidOperationException("정적 데이터를 불러오지 못했습니다. 창을 다시 열어주세요.");
            var date = new GameDate(2026, 6, 1);
            var id = 0;
            var generator = new WrestlerGenerator(content, generationSeed.value, () => $"preview-wrestler-{++id}");
            var generated = new GameSave { CurrentDate = date, WorldSeed = generationSeed.value };
            for (var i = 0; i < 2; i++) generated.Wrestlers.Add(generator.GenerateCandidate((WrestlerGender)gender.index, date));
            save = generated;
            ResetPlayback();
            cards.Clear();
            foreach (var wrestler in save.Wrestlers)
            {
                var card = new VisualElement();
                card.style.flexGrow = 1;
                card.style.flexBasis = 0;
                card.style.paddingRight = 12;
                var a = wrestler.Attributes;
                var title = new Label(wrestler.Identity.RingName);
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                card.Add(title);
                card.Add(new Label($"{content.Styles[wrestler.Presentation.WrestlingStyleId].DisplayName} · 종합 {WrestlerOverallCalculator.Match(wrestler):F1}"));
                card.Add(new Label($"타격 {a.Brawling:F1}  힘 {a.Power:F1}  테크닉 {a.Technical:F1}  공중기 {a.HighFlying:F1}"));
                card.Add(new Label($"셀링 {a.Selling:F1}  체력 {a.Stamina:F1}  경기 운영 {a.RingPsychology:F1}"));
                var moves = new Label($"시그니처: {Moves(wrestler.Presentation.SignatureMoveIds)}\n피니셔: {Moves(wrestler.Presentation.FinisherMoveIds)}");
                moves.style.whiteSpace = WhiteSpace.Normal;
                card.Add(moves);
                card.Query<Label>().ForEach(label => label.style.whiteSpace = WhiteSpace.Normal);
                cards.Add(card);
            }
            winner.choices = save.Wrestlers.Select((x, i) => $"{i + 1}. {x.Identity.RingName}").ToList();
            winner.index = 0;
            status.text = "경기 설정을 선택하고 실행하세요.";
            UpdateControls();
        }

        private string Moves(IEnumerable<string> ids) => string.Join(", ", ids.Select(id => content.Moves[id].DisplayName));

        private void RunMatch()
        {
            if (save == null) throw new InvalidOperationException("먼저 선수 두 명을 생성하세요.");
            if (minutes.value < 1 || minutes.value > 60) throw new InvalidOperationException("경기 시간은 1~60분으로 입력하세요.");
            ResetPlayback();
            var plan = new MatchPlanState
            {
                Id = "preview-match", MatchTypeId = "matchtype_001", FinishType = (MatchFinishType)finish.index,
                Sides = save.Wrestlers.Select((x, i) => new MatchSideState { Id = $"side-{i}", MemberIds = new List<string> { x.Id } }).ToList(),
                WinningSideId = $"side-{winner.index}", FinishPerformerId = save.Wrestlers[winner.index].Id,
                LoserTargetId = save.Wrestlers[1 - winner.index].Id
            };
            save.MatchPlans.Clear();
            save.MatchPlans.Add(plan);
            save.Shows.Clear();
            save.Shows.Add(new ShowState { Id = "preview-show", Date = save.CurrentDate, ShowVersion = 1, Status = ShowStatus.Confirmed });
            save.ShowEvents.Clear();
            save.ShowEvents.Add(new ShowEventState { Id = "preview-event", ShowId = "preview-show", DetailId = plan.Id,
                EventType = ShowEventType.Match, PlannedDuration = minutes.value });
            result = new MatchEvaluator(content, content, () => "preview-result", content.GetMoveRules)
                .EvaluateTechnical(save, "preview-event", 0f, matchSeed.value);
            save.MatchResults.Clear();
            save.MatchResults.Add(result);
            status.text = $"00:00 / {minutes.value:00}:00 · 경기 준비";
            UpdateControls();
        }

        private void Advance()
        {
            if (result == null || eventIndex >= result.EngineState.Events.Count) return;
            var value = result.EngineState.Events[eventIndex];
            var text = MatchNarrationService.NarrateEngineEvent(result, eventIndex, id => save.Wrestlers.Single(x => x.Id == id).Identity.RingName);
            var line = new VisualElement();
            line.style.marginBottom = 14;
            TableRow(line, true, "시각 / 소요", "State", "행동", "결과 / 충격");
            TableRow(line, false, $"{value.MatchTimeSeconds / 60:00}:{value.MatchTimeSeconds % 60:00} / {value.DurationSeconds}초",
                $"[{value.Before.Phase} → {value.After.Phase}]", value.Type.ToString(), $"{value.Result} / {value.Impact:F1}");
            var commentary = new Label(text);
            commentary.style.whiteSpace = WhiteSpace.Normal;
            commentary.style.marginBottom = 5;
            line.Add(commentary);
            TableRow(line, true, "선수", "피로", "주도권", "행동 준비도", "자세");
            foreach (var after in value.After.Participants)
            {
                var before = value.Before.Participants.Single(x => x.Id == after.Id);
                TableRow(line, false, save.Wrestlers.Single(x => x.Id == after.Id).Identity.RingName,
                    Change(before.Fatigue, after.Fatigue), Change(before.MatchControl, after.MatchControl),
                    Change(before.ActionReadiness, after.ActionReadiness),
                    $"{(before.IsDowned ? "다운" : "서 있음")} → {(after.IsDowned ? "다운" : "서 있음")}");
            }
            TableRow(line, true, "경기", "강도", "긴장감", "관중 열기");
            TableRow(line, false, "변화 전 → 후 (증감)", Change(value.Before.Intensity, value.After.Intensity),
                Change(value.Before.Drama, value.After.Drama), Change(value.Before.CrowdHeat, value.After.CrowdHeat));
            log.Add(line);
            log.schedule.Execute(() => log.scrollOffset = new Vector2(0, log.verticalScroller.highValue));
            eventIndex++;
            var elapsed = value.MatchTimeSeconds + value.DurationSeconds;
            status.text = $"{elapsed / 60:00}:{elapsed % 60:00} · 공방 {eventIndex}/{result.EngineState.Events.Count}";
            if (eventIndex == result.EngineState.Events.Count)
            {
                playing = false;
                summary.text = $"경기 평가 {result.FinalMatchQuality:F2}/20 · {FinishNames[(int)result.FinishType]}\n" +
                    string.Join("  |  ", result.EngineState.Participants.Select(x =>
                        $"{save.Wrestlers.Single(w => w.Id == x.Id).Identity.RingName}: 최종 피로 {x.Fatigue:F1}, 주도권 {x.MatchControl:F1}"));
            }
            UpdateControls();
        }

        private void ResetPlayback()
        {
            playing = false;
            result = null;
            eventIndex = 0;
            log.Clear();
            summary.text = string.Empty;
        }

        private static string Change(float before, float after) => $"{before:F1} → {after:F1}\n({after - before:+0.0;-0.0;0.0})";

        private static void TableRow(VisualElement parent, bool header, params string[] values)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            if (header) row.style.backgroundColor = new Color(.5f, .5f, .5f, .15f);
            foreach (var value in values)
            {
                var cell = new Label(value);
                cell.style.flexGrow = 1;
                cell.style.flexBasis = 0;
                cell.style.minWidth = 0;
                cell.style.whiteSpace = WhiteSpace.Normal;
                cell.style.paddingLeft = cell.style.paddingRight = 5;
                cell.style.paddingTop = cell.style.paddingBottom = 4;
                cell.style.borderBottomWidth = cell.style.borderRightWidth = 1;
                cell.style.borderBottomColor = cell.style.borderRightColor = new Color(.5f, .5f, .5f, .3f);
                if (header) cell.style.unityFontStyleAndWeight = FontStyle.Bold;
                row.Add(cell);
            }
            parent.Add(row);
        }

        private void UpdateControls()
        {
            run.SetEnabled(save != null);
            export.SetEnabled(result?.EngineState?.IsFinished == true);
            var active = result != null && eventIndex < result.EngineState.Events.Count;
            play.SetEnabled(active);
            next.SetEnabled(active);
            end.SetEnabled(active);
            play.text = playing ? "일시정지" : "재생";
            foreach (var row in rootVisualElement.Children())
                if (row.style.flexDirection.value == FlexDirection.Row)
                    foreach (var child in row.Children()) { child.style.flexGrow = 1; child.style.flexBasis = 0; child.style.minWidth = 0; }
        }

        [Serializable]
        private sealed class PreviewLog
        {
            public int GenerationSeed;
            public List<WrestlerState> Wrestlers;
            public MatchPlanState Plan;
            public MatchResultState Match;
            public List<string> Commentary;
        }

        private string ExportLogs(string folder)
        {
            if (result?.EngineState?.IsFinished != true) throw new InvalidOperationException("먼저 경기를 실행하세요.");
            string Name(string id) => save.Wrestlers.FirstOrDefault(x => x.Id == id)?.Identity.RingName ?? string.Empty;
            var commentary = result.EngineState.Events.Select((_, i) => MatchNarrationService.NarrateEngineEvent(result, i, Name)).ToList();
            var json = JsonUtility.ToJson(new PreviewLog
            {
                GenerationSeed = save.WorldSeed, Wrestlers = save.Wrestlers, Plan = save.MatchPlans.Single(),
                Match = result, Commentary = commentary
            }, true);
            var csv = new StringBuilder();
            void Row(params object[] cells) => csv.AppendLine(string.Join(",", cells.Select(cell =>
            {
                var value = Convert.ToString(cell, CultureInfo.InvariantCulture) ?? string.Empty;
                // Spreadsheet applications must treat names and commentary as text, never formulas.
                if (cell is string && value.Length > 0 && "=+-@\t\r\n".Contains(value[0])) value = "'" + value;
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            })));
            Row("공방", "시각(초)", "소요(초)", "State 이전", "State 이후", "행동", "결과", "행위자 ID", "행위자", "대상 ID", "대상", "기술 ID", "충격",
                "선수 ID", "선수", "피로 이전", "피로 이후", "피로 증감", "주도권 이전", "주도권 이후", "주도권 증감",
                "준비도 이전", "준비도 이후", "준비도 증감", "다운 이전", "다운 이후", "강도 이전", "강도 이후", "강도 증감",
                "긴장감 이전", "긴장감 이후", "긴장감 증감", "열기 이전", "열기 이후", "열기 증감", "중계");
            for (var i = 0; i < result.EngineState.Events.Count; i++)
            {
                var value = result.EngineState.Events[i];
                foreach (var after in value.After.Participants)
                {
                    var before = value.Before.Participants.Single(x => x.Id == after.Id);
                    Row(i + 1, value.MatchTimeSeconds, value.DurationSeconds, value.Before.Phase, value.After.Phase, value.Type, value.Result,
                        value.PerformerId, Name(value.PerformerId), value.ReceiverId, Name(value.ReceiverId), value.MoveId, value.Impact,
                        after.Id, Name(after.Id), before.Fatigue, after.Fatigue, after.Fatigue - before.Fatigue,
                        before.MatchControl, after.MatchControl, after.MatchControl - before.MatchControl,
                        before.ActionReadiness, after.ActionReadiness, after.ActionReadiness - before.ActionReadiness,
                        before.IsDowned, after.IsDowned, value.Before.Intensity, value.After.Intensity, value.After.Intensity - value.Before.Intensity,
                        value.Before.Drama, value.After.Drama, value.After.Drama - value.Before.Drama,
                        value.Before.CrowdHeat, value.After.CrowdHeat, value.After.CrowdHeat - value.Before.CrowdHeat, commentary[i]);
                }
            }
            var directory = Path.Combine(folder, $"match-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "match.json"), json, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "events.csv"), csv.ToString(), new UTF8Encoding(true));
            return directory;
        }

        private void Attempt(Action action)
        {
            try { error.style.display = DisplayStyle.None; action(); }
            catch (Exception exception) { playing = false; error.text = exception.Message; error.style.display = DisplayStyle.Flex; UpdateControls(); }
        }
    }
}
