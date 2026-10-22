using System;
using System.Linq;
using PWManager.Data.Loading;
using PWManager.Domain.Services;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

public static class MovePreviewSceneBuilder
{
    public const string ScenePath = "Assets/TopViewTest/Move Preview.unity";

    [MenuItem("PWManager/Top View/Create Move Preview")]
    public static void Create()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save the open scene first.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
            throw new InvalidOperationException("The preview already exists; open it instead.");
        var scene = EditorSceneManager.OpenScene("Assets/TopViewTest/TopView Arena Test.unity");
        var arena = UnityEngine.Object.FindFirstObjectByType<TopViewArenaTest>();
        arena.enabled = false;
        var preview = arena.gameObject.AddComponent<MovePreviewScene>(); preview.arena = arena;
        arena.gameObject.name = "Move Preview";
        arena.viewCamera.transform.position = new Vector3(arena.ringCenter.x, 45, arena.ringCenter.z);
        arena.viewCamera.orthographicSize = 5;
        arena.viewCamera.rect = new Rect(.32f, .15f, .68f, .63f);
        foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t && (t.name == "Referee" || t.name == "Commentator")) UnityEngine.Object.DestroyImmediate(t.gameObject);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets(); Selection.activeGameObject = preview.gameObject;
    }

    [MenuItem("PWManager/Top View/Check Move Preview")]
    public static string Check()
    {
        var moves = MoveTable.Load(); int sampled = 0;
        foreach (var move in moves.Where(MovePreviewScene.CanPreview))
        {
            var state = MovePreviewScene.BuildPreview(move); var e = state.Events.Single();
            if (e.TechniqueId != move.Id || e.Before.Participants.Count != 2 || e.Motion.Count < 5)
                throw new InvalidOperationException("Missing technique or motion: " + move.Id);
            var a = state.Participants[0]; var b = state.Participants[1];
            if (move.Rules.ExecutionStyle == MoveExecutionStyle.Running && move.Rules.Situation != MoveSituation.CornerStanding &&
                e.Before.Participants[0].RingX > -.85f)
                throw new InvalidOperationException("Running preview started without a run-up: " + move.Id);
            if (!MatchTechniques.FitsSituation(move.Rules.Situation, Before(e, a.Id), Before(e, b.Id)))
                throw new InvalidOperationException("Incorrect starting posture: " + move.Id);
            foreach (float progress in new[] { 0f, .3f, .42f, .5f, .65f, 1f })
                foreach (var p in state.Participants)
                {
                    var pose = MatchMovement.Sample(e, p.Id, progress);
                    if (!float.IsFinite(pose.X + pose.Z + pose.Height) || Math.Max(Math.Abs(pose.X), Math.Abs(pose.Z)) > 1.4f)
                        throw new InvalidOperationException("Invalid preview pose: " + move.Id);
                    sampled++;
                }
            if (move.Rules.Situation is MoveSituation.DivingStanding or MoveSituation.DivingGrounded && e.Before.Participants[0].Height <= 0)
                throw new InvalidOperationException("Aerial preview did not start on the ropes.");
        }
        if (Application.isPlaying) CheckControls();
        return $"{moves.Count(MovePreviewScene.CanPreview)} techniques, {sampled} poses passed; controls: {Application.isPlaying}";
    }

    private static MatchEngineParticipant Before(MatchEngineEvent e, string id)
    {
        var p = e.Before.Participants.Single(x => x.Id == id);
        return new MatchEngineParticipant { Id = id, Posture = p.Posture, IsDowned = p.IsDowned,
            Position = p.Position, RingX = p.RingX, RingZ = p.RingZ, FacingX = p.FacingX, FacingZ = p.FacingZ };
    }

    private static void CheckControls()
    {
        var preview = UnityEngine.Object.FindFirstObjectByType<MovePreviewScene>();
        var root = preview.GetComponent<UIDocument>().rootVisualElement;
        var list = root.Q<ListView>("preview-moves");
        var search = root.Q<TextField>("preview-search");
        var situation = root.Q<DropdownField>("preview-situation");
        var slider = root.Q<Slider>("preview-timeline");
        var shape = root.Q<DropdownField>("preview-shape");
        var target = root.Q<LimbDamageIndicator>("preview-target-body");
        if (list.Query<Label>().ToList().Any(l => l.text.Length > 0 && l.resolvedStyle.unityTextAlign != TextAnchor.MiddleLeft))
            throw new InvalidOperationException("Move names are not vertically centered.");
        shape.index = 0;
        var click = typeof(Clickable).GetMethod("SimulateSingleClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        search.value = "수플렉스";
        if (list.itemsSource.Count == 0 || list.itemsSource.Cast<MatchTechnique>().Any(m => !m.Rules.Name.Contains("수플렉스")))
            throw new InvalidOperationException("Search did not filter techniques.");
        search.value = "Crossface";
        if (list.itemsSource.Count != 1 || preview.Selected.Key != "crossface") throw new InvalidOperationException("English search failed.");
        search.value = "no-such-move-123";
        if (list.itemsSource.Count != 0 || preview.Selected != null || slider.enabledSelf)
            throw new InvalidOperationException("Empty search retained an unrelated preview.");
        search.value = ""; situation.value = MovePreviewScene.SituationName(MoveSituation.DivingGrounded);
        if (list.itemsSource.Count == 0 || list.itemsSource.Cast<MatchTechnique>().Any(m => m.Rules.Situation != MoveSituation.DivingGrounded))
            throw new InvalidOperationException("Situation filter failed.");
        situation.index = 0;
        foreach (var move in list.itemsSource.Cast<MatchTechnique>().ToArray())
        {
            list.SetSelection(list.itemsSource.IndexOf(move));
            if (preview.Selected.Id != move.Id || slider.enabledSelf != MovePreviewScene.CanPreview(move))
                throw new InvalidOperationException("Selection failed: " + move.Id);
            var expected = Vector4.zero;
            if (move.Action is not (MatchActionType.Probe or MatchActionType.TieUp or MatchActionType.Chain))
                expected[(int)move.Rules.TargetRegion] = 100;
            if (target.Damage != expected) throw new InvalidOperationException("Preview highlighted the wrong target region: " + move.Id);
            if (!MovePreviewScene.CanPreview(move)) continue;
            slider.value = .42f;
            if (!preview.Paused || Math.Abs(preview.arena.clock - 2.52f) > .001f) throw new InvalidOperationException("Scrubbing failed.");
            foreach (var marker in new[] { preview.arena.blue, preview.arena.red })
                if (Math.Abs(marker.localScale.x - marker.localScale.z) > .001f || marker.localScale.y > .1f)
                    throw new InvalidOperationException("A chip stopped being circular: " + move.Id);
            foreach (float progress in new[] { .65f, .7f, .82f, 1f })
            {
                slider.value = progress;
                foreach (var marker in new[] { preview.arena.blue, preview.arena.red })
                {
                    var offset = marker.position - preview.arena.ringCenter;
                    float floor = Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.z)) > preview.arena.halfRing ? 0 : preview.arena.ringCenter.y;
                    if (Math.Abs(marker.localScale.x - marker.localScale.z) > .001f || marker.localScale.y > .1f ||
                        marker.GetComponent<MeshFilter>().sharedMesh.vertices.Any(v => marker.TransformPoint(v).y < floor))
                        throw new InvalidOperationException("General round-chip rebound deformed or crossed the floor: " + move.Id);
                }
            }
        }
        foreach (string key in new[] { "body-slam", "standing-powerbomb", "snapmare", "diving-crossbody" })
        {
            preview.Select(MoveTable.Load().Single(m => m.Key == key)); slider.value = .7f;
            var e = preview.arena.CurrentEvent;
            var receiver = MatchMovement.Sample(e, e.ReceiverId, .7f);
            var original = preview.arena.ringCenter + new Vector3(receiver.X * preview.arena.halfRing, 0, receiver.Z * preview.arena.halfRing);
            if (Vector3.ProjectOnPlane(preview.arena.red.position - original, Vector3.up).magnitude < .02f)
                throw new InvalidOperationException("Technique did not receive a visible landing response: " + key);
            var bounced = preview.arena.red.position;
            slider.value = 1; receiver = MatchMovement.Sample(e, e.ReceiverId, 1);
            original = preview.arena.ringCenter + new Vector3(receiver.X * preview.arena.halfRing, 0, receiver.Z * preview.arena.halfRing);
            if (Vector3.ProjectOnPlane(preview.arena.red.position - original, Vector3.up).magnitude > .001f)
                throw new InvalidOperationException("Technique rebound changed its recorded endpoint: " + key);
            slider.value = .7f;
            if (preview.arena.red.position != bounced) throw new InvalidOperationException("Technique rebound changed on seeking: " + key);
        }
        foreach (string key in new[] { "circling", "bearhug", "arm-wringer" })
        {
            preview.Select(MoveTable.Load().Single(m => m.Key == key)); slider.value = .7f;
            var e = preview.arena.CurrentEvent;
            var receiver = MatchMovement.Sample(e, e.ReceiverId, .7f);
            var original = preview.arena.ringCenter + new Vector3(receiver.X * preview.arena.halfRing, 0, receiver.Z * preview.arena.halfRing);
            if (Vector3.ProjectOnPlane(preview.arena.red.position - original, Vector3.up).magnitude > .001f)
                throw new InvalidOperationException("A control technique received a false landing bounce: " + key);
        }
        foreach (var move in MoveTable.Load().Where(m => MovePreviewScene.CanPreview(m) && m.Rules.Kind == MoveKind.Strike))
        {
            preview.Select(move); slider.value = .65f;
            if (!TopViewArenaTest.IsHit(preview.arena.CurrentEvent)) continue;
            float distance = Vector3.ProjectOnPlane(preview.arena.blue.position - preview.arena.red.position, Vector3.up).magnitude;
            if (distance > (preview.arena.blue.localScale.x + preview.arena.red.localScale.x) * .5f + .001f)
                throw new InvalidOperationException("Successful strike did not touch the receiver: " + move.Id);
        }
        foreach (var move in MoveTable.Load().Where(MovePreviewScene.CanPreview))
        {
            preview.Select(move); var impactEvent = preview.arena.CurrentEvent;
            if (!TopViewArenaTest.IsHit(impactEvent) || impactEvent.Motion.Max(f => f.Participants.Single(p => p.Id == impactEvent.ReceiverId).Height) < .2f) continue;
            preview.Seek(.5f);
            if (preview.arena.HitEnvelope > 0) throw new InvalidOperationException("A lift produced an early impact: " + move.Id);
            var table = UnityEngine.Object.FindFirstObjectByType<AnnounceTableReplay>();
            preview.Seek(1); float ground = preview.arena.red.position.y - (table ? table.PlayerOffset(impactEvent, impactEvent.ReceiverId, 1).y : 0);
            preview.Seek(.65f);
            float landing = preview.arena.red.position.y - (table ? table.PlayerOffset(impactEvent, impactEvent.ReceiverId, .65f).y : 0);
            if (landing > ground + .12f || preview.arena.HitEnvelope <= 0)
                throw new InvalidOperationException("Impact flashed while the receiver was still lifted: " + move.Id);
        }
        preview.Select(MoveTable.Load().Single(m => m.Key == "uppercut"));
        preview.Seek(.2f); float standingRate = preview.arena.PlaybackRate(preview.arena.CurrentEvent);
        float standingClock = preview.arena.clock;
        typeof(MovePreviewScene).GetMethod("Advance", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Invoke(preview, new object[] { .1f });
        if (Math.Abs(preview.arena.clock - standingClock - .5f) > .001f)
            throw new InvalidOperationException("Stationary strike preview did not use its shorter clip.");
        preview.Seek(.575f);
        if (Math.Abs(preview.arena.PlaybackRate(preview.arena.CurrentEvent) / standingRate - 1.8f) > .001f)
            throw new InvalidOperationException("Stationary strike did not accelerate its impact phase.");
        preview.Select(MoveTable.Load().Single(m => m.Key == "superkick"));
        var kick = preview.arena.CurrentEvent;
        slider.value = .5f; var waiting = preview.arena.red.position;
        slider.value = .6f;
        if (Vector3.ProjectOnPlane(waiting - preview.arena.red.position, Vector3.up).magnitude > .001f)
            throw new InvalidOperationException("Superkick knocked the receiver away before contact.");
        slider.value = .8f;
        if (Vector3.ProjectOnPlane(waiting - preview.arena.red.position, Vector3.up).magnitude < .3f)
            throw new InvalidOperationException("Superkick lost its knockback after contact.");
        var flightDirection = Vector3.ProjectOnPlane(preview.arena.red.position - waiting, Vector3.up).normalized;
        float previousFlight = 0;
        float previousActor = float.PositiveInfinity;
        for (int i = 0; i <= 70; i++)
        {
            preview.Seek(.65f + i * .005f);
            float flight = Vector3.Dot(preview.arena.red.position - waiting, flightDirection);
            float actor = Vector3.Dot(preview.arena.blue.position, flightDirection);
            if (flight < previousFlight - .001f) throw new InvalidOperationException("Superkick knockback reversed direction.");
            if (i == 2 && flight < .05f) throw new InvalidOperationException("Superkick delayed knockback after contact.");
            previousFlight = flight;
            if (actor > previousActor + .001f) throw new InvalidOperationException("Superkick chased the receiver after contact.");
            previousActor = actor;
        }
        kick.Result = MatchActionResult.Countered; slider.value = .65f;
        if (Vector3.ProjectOnPlane(preview.arena.blue.position - preview.arena.red.position, Vector3.up).magnitude < 1)
            throw new InvalidOperationException("Failed superkick forced a successful contact.");
        CheckSubmissionFeedback(preview);
        CheckControlFeedback(preview);
        CheckTakeoffRopes(preview);
        foreach (var move in MoveTable.Load().Where(MovePreviewScene.CanPreview).Where(m =>
            m.Rules.Kind != MoveKind.Submission && m.Rules.MotionProfile is MoveMotionProfile.Hold or MoveMotionProfile.Chain))
        {
            preview.Select(move); preview.Seek(.65f);
            if (TopViewArenaTest.IsHit(preview.arena.CurrentEvent) || preview.arena.HitEnvelope > 0)
                throw new InvalidOperationException("A non-impact grip flashed as a hit: " + move.Id);
        }
        foreach (string key in new[] { "missile-dropkick", "diving-double-axe" })
        {
            preview.Select(MoveTable.Load().Single(m => m.Key == key)); preview.Seek(.65f);
            if (Vector3.ProjectOnPlane(preview.arena.blue.position - preview.arena.red.position, Vector3.up).magnitude >
                (preview.arena.blue.localScale.x + preview.arena.red.localScale.x) * .5f + .001f)
                throw new InvalidOperationException("Aerial impact happened after separation: " + key);
            var waitingTarget = preview.arena.red.position;
            preview.Seek(.8f);
            if (Vector3.ProjectOnPlane(preview.arena.red.position - waitingTarget, Vector3.up).magnitude < .3f)
                throw new InvalidOperationException("Aerial impact lost its knockback: " + key);
        }
        preview.Select(MoveTable.Load().Single(m => m.Key == "body-scissors")); preview.Seek(.65f);
        var occlusionA = preview.arena.blue.position; var occlusionB = preview.arena.red.position;
        var rotationA = preview.arena.blue.rotation; var rotationB = preview.arena.red.rotation;
        try
        {
            foreach (int upper in new[] { 0, 1 })
            {
                var chips = new[] { preview.arena.blue, preview.arena.red };
                for (int i = 0; i < 2; i++)
                {
                    chips[i].position = preview.arena.ringCenter + Vector3.up * (.12f + (i == upper ? .01f : 0));
                    chips[i].rotation = Quaternion.identity;
                }
                preview.arena.UpdateFacingVisibility();
                if (!chips[upper].Find("Facing").GetComponent<Renderer>().enabled || !chips[1 - upper].Find("Facing").GetComponent<Renderer>().enabled)
                    throw new InvalidOperationException("Overlapping chips lost their posture symbols.");
            }
            preview.arena.blue.position += Vector3.right;
            preview.arena.UpdateFacingVisibility();
            if (!preview.arena.blue.Find("Facing").GetComponent<Renderer>().enabled || !preview.arena.red.Find("Facing").GetComponent<Renderer>().enabled)
                throw new InvalidOperationException("Separated chips lost their facing dots.");
        }
        finally
        {
            preview.arena.blue.position = occlusionA; preview.arena.red.position = occlusionB;
            preview.arena.blue.rotation = rotationA; preview.arena.red.rotation = rotationB;
            preview.arena.UpdateFacingVisibility();
        }
        foreach (var move in MoveTable.Load().Where(MovePreviewScene.CanPreview).Where(m =>
            m.Rules.ExecutionStyle == MoveExecutionStyle.Running && m.Rules.Kind == MoveKind.Strike))
        {
            string key = move.Key; preview.Select(move);
            preview.Seek(.2f); float normal = preview.arena.PlaybackRate(preview.arena.CurrentEvent);
            preview.Seek(.425f); float burst = preview.arena.PlaybackRate(preview.arena.CurrentEvent);
            if (Math.Abs(burst - normal) > .001f) throw new InvalidOperationException("Charge timeline changed speed: " + key);
            var tick = typeof(MovePreviewScene).GetMethod("Advance", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            float clock = preview.arena.clock;
            tick.Invoke(preview, new object[] { .1f });
            if (Math.Abs(preview.arena.clock - clock - normal * .1f) > .001f) throw new InvalidOperationException("Preview ignored charge timing.");
            Vector3 At(float progress) { preview.Seek(progress); return preview.arena.blue.position; }
            float Speed(float progress) => Vector3.ProjectOnPlane(At(progress + .001f) - At(progress - .001f), Vector3.up).magnitude / .002f;
            float running = Speed(.45f);
            foreach (float progress in new[] { .5f, .55f, .6f, .64f, .65f })
                if (Speed(progress) < running * .9f) throw new InvalidOperationException("Charge slowed before contact: " + key);
            preview.Seek(.8f);
            if (Math.Abs(preview.arena.PlaybackRate(preview.arena.CurrentEvent) - normal) > .001f)
                throw new InvalidOperationException("Charge accelerated the landing or recovery.");
        }
        preview.Select(MoveTable.Load().Single(m => m.Key == "vertical-suplex"));
        if (!shape.enabledSelf) throw new InvalidOperationException("Suplex shape selector is disabled.");
        shape.index = 1;
        slider.value = 0;
        if (preview.arena.blue.localScale.y < .5f || preview.arena.red.up.y < .999f)
            throw new InvalidOperationException("Suplex capsules did not start standing.");
        slider.value = .46f;
        if (preview.arena.red.up.y > -.999f || preview.arena.red.position.y <= preview.arena.blue.position.y + 1)
            throw new InvalidOperationException("Suplex receiver was not held upside down.");
        foreach (float progress in new[] { .65f, .9f, 1f })
        {
            slider.value = progress;
            foreach (var marker in new[] { preview.arena.blue, preview.arena.red })
                if (Math.Abs(marker.up.y) > .001f || marker.GetComponent<Renderer>().bounds.min.y < preview.arena.ringCenter.y)
                    throw new InvalidOperationException("Suplex did not remain lying above the mat.");
                else if (marker.GetComponent<Renderer>().sharedMaterial.GetFloat("_Cull") != (float)UnityEngine.Rendering.CullMode.Front ||
                    marker.Find("Fill").GetComponent<MeshFilter>().sharedMesh.name != "Capsule" || !marker.Find("Fill").GetComponent<Renderer>().enabled)
                    throw new InvalidOperationException("Capsule outline or colored body was lost.");
        }
        foreach (float progress in new[] { 0f, .3f, .42f, .55f, .6f, .65f, .7f, .75f, .82f, .9f, 1f })
        {
            slider.value = progress;
            foreach (var marker in new[] { preview.arena.blue, preview.arena.red })
            {
                var mesh = marker.GetComponent<MeshFilter>().sharedMesh;
                if (mesh.name != "Capsule" || Math.Abs(marker.localScale.x - marker.localScale.y) > .001f ||
                    mesh.vertices.Any(v => marker.TransformPoint(v).y < preview.arena.ringCenter.y))
                    throw new InvalidOperationException("Suplex capsule deformed or penetrated the mat.");
                var outline = marker.GetComponent<Renderer>().sharedMaterial;
                var color = marker.Find("Fill").GetComponent<Renderer>().sharedMaterial.GetColor("_BaseColor");
                if (!outline.HasProperty("_Cull") || outline.GetFloat("_Cull") != (float)UnityEngine.Rendering.CullMode.Front ||
                    outline.GetColor("_BaseColor").maxColorComponent > .1f || color.maxColorComponent < .2f)
                    throw new InvalidOperationException("Capsule lost its dark silhouette or wrestler color.");
            }
        }
        float LowestPoint() => preview.arena.red.GetComponent<MeshFilter>().sharedMesh.vertices
            .Min(v => preview.arena.red.TransformPoint(v).y);
        slider.value = .65f; float landed = LowestPoint();
        var landedPosition = preview.arena.red.position;
        var landedDirection = Vector3.ProjectOnPlane(preview.arena.red.up, Vector3.up);
        slider.value = .7f; float firstBounce = LowestPoint();
        var bouncedPosition = preview.arena.red.position; var bouncedRotation = preview.arena.red.rotation;
        var bouncedScale = preview.arena.red.localScale;
        if (Vector3.ProjectOnPlane(bouncedPosition - landedPosition, Vector3.up).magnitude < .08f ||
            Vector3.Angle(landedDirection, Vector3.ProjectOnPlane(preview.arena.red.up, Vector3.up)) < 1)
            throw new InvalidOperationException("Landing rebound was not visible as planar movement and rotation.");
        slider.value = .82f; float secondBounce = LowestPoint();
        slider.value = 1;
        if (firstBounce <= landed + .01f || secondBounce <= landed + .001f || secondBounce >= firstBounce || Math.Abs(LowestPoint() - landed) > .001f)
            throw new InvalidOperationException("Landing rebound did not rise, damp and settle.");
        if (Vector3.ProjectOnPlane(preview.arena.red.position - landedPosition, Vector3.up).magnitude > .001f)
            throw new InvalidOperationException("Landing recoil did not settle at the recorded endpoint.");
        slider.value = .7f;
        if (preview.arena.red.position != bouncedPosition || preview.arena.red.rotation != bouncedRotation || preview.arena.red.localScale != bouncedScale)
            throw new InvalidOperationException("Landing rebound changed when seeking back.");
        shape.index = 0;
        if (Math.Abs(preview.arena.clock - 4.2f) > .001f || !preview.Paused ||
            Vector3.ProjectOnPlane(preview.arena.red.position - bouncedPosition, Vector3.up).magnitude > .001f)
            throw new InvalidOperationException("Shape switching changed playback time or planar motion.");
        foreach (float progress in new[] { 0f, .42f, .65f, .7f, .82f, 1f })
        {
            slider.value = progress;
            foreach (var marker in new[] { preview.arena.blue, preview.arena.red })
                if (marker.GetComponent<MeshFilter>().sharedMesh.name != "Cylinder" || marker.up.y < .999f ||
                    Math.Abs(marker.localScale.x - marker.localScale.z) > .001f || marker.localScale.y > .1f ||
                    Math.Abs(marker.Find("Fill").localPosition.y - 1) > .001f)
                    throw new InvalidOperationException("Round-chip suplex was deformed or kept capsule parts.");
        }
        slider.value = .7f; shape.index = 1;
        if (preview.arena.red.position != bouncedPosition || preview.arena.red.rotation != bouncedRotation || preview.arena.red.localScale != bouncedScale)
            throw new InvalidOperationException("Switching back did not restore the capsule pose.");
        preview.Select(MoveTable.Load().Single(m => m.Key == "body-slam"));
        slider.value = .42f;
        if (preview.arena.blue.localScale.y > .1f || Math.Abs(preview.arena.blue.Find("Fill").localPosition.y - 1) > .001f ||
            preview.arena.blue.GetComponent<MeshFilter>().sharedMesh.name != "Cylinder" || !preview.arena.blue.Find("Fill").GetComponent<Renderer>().enabled)
            throw new InvalidOperationException("Capsule example leaked into other techniques.");
        if (shape.enabledSelf) throw new InvalidOperationException("Shape selector is enabled for an unsupported technique.");
        preview.Select(MoveTable.Load().Single(m => m.Key == "vertical-suplex"));
        click.Invoke(root.Q<Button>("preview-pause").clickable, new object[] { null, 0 });
        if (!preview.Paused) throw new InvalidOperationException("Pause failed.");
        click.Invoke(root.Q<Button>("preview-pause").clickable, new object[] { null, 0 });
        if (preview.Paused) throw new InvalidOperationException("Resume failed.");
        slider.value = .8f;
        click.Invoke(root.Q<Button>("preview-restart").clickable, new object[] { null, 0 });
        if (preview.Paused || preview.arena.clock != 0) throw new InvalidOperationException("Restart failed.");
        var advance = typeof(MovePreviewScene).GetMethod("Advance", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        root.Q<DropdownField>("preview-speed").index = 0;
        advance.Invoke(preview, new object[] { 1f });
        if (Math.Abs(preview.arena.clock - 1) > .001f) throw new InvalidOperationException("Half speed failed.");
        root.Q<DropdownField>("preview-speed").index = 2; preview.Restart();
        advance.Invoke(preview, new object[] { 1f });
        if (Math.Abs(preview.arena.clock - 3) > .001f) throw new InvalidOperationException("Fast speed failed.");
        root.Q<DropdownField>("preview-speed").index = 1;
        root.Q<Toggle>("preview-repeat").value = false; preview.Restart();
        advance.Invoke(preview, new object[] { 4f });
        if (!preview.Paused || preview.arena.clock != 6) throw new InvalidOperationException("One-shot did not stop.");
        root.Q<Toggle>("preview-repeat").value = true; preview.Restart();
        advance.Invoke(preview, new object[] { 4f });
        if (preview.Paused || Math.Abs(preview.arena.clock - .4f) > .001f) throw new InvalidOperationException("Loop failed.");
        list.SetSelection(list.itemsSource.Cast<MatchTechnique>().ToList().FindIndex(m => m.Key == "vertical-suplex"));
        shape.index = 0;
        preview.Restart();
    }

    private static void CheckTakeoffRopes(MovePreviewScene preview)
    {
        foreach (var move in MoveTable.Load().Where(MovePreviewScene.CanPreview).Where(m =>
            m.Rules.Situation is MoveSituation.DivingStanding or MoveSituation.DivingGrounded))
        {
            preview.Select(move); var arena = preview.arena; var e = arena.CurrentEvent;
            arena.Seek(0);
            var rest = arena.ropes.Select(r => Enumerable.Range(0, r.positionCount).Select(r.GetPosition).ToArray()).ToArray();
            arena.Seek(.1f * arena.PlaybackRate(e));
            float maximum = 0; int moving = 0;
            for (int line = 0; line < arena.ropes.Length; line++)
            {
                var rope = arena.ropes[line]; float displacement = 0;
                for (int i = 1; i < rope.positionCount - 1; i++)
                    displacement = Mathf.Max(displacement, Vector3.Distance(rope.GetPosition(i), rest[line][i]));
                maximum = Mathf.Max(maximum, displacement);
                if (displacement > .004f) moving++;
                if (Vector3.Distance(rope.GetPosition(0), rest[line][0]) > .0001f ||
                    Vector3.Distance(rope.GetPosition(rope.positionCount - 1), rest[line][^1]) > .0001f)
                    throw new InvalidOperationException("Takeoff moved a rope attachment.");
            }
            if (moving < 6 || maximum > .1f) throw new InvalidOperationException("Missing or excessive takeoff rope recoil: " + move.Id);
        }
    }

    private static void CheckControlFeedback(MovePreviewScene preview)
    {
        var block = new MaterialPropertyBlock();
        foreach (var move in MoveTable.Load().Where(MovePreviewScene.CanPreview).Where(m => m.Action is MatchActionType.Chain or MatchActionType.TieUp))
        {
            preview.Select(move); var arena = preview.arena; var e = arena.CurrentEvent;
            arena.Seek(0);
            if (arena.ControlEnvelope > 0) throw new InvalidOperationException("Control blink began before contact.");
            var actor = arena.blue.Find("Fill").GetComponent<Renderer>();
            var receiver = arena.red.Find("Fill").GetComponent<Renderer>();
            actor.GetPropertyBlock(block); var actorColor = block.GetColor("_BaseColor");
            var grip = e.Motion.First(f => f.Participants.Any(p => p.Grip != MatchGrip.None));
            Color At(float age)
            {
                arena.Seek(e.DurationSeconds * grip.Progress + age * arena.PlaybackRate(e));
                receiver.GetPropertyBlock(block); return block.GetColor("_BaseColor");
            }
            var lit = At(.12f); var dark = At(.4f); var next = At(.92f);
            actor.GetPropertyBlock(block);
            if (Vector4.Distance(lit, dark) < .1f || Vector4.Distance(lit, next) > .001f ||
                Vector4.Distance(actorColor, block.GetColor("_BaseColor")) > .001f ||
                arena.ControlEnvelope < .6f || arena.ControlEnvelope > .651f || arena.HitEnvelope != 0 || arena.SubmissionEnvelope != 0)
                throw new InvalidOperationException("Control blink failed: " + move.Id);
            foreach (var result in new[] { MatchActionResult.Escaped, MatchActionResult.Countered, MatchActionResult.Interrupted })
            {
                e.Result = result; At(.12f);
                if (arena.ControlEnvelope != 0) throw new InvalidOperationException("Released grip continued blinking.");
            }
        }
    }

    private static void CheckSubmissionFeedback(MovePreviewScene preview)
    {
        var block = new MaterialPropertyBlock();
        foreach (var move in MoveTable.Load().Where(m => MovePreviewScene.CanPreview(m) && m.Rules.Kind == MoveKind.Submission))
        {
            preview.Select(move);
            var arena = preview.arena; var e = arena.CurrentEvent;
            var receiver = arena.red.Find("Fill").GetComponent<Renderer>();
            var actor = arena.blue.Find("Fill").GetComponent<Renderer>();
            arena.Seek(e.DurationSeconds * .49f);
            if (arena.SubmissionEnvelope != 0) throw new InvalidOperationException("Submission blink began before the hold: " + move.Id);
            actor.GetPropertyBlock(block); var actorColor = block.GetColor("_BaseColor");
            Color At(float age)
            {
                arena.Seek(e.DurationSeconds * .5f + age * arena.PlaybackRate(e));
                receiver.GetPropertyBlock(block); return block.GetColor("_BaseColor");
            }
            var lit = At(.09f); var dark = At(.3f); var next = At(.64f);
            actor.GetPropertyBlock(block);
            if (Vector4.Distance(lit, dark) < .2f || Vector4.Distance(lit, next) > .001f ||
                Vector4.Distance(actorColor, block.GetColor("_BaseColor")) > .001f || arena.HitEnvelope != 0)
                throw new InvalidOperationException("Submission did not blink periodically on only the receiver: " + move.Id);
            arena.Seek(e.DurationSeconds);
            if (arena.SubmissionEnvelope != 0) throw new InvalidOperationException("Submission blink continued past the clip.");
        }
        preview.Select(MoveTable.Load().Single(m => m.Key == "crossface"));
        var state = preview.arena.Engine; var start = state.Events[0];
        foreach (var type in new[] { MatchActionType.Signature, MatchActionType.Finisher })
        {
            start.Type = type;
            preview.arena.Seek(3 + .09f * preview.arena.PlaybackRate(start));
            if (preview.arena.SubmissionEnvelope < .7f) throw new InvalidOperationException("Submission role lost its pressure blink.");
        }
        var hold = new MatchEngineEvent { Type = MatchActionType.Hold, Result = MatchActionResult.Held,
            Detail = "hold", CauseEventIndex = 0, PerformerId = start.PerformerId, ReceiverId = start.ReceiverId,
            MatchTimeSeconds = 6, DurationSeconds = 6, Before = start.After, After = start.After, Impact = 1 };
        state.Events.Add(hold); state.ElapsedSeconds = 12;
        preview.arena.LoadMatch(state);
        foreach (float age in new[] { .09f, .64f })
        {
            preview.arena.Seek(6 + age * preview.arena.PlaybackRate(hold));
            if (preview.arena.SubmissionEnvelope < .7f) throw new InvalidOperationException("Continuing hold lost its pressure blink.");
        }
        foreach (var result in new[] { MatchActionResult.Escaped, MatchActionResult.RopeBreak, MatchActionResult.Interrupted,
            MatchActionResult.Countered, MatchActionResult.ExecutionFailure, MatchActionResult.Submitted, MatchActionResult.PassedOut })
        {
            hold.Result = result; preview.arena.Seek(6 + .09f * preview.arena.PlaybackRate(hold));
            if (preview.arena.SubmissionEnvelope != 0) throw new InvalidOperationException("Released hold retained its pressure blink.");
        }
        hold.Result = MatchActionResult.Held; hold.Detail = "count-out";
        preview.arena.Seek(6 + .09f * preview.arena.PlaybackRate(hold));
        if (preview.arena.SubmissionEnvelope != 0) throw new InvalidOperationException("Count-out received submission feedback.");
    }
}
