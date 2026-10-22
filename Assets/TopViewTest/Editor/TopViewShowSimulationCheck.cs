using System;
using System.Linq;
using System.Reflection;
using PWManager.Domain.Models;
using PWManager.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

public static class TopViewShowSimulationCheck
{
    public static string Check()
    {
        var arena = UnityEngine.Object.FindAnyObjectByType<TopViewArenaTest>();
        if (!Application.isPlaying || !arena || arena.gameObject.scene.name != "TopView Show Simulation")
            throw new InvalidOperationException("Run TopView Show Simulation first.");
        T Field<T>(string name) => (T)typeof(TopViewArenaTest).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(arena);
        var save = Field<GameSave>("showSimulationSave");
        var result = Field<ShowResultState>("showSimulationResult");
        var root = arena.GetComponent<UIDocument>().rootVisualElement;
        void Require(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
        void Click(string name)
        {
            var button = root.Q<Button>(name);
            Require(button != null && button.enabledInHierarchy, "Control missing or disabled: " + name);
            typeof(Clickable).GetMethod("SimulateSingleClick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Invoke(button.clickable, new object[] { null, 0 });
        }
        Require(save != null && DashboardSession.ActiveSave == save && DashboardSession.IsTransient, "Preview does not own a transient save.");
        Require(result.TimelineResultIds.Count == 3 && save.MatchResults.Count == 2 && save.PromoResults.Count == 1, "Show execution results are incomplete.");
        arena.ShowSimulationSegment(0);
        var technical = new PWManager.Domain.Services.MatchEngineEvent { Type = PWManager.Domain.Services.MatchActionType.LimbAttack, ExecutionQuality = 1 };
        var aerial = new PWManager.Domain.Services.MatchEngineEvent { Type = PWManager.Domain.Services.MatchActionType.Dive, ExecutionQuality = 1 };
        Require(TopViewFanCrowd.Affinity(technical, arena.Wrestlers[0], 2) > TopViewFanCrowd.Affinity(technical, arena.Wrestlers[0], 1), "Technical wrestling does not favor the hardcore audience.");
        Require(TopViewFanCrowd.Affinity(aerial, arena.Wrestlers[0], 1) > TopViewFanCrowd.Affinity(aerial, arena.Wrestlers[0], 2), "Spectacle does not favor the casual audience.");
        var fanFrames = Field<TopViewFanCrowdFrame[]>("showFanFrames");
        Require(fanFrames.Any(f => f.Heat.Max() - f.Heat.Min() > 5), "Fan groups never diverge during the real match.");
        Require(Field<int[]>("showFanCounts").Sum() == arena.crowdPanels.Length, "Audience composition lost panels.");
        Require(fanFrames.All(f => Enumerable.Range(0, 3).All(g => f.Support[g, 0] > 0 && f.Support[g, 1] > 0 &&
            Mathf.Abs(f.Support[g, 0] + f.Support[g, 1] - 1) < .001f)), "Audience support is not shared between both wrestlers.");
        var mixed = new TopViewFanCrowdFrame();
        for (int g = 0; g < 3; g++) { mixed.Heat[g] = 60; mixed.Focus[g] = 0; mixed.Support[g, 0] = .55f; mixed.Support[g, 1] = .45f; }
        typeof(TopViewArenaTest).GetMethod("AnimateShowFanCrowd", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(arena, new object[] { mixed });
        var mixedFill = new MaterialPropertyBlock(); bool hasBlue = false, hasRed = false;
        foreach (var panel in arena.crowdPanels)
        {
            panel.GetPropertyBlock(mixedFill); var tint = mixedFill.GetColor("_BaseColor");
            hasBlue |= tint.b - tint.r > .04f; hasRed |= tint.r - tint.b > .04f;
        }
        Require(hasBlue && hasRed, "Blue and red audience panels are not visible simultaneously.");
        Require(root.Query<ProgressBar>().ToList().All(bar => string.IsNullOrEmpty(bar.title)), "A gauge caption remains inside a bar.");
        Require(!root.Q<ProfileMetricGauge>("show-condition-0").Q<Label>().text.Contains("컨디션"), "Condition caption remains inside the circle.");
        Require(save.Wrestlers.All(w => w.Presentation.Appearance.Part("eyewear") == 1 && w.Presentation.Appearance.Part("headwear") == 1 &&
            w.Presentation.Appearance.Part("beard") == 1 && w.Presentation.Appearance.Part("mask") == 1), "Preview appearance contains extravagant accessories.");
        Require(!save.Wrestlers.Any(w => new[] { "Gunther", "Seth Rollins", "Cody Rhodes", "Roman Reigns" }.Contains(w.Identity.RingName)), "Real-world wrestler names remain.");
        Require(!root.Query<Label>().ToList().Any(l => l.text == "부위별 손상"), "Old damage caption remains.");
        arena.Seek(arena.Engine.ElapsedSeconds * .45f);
        var snapshot = arena.clock >= arena.CurrentEvent.MatchTimeSeconds + arena.CurrentEvent.DurationSeconds * .65f ? arena.CurrentEvent.After : arena.CurrentEvent.Before;
        for (int i = 0; i < 2; i++)
        {
            float expected = Mathf.Clamp(100 - snapshot.Participants.Single(p => p.Id == arena.Engine.Participants[i].Id).Fatigue, 0, 100);
            Require(Mathf.Abs(root.Q<ProfileMetricGauge>("show-condition-" + i).Value - expected) < .01f, "Live condition gauge does not follow fatigue.");
        }
        Require(Field<Label[]>("stateLabels").All(l => !l.text.Contains("·") && !l.text.Contains("누움")), "Pose label contains location or old wording.");
        var animate = typeof(TopViewArenaTest).GetMethod("AnimateCrowdPanels", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(Color) }, null);
        var motion = typeof(TopViewArenaTest).GetField("crowdMotionTime", BindingFlags.Instance | BindingFlags.NonPublic);
        var accent = typeof(TopViewArenaTest).GetField("crowdAccent", BindingFlags.Instance | BindingFlags.NonPublic);
        accent.SetValue(arena, 0f);
        var fill = new MaterialPropertyBlock();
        motion.SetValue(arena, 0f); animate.Invoke(arena, new object[] { 10, Color.red });
        var position = arena.crowdPanels[0].transform.position;
        arena.crowdPanels[0].GetPropertyBlock(fill); var color = fill.GetColor("_BaseColor");
        motion.SetValue(arena, .2f); animate.Invoke(arena, new object[] { 10, Color.red });
        arena.crowdPanels[0].GetPropertyBlock(fill);
        Require(Vector3.Distance(position, arena.crowdPanels[0].transform.position) > .01f &&
            Vector4.Distance(color, fill.GetColor("_BaseColor")) > .01f, "Audience does not bounce and blink.");
        float routineBrightness = fill.GetColor("_BaseColor").r;
        accent.SetValue(arena, 1f); animate.Invoke(arena, new object[] { 10, Color.red });
        arena.crowdPanels[0].GetPropertyBlock(fill);
        Require(routineBrightness < .3f && fill.GetColor("_BaseColor").r > routineBrightness + .3f, "Routine audience is too bright or major reactions lack an accent.");
        accent.SetValue(arena, 0f);
        arena.Seek(0);
        Require(root.Q<Image>("show-portrait-0").image != null && root.Q<Image>("show-portrait-1").image != null, "Wrestler portraits are missing.");
        Click("show-name-0"); Require(arena.ViewingProfile, "Wrestler name did not open the profile.");
        WrestlerProfileController.CloseOpenProfile(false);
        Require(!arena.ViewingProfile, "Wrestler profile did not close.");
        bool paused = arena.paused;
        Click("show-pause"); Require(arena.paused != paused, "Pause button did not toggle playback.");
        Click("show-pause"); Require(arena.paused == paused, "Resume button did not toggle playback.");
        root.Q<DropdownField>("show-speed").value = "4배속";
        Require(arena.playbackSpeed == 4, "Playback speed did not change.");
        root.Q<Slider>("show-timeline").value = arena.Engine.ElapsedSeconds * .3f;
        Require(arena.paused && Mathf.Abs(arena.clock - arena.Engine.ElapsedSeconds * .3f) < .01f, "Timeline did not seek and pause.");
        float clock = arena.clock; Click("show-highlight");
        Require(arena.clock > clock, "Highlight navigation did not advance.");
        Click("show-next"); Require(Field<bool>("showPromo") && arena.Wrestlers.All(w => save.Wrestlers.Contains(w)), "Promo did not load its actual cast.");
        Require(!arena.blue.gameObject.activeSelf && !arena.red.gameObject.activeSelf, "Previous match chips remained in the promo.");
        Click("show-next"); Require(!Field<bool>("showPromo") && arena.Engine == save.MatchResults[1].EngineState, "Main event did not load the executed match.");
        Require(arena.blue.gameObject.activeSelf && arena.red.gameObject.activeSelf, "Main event chips did not return.");
        Require(save.Wrestlers.Count == 105, "Segment change discarded the full show roster.");
        Click("show-finish"); Require(Field<bool>("showSummary") && root.Q<Button>("show-apply") != null, "Show summary did not open.");
        Click("show-apply"); Require(save.Shows[0].Status == ShowStatus.Completed && !root.Q<Button>("show-apply").enabledSelf, "Show result was not settled exactly once.");
        Click("show-replay"); Require(!Field<bool>("showSummary") && Field<int>("showSegment") == 0, "Replay did not return to the opening match.");
        arena.paused = true;
        return "PASS: actual show execution (2 matches + promo), transient full roster, portraits and profile links, pause/resume, speed, timeline, highlights, segment changes, summary, settlement and replay.";
    }
}
