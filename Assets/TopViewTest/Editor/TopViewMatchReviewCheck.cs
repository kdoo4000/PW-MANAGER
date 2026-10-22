using System;
using System.IO;
using System.Reflection;
using PWManager.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class TopViewMatchReviewCheck
{
    [MenuItem("PWManager/Top View/Check Match Review")]
    public static void RunMenu() => Debug.Log(Check());

    public static string Check(string outputDirectory = null)
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Play the TopView Arena Test scene first.");
        var arena = UnityEngine.Object.FindAnyObjectByType<TopViewArenaTest>();
        var root = arena.GetComponent<UIDocument>().rootVisualElement;
        if (root.Q("match-review-controls") == null) throw new InvalidOperationException("Match-by-match playback controls are missing.");
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        void Click(string name)
        {
            var button = root.Q<Button>(name);
            Require(button != null && button.enabledInHierarchy, "Unavailable control: " + name);
            typeof(Clickable).GetMethod("SimulateSingleClick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Invoke(button.clickable, new object[] { null, 0 });
        }
        var engine = root.Q<DropdownField>("review-engine");
        Require(engine != null && engine.choices.Contains(TopViewMatchLibrary.Previous), "The reference match must expose all three recordings.");
        foreach (var version in new[] { TopViewMatchLibrary.Legacy, TopViewMatchLibrary.Previous, TopViewMatchLibrary.Improved })
        {
            engine.value = version;
            Require(arena.paused && arena.clock == 0 && arena.Engine != null, "Switching engines must reset and pause the replay.");
            Require(arena.Engine.EngineVersion == (version == TopViewMatchLibrary.Legacy ? 4 : version == TopViewMatchLibrary.Previous ? 5 : 6),
                "The displayed engine does not match its recording.");
        }
        Click("review-play"); Require(!arena.paused, "Play did not start.");
        Click("review-play"); Require(arena.paused, "Pause did not stop.");
        Click("review-next-exchange"); Require(arena.clock > 0 && arena.paused, "Next exchange did not move and pause.");
        Click("review-previous-exchange"); Require(arena.clock == 0, "Previous exchange did not return to its beginning.");
        var first = TopViewMatchLibrary.ExchangeRange(arena.Engine, arena.EventIndex);
        var last = arena.Engine.Events[first.end];
        Click("review-play-exchange");
        typeof(TopViewArenaTest).GetMethod("AdvancePlayback").Invoke(arena, new object[] { 60f });
        Require(arena.paused && arena.clock < last.MatchTimeSeconds + last.DurationSeconds && arena.clock > 0,
            "Playing one exchange must stop before entering the following exchange.");
        var timeline = root.Q<Slider>("review-timeline");
        timeline.value = 120;
        Require(Math.Abs(arena.clock - 120) < .001 && arena.paused, "Timeline seeking did not pause at the selected time.");
        root.Q<DropdownField>("review-speed").value = "0.5배";
        Require(Math.Abs(arena.playbackSpeed - .5f) < .001, "Playback speed did not change.");
        root.Q<DropdownField>("review-speed").value = "1배";
        var previous = arena.Engine;
        Click("review-next-match");
        Require(!ReferenceEquals(previous, arena.Engine) && arena.clock == 0 && arena.paused, "Next match reused the previous replay.");
        Click("review-previous-match");
        var group = root.Q<DropdownField>("review-group");
        group.value = TopViewMatchLibrary.Fixtures;
        Require(root.Q<DropdownField>("review-match").choices.Count == 5, "The original five fixtures are missing.");
        group.value = TopViewMatchLibrary.Styles;
        var seed = root.Q<DropdownField>("review-seed");
        seed.value = "20261018";
        Require(!engine.choices.Contains(TopViewMatchLibrary.Previous), "An unrecorded engine version must not be offered.");
        seed.value = "20261017";
        Require(root.Q<DropdownField>("review-match").choices.Count == 49, "All face/heel style placements must be selectable.");
        var match = root.Q<DropdownField>("review-match");
        match.index = 3;
        engine.value = TopViewMatchLibrary.Improved;
        Click("review-restart");
        Require(arena.clock == 0 && arena.paused, "Restart must reset the selected recording and pause.");
        Click("wrestler-name-0");
        Require(arena.ViewingProfile, "Wrestler names must open their profile.");
        WrestlerProfileController.CloseOpenProfile(false);
        var camera = root.Q<Toggle>("review-camera");
        camera.value = false; Require(!arena.automaticCamera, "Manual camera was not selected.");
        camera.value = true; Require(arena.automaticCamera, "Automatic camera was not restored.");
        timeline.value = arena.Engine.ElapsedSeconds;
        previous = arena.Engine;
        Require(arena.Finished && arena.paused && ReferenceEquals(previous, arena.Engine), "A completed match must stay selected and paused.");
        Click("review-restart");
        var report = "Engine switching, recorded rosters, profiles, play/pause, exchange navigation, one-exchange playback, timeline, speed, match navigation, groups, seeds, camera and match completion passed.";
        if (outputDirectory != null)
        {
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(Path.Combine(outputDirectory, "playback-check.txt"), report);
        }
        return report;
    }

    public static string CheckLayout(string outputDirectory = null)
    {
        var arena = UnityEngine.Object.FindAnyObjectByType<TopViewArenaTest>();
        var root = arena.GetComponent<UIDocument>().rootVisualElement;
        var controls = root.Q("match-review-controls").worldBound;
        var commentary = root.Q("match-commentary").worldBound;
        var states = root.Q("wrestler-states").worldBound;
        if (!(controls.width > 0 && controls.height > 0 && commentary.yMax <= controls.yMin + 1 &&
            controls.xMin >= 0 && controls.xMax <= root.worldBound.width + 1 &&
            states.yMax < commentary.yMin && arena.viewCamera.rect.height > 0))
            throw new InvalidOperationException("Playback controls, commentary or arena viewport overlap.");
        foreach (var element in root.Q("match-review-controls").Query<Button>().ToList())
            if (element.worldBound.xMax > controls.xMax + 1 || element.worldBound.yMax > controls.yMax + 1)
                throw new InvalidOperationException("A playback button overflows its container: " + element.name);
        var report = $"Layout passed at {Screen.width}x{Screen.height}: controls {controls}, commentary {commentary}, camera {arena.viewCamera.rect}.";
        if (outputDirectory != null) File.WriteAllText(Path.Combine(outputDirectory, $"layout-{Screen.width}x{Screen.height}.txt"), report);
        return report;
    }
}
