using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;

public sealed class TopViewMatchRecording
{
    public string Key, Title, Group;
    public int Seed;
    public readonly Dictionary<string, string> Versions = new();

    public (GameSave save, MatchResultState result) Load(string version)
    {
        if (!Versions.TryGetValue(version, out var directory)) throw new ArgumentException("This engine has no recording for the selected match.");
        var save = JsonUtility.FromJson<GameSave>(File.ReadAllText(Path.Combine(directory, "input.json")));
        var result = JsonUtility.FromJson<MatchResultState>(File.ReadAllText(Path.Combine(directory, "result.json")));
        var state = result?.EngineState;
        if (state == null || !state.IsFinished || state.IsAborted || state.Events == null || state.Events.Count == 0 ||
            state.Participants.Any(p => !save.Wrestlers.Any(w => w.Id == p.Id)))
            throw new InvalidDataException("The recording has no complete match or wrestler records.");
        return (save, result);
    }
}

public static class TopViewMatchLibrary
{
    public const string Legacy = "기존 엔진", Previous = "개선 전 실험판", Improved = "개선 후 실험판";
    public const string Styles = "스타일 조합", Fixtures = "기존 사례";

    public static List<TopViewMatchRecording> Discover(string root, Func<string, string> styleName)
    {
        var records = new List<TopViewMatchRecording>();
        var stylesRoot = Path.Combine(root, "flow-improvement/final/styles");
        if (Directory.Exists(stylesRoot))
            foreach (var seedDirectory in Directory.GetDirectories(stylesRoot, "seed-*").OrderBy(x => x, StringComparer.Ordinal))
            {
                if (!int.TryParse(Path.GetFileName(seedDirectory).Substring(5), out var seed)) continue;
                foreach (var pair in Directory.GetDirectories(seedDirectory).OrderBy(x => x, StringComparer.Ordinal))
                {
                    var key = Path.GetFileName(pair);
                    var ids = key.Split(new[] { "-vs-" }, StringSplitOptions.None);
                    if (ids.Length != 2 || !int.TryParse(ids[0], out var face) || !int.TryParse(ids[1], out var heel)) continue;
                    var record = new TopViewMatchRecording { Key = $"styles/{seed}/{key}", Group = Styles, Seed = seed,
                        Title = $"{styleName($"style_{face:000}")}(선역) vs {styleName($"style_{heel:000}")}(악역)" };
                    Add(record, Legacy, Path.Combine(pair, "legacy"));
                    Add(record, Improved, Path.Combine(pair, "cooperative"));
                    if (seed == 20261017) Add(record, Previous, Path.Combine(root, "perfect-style-combinations", key, "cooperative"));
                    if (record.Versions.Count > 0) records.Add(record);
                }
            }
        var fixturesRoot = Path.Combine(root, "flow-improvement/final/fixtures");
        var names = new[] { "5분 균형 경기", "10분 테크니션 경기", "15분 낮은 스태미나 경기", "20분 베테랑 대 신인", "30분 장기전" };
        if (Directory.Exists(fixturesRoot))
            foreach (var directory in Directory.GetDirectories(fixturesRoot).OrderBy(x => x, StringComparer.Ordinal))
            {
                var key = Path.GetFileName(directory);
                if (!int.TryParse(key.Split('-')[0], out var number) || number < 1 || number > names.Length) continue;
                var record = new TopViewMatchRecording { Key = "fixtures/" + key, Group = Fixtures, Title = names[number - 1] };
                Add(record, Legacy, Path.Combine(directory, "legacy"));
                Add(record, Improved, Path.Combine(directory, "cooperative"));
                Add(record, Previous, Path.Combine(root, key, "cooperative"));
                if (record.Versions.Count > 0) records.Add(record);
            }
        return records;
    }

    private static void Add(TopViewMatchRecording record, string version, string directory)
    {
        if (File.Exists(Path.Combine(directory, "input.json")) && File.Exists(Path.Combine(directory, "result.json")))
            record.Versions.Add(version, directory);
    }

    public static (int start, int end) ExchangeRange(MatchEngineState state, int eventIndex)
    {
        if (state?.Events == null || eventIndex < 0 || eventIndex >= state.Events.Count) throw new ArgumentOutOfRangeException(nameof(eventIndex));
        int start = eventIndex, end = eventIndex;
        int id = state.Events[eventIndex].ExchangeId;
        if (id > 0)
        {
            while (start > 0 && state.Events[start - 1].ExchangeId == id) start--;
            while (end + 1 < state.Events.Count && state.Events[end + 1].ExchangeId == id) end++;
        }
        return (start, end);
    }
}
