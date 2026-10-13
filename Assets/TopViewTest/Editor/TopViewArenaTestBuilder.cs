using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Data.Catalogs;
using PWManager.Domain.Services;
using PWManager.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

public static class TopViewArenaTestBuilder
{
    private const string Root = "Assets/TopViewTest";
    private static readonly Dictionary<string, Material> Materials = new();

    [MenuItem("PWManager/Top View/Create Arena Test")]
    public static void Create()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before creating the test scene.");
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save the open scene before creating the test scene.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Root + "/TopView Arena Test.unity") != null)
            throw new InvalidOperationException("The test scene already exists; open it instead of overwriting.");
        AssetDatabase.Refresh();
        Materials.Clear();
        System.IO.Directory.CreateDirectory(Root + "/Materials");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var ring = Model("ProWrestlingRing_LOD0");
        Model("ProWrestlingArena_LOD0");
        var venue = Model("ProWrestlingVenue_LOD1");
        var canvas = ring.GetComponentsInChildren<Renderer>().First(r => r.name.Contains("Canvas"));
        Vector3 center = canvas.bounds.center;
        center.y = canvas.bounds.max.y;
        var stage = venue.GetComponentsInChildren<Renderer>().First(r => r.name.Contains("Entrance_ArenaRubber"));
        Vector3 entrance = stage.bounds.center;
        entrance.z = Mathf.Abs(stage.bounds.min.z) > Mathf.Abs(stage.bounds.max.z) ? stage.bounds.min.z + 3 : stage.bounds.max.z - 3;
        entrance.x = center.x;
        var controller = new GameObject("Arena Test").AddComponent<TopViewArenaTest>();
        controller.impactPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Effects/Match Impact.prefab");
        AddCommentary(controller);
        controller.catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>("Assets/Game/Data/Static/GameStaticContentCatalog.asset");
        controller.ringCenter = center;
        controller.entrance = entrance;
        controller.blue = Marker("Blue", center + new Vector3(.5f, .12f, .8f), new Color(.08f,.5f,1), .29f);
        controller.red = Marker("Red", center + new Vector3(-.8f,.12f,-.4f), new Color(1,.15f,.2f), .29f);
        Marker("Referee", center + new Vector3(1.3f,.12f,-1.5f), new Color(.85f,.88f,.9f), .14f);
        var desk = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).First(r => r.name.Contains("AnnounceChairs_Pad"));
        for (int i = -1; i <= 1; i++) Marker("Commentator", new Vector3(desk.bounds.center.x + i*1.1f,desk.bounds.max.y+.1f,desk.bounds.center.z), new Color(.8f,.62f,.43f), .2f);
        controller.ropes = new LineRenderer[12];
        var ropeMaterial = ring.GetComponentsInChildren<Renderer>(true).Single(r => r.name.EndsWith("_Rope")).sharedMaterial;
        for (int i = 0; i < 12; i++)
        {
            var line = new GameObject("Rope " + i).AddComponent<LineRenderer>();
            line.transform.SetParent(controller.transform);
            line.sharedMaterial = ropeMaterial;
            line.generateLightingData = true;
            line.positionCount = 33;
            line.widthMultiplier = .044f;
            line.numCapVertices = 4;
            line.numCornerVertices = 4;
            line.shadowCastingMode = ShadowCastingMode.Off;
            controller.ropes[i] = line;
        }
        var camera = new GameObject("Top View Camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.orthographic = true;
        camera.orthographicSize = 15;
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 100;
        camera.backgroundColor = new Color(.035f,.043f,.055f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.transform.position = Vector3.Lerp(center, entrance, .38f);
        camera.transform.position = new Vector3(camera.transform.position.x,45,camera.transform.position.z);
        camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.right * Mathf.Sign(entrance.z-center.z));
        camera.gameObject.AddComponent<AudioListener>();
        controller.viewCamera = camera;
        controller.Seek(0);
        var light = new GameObject("Arena Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.transform.rotation = Quaternion.Euler(65,-25,0);
        light.shadows = LightShadows.Soft;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.55f,.59f,.65f);
        RenderSettings.fog = false;
        EditorSceneManager.SaveScene(scene, Root + "/TopView Arena Test.unity");
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = controller.gameObject;
        Check();
    }

    private static GameObject Model(string name)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/" + name + ".fbx");
        if (!asset) throw new InvalidOperationException("Missing model: " + name);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        foreach (var renderer in model.GetComponentsInChildren<Renderer>())
        {
            if ((name.Contains("Arena") && (renderer.name.StartsWith("Ring_") || renderer.name.StartsWith("Steps_"))) || (name.Contains("Venue") && !renderer.name.StartsWith("Venue_")) || renderer.name.Contains("Roof") || renderer.name.Contains("_Rig_") || renderer.name.Contains("_Fixtures_") || renderer.name.EndsWith("_Rope"))
            { renderer.gameObject.SetActive(false); continue; }
            if (!name.StartsWith("ProWrestlingRing_"))
                renderer.sharedMaterials = renderer.sharedMaterials.Select(MaterialFor).ToArray();
        }
        return model;
    }

    private static Material MaterialFor(Material original)
    {
        string key = original ? original.name.Split('_').Last().Replace(" (Instance)","") : "Default";
        if (Materials.TryGetValue(key,out var existing)) return existing;
        string path = Root + "/Materials/" + key + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/" + key + "_BaseColor.png");
            material.SetColor("_BaseColor",texture ? Color.white : new Color(.3f,.33f,.38f));
            material.SetTexture("_BaseMap",texture);
            material.SetFloat("_Smoothness", .2f);
            AssetDatabase.CreateAsset(material,path);
        }
        Materials[key] = material;
        return material;
    }

    private static Material FlatMaterial(string name, Color color)
    {
        string path = Root + "/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Unlit")); material.SetColor("_BaseColor",color); AssetDatabase.CreateAsset(material,path); }
        return material;
    }

    private static Transform Marker(string name, Vector3 position, Color color, float radius)
    {
        var outline = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        outline.name = name;
        outline.transform.position = position;
        outline.transform.localScale = new Vector3(radius*2.25f,.025f,radius*2.25f);
        outline.GetComponent<Renderer>().sharedMaterial = FlatMaterial("MarkerOutline",new Color(.025f,.035f,.05f));
        UnityEngine.Object.DestroyImmediate(outline.GetComponent<Collider>());
        var fill = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        fill.name = "Fill";
        fill.transform.SetParent(outline.transform,false);
        fill.transform.localPosition = new Vector3(0,1,0);
        fill.transform.localScale = new Vector3(.85f,.5f,.85f);
        fill.GetComponent<Renderer>().sharedMaterial = FlatMaterial(name,color);
        UnityEngine.Object.DestroyImmediate(fill.GetComponent<Collider>());
        AddFacing(outline.transform);
        return outline.transform;
    }

    private static void AddCommentary(TopViewArenaTest c)
    {
        var document = c.GetComponent<UIDocument>();
        if (!document) document = c.gameObject.AddComponent<UIDocument>();
        document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Game/Presentation/Resources/PWManagerRuntime/PWManagerPanelSettings.asset");
        document.sortingOrder = 10;
        c.strongFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Game/Presentation/Resources/PWManagerUI/Fonts/SEBANGGothic-Bold.ttf");
        c.commentaryFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Game/Presentation/Resources/PWManagerUI/Fonts/SEBANGGothic-Regular.ttf");
    }
    private static void AddFacing(Transform marker)
    {
        if (marker.Find("Facing")) return;
        var dot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        dot.name = "Facing";
        dot.transform.SetParent(marker, false);
        dot.transform.localPosition = new Vector3(0, 1.6f, .32f);
        dot.transform.localScale = new Vector3(.16f, .5f, .16f);
        dot.GetComponent<Renderer>().sharedMaterial = FlatMaterial("Facing", Color.white);
        UnityEngine.Object.DestroyImmediate(dot.GetComponent<Collider>());
    }

    [MenuItem("PWManager/Top View/Connect Match Engine")]
    public static void ConnectMatchEngine()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before saving the scene.");
        var c = UnityEngine.Object.FindFirstObjectByType<TopViewArenaTest>();
        if (!c || c.gameObject.scene.path != Root + "/TopView Arena Test.unity") throw new InvalidOperationException("Open the arena test scene.");
        AddCommentary(c);
        c.catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>("Assets/Game/Data/Static/GameStaticContentCatalog.asset");
        AddFacing(c.blue); AddFacing(c.red);
        c.InitializeMatch();
        c.SetView(0);
        c.automaticCamera = true;
        EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
        EditorSceneManager.SaveScene(c.gameObject.scene);
        AssetDatabase.SaveAssets();
        CheckMatchPlayback();
    }

    [MenuItem("PWManager/Top View/Check Match Playback")]
    public static void CheckMatchPlayback()
    {
        var c = UnityEngine.Object.FindFirstObjectByType<TopViewArenaTest>();
        if (!c) throw new InvalidOperationException("Open the arena test scene.");
        if (c.Engine == null) c.InitializeMatch();
        if (!c.Engine.IsFinished || c.Engine.IsAborted || c.Engine.Events.Count < 2) throw new InvalidOperationException("Engine did not finish.");
        float original = c.clock;
        int moved = 0, grounded = 0;
        var storedScales = new SerializedObject(c).FindProperty("markerBaseScales");
        var originalScales = Enumerable.Range(0, c.Engine.Participants.Count).Select(i => storedScales.GetArrayElementAtIndex(i).vector3Value).ToArray();
        Vector3 previous = c.blue.position;
        foreach (var e in c.Engine.Events)
        {
            c.Seek(e.MatchTimeSeconds + e.DurationSeconds);
            if (Vector3.Distance(previous, c.blue.position) > .05f) moved++;
            previous = c.blue.position;
            if (string.IsNullOrWhiteSpace(c.CommentaryText)) throw new InvalidOperationException("Missing engine commentary.");
            var transforms = c.Engine.Participants.Select(p => c.MarkerFor(p.Id)).ToArray();
            for (int i = 0; i < transforms.Length; i++)
            {
                var snapshot = e.After.Participants.Single(p => p.Id == c.Engine.Participants[i].Id);
                if (c.Engine.EngineVersion >= 3)
                {
                    var pose = MatchMovement.Sample(e, snapshot.Id, 1);
                    var expectedPosition = c.ringCenter + new Vector3(pose.X * c.halfRing,
                        .12f + (pose.Height < 0 ? pose.Height / .4f * c.ringCenter.y : pose.Height * c.halfRing), pose.Z * c.halfRing);
                    if (Vector3.Distance(transforms[i].position, expectedPosition) > .0001f)
                        throw new InvalidOperationException("Marker boundary differs from the recorded engine pose.");
                    c.Seek(Mathf.Max(e.MatchTimeSeconds, e.MatchTimeSeconds + e.DurationSeconds - .0001f));
                    float remaining = e.MatchTimeSeconds + e.DurationSeconds - c.clock;
                    float travel = e.MotionSeconds > 0 && pose.HasVelocity ?
                        new Vector2(pose.VelocityX, pose.VelocityZ).magnitude * c.halfRing * e.MotionSeconds / e.DurationSeconds * remaining : 0;
                    if (Vector3.Distance(transforms[i].position, expectedPosition) > .0001f + travel * 1.01f)
                        throw new InvalidOperationException("Marker jumps between engine events.");
                    c.Seek(e.MatchTimeSeconds + e.DurationSeconds);
                }
                // At the exact next-event boundary its Before snapshot preserves this final state.
                if (snapshot.IsDowned) grounded++;
                if (Application.isPlaying)
                {
                    var root = c.GetComponent<UIDocument>().rootVisualElement;
                    var values = new[] { snapshot.Fatigue, snapshot.Stun, snapshot.MatchControl };
                    for (int j = 0; j < values.Length; j++)
                    {
                        var bar = root.Q<ProgressBar>($"metric-{i}-{j}");
                        if (bar == null || Mathf.Abs(bar.value - values[j]) > .001f)
                            throw new InvalidOperationException("Status box differs from the engine snapshot.");
                    }
                    var indicator = root.Q<LimbDamageIndicator>("limb-damage-" + i);
                    var damage = new Vector4(snapshot.HeadDamage, snapshot.BodyDamage, snapshot.ArmDamage, snapshot.LegDamage);
                    if (indicator == null || Vector4.Distance(indicator.Damage, damage) > .001f)
                        throw new InvalidOperationException("Limb damage indicator differs from the engine snapshot.");
                    if (root.Q<ProgressBar>($"metric-{i}-3") != null)
                        throw new InvalidOperationException("Old limb damage bars remain.");
                }
                var finalPose = e.Motion != null ? MatchMovement.Sample(e, snapshot.Id, 1) : null;
                float size = 1 + Mathf.Clamp(finalPose?.Height ?? 0, 0, 1.2f) * .75f;
                var expectedScale = new Vector3(originalScales[i].x * size, originalScales[i].y, originalScales[i].z * size);
                if (Vector3.Distance(transforms[i].localScale, expectedScale) > .0001f)
                    throw new InvalidOperationException("Marker scale differs from its recorded height.");
                var p = transforms[i].position;
                if (snapshot.Position == MatchPosition.Outside && Mathf.Max(Mathf.Abs(p.x - c.ringCenter.x), Mathf.Abs(p.z - c.ringCenter.z)) < c.halfRing + transforms[i].localScale.x * .5f)
                    throw new InvalidOperationException("Outside marker is hidden by the ring platform.");
                if (float.IsNaN(p.x) || float.IsNaN(p.z) || Mathf.Abs(p.x - c.ringCenter.x) > 4.1f || Mathf.Abs(p.z - c.ringCenter.z) > 4.1f)
                    throw new InvalidOperationException("Invalid marker position.");
            }
        }
        if (moved == 0 || grounded == 0) throw new InvalidOperationException("Missing engine movement or downed state.");
        float sample = c.Engine.ElapsedSeconds * .4f;
        c.Seek(sample); var expected = c.blue.position;
        var expectedScaleAtSample = c.blue.localScale; var expectedRotation = c.blue.rotation;
        c.Seek(0); c.Seek(sample);
        if (Vector3.Distance(expected, c.blue.position) > .0001f || Vector3.Distance(expectedScaleAtSample, c.blue.localScale) > .0001f ||
            Quaternion.Angle(expectedRotation, c.blue.rotation) > .001f) throw new InvalidOperationException("Seek is not deterministic.");
        c.Seek(c.Engine.ElapsedSeconds); var end = c.blue.position;
        c.Seek(c.Engine.ElapsedSeconds + 100);
        if (!c.Finished || Vector3.Distance(end, c.blue.position) > .0001f) throw new InvalidOperationException("Playback continued after the finish.");
        foreach (var e in c.Engine.Events.Where(e => e.Type == MatchActionType.Climb || e.Detail == "reach-ropes"))
        {
            c.Seek(e.MatchTimeSeconds + e.DurationSeconds * .65f + .1f);
            if (e.Type == MatchActionType.Climb)
            {
                var marker = c.MarkerFor(e.PerformerId);
                if (marker.position.y < c.ringCenter.y + 1) throw new InvalidOperationException("Climb did not reach the top rope.");
            }
            else if (!c.ropes.Any(r => Enumerable.Range(1, r.positionCount - 2).Any(i =>
                Vector3.Distance(r.GetPosition(i), Vector3.Lerp(r.GetPosition(0), r.GetPosition(r.positionCount - 1), i / (float)(r.positionCount - 1))) > .02f)))
                throw new InvalidOperationException("Engine rope contact did not bend a rope.");
        }
        int travelling = 0;
        foreach (var e in c.Engine.Events.Where(e => e.Type is MatchActionType.IrishWhip or MatchActionType.CornerWhip or MatchActionType.RopeRun))
        {
            int mover = e.Type == MatchActionType.RopeRun ? c.Engine.Participants.FindIndex(p => p.Id == e.PerformerId) :
                c.Engine.Participants.FindIndex(p => p.Id == (e.Result == MatchActionResult.Countered ? e.PerformerId : e.ReceiverId));
            var marker = c.MarkerFor(c.Engine.Participants[mover].Id);
            c.Seek(e.MatchTimeSeconds); var start = marker.position;
            c.Seek(e.MatchTimeSeconds + e.DurationSeconds * .65f); var destination = marker.position;
            if (Vector3.Distance(start, destination) > 1) travelling++;
            // Whips release the wrist at .35; sample after the runner starts moving.
            c.Seek(e.MatchTimeSeconds + e.DurationSeconds * .5f); var middle = marker.position;
            if (Vector3.Distance(start, destination) > 1 && (Vector3.Distance(start, middle) < .1f || Vector3.Distance(destination, middle) < .1f))
                throw new InvalidOperationException("Running snapped instead of travelling through the ring.");
            if (c.HitEnvelope != 0) throw new InvalidOperationException("A movement setup produced a fake hit.");
        }
        if (c.Engine.Events.Any(e => e.Type == MatchActionType.IrishWhip) && travelling == 0)
            throw new InvalidOperationException("Whips never moved a wrestler across the ring.");
        int hits = 0;
        foreach (var e in c.Engine.Events)
        {
            c.Seek(e.MatchTimeSeconds + e.DurationSeconds * .65f + .1f);
            bool hit = TopViewArenaTest.IsHit(e);
            if ((c.HitEnvelope > 0) != hit) throw new InvalidOperationException("Hit feedback disagrees with the engine outcome.");
            if (!hit) continue;
            hits++;
            c.Seek(e.MatchTimeSeconds + e.DurationSeconds * .65f - .01f);
            if (c.HitEnvelope != 0) throw new InvalidOperationException("Hit feedback started before contact.");
            c.Seek(e.MatchTimeSeconds + e.DurationSeconds * .99f);
            float hitAge = e.DurationSeconds * .34f / Mathf.Max(.01f, c.PlaybackRate(e));
            if ((c.HitEnvelope > 0) != (hitAge < .3f)) throw new InvalidOperationException("Hit feedback did not match elapsed screen time.");
        }
        if (hits == 0) throw new InvalidOperationException("Missing successful hit feedback.");
        foreach (var e in c.Engine.Events)
        {
            foreach (float progress in new[] { .3f, .5f, .65f, .85f })
            {
                c.Seek(e.MatchTimeSeconds + e.DurationSeconds * progress);
                foreach (var marker in c.Engine.Participants.Select(p => c.MarkerFor(p.Id)))
                    if (Mathf.Abs(marker.localScale.x - marker.localScale.z) > .0001f || marker.localScale.y > .1f)
                        throw new InvalidOperationException("Marker stopped being a thin round chip.");
                bool covering = e.Type == MatchActionType.Cover || e.Type == MatchActionType.Pin && progress <= .65f ||
                    e.Type is MatchActionType.Finish or MatchActionType.Fall && e.Result is MatchActionResult.Pinfall or MatchActionResult.RollUp;
                if (covering)
                {
                    var coverer = c.MarkerFor(e.PerformerId);
                    var receiver = c.MarkerFor(e.ReceiverId);
                    if (coverer.position.y <= receiver.position.y + .1f)
                        throw new InvalidOperationException("Covering wrestler is below the receiver.");
                }
            }
        }
        foreach (var e in c.Engine.Events.Where(e => e.Motion != null && e.Motion.Any(f => f.Participants.Any(p => p.Height > .4f))))
        {
            var highest = e.Motion.OrderByDescending(f => f.Participants.Max(p => p.Height)).First();
            c.Seek(e.MatchTimeSeconds + e.DurationSeconds * highest.Progress);
            foreach (var pose in highest.Participants.Where(p => p.Height > .4f))
            {
                int index = c.Engine.Participants.FindIndex(p => p.Id == pose.Id);
                var marker = c.MarkerFor(pose.Id);
                if (marker.localScale.x * marker.localScale.z <= originalScales[index].x * originalScales[index].z * 1.3f)
                    throw new InvalidOperationException("Elevation did not enlarge the marker.");
                var shadow = c.transform.Find(marker.name + " Shadow");
                if (!shadow || marker.position.y - shadow.position.y < .8f ||
                    Vector2.Distance(new Vector2(shadow.position.x, shadow.position.z), new Vector2(marker.position.x, marker.position.z)) < .2f)
                    throw new InvalidOperationException("Elevated marker lacks a shadow on the floor.");
            }
        }
        var firstHit = c.Engine.Events.First(TopViewArenaTest.IsHit);
        var hitMarker = c.MarkerFor(firstHit.ReceiverId);
        var block = new MaterialPropertyBlock();
        float hitTime = firstHit.MatchTimeSeconds + firstHit.DurationSeconds * .65f;
        c.Seek(hitTime + .01f * c.PlaybackRate(firstHit));
        hitMarker.Find("Fill").GetComponent<Renderer>().GetPropertyBlock(block);
        var lit = block.GetColor("_BaseColor");
        c.Seek(hitTime + .06f * c.PlaybackRate(firstHit));
        hitMarker.Find("Fill").GetComponent<Renderer>().GetPropertyBlock(block);
        if (Vector4.Distance(lit, block.GetColor("_BaseColor")) < .1f) throw new InvalidOperationException("Hit did not blink.");
        c.Seek(original);
        Check();
        Debug.Log($"TopView match playback passed: {c.Engine.Events.Count} engine events, {moved} movements, {grounded} downed snapshots, deterministic seek and finish.");
    }
    [MenuItem("PWManager/Top View/Check Wrestler Viewer")]
    public static void CheckWrestlerViewer()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run the arena test first.");
        var c = UnityEngine.Object.FindFirstObjectByType<TopViewArenaTest>();
        var save = DashboardSession.ActiveSave;
        var root = c.GetComponent<UIDocument>().rootVisualElement;
        if (save == null || !DashboardSession.IsTransient || save.Wrestlers.Count != c.Engine.Participants.Count)
            throw new InvalidOperationException("Generated wrestlers are missing from the preview session.");
        var profile = UnityEngine.Object.FindFirstObjectByType<WrestlerProfileController>().GetComponent<UIDocument>().rootVisualElement;
        if (profile.panel == null || profile.worldBound.width < 100 || profile.worldBound.height < 100)
            throw new InvalidOperationException("The profile document is detached or has no screen area.");
        var screen = profile.Q("wrestler-profile");
        float original = c.clock;
        bool paused = c.paused;
        var click = typeof(Clickable).GetMethod("SimulateSingleClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var update = typeof(TopViewArenaTest).GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        try
        {
            for (int i = 0; i < c.Engine.Participants.Count; i++)
            {
                var wrestler = save.Wrestlers.Single(w => w.Id == c.Engine.Participants[i].Id);
                var button = root.Q<Button>("wrestler-name-" + i);
                if (button == null || button.text != WrestlerNameText.DisplayName(wrestler) || (string)button.userData != wrestler.Id)
                    throw new InvalidOperationException("The HUD name does not identify its actual wrestler.");
                click.Invoke(button.clickable, new object[] { null, 0 });
                if (profile.Q<Label>("wp-name").text != wrestler.Identity.RingName || screen.style.display.value != DisplayStyle.Flex)
                    throw new InvalidOperationException("The name click opened the wrong profile.");
                c.paused = false;
                float before = c.clock;
                update.Invoke(c, null);
                if (c.clock != before) throw new InvalidOperationException("Playback advanced while viewing a profile.");
                if (root.Q("wrestler-states").style.display.value != DisplayStyle.None || root.Q("match-commentary").style.display.value != DisplayStyle.None)
                    throw new InvalidOperationException("The simulation HUD stayed visible behind the profile.");
                click.Invoke(profile.Q<Button>("wrestler-profile-back").clickable, new object[] { null, 0 });
                if (screen.style.display.value != DisplayStyle.None) throw new InvalidOperationException("Profile back did not return to the match.");
                update.Invoke(c, null);
                if (root.Q("wrestler-states").style.display.value != DisplayStyle.Flex || root.Q("match-commentary").style.display.value != DisplayStyle.Flex)
                    throw new InvalidOperationException("The simulation HUD did not return after closing the profile.");
            }
            c.Seek(0);
            c.paused = false;
            update.Invoke(c, null);
            if (Time.deltaTime > 0 && c.clock <= 0) throw new InvalidOperationException("Playback did not resume after closing the profile.");
            var participant = save.Wrestlers[1];
            c.Seek(c.Engine.Events.First(e => e.PerformerId == participant.Id && e.ReceiverId != null).MatchTimeSeconds);
            var text = root.Q<Label>("commentary-text");
            using (var pointer = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0 }))
            using (var link = PointerUpLinkTagEvent.GetPooled(pointer, "wrestler:" + Uri.EscapeDataString(participant.Id), WrestlerNameText.DisplayName(participant)))
            {
                link.target = text;
                text.SendEvent(link);
            }
            if (profile.Q<Label>("wp-name").text != participant.Identity.RingName || !c.ViewingProfile)
                throw new InvalidOperationException("The commentary link opened the wrong profile.");
            click.Invoke(profile.Q<Button>("wrestler-profile-back").clickable, new object[] { null, 0 });
            foreach (var e in c.Engine.Events.Where(TopViewArenaTest.IsHit))
            {
                float rate = c.PlaybackRate(e);
                if (e.DurationSeconds / rate > 1.61f) throw new InvalidOperationException("An attack still plays too slowly.");
            }
            c.Seek(c.Engine.Events.First(e => e.PerformerId != null && e.ReceiverId != null).MatchTimeSeconds);
            if (!root.Q<Label>("commentary-text").text.Contains("<link=\"wrestler:"))
                throw new InvalidOperationException("Commentary names are not profile links.");
        }
        finally
        {
            WrestlerProfileController.CloseOpenProfile(false);
            c.paused = paused;
            c.Seek(original);
        }
        Debug.Log("TopView wrestler viewer passed: generated records, both name clicks, correct profiles, back navigation, paused profile viewing, commentary links and attack tempo.");
    }
    [MenuItem("PWManager/Top View/Check Arena Test")]
    public static void Check()
    {
        var controller = UnityEngine.Object.FindFirstObjectByType<TopViewArenaTest>();
        if (!controller || !controller.viewCamera || !controller.blue || !controller.red || controller.ropes.Length != 12) throw new InvalidOperationException("Arena test references are incomplete.");
        if (Mathf.Abs(TopViewArenaTest.RopeOffset(0,.5f,.1f,.5f)) > .0001f || Mathf.Abs(TopViewArenaTest.RopeOffset(1,.5f,.1f,.5f)) > .0001f) throw new InvalidOperationException("Rope endpoints moved.");
        if (TopViewArenaTest.RopeOffset(.5f,.5f,.1f,.5f) < .1f || Mathf.Abs(TopViewArenaTest.RopeOffset(.5f,.5f,3,.5f)) > .0001f) throw new InvalidOperationException("Rope impulse or settling failed.");
        if (controller.ropes.Any(r=>!r || r.positionCount != 33)) throw new InvalidOperationException("Rope geometry is incomplete.");
        var active = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        if (active.Count(r => r.name.StartsWith("Steps_") && r.name.Contains("_Base_")) != 2 || active.Count(r => r.name.StartsWith("Ring_") && r.name.EndsWith("_Canvas")) != 1) throw new InvalidOperationException("Duplicate ring or stairs.");
        if (Mathf.Abs(controller.ropes[0].GetPosition(0).x - controller.ringCenter.x + controller.ropeEnd) > .001f) throw new InvalidOperationException("Rope does not start at the turnbuckle anchor.");
        Debug.Log("TopView Arena Test: references, fixed rope endpoints, impulse and settling passed.");
    }
}










