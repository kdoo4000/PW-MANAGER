using System;
using System.IO;
using System.Linq;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using UnityEditor;
using UnityEngine;

public static class MatchMultiPlaybackCheck
{
    [MenuItem("PWManager/Top View/Check Multi Match Playback")]
    public static string Check()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run the arena first.");
        MoveTable.Initialize();
        var arena = UnityEngine.Object.FindFirstObjectByType<TopViewArenaTest>();
        var original = arena.Engine; var wrestlers = arena.Wrestlers; float clock = arena.clock; bool paused = arena.paused;
        int sampled = 0;
        try
        {
            arena.paused = true;
            for (int index = 29; index <= 32; index++)
            {
                var save = JsonUtility.FromJson<GameSave>(File.ReadAllText($"artifacts/match-scenario-audit-20261008/case-{index:00}/replay-save.json"));
                var engine = save.MatchResults.Single().EngineState;
                typeof(TopViewArenaTest).GetProperty("Wrestlers").SetValue(arena, engine.Participants.Select(p => save.Wrestlers.Single(w => w.Id == p.Id)).ToArray());
                arena.LoadMatch(engine);
                TopViewArenaTestBuilder.CheckMatchPlayback();
                TopViewArenaTestBuilder.CheckWrestlerViewer();
                sampled += engine.Events.Count;
            }
            return $"4 multi matches, {sampled} events passed.";
        }
        finally
        {
            typeof(TopViewArenaTest).GetProperty("Wrestlers").SetValue(arena, wrestlers);
            arena.LoadMatch(original); arena.Seek(clock); arena.paused = paused;
        }
    }
}
