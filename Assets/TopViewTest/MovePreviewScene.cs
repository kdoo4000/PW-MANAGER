using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Data.Loading;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class MovePreviewScene : MonoBehaviour
{
    public TopViewArenaTest arena;
    public MatchTechnique Selected { get; private set; }
    public bool Paused { get; private set; }
    private float ClipSeconds => Selected?.Rules.ExecutionStyle == MoveExecutionStyle.Stationary && Selected.Rules.Kind == MoveKind.Strike &&
        Selected.Rules.MotionProfile is MoveMotionProfile.Strike or MoveMotionProfile.Kick ? 1.2f :
        Selected?.Rules.ExecutionStyle == MoveExecutionStyle.Running && Selected.Rules.Kind == MoveKind.Strike ? 2 : 3;
    private readonly List<MatchTechnique> visible = new();
    private IReadOnlyList<MatchTechnique> moves;
    private ListView list;
    private DropdownField performerSituation, receiverSituation, speed, chipShape;
    private TextField search;
    private Label title, english, details, count, unavailable;
    private LimbDamageIndicator targetBody;
    private Label targetRegion;
    private Button pause;
    private Toggle repeat;
    private Slider timeline;
    private VisualElement viewport;
    private float elapsed;
    private (Vector3 scale, Vector3 position, Vector3 facingScale, Material material, Mesh mesh, Mesh fillMesh, bool fillEnabled)[] chipDetails;
    private Mesh capsuleMesh;
    private Material capsuleOutline;

    public static bool CanPreview(MatchTechnique move) => move.Enabled &&
        MatchTechniques.IsSupported(move.Rules.Situation) && move.Action != MatchActionType.DoubleTeam;

    public static MatchEngineState BuildPreview(MatchTechnique move)
    {
        if (move == null || !CanPreview(move)) throw new ArgumentException("This technique has no singles preview.");
        var a = new MatchEngineParticipant { Id = "preview-actor", RingX = -.38f, FacingX = 1 };
        var b = new MatchEngineParticipant { Id = "preview-receiver", RingX = .15f, FacingX = -1 };
        var s = move.Rules.Situation;
        b.Posture = s switch
        {
            MoveSituation.GroundProne => MatchPosture.Prone,
            MoveSituation.GroundSeated => MatchPosture.Seated,
            MoveSituation.GroundKneeling => MatchPosture.Kneeling,
            _ => MatchTechniques.IsGrounded(s) ? MatchPosture.Supine : MatchPosture.Standing
        };
        b.IsDowned = b.Posture != MatchPosture.Standing;
        if (MatchTechniques.IsRear(s))
            b.FacingX = 1;
        if (move.Rules.ExecutionStyle == MoveExecutionStyle.Running && s != MoveSituation.CornerStanding)
        { a.RingX = -.92f; a.Position = MatchPosition.Ropes; }
        if (s == MoveSituation.CornerStanding)
        { b.RingX = b.RingZ = .92f; b.Position = MatchPosition.Corner; a.RingX = a.RingZ = -.35f; }
        if (s is MoveSituation.DivingStanding or MoveSituation.DivingGrounded or MoveSituation.TopRopeGrapple)
        {
            a.RingX = a.RingZ = .92f; a.Height = .5f; a.Position = MatchPosition.TopRope;
            a.FacingX = a.FacingZ = -.7071f;
            if (s == MoveSituation.TopRopeGrapple)
            {
                a.RingX = a.RingZ = .72f;
                b.RingX = b.RingZ = .92f; b.Height = .5f; b.Position = MatchPosition.TopRope;
                a.FacingX = a.FacingZ = .7071f; b.FacingX = b.FacingZ = -.7071f;
            }
        }
        if (s is MoveSituation.RingsideStanding or MoveSituation.RingsideGrounded)
        {
            a.Position = b.Position = MatchPosition.Outside;
            a.RingZ = b.RingZ = -1.2f; a.RingX = .35f; b.RingX = -.18f;
            a.Height = b.Height = -.4f; a.FacingZ = b.FacingZ = 0; a.FacingX = -1; b.FacingX = 1;
        }
        if (MatchTechniques.IsOutsideDive(s))
        {
            bool ropes = MatchTechniques.IsRopeDive(s);
            a.Position = ropes ? MatchPosition.Ropes : MatchPosition.TopRope;
            a.RingX = ropes ? 0 : .92f; a.RingZ = s == MoveSituation.RingsideDive ? .92f : -.92f;
            a.Height = ropes ? 0 : .5f; a.FacingX = 0; a.FacingZ = -1;
            b.Position = MatchPosition.Outside; b.RingX = a.RingX; b.RingZ = -1.3f;
            b.Height = -.4f; b.FacingX = 0; b.FacingZ = 1;
        }
        if (s == MoveSituation.Table)
        {
            a.Position = b.Position = MatchPosition.Outside; a.RingX = b.RingX = 1.3f;
            a.RingZ = -.25f; b.RingZ = .25f; a.Height = b.Height = -.4f;
            a.FacingX = b.FacingX = 0; a.FacingZ = 1; b.FacingZ = -1;
        }
        var facing = new Vector2(b.RingX - a.RingX, b.RingZ - a.RingZ).normalized;
        a.FacingX = facing.x; a.FacingZ = facing.y;
        float receiverFacing = MatchTechniques.IsRear(s) ? 1 : -1;
        b.FacingX = facing.x * receiverFacing; b.FacingZ = facing.y * receiverFacing;
        var state = new MatchEngineState { EngineVersion = 3, SpatialInitialized = true,
            Participants = new() { a, b }, ElapsedSeconds = 6, TargetDurationSeconds = 6 };
        var e = new MatchEngineEvent { Type = move.Action, PerformerId = a.Id, ReceiverId = b.Id,
            TechniqueId = move.Id, TechniqueName = move.Rules.Name, MoveId = move.Id,
            TargetRegion = move.Rules.TargetRegion, Result = MatchActionResult.FullSuccess,
            DurationSeconds = 6, Before = MatchEngineSnapshot.Capture(state),
            Impact = move.Action is MatchActionType.Probe or MatchActionType.TieUp ||
                move.Rules.Kind != MoveKind.Submission && move.Rules.MotionProfile is MoveMotionProfile.Chain or MoveMotionProfile.Hold ? 0 : MatchPhysics.AttackImpact(move.Action, b.IsDowned) };
        if (s == MoveSituation.Table)
        { e.Detail = "announce-table-break"; e.Result = MatchActionResult.TableBroken; e.Impact = 18; }
        b.Posture = move.Rules.EndPosture; b.IsDowned = b.Posture != MatchPosture.Standing;
        if (a.Position is MatchPosition.TopRope or MatchPosition.Ropes)
            a.Position = MatchTechniques.IsOutsideDive(s) ? MatchPosition.Outside : MatchPosition.Center;
        if (b.Position == MatchPosition.TopRope) b.Position = MatchPosition.Center;
        MatchMovement.Apply(state, e, e.Before);
        e.After = MatchEngineSnapshot.Capture(state);
        state.Events.Add(e);
        return state;
    }

    private void Start()
    {
        arena.enabled = false;
        chipDetails = new[] { arena.blue, arena.red }.Select(t =>
            (t.Find("Fill").localScale, t.Find("Fill").localPosition, t.Find("Facing").localScale,
                t.GetComponent<Renderer>().sharedMaterial, t.GetComponent<MeshFilter>().sharedMesh,
                t.Find("Fill").GetComponent<MeshFilter>().sharedMesh,
                t.Find("Fill").GetComponent<Renderer>().enabled)).ToArray();
        var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        capsuleMesh = capsule.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(capsule);
        capsuleOutline = new Material(chipDetails[0].material) { hideFlags = HideFlags.DontSave };
        capsuleOutline.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Front);
        moves = MoveTable.Load().Where(m => m.Enabled || !MatchTechniques.IsSupported(m.Rules.Situation)).ToList();
        var root = GetComponent<UIDocument>().rootVisualElement;
        root.Clear(); root.AddToClassList("move-preview");
        root.style.unityFont = arena.commentaryFont;
        root.styleSheets.Add(Resources.Load<StyleSheet>("MovePreview"));
        var header = new VisualElement { name = "preview-header" }; header.AddToClassList("header");
        var heading = new Label("기술 미리보기"); heading.AddToClassList("heading");
        heading.style.unityFont = arena.strongFont; header.Add(heading); root.Add(header);
        var body = new VisualElement(); body.AddToClassList("body"); root.Add(body);
        var sidebar = new VisualElement(); sidebar.AddToClassList("sidebar"); body.Add(sidebar);
        performerSituation = new DropdownField("시전자 상황", new List<string> { "전체" }.Concat(
            moves.Select(PerformerSituationName).Distinct()).ToList(), 0) { name = "preview-performer-situation" };
        receiverSituation = new DropdownField("접수자 상황", new List<string> { "전체" }.Concat(
            moves.Select(m => ReceiverSituationName(m.Rules.Situation)).Distinct()).ToList(), 0) { name = "preview-receiver-situation" };
        search = new TextField("기술 검색") { name = "preview-search" };
        sidebar.Add(performerSituation); sidebar.Add(receiverSituation); sidebar.Add(search);
        count = new Label(); count.AddToClassList("muted"); sidebar.Add(count);
        list = new ListView(visible, 42, () => new Label(), (element, index) =>
        {
            var move = visible[index]; var label = (Label)element;
            label.text = move.Rules.Name + (CanPreview(move) ? "" : " (준비 중)");
            label.EnableInClassList("unavailable-row", !CanPreview(move));
        }) { name = "preview-moves", selectionType = SelectionType.Single };
        list.selectionChanged += selection => { if (selection.FirstOrDefault() is MatchTechnique move) Select(move); };
        sidebar.Add(list);
        var right = new VisualElement(); right.AddToClassList("right"); body.Add(right);
        var info = new VisualElement(); info.AddToClassList("info"); right.Add(info);
        var description = new VisualElement(); description.AddToClassList("move-description"); info.Add(description);
        title = new Label(); title.AddToClassList("move-title"); title.style.unityFont = arena.strongFont;
        english = new Label(); english.AddToClassList("muted");
        details = new Label(); details.AddToClassList("details");
        description.Add(title); description.Add(english); description.Add(details);
        var target = new VisualElement(); target.AddToClassList("move-target"); info.Add(target);
        target.Add(new Label("공격 부위"));
        targetBody = new LimbDamageIndicator { name = "preview-target-body", pickingMode = PickingMode.Ignore };
        targetBody.AddToClassList("target-body"); target.Add(targetBody);
        targetRegion = new Label { name = "preview-target-region" }; target.Add(targetRegion);
        viewport = new VisualElement { name = "preview-viewport", pickingMode = PickingMode.Ignore };
        viewport.AddToClassList("viewport"); right.Add(viewport);
        unavailable = new Label(); unavailable.AddToClassList("unavailable-message"); viewport.Add(unavailable);
        var legend = new VisualElement(); legend.AddToClassList("legend");
        var actor = new Label("시전자"); actor.AddToClassList("actor");
        var receiver = new Label("접수자"); receiver.AddToClassList("receiver");
        legend.Add(actor); legend.Add(receiver); viewport.Add(legend);
        var controls = new VisualElement(); controls.AddToClassList("controls"); right.Add(controls);
        timeline = new Slider("재생 위치", 0, 1) { name = "preview-timeline" };
        timeline.RegisterValueChangedCallback(e => { if (Selected != null && CanPreview(Selected)) { SetPaused(true); Seek(e.newValue); } });
        controls.Add(timeline);
        var row = new VisualElement(); row.AddToClassList("control-row"); controls.Add(row);
        pause = new Button(() => SetPaused(!Paused)) { text = "일시정지", name = "preview-pause" };
        var restart = new Button(Restart) { text = "다시 재생", name = "preview-restart" };
        repeat = new Toggle("반복 재생") { value = true, name = "preview-repeat" };
        speed = new DropdownField("속도", new List<string> { "0.5×", "1×", "1.5×" }, 1) { name = "preview-speed" };
        chipShape = new DropdownField("표현", new List<string> { "원형 칩", "캡슐" }, 0) { name = "preview-shape" };
        chipShape.RegisterValueChangedCallback(_ =>
        {
            if (Selected == null || !CanPreview(Selected)) return;
            RestoreChips(); Render(Mathf.Clamp01(elapsed / ClipSeconds));
        });
        row.Add(pause); row.Add(restart); row.Add(repeat); row.Add(speed); row.Add(chipShape);
        search.RegisterValueChangedCallback(_ => Filter());
        performerSituation.RegisterValueChangedCallback(_ => Filter());
        receiverSituation.RegisterValueChangedCallback(_ => Filter());
        Filter();
        int initial = visible.FindIndex(m => m.Key == "vertical-suplex");
        list.SetSelection(Math.Max(0, initial));
        list.schedule.Execute(() => list.ScrollToItem(Math.Max(0, initial)));
    }

    private void Filter()
    {
        string selectedId = Selected?.Id;
        visible.Clear();
        visible.AddRange(moves.Where(m => (performerSituation.index == 0 || PerformerSituationName(m) == performerSituation.value) &&
            (receiverSituation.index == 0 || ReceiverSituationName(m.Rules.Situation) == receiverSituation.value) &&
            (m.Rules.Name.Contains(search.value ?? "", StringComparison.OrdinalIgnoreCase) ||
             m.EnglishName.Contains(search.value ?? "", StringComparison.OrdinalIgnoreCase))));
        list.Rebuild(); count.text = $"{visible.Count}개 기술";
        int index = visible.FindIndex(m => m.Id == selectedId);
        if (visible.Count > 0)
        {
            index = index >= 0 ? index : 0;
            list.SetSelection(index);
            if (Selected != visible[index]) Select(visible[index]);
        }
        else
        {
            Selected = null; title.text = "검색 결과 없음"; english.text = "";
            targetBody.SetDamage(0, 0, 0, 0); targetRegion.text = "없음";
            details.text = "다른 검색어를 입력하거나 상황을 전체로 변경하세요.";
            SetAvailable(false, "");
        }
    }

    private void RestoreChips()
    {
        for (int i = 0; i < chipDetails.Length; i++)
        {
            var marker = i == 0 ? arena.blue : arena.red;
            marker.Find("Fill").localScale = chipDetails[i].scale;
            marker.Find("Fill").localPosition = chipDetails[i].position;
            marker.Find("Facing").localScale = chipDetails[i].facingScale;
            marker.GetComponent<Renderer>().sharedMaterial = chipDetails[i].material;
            marker.GetComponent<Renderer>().SetPropertyBlock(null);
            marker.GetComponent<MeshFilter>().sharedMesh = chipDetails[i].mesh;
            marker.Find("Fill").GetComponent<MeshFilter>().sharedMesh = chipDetails[i].fillMesh;
            marker.Find("Fill").GetComponent<Renderer>().enabled = chipDetails[i].fillEnabled;
        }
    }

    public void Select(MatchTechnique move)
    {
        RestoreChips();
        Selected = move;
        title.text = move.Rules.Name; english.text = move.EnglishName;
        bool attacks = move.Action is not (MatchActionType.Probe or MatchActionType.TieUp or MatchActionType.Chain);
        var region = move.Rules.TargetRegion;
        targetBody.SetDamage(attacks && region == BodyRegion.Head ? 100 : 0, attacks && region == BodyRegion.Body ? 100 : 0,
            attacks && region == BodyRegion.Arms ? 100 : 0, attacks && region == BodyRegion.Legs ? 100 : 0);
        targetRegion.text = !attacks ? "없음" : region switch
        { BodyRegion.Head => "머리", BodyRegion.Body => "몸통", BodyRegion.Arms => "팔", _ => "다리" };
        string styles = move.AllowedStyleIds.Length == 0 ? "모든 스타일" : string.Join(" · ", move.AllowedStyleIds.Select(
            id => arena.catalog.WrestlingStyles.FirstOrDefault(s => s.Id == id)?.DisplayName ?? id));
        var roles = new List<string>();
        if (move.CanUseGenerally) roles.Add("일반 기술");
        if (move.CanBeSignature) roles.Add("시그니처 지정 가능");
        if (move.CanBeFinisher) roles.Add("피니셔 지정 가능");
        string execution = move.Rules.ExecutionStyle switch { MoveExecutionStyle.Running => "달리며 시전", MoveExecutionStyle.Aerial => "공중에서 시전", _ => "제자리에서 시전" };
        details.text = $"{SituationName(move.Rules.Situation)}  ·  {execution}  ·  {styles}\n{string.Join(" · ", roles)}";
        bool available = CanPreview(move);
        SetAvailable(available, move.Action == MatchActionType.DoubleTeam ? "더블팀은 3인 연출이 필요합니다." : "이 상황의 연출은 아직 준비 중입니다.");
        if (!available) return;
        arena.LoadMatch(BuildPreview(move));
        // Match the renderer's impact envelope to this preview's three-second clip.
        arena.playbackSpeed = 2;
        arena.playbackSpeed *= (6 / ClipSeconds) / arena.PlaybackRate(arena.CurrentEvent);
        Restart();
    }

    private void SetAvailable(bool available, string message)
    {
        arena.blue.gameObject.SetActive(available); arena.red.gameObject.SetActive(available);
        timeline.SetEnabled(available); pause.SetEnabled(available);
        chipShape.SetEnabled(available && Selected.Key == "vertical-suplex");
        GetComponent<UIDocument>().rootVisualElement.Q<Button>("preview-restart").SetEnabled(available);
        unavailable.text = available ? "" : message;
        unavailable.style.display = available || message.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
        viewport.Q(className: "legend").style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
        if (!available && arena.Engine != null) arena.Seek(0);
        foreach (var marker in new[] { arena.blue, arena.red })
        {
            var shadow = arena.transform.Find(marker.name + " Shadow");
            if (shadow) shadow.gameObject.SetActive(available);
        }
    }

    public void Seek(float progress)
    {
        if (Selected == null || !CanPreview(Selected)) return;
        elapsed = Mathf.Clamp01(progress) * ClipSeconds;
        Render(elapsed / ClipSeconds);
        timeline.SetValueWithoutNotify(Mathf.Clamp01(progress));
    }

    public void Restart() { Seek(0); SetPaused(false); }
    private void SetPaused(bool value) { Paused = value; pause.text = value ? "재생" : "일시정지"; }

    private void Render(float progress)
    {
        arena.Seek(progress * 6);
        var e = arena.CurrentEvent;
        bool suplex = Selected.Key == "vertical-suplex";
        bool capsule = suplex && chipShape.index == 1;
        if (!suplex && (Selected.Rules.Situation == MoveSituation.Table || e.Detail == "bridge" || e.Motion == null || e.Impact <= 0 || Selected.Rules.MotionProfile is
            MoveMotionProfile.Circle or MoveMotionProfile.HandFight or MoveMotionProfile.TieUp or MoveMotionProfile.Chain or
            MoveMotionProfile.Hold)) return;
        bool strike = Selected.Rules.Kind == MoveKind.Strike;
        float settling = Mathf.Clamp01((progress - (strike ? .8f : .65f)) / (strike ? .2f : .22f));
        float damping = (1 - settling) * (1 - settling);
        float rebound = Mathf.Abs(Mathf.Sin(settling * Mathf.PI * 2)) * damping;
        float rocking = Mathf.Sin(settling * Mathf.PI * 4) * damping;
        for (int i = 0; i < 2; i++)
        {
            var marker = i == 0 ? arena.blue : arena.red;
            var initial = e.Before.Participants[i];
            float strength = 1;
            var forward = new Vector3(initial.FacingX, 0, initial.FacingZ);
            if (!suplex)
            {
                var contact = MatchMovement.Sample(e, initial.Id, .65f);
                var approach = MatchMovement.Sample(e, initial.Id, .5f);
                var end = MatchMovement.Sample(e, initial.Id, 1);
                float descent = e.Motion.Where(f => f.Progress < .65f).Max(f => f.Participants.Single(p => p.Id == initial.Id).Height) - contact.Height;
                bool falls = initial.Posture == MatchPosture.Standing && contact.Posture != MatchPosture.Standing &&
                    new Vector2(contact.X - approach.X, contact.Z - approach.Z).magnitude > .025f;
                bool lands = descent > .08f && Math.Abs(contact.Height - end.Height) < .05f;
                bool groundedHit = i == 1 && initial.Posture != MatchPosture.Standing;
                if (!falls && !lands && !groundedHit) continue;
                strength = i == 0 ? contact.Posture is MatchPosture.Supine or MatchPosture.Prone ? 1 : .45f
                    : falls || lands ? 1 : .35f;
                strength *= Mathf.Clamp(.7f + e.Impact * .025f + Mathf.Max(0, descent) * .6f, .7f, 1.5f);
                forward = new Vector3(contact.X - approach.X, 0, contact.Z - approach.Z);
                if (forward.sqrMagnitude < .0001f)
                {
                    var other = MatchMovement.Sample(e, e.Before.Participants[1 - i].Id, .65f);
                    forward = new Vector3(contact.X - other.X, 0, contact.Z - other.Z);
                }
                if (forward.sqrMagnitude < .0001f) forward = new Vector3(initial.FacingX, 0, initial.FacingZ);
                forward.Normalize();
            }
            float bend = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .22f, progress));
            float lift = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.22f, .42f, progress));
            float fall = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.5f, .65f, progress));
            float roll = i == 0 ? Mathf.Lerp(15 * bend * (1 - lift), -180, fall)
                : Mathf.Lerp(Mathf.Lerp(0, 180, lift), 270, fall);
            float bounceHeight = rebound * (i == 0 ? .06f : .12f) * strength;
            roll += rocking * (i == 0 ? 5 : 10);
            var side = new Vector3(-forward.z, 0, forward.x);
            var recoil = (forward * rebound * (i == 0 ? .14f : .26f) + side * rocking * (i == 0 ? .025f : .055f)) * strength;
            if (strike) recoil = Vector3.zero;
            float bounceSize = 1 + bounceHeight / arena.halfRing * .75f;
            if (!capsule)
            {
                marker.localScale = new Vector3(marker.localScale.x * bounceSize, marker.localScale.y, marker.localScale.z * bounceSize);
            }
            else
            {
                marker.rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(roll, 0, 0);
                float size = marker.localScale.x / .6525f * bounceSize;
                marker.localScale = Vector3.one * (.55f * size);
                marker.GetComponent<MeshFilter>().sharedMesh = capsuleMesh;
                var fill = marker.Find("Fill");
                fill.GetComponent<MeshFilter>().sharedMesh = capsuleMesh;
                fill.localPosition = Vector3.zero; fill.localScale = Vector3.one * .96f;
                var body = marker.GetComponent<Renderer>();
                // Back faces of the outer shell draw the silhouette without blackening the colored sides.
                body.sharedMaterial = capsuleOutline;
                var facing = marker.Find("Facing");
                facing.localPosition = new Vector3(0, .965f, 0); facing.localScale = new Vector3(.16f, .01f, .16f);
                // A capsule's support height is its radius plus the vertical part of its straight section.
                float vertical = Mathf.Abs(marker.up.y);
                float extent = marker.localScale.x * .5f + vertical * marker.localScale.y * .5f;
                marker.position += Vector3.up * (extent + .02f - .12f);
            }
            marker.position += recoil + Vector3.up * bounceHeight;
            var shadow = arena.transform.Find(marker.name + " Shadow");
            if (shadow)
            {
                shadow.position += recoil + new Vector3(-.25f, 0, .35f) * bounceHeight;
                float shadowSize = 1 - bounceHeight / arena.halfRing * .25f;
                shadow.localScale = new Vector3(shadow.localScale.x * shadowSize, shadow.localScale.y, shadow.localScale.z * shadowSize);
            }
        }
        arena.UpdateFacingVisibility();
    }

    private void OnDestroy()
    {
        if (capsuleOutline) Destroy(capsuleOutline);
    }

    private void Update()
    {
        if (viewport == null) return;
        var root = GetComponent<UIDocument>().rootVisualElement;
        var area = viewport.worldBound; var screen = root.worldBound;
        if (screen.width > 0 && area.width > 0)
        {
            arena.viewCamera.rect = new Rect(area.x / screen.width, 1 - area.yMax / screen.height,
                area.width / screen.width, area.height / screen.height);
            arena.viewCamera.orthographicSize = Mathf.Max(4.7f, 4.7f / Mathf.Max(.3f, area.width / area.height));
        }
        Advance(Time.deltaTime);
    }

    private void Advance(float seconds)
    {
        if (Selected == null || !CanPreview(Selected) || Paused) return;
        elapsed += seconds * (speed.index == 0 ? .5f : speed.index == 2 ? 1.5f : 1) * arena.PlaybackRate(arena.CurrentEvent) / (6 / ClipSeconds);
        if (elapsed > ClipSeconds + .8f)
        {
            if (repeat.value) elapsed %= ClipSeconds + .8f;
            else { elapsed = ClipSeconds; SetPaused(true); }
        }
        float progress = Mathf.Clamp01(elapsed / ClipSeconds);
        Render(progress); timeline.SetValueWithoutNotify(progress);
    }

    private static string PerformerSituationName(MatchTechnique move) => move.Rules.Situation switch
    {
        MoveSituation.RunningRearStanding or MoveSituation.CornerRear => "후면 달리기",
        MoveSituation.StandingRearStrike or MoveSituation.StandingRearGrapple or MoveSituation.StandingRearSubmission => "후면",
        MoveSituation.DivingStanding or MoveSituation.DivingGrounded or MoveSituation.TopRopeGrapple or
            MoveSituation.DivingOutsideStanding or MoveSituation.DivingOutsideGrounded => "탑 로프",
        MoveSituation.TopRopeRearGrapple => "탑 로프 · 후면",
        MoveSituation.SpringboardStanding or MoveSituation.SpringboardGrounded or
            MoveSituation.SpringboardOutsideStanding or MoveSituation.SpringboardOutsideGrounded => "스프링보드",
        MoveSituation.RingsideDive => "달리기",
        MoveSituation.RingsideStanding or MoveSituation.RingsideGrounded or MoveSituation.OutsideToApron or MoveSituation.Table => "링 밖",
        MoveSituation.ApronBoth or MoveSituation.ApronToRing or MoveSituation.ApronToOutside => "에이프런",
        MoveSituation.RingToApron => "링 안",
        MoveSituation.DoubleTeamStanding or MoveSituation.DoubleTeamGrounded => "더블팀",
        MoveSituation.Carry => "상대를 들어 올림",
        MoveSituation.Ladder => "사다리 위",
        MoveSituation.Cage => "철창",
        _ => move.Rules.ExecutionStyle switch
        { MoveExecutionStyle.Running => "달리기", MoveExecutionStyle.Aerial => "공중", _ => "제자리" }
    };

    private static string ReceiverSituationName(MoveSituation s) => s switch
    {
        MoveSituation.GroundProne => "엎드려 있음",
        MoveSituation.GroundSeated or MoveSituation.RunningSeated => "앉아 있음",
        MoveSituation.GroundKneeling or MoveSituation.RunningKneeling => "무릎 꿇음",
        MoveSituation.GroundStrike or MoveSituation.GroundLimb or MoveSituation.GroundSubmission or
            MoveSituation.DivingGrounded or MoveSituation.RunningGrounded or MoveSituation.DoubleTeamGrounded or
            MoveSituation.SpringboardGrounded => "누워 있음",
        MoveSituation.CornerStanding or MoveSituation.CornerRear => "코너에 서 있음",
        MoveSituation.CornerSeated => "코너에 앉아 있음",
        MoveSituation.CornerHanging => "거꾸로 매달림",
        MoveSituation.TopRopeGrapple or MoveSituation.TopRopeRearGrapple => "탑 로프 위",
        MoveSituation.RingsideStanding or MoveSituation.DivingOutsideStanding or
            MoveSituation.SpringboardOutsideStanding or MoveSituation.RingsideDive => "링 밖 · 서 있음",
        MoveSituation.RingsideGrounded or MoveSituation.DivingOutsideGrounded or
            MoveSituation.SpringboardOutsideGrounded or MoveSituation.ApronToOutside => "링 밖 · 누워 있음",
        MoveSituation.ApronBoth or MoveSituation.RingToApron or MoveSituation.OutsideToApron => "에이프런 위",
        MoveSituation.ApronToRing => "링 안 · 서 있음",
        MoveSituation.RopeLeaning => "로프에 기댐",
        MoveSituation.RopeHung => "로프에 걸림",
        MoveSituation.RunningRebound => "로프 반동 중",
        MoveSituation.Carry => "들려 있음",
        MoveSituation.Table => "테이블 앞",
        MoveSituation.Ladder or MoveSituation.Cage => "접수자 없음",
        MoveSituation.Automatic => "기타",
        _ => "서 있음"
    };

    public static string SituationName(MoveSituation s) => s switch
    {
        MoveSituation.Probe => "탐색", MoveSituation.TieUp => "타이업", MoveSituation.Chain => "체인 레슬링",
        MoveSituation.StandingStrike => "서 있는 상대 · 타격", MoveSituation.StandingHeavyStrike => "서 있는 상대 · 강타",
        MoveSituation.StandingGrapple => "서 있는 상대 · 잡기", MoveSituation.StandingLimb => "서 있는 상대 · 부위 공격",
        MoveSituation.StandingSubmission => "서 있는 상대 · 서브미션", MoveSituation.GroundStrike => "누운 상대 · 타격",
        MoveSituation.GroundLimb => "누운 상대 · 부위 공격", MoveSituation.GroundSubmission => "누운 상대 · 서브미션",
        MoveSituation.DivingStanding => "탑 로프 · 서 있는 상대", MoveSituation.DivingGrounded => "탑 로프 · 누운 상대",
        MoveSituation.RunningStanding => "달리기 · 서 있는 상대", MoveSituation.RunningGrounded => "달리기 · 누운 상대",
        MoveSituation.RunningRearStanding => "후면 달리기 · 서 있는 상대",
        MoveSituation.CornerStanding => "코너 · 서 있는 상대", MoveSituation.RingsideStanding => "링 밖 · 서 있는 상대",
        MoveSituation.RingsideGrounded => "링 밖 · 누운 상대", MoveSituation.TopRopeGrapple => "탑 로프 · 잡기",
        MoveSituation.DoubleTeamStanding => "더블팀 · 서 있는 상대", MoveSituation.DoubleTeamGrounded => "더블팀 · 누운 상대",
        MoveSituation.StandingRearStrike => "후면 · 타격", MoveSituation.StandingRearGrapple => "후면 · 잡기",
        MoveSituation.StandingRearSubmission => "후면 · 서브미션", MoveSituation.GroundProne => "엎드린 상대",
        MoveSituation.GroundSeated => "앉은 상대", MoveSituation.GroundKneeling => "무릎 꿇은 상대",
        MoveSituation.RunningSeated => "달리기 · 앉은 상대", MoveSituation.RunningKneeling => "달리기 · 무릎 꿇은 상대",
        MoveSituation.RunningRebound => "로프 반동", MoveSituation.CornerRear => "코너 · 후면",
        MoveSituation.CornerSeated => "코너 · 앉은 상대", MoveSituation.CornerHanging => "코너 · 거꾸로 매달린 상대",
        MoveSituation.TopRopeRearGrapple => "탑 로프 · 후면 잡기", MoveSituation.DivingOutsideStanding => "탑 로프 · 링 밖 서 있는 상대",
        MoveSituation.DivingOutsideGrounded => "탑 로프 · 링 밖 누운 상대", MoveSituation.RopeLeaning => "로프에 기댄 상대",
        MoveSituation.RopeHung => "로프에 걸린 상대", MoveSituation.SpringboardStanding => "스프링보드 · 서 있는 상대",
        MoveSituation.SpringboardGrounded => "스프링보드 · 누운 상대", MoveSituation.SpringboardOutsideStanding => "스프링보드 · 링 밖 서 있는 상대",
        MoveSituation.SpringboardOutsideGrounded => "스프링보드 · 링 밖 누운 상대", MoveSituation.RingsideDive => "링 밖으로 다이브",
        MoveSituation.ApronBoth => "에이프런 · 잡기", MoveSituation.ApronToRing => "에이프런에서 링 안으로",
        MoveSituation.RingToApron => "링 안에서 에이프런으로", MoveSituation.ApronToOutside => "에이프런에서 링 밖으로",
        MoveSituation.OutsideToApron => "링 밖에서 에이프런으로", MoveSituation.Carry => "들어 올린 상태",
        MoveSituation.Ladder => "사다리", MoveSituation.Table => "테이블", MoveSituation.Cage => "철창", _ => "기타"
    };
}
