using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

// Replays engine events without changing the player's save or the engine's results.
public sealed partial class TopViewArenaTest : MonoBehaviour
{
    public Camera viewCamera;
    public Transform blue, red;
    public LineRenderer[] ropes;
    public StaticContentCatalog catalog;
    public Font commentaryFont, strongFont;
    public GameObject impactPrefab;
    public float HitEnvelope { get; private set; }
    public float SubmissionEnvelope { get; private set; }
    public float ControlEnvelope { get; private set; }
    public string CommentaryText { get; private set; }
    public Vector3 ringCenter, entrance;
    public float halfRing = 2.845f, ropeEnd = 2.601f, ropeStrength = .55f;
    public int seed = 13, matchDurationSeconds = 300;
    public float playbackSpeed = 2;
    public bool automaticCamera = true, paused;
    public float clock;
    public MatchEngineState Engine { get; private set; }
    public int EventIndex { get; private set; }
    public MatchEngineEvent CurrentEvent => Engine?.Events[EventIndex];
    public bool Finished => Engine != null && clock >= Engine.ElapsedSeconds;

    private struct Frame { public Vector3[] from, to; }
    private struct Contact { public float time, along, secondsPerEngineSecond, strength; public int side; }
    private readonly List<Frame> frames = new();
    private readonly List<Contact> contacts = new();
    private List<MatchEngineEvent> replayMotions;
    private Transform[] markers;
    private readonly List<Transform> additionalMarkers = new();
    public Transform MarkerFor(string participantId) => Engine == null || Index(participantId) < 0 ? null : markers[Index(participantId)];
    private Vector3 target;
    private float targetSize = 6;
    private Vector2 previousMouse;
    private Label commentary;
    private Label matchTime;
    private VisualElement statesRow, commentaryBar;
    private MatchResultState narrationMatch;
    public WrestlerState[] Wrestlers { get; private set; }
    private GameSave previewSave, previousSave;
    private GameObject profileObject;
    private bool previousTransient;
    private VisualElement profileScreen;
    public bool ViewingProfile => profileScreen != null && profileScreen.style.display.value == DisplayStyle.Flex;
    private int narratedIndex = -1;
    private Label[] stateLabels;
    private ProgressBar[][] stateBars;
    private LimbDamageIndicator[] damageIndicators;
    private ParticleSystem impactEffect;
    private Renderer[] markerFills;
    private Color[] markerColors;
    [SerializeField] private Vector3[] markerBaseScales;
    private Transform[] markerShadows;
    private Material shadowMaterial;
    private MaterialPropertyBlock flash;
    private Vector3 cameraShake;
    private AnnounceTableReplay announceTable;
    private MatchPosture[] markerPostures;
    private Mesh chipTriangle, chipCircle, chipFacingCylinder;
    private Material chipFacingMaterial;
    private MaterialPropertyBlock chipFill;
    public Renderer[] crowdPanels = Array.Empty<Renderer>();
    public float ringsideViewSize = 5;
    private MaterialPropertyBlock crowdFill;
    private Vector3[] crowdPanelRestPositions;
    private float crowdMotionTime;
    private float crowdAccent;

    public static float RopeOffset(float along, float contact, float elapsed, float strength)
    {
        if (elapsed < 0 || elapsed > 2.5f) return 0;
        float envelope = Mathf.Sin(Mathf.PI * Mathf.Clamp01(along));
        float local = Mathf.Exp(-Mathf.Pow((along - contact) * 4, 2));
        return strength * envelope * local * Mathf.Exp(-elapsed * 2.8f) * Mathf.Sin(elapsed * 14);
    }

    private void Start()
    {
        if (gameObject.scene.name == "TopView Show Simulation") { StartShowSimulation(); return; }
        InitializeMatchReview();
        if (Engine == null) InitializeMatch();
        target = ringCenter;
        CreateCommentary();
        CreateProfile();
        CreateMatchReviewControls();
        Seek(clock);
        UpdateCommentary();
    }

    public void InitializeMatch()
    {
        if (!catalog) throw new InvalidOperationException("Assign the game's static content catalog.");
        var content = new StaticContentRegistry(catalog);
        int id = 0;
        var generator = new WrestlerGenerator(content, seed, () => $"topview-{++id}");
        Wrestlers = Enumerable.Range(0, 2).Select(_ =>
            generator.GenerateCandidate((WrestlerGender)0, new GameDate(2026, 6, 1))).ToArray();
        var participants = Wrestlers.Select(wrestler =>
        {
            return new MatchEngineParticipant
            {
                Id = wrestler.Id, Attributes = wrestler.Attributes, WrestlingStyleId = wrestler.Presentation.WrestlingStyleId,
                Alignment = wrestler.Roster.Alignment,
                TargetRegion = wrestler.Presentation.FinisherMoveIds.Select(content.GetMoveRules).FirstOrDefault()?.TargetRegion ??
                    (wrestler.Attributes.Power > wrestler.Attributes.Technical ? BodyRegion.Body : BodyRegion.Legs),
                SignatureMoveIds = wrestler.Presentation.SignatureMoveIds.ToList(),
                FinisherMoveIds = wrestler.Presentation.FinisherMoveIds.ToList()
            };
        }).ToArray();
        LoadMatch(new MatchSimulator(seed, findMove: content.GetMoveRules).Simulate(participants, matchDurationSeconds,
            new MatchEngineBooking { WinnerId = participants[0].Id, LoserId = participants[1].Id, FinishType = MatchFinishType.Pinfall }));
        if (previewSave != null)
        {
            previewSave.Wrestlers = Wrestlers.ToList();
            WrestlerProfileController.CloseOpenProfile(false);
            CreateCommentary();
        }
    }

    // Existing evaluated matches can use the same visual playback through this entry point.
    public void LoadMatch(MatchEngineState state)
    {
        if (state == null || state.IsAborted || state.Participants.Count < 2 || state.Events.Count == 0 ||
            state.Participants.Select(p => p.Id).Distinct().Count() != state.Participants.Count ||
            state.Events.Any(e => e.Before?.Participants.Count != state.Participants.Count || e.After?.Participants.Count != state.Participants.Count ||
                state.Participants.Any(p => !e.Before.Participants.Any(x => x.Id == p.Id) || !e.After.Participants.Any(x => x.Id == p.Id))))
            throw new ArgumentException("Playback requires distinct participants with complete snapshots.");
        bool rosterChanged = Engine == null || !Engine.Participants.Select(p => p.Id).SequenceEqual(state.Participants.Select(p => p.Id));
        if (Wrestlers == null || state.Participants.Any(p => !Wrestlers.Any(w => w?.Id == p.Id)))
            Wrestlers = state.Participants.Select(p => DashboardSession.ActiveSave?.Wrestlers.FirstOrDefault(w => w.Id == p.Id)).ToArray();
        Engine = state;
        announceTable = GetComponent<AnnounceTableReplay>() ?? gameObject.AddComponent<AnnounceTableReplay>();
        announceTable.hideFlags = HideFlags.DontSave;
        announceTable.Load(this);
        narrationMatch = new MatchResultState { EngineState = state };
        narratedIndex = -1;
        if (markerBaseScales == null) markerBaseScales = new[] { blue.localScale, red.localScale };
        while (additionalMarkers.Count < state.Participants.Count - 2)
        {
            var marker = Instantiate(blue, transform); marker.name = "Participant " + (additionalMarkers.Count + 3);
            marker.localScale = markerBaseScales[0]; additionalMarkers.Add(marker);
        }
        for (int i = 0; i < additionalMarkers.Count; i++) additionalMarkers[i].gameObject.SetActive(i < state.Participants.Count - 2);
        if (markerShadows != null) foreach (var shadow in markerShadows) if (shadow) shadow.gameObject.SetActive(false);
        markers = new[] { blue, red }.Concat(additionalMarkers.Take(state.Participants.Count - 2)).ToArray();
        markerPostures = state.Events[0].Before.Participants.Select(p => p.Posture).ToArray();
        markerBaseScales = Enumerable.Range(0, markers.Length).Select(i => markerBaseScales[Mathf.Min(i, 1)]).ToArray();
        markerFills = markers.Select(t => t.Find("Fill").GetComponent<Renderer>()).ToArray();
        markerColors = markerFills.Select(r => r.sharedMaterial.GetColor("_BaseColor")).ToArray();
        for (int i = 2; i < markers.Length; i++) markerColors[i] = i % 2 == 0 ? new Color(.95f, .65f, .12f) : new Color(.15f, .75f, .48f);
        if (previewSave != null && previewSave != showSimulationSave) previewSave.Wrestlers = Wrestlers.Where(w => w != null).ToList();
        if (statesRow != null && rosterChanged && Wrestlers.All(w => w != null))
        { var root = statesRow.parent; statesRow.RemoveFromHierarchy(); CreateStates(root); }
        frames.Clear(); contacts.Clear();
        var positions = Enumerable.Range(0, markers.Length).Select(i => state.EngineVersion >= 3
            ? WorldPosition(Snapshot(state.Events[0].Before, i)) : Anchor(Snapshot(state.Events[0].Before, i).Position, i)).ToArray();
        foreach (var e in state.Events)
        {
            var from = positions.ToArray();
            if (e.Motion != null)
            {
                for (int i = 0; i < markers.Length; i++) positions[i] = WorldPosition(Snapshot(e.After, i));
            }
            else
            {
                for (int i = 0; i < markers.Length; i++)
                    if (Snapshot(e.After, i).Position != Snapshot(e.Before, i).Position)
                        positions[i] = Anchor(Snapshot(e.After, i).Position, i);
                int actor = Index(e.PerformerId), receiver = Index(e.ReceiverId);
                if (actor >= 0 && receiver >= 0) PlanMovement(e, actor, receiver, from, positions);
            }
            frames.Add(new Frame { from = from, to = positions.ToArray() });
            for (int i = 0; i < markers.Length; i++)
            {
                var region = Snapshot(e.After, i).Position;
                if ((region is MatchPosition.Ropes or MatchPosition.Corner or MatchPosition.TopRope) &&
                    (region != Snapshot(e.Before, i).Position || e.Type == MatchActionType.RopeBreak))
                {
                    Vector3 p = positions[i] - ringCenter;
                    int side = Mathf.Abs(p.x) > Mathf.Abs(p.z) ? p.x > 0 ? 1 : 3 : p.z > 0 ? 2 : 0;
                    contacts.Add(new Contact { time = e.MatchTimeSeconds + e.DurationSeconds * (e.MotionSeconds > 0 ? 1 : .65f), side = side,
                        secondsPerEngineSecond = e.MotionSeconds > 0 ? e.MotionSeconds / e.DurationSeconds : 1,
                        strength = 1,
                        along = Mathf.InverseLerp(-ropeEnd, ropeEnd, side % 2 == 0 ? p.x : p.z) });
                }
                var before = Snapshot(e.Before, i);
                if (before.Position != MatchPosition.TopRope || e.Motion == null) continue;
                var initial = e.Motion[0].Participants.Single(p => p.Id == before.Id);
                int departure = e.Motion.FindIndex(f =>
                {
                    var p = f.Participants.Single(p => p.Id == before.Id);
                    return Mathf.Abs(p.Height - initial.Height) > .02f ||
                        new Vector2(p.X - initial.X, p.Z - initial.Z).sqrMagnitude > .0001f;
                });
                if (departure <= 0) continue;
                float takeoff = e.MatchTimeSeconds + e.Motion[departure - 1].Progress * e.DurationSeconds;
                float rate = Mathf.Max(.01f, PlaybackRate(e));
                contacts.Add(new Contact { time = takeoff, side = initial.X > 0 ? 1 : 3, strength = .2f,
                    secondsPerEngineSecond = 1 / rate,
                    along = Mathf.InverseLerp(-ropeEnd, ropeEnd, initial.Z * halfRing) });
                contacts.Add(new Contact { time = takeoff, side = initial.Z > 0 ? 2 : 0, strength = .2f,
                    secondsPerEngineSecond = 1 / rate,
                    along = Mathf.InverseLerp(-ropeEnd, ropeEnd, initial.X * halfRing) });
            }
        }
        BuildReplayMotion();
        Seek(0);
    }

    private Vector3 Boundary(Vector3 direction, bool corner = false)
    {
        direction = FlatDirection(direction, 0);
        if (corner)
            return ringCenter + new Vector3(direction.x >= 0 ? 2.3f : -2.3f, .12f, direction.z >= 0 ? 2.3f : -2.3f);
        return ringCenter + (Mathf.Abs(direction.x) > Mathf.Abs(direction.z)
            ? new Vector3(Mathf.Sign(direction.x) * (halfRing - .3f), .12f, Mathf.Clamp(direction.z, -.6f, .6f))
            : new Vector3(Mathf.Clamp(direction.x, -.6f, .6f), .12f, Mathf.Sign(direction.z) * (halfRing - .3f)));
    }

    private void PlanMovement(MatchEngineEvent e, int actor, int receiver, Vector3[] from, Vector3[] positions)
    {
        var direction = FlatDirection(from[actor] - from[receiver], actor);
        var across = new Vector3(-direction.z, 0, direction.x);
        if (e.Type == MatchActionType.ExitRing)
        {
            var edge = Boundary(direction);
            positions[actor] = edge + FlatDirection(edge - ringCenter, actor) * .9f;
            positions[actor].y = .12f;
            positions[receiver] = from[receiver];
        }
        else if (e.Type == MatchActionType.Pursue)
        {
            positions[receiver] = from[receiver];
            var outward = FlatDirection(from[receiver] - ringCenter, actor);
            positions[actor] = from[receiver] + new Vector3(-outward.z, 0, outward.x) * .9f;
            positions[actor].y = .12f;
        }
        else if (e.Type == MatchActionType.RingsideAttack)
        {
            positions[receiver] = from[receiver] - direction * (e.Impact > 0 ? .18f : 0);
            positions[actor] = positions[receiver] + direction * .72f;
        }
        else if (e.Detail == "ringside-return")
        {
            positions[receiver] = ClampCenter(from[receiver] - across * .45f);
            positions[actor] = ClampCenter(from[receiver] + across * .45f);
        }
        else if (e.Detail == "aerial-interrupted")
        {
            positions[receiver] = from[receiver];
            positions[actor] = from[receiver] + FlatDirection(ringCenter - from[receiver], actor) * .72f;
        }
        else if (e.Detail == "crowd-pause")
        {
            positions[actor] = from[actor]; positions[receiver] = from[receiver];
        }
        else if (e.Type is MatchActionType.IrishWhip or MatchActionType.CornerWhip)
        {
            int runner = e.Result == MatchActionResult.Countered ? actor : receiver;
            int sender = runner == actor ? receiver : actor;
            // ponytail: v2 replays have regions only; v3 uses recorded motion.
            float angle = e.ExchangeId * 2.39996f;
            positions[runner] = Boundary(new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)), e.Type == MatchActionType.CornerWhip);
            positions[sender] = ClampCenter(from[sender]);
        }
        else if (e.Type == MatchActionType.RopeRun)
        {
            positions[actor] = e.Detail == "rebound" ? ClampCenter(from[receiver] + direction * .95f) : Boundary(direction);
            positions[receiver] = from[receiver];
        }
        else if (e.Type == MatchActionType.Evade)
        {
            positions[actor] = ClampCenter(from[actor] + across * 1.25f);
            positions[receiver] = Boundary(direction);
        }
        else if (e.Type == MatchActionType.CornerAttack)
        {
            var corner = from[receiver];
            if (e.Detail == "missed-charge")
            {
                positions[actor] = corner;
                positions[receiver] = ClampCenter(corner + across * 1.25f + FlatDirection(ringCenter - corner, receiver) * .8f);
            }
            else
            {
                positions[receiver] = corner;
                positions[actor] = corner + FlatDirection(ringCenter - corner, actor) * .8f;
            }
        }
        else if (e.Type == MatchActionType.Retreat || e.Type == MatchActionType.Rest && e.Detail != "delayed-cover" ||
            e.Result == MatchActionResult.Escaped && e.Type is MatchActionType.Hold or MatchActionType.Chain ||
            e.Type == MatchActionType.RopeBreak || e.Type == MatchActionType.Pin && e.Result is MatchActionResult.OneCount or MatchActionResult.TwoCount or MatchActionResult.NearFall)
        {
            positions[receiver] = from[receiver];
            positions[actor] = e.Detail == "corner-run-up"
                ? ClampCenter(ringCenter + FlatDirection(ringCenter - from[receiver], actor) * 1.35f)
                : e.Type == MatchActionType.RopeBreak ? from[actor] : ClampCenter(from[actor] + direction * .8f + across * .35f);
            if (e.Type == MatchActionType.Chain && e.Result == MatchActionResult.Escaped)
                positions[receiver] = ClampCenter(from[receiver] - direction * .8f);
            if (e.Type == MatchActionType.RopeBreak)
                positions[receiver] = ClampCenter(from[receiver] - direction * .8f);
        }
        else if (e.Type == MatchActionType.Probe)
        {
            float angle = frames.Count * .7f + .8f;
            positions[actor] = ringCenter + new Vector3(Mathf.Cos(angle) * 1.1f, .12f, Mathf.Sin(angle) * 1.1f);
            positions[receiver] = ringCenter + new Vector3(-Mathf.Cos(angle) * 1.1f, .12f, -Mathf.Sin(angle) * 1.1f);
        }
        else if (CloseContact(e.Type) && Snapshot(e.After, actor).Position is MatchPosition.Center or MatchPosition.Corner)
        {
            float gap = e.Type is MatchActionType.Pin or MatchActionType.Cover ? .3f : .72f;
            positions[actor] = ClampCenter(positions[receiver] + direction * gap);
            if (e.Impact > 0 && Snapshot(e.After, receiver).Position == MatchPosition.Center)
            {
                var technique = MatchTechniques.Find(e);
                float travel = e.Type == MatchActionType.RunningAttack || technique?.Rules.Kind == MoveKind.Grapple ? .9f : Mathf.Min(.4f, e.Impact * .025f);
                positions[receiver] = ClampCenter(positions[receiver] - direction * travel);
                positions[actor] = ClampCenter(positions[receiver] + direction * gap);
            }
        }
    }
    private int Index(string id) => Engine.Participants.FindIndex(p => p.Id == id);
    private float WorldHeight(float height) => .12f + (height < 0 ? height / .4f * ringCenter.y : height * halfRing);
    private Vector3 WorldPosition(MatchParticipantSnapshot pose) => ringCenter + new Vector3(pose.RingX * halfRing, WorldHeight(pose.Height), pose.RingZ * halfRing);
    private MatchParticipantSnapshot Snapshot(MatchEngineSnapshot snapshot, int i) => snapshot.Participants.Single(p => p.Id == Engine.Participants[i].Id);

    private Vector3 Anchor(MatchPosition position, int i)
    {
        float sign = i == 0 ? -1 : 1;
        // Old replays have no spatial snapshots.
        return ringCenter + (position switch
        {
            MatchPosition.Ropes => new Vector3(.6f * sign, .12f, sign * (halfRing - .12f)),
            MatchPosition.Corner => new Vector3(sign * 2.35f, .12f, sign * 2.35f),
            MatchPosition.TopRope => new Vector3(sign * ropeEnd, 1.52f, sign * ropeEnd),
            MatchPosition.Apron => new Vector3(.7f * sign, .12f, sign * (halfRing + .35f)),
            MatchPosition.Outside => new Vector3(.7f * sign, -ringCenter.y + .12f, sign * (halfRing + 1.1f)),
            _ => new Vector3(sign * 1.1f, .12f, 0)
        });
    }

    private Vector3 ClampCenter(Vector3 p) => ringCenter + new Vector3(Mathf.Clamp(p.x - ringCenter.x, -1.95f, 1.95f), .12f, Mathf.Clamp(p.z - ringCenter.z, -1.95f, 1.95f));
    private static Vector3 FlatDirection(Vector3 direction, int i)
    {
        direction.y = 0;
        return direction.sqrMagnitude < .001f ? (i == 0 ? Vector3.left : Vector3.right) : direction.normalized;
    }
    private static bool CloseContact(MatchActionType type) => type is MatchActionType.BasicStrike or MatchActionType.HeavyStrike or
        MatchActionType.Grapple or MatchActionType.GroundAttack or MatchActionType.Submission or MatchActionType.Pin or
        MatchActionType.Signature or MatchActionType.Finisher or MatchActionType.Dive or MatchActionType.LimbAttack or
        MatchActionType.RunningAttack or MatchActionType.CornerAttack or MatchActionType.StandingHold or MatchActionType.PrepareAttack or MatchActionType.TopRopeAttack or MatchActionType.RingsideAttack or MatchActionType.Counter or MatchActionType.Cover or MatchActionType.Hold or MatchActionType.Lift or MatchActionType.TieUp or MatchActionType.Chain;

    public void SetView(int preset)
    {
        automaticCamera = false;
        target = preset == 0 ? Vector3.Lerp(ringCenter, entrance, .38f) : preset == 2 ? entrance : ringCenter;
        targetSize = preset == 0 ? 15 : preset == 2 ? 7 : Mathf.Max(6, ringsideViewSize);
    }

    public void RestartDemo() { paused = false; Seek(0); automaticCamera = true; }

    // Keep engine time and outcomes intact; only compress the screen time of each action.
    public float PlaybackRate(MatchEngineEvent e, float? atProgress = null)
    {
        if (e == null) return 0;
        if (e.Detail == "double-down") return e.DurationSeconds * Mathf.Max(0, playbackSpeed) / 2.8f;
        if (e.Detail == "double-rise") return e.DurationSeconds * Mathf.Max(0, playbackSpeed) / 1.4f;
        if (e.MotionSeconds > 0) return e.DurationSeconds * Mathf.Max(0, playbackSpeed) / (2 * e.MotionSeconds);
        float seconds = e.Type switch
        {
            MatchActionType.BasicStrike or MatchActionType.GroundAttack or MatchActionType.LimbAttack or
                MatchActionType.Counter or MatchActionType.RingsideAttack => .9f,
            MatchActionType.HeavyStrike or MatchActionType.RunningAttack or MatchActionType.CornerAttack => 1.1f,
            MatchActionType.Grapple => 1.35f,
            MatchActionType.Signature or MatchActionType.Finisher or MatchActionType.Dive or MatchActionType.TopRopeAttack => 1.6f,
            MatchActionType.IrishWhip or MatchActionType.CornerWhip or MatchActionType.RopeRun or MatchActionType.Evade => .9f,
            MatchActionType.Submission or MatchActionType.StandingHold or MatchActionType.Hold => 1.8f,
            MatchActionType.Pin or MatchActionType.Cover => 2.1f,
            MatchActionType.Rest => 1.5f,
            _ => 1.2f
        };
        var move = MatchTechniques.Find(e);
        bool charging = move?.Rules.ExecutionStyle == MoveExecutionStyle.Running;
        bool quickStrike = move?.Rules.ExecutionStyle == MoveExecutionStyle.Stationary && move.Rules.Kind == MoveKind.Strike &&
            move.Rules.MotionProfile is MoveMotionProfile.Strike or MoveMotionProfile.Kick;
        float progress = atProgress ?? Mathf.Clamp01((clock - e.MatchTimeSeconds) / Math.Max(1, e.DurationSeconds));
        float burst = charging ? Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.25f, .3f, progress)) *
            (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.6f, .65f, progress))) : 0;
        float snap = quickStrike ? Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.5f, .55f, progress)) *
            (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.6f, .65f, progress))) : 0;
        return e.DurationSeconds * Mathf.Max(0, playbackSpeed) / (2 * seconds) * (1 + .6f * burst + .8f * snap);
    }

    private void CreateProfile()
    {
        if (Wrestlers == null || Wrestlers.Any(w => w == null))
            throw new InvalidOperationException("Load the match's wrestler records before viewing it.");
        previousSave = DashboardSession.ActiveSave;
        previousTransient = DashboardSession.IsTransient;
        previewSave = GameSave.CreateNew(seed, DateTime.UtcNow);
        previewSave.Wrestlers = Wrestlers.ToList();
        DashboardSession.SetActiveSave(previewSave, transient: true);
        var ui = profileObject = new GameObject("Top View Wrestler Profile");
        ui.SetActive(false);
        var document = ui.AddComponent<UIDocument>();
        document.panelSettings = GetComponent<UIDocument>().panelSettings;
        document.sortingOrder = 100;
        document.visualTreeAsset = Resources.Load<VisualTreeAsset>("PWManagerUI/WrestlerProfile");
        ui.AddComponent<WrestlerProfileController>();
        ui.SetActive(true);
        document.rootVisualElement.pickingMode = PickingMode.Ignore;
        profileScreen = document.rootVisualElement.Q("wrestler-profile");
    }

    public void Seek(float seconds)
    {
        if (Engine != null && frames.Count != Engine.Events.Count) LoadMatch(Engine);
        clock = Mathf.Clamp(seconds, 0, Engine?.ElapsedSeconds ?? 0);
        if (Engine != null)
        {
            EventIndex = Engine.Events.FindLastIndex(e => e.MatchTimeSeconds <= clock);
            var e = CurrentEvent;
            var frame = frames[EventIndex];
            float progress = Mathf.Clamp01((clock - e.MatchTimeSeconds) / e.DurationSeconds);
            announceTable?.Sample(clock);
            int actor = Index(e.PerformerId), receiver = Index(e.ReceiverId);
            float hitAge = (clock - e.MatchTimeSeconds - e.DurationSeconds * .65f) / Mathf.Max(.01f, PlaybackRate(e));
            bool hit = IsHit(e);
            var rules = MatchTechniques.Find(e)?.Rules;
            bool strike = hit && rules?.Kind == MoveKind.Strike && rules.MotionProfile is MoveMotionProfile.Strike or MoveMotionProfile.Kick;
            bool needsContact = false;
            bool chargingContact = e.Motion != null && rules?.ExecutionStyle == MoveExecutionStyle.Running &&
                e.Motion.Any(f => f.Progress == .65f && f.Participants.Any(p => p.Id == e.PerformerId && p.HasVelocity));
            if (strike && e.Motion != null && actor >= 0 && receiver >= 0)
            {
                var a = MatchMovement.Sample(e, e.PerformerId, .65f);
                var b = MatchMovement.Sample(e, e.ReceiverId, .65f);
                float radius = (markerBaseScales[actor].x * MarkerSize(a.Height) +
                    markerBaseScales[receiver].x * MarkerSize(b.Height)) * .5f;
                needsContact = new Vector2(a.X - b.X, a.Z - b.Z).magnitude * halfRing > radius;
            }
            for (int i = 0; i < markers.Length; i++)
            {
                if (e.Motion != null)
                {
                    float sample = needsContact && i == receiver && progress > .5f ? progress <= .65f ? .5f :
                        progress < .8f ? progress - .15f : .65f + (progress - .8f) * 1.75f : progress;
                    var pose = MatchMovement.Sample(replayMotions[EventIndex], Engine.Participants[i].Id, sample);
                    if (needsContact && i == receiver && progress > .65f && progress < .8f)
                    {
                        var waiting = MatchMovement.Sample(e, e.ReceiverId, .5f);
                        var knocked = MatchMovement.Sample(e, e.ReceiverId, .65f);
                        float flight = 1 - Mathf.Pow(1 - Mathf.InverseLerp(.65f, .8f, progress), 3);
                        pose.X = Mathf.Lerp(waiting.X, knocked.X, flight);
                        pose.Z = Mathf.Lerp(waiting.Z, knocked.Z, flight);
                    }
                    markers[i].position = ringCenter + new Vector3(pose.X * halfRing, WorldHeight(pose.Height), pose.Z * halfRing);
                    if (announceTable) markers[i].position += announceTable.PlayerOffset(e, Engine.Participants[i].Id, progress);
                    markers[i].rotation = Quaternion.LookRotation(new Vector3(pose.FacingX, 0, pose.FacingZ), Vector3.up);
                    if (announceTable) markers[i].rotation = announceTable.PlayerRotation * markers[i].rotation;
                    UpdateMarkerVisual(i);
                    markerPostures[i] = pose.Posture;
                    bool aerialActor = i == actor && rules?.ExecutionStyle == MoveExecutionStyle.Aerial && rules.Kind == MoveKind.Aerial;
                    bool flips = e.Motion.Any(f => f.Participants.Any(p => p.Id == pose.Id && Mathf.Abs(p.Tilt) >= 180));
                    if (aerialActor || flips)
                    {
                        float tilt = pose.Tilt;
                        var landing = MatchMovement.Sample(e, pose.Id, .65f);
                        if (aerialActor)
                        {
                            float landingTilt = landing.Tilt;
                            // Back landings remove their directional quarter-turn; belly landings use the forward fall.
                            float baseTilt = landing.Posture == MatchPosture.Prone ? 90 : Mathf.Sign(landingTilt) * 90;
                            float flat = Mathf.Abs(landingTilt) >= 90 ? landingTilt - baseTilt : 0;
                            if (Mathf.Abs(landingTilt) >= 90)
                            {
                                tilt *= flat / landingTilt;
                                if (sample >= .65f) tilt = flat;
                            }
                            else if (landing.Posture == MatchPosture.Prone)
                            {
                                tilt += Mathf.Sin(Mathf.PI * Mathf.InverseLerp(.2f, .65f, sample)) * 35;
                                if (sample >= .65f) tilt = flat;
                            }
                        }
                        // Resting poses reset Tilt to zero; keep the completed turn instead of unwinding it.
                        else if (sample >= .65f) tilt = landing.Tilt;
                        markers[i].rotation *= Quaternion.Euler(tilt, 0, 0);
                        markers[i].localScale = new Vector3(markers[i].localScale.x, .045f, markers[i].localScale.z);
                        float vertical = Mathf.Abs(markers[i].up.y);
                        float extent = Mathf.Sqrt(Mathf.Max(0, 1 - vertical * vertical)) * markers[i].localScale.x * .5f + vertical * .0675f;
                        var position = markers[i].position;
                        float floor = Mathf.Max(Mathf.Abs(pose.X), Mathf.Abs(pose.Z)) > 1 ? 0 : ringCenter.y;
                        position.y = Mathf.Max(position.y, floor + extent + .02f);
                        markers[i].position = position;
                    }
                    continue;
                }
                Vector3 p = Vector3.Lerp(frame.from[i], frame.to[i], Mathf.SmoothStep(0, 1, Mathf.Clamp01(progress / .65f)));
                if (hit && i == receiver)
                {
                    float recoil = Mathf.Clamp01(hitAge / .1f);
                    p = Vector3.Lerp(frame.from[i], frame.to[i], Mathf.SmoothStep(0, 1, recoil));
                    p += FlatDirection(frame.to[receiver] - frame.to[actor], receiver) * Mathf.Sin(recoil * Mathf.PI) * (.08f + e.Impact * .01f);
                }
                if ((i == actor || i == receiver) && (e.Type == MatchActionType.Probe || e.Type == MatchActionType.Chain && e.Result != MatchActionResult.Escaped))
                {
                    var axis = FlatDirection(frame.to[receiver] - frame.to[actor], i);
                    p += new Vector3(-axis.z, 0, axis.x) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(progress / .65f)) * (i == actor ? .35f : -.35f);
                }
                if (e.Type == MatchActionType.Evade && i == actor)
                {
                    p = Vector3.Lerp(frame.from[i], frame.to[i], Mathf.SmoothStep(0, 1, Mathf.Clamp01(progress / .28f)));
                    if (e.Detail == "leapfrog") p.y += Mathf.Sin(Mathf.PI * Mathf.Clamp01(progress / .65f)) * .85f;
                }
                if (e.Type == MatchActionType.CornerAttack && e.Detail == "missed-charge" && i == receiver)
                    p = Vector3.Lerp(frame.from[i], frame.to[i], Mathf.SmoothStep(0, 1, Mathf.Clamp01(progress / .3f)));
                float pulse = progress > .35f && progress < .8f ? Mathf.Sin(Mathf.PI * (progress - .35f) / .45f) : 0;
                if (i == actor && receiver >= 0 && CloseContact(e.Type))
                {
                    Vector3 direction = FlatDirection(frame.to[receiver] - frame.to[actor], actor);
                    bool aerial = Snapshot(e.Before, actor).Position == MatchPosition.TopRope;
                    if (aerial) p.y += Mathf.Sin(Mathf.PI * progress) * 1.2f;
                    else if (e.Type is not (MatchActionType.Pin or MatchActionType.Cover or MatchActionType.Hold or MatchActionType.Submission or MatchActionType.StandingHold or MatchActionType.PrepareAttack))
                        p += direction * (hit ? hitAge < 0 ? Mathf.Clamp01(1 + hitAge / .12f) : Mathf.Exp(-hitAge * 18) : pulse) * .18f;
                }
                if (i == receiver && e.Type is not (MatchActionType.IrishWhip or MatchActionType.CornerWhip or MatchActionType.Evade) && e.Result is MatchActionResult.Countered or MatchActionResult.Blocked or MatchActionResult.Escaped)
                    p += FlatDirection(frame.from[i] - frame.from[Mathf.Max(0, actor)], i) * pulse * .25f;
                p += announceTable ? announceTable.PlayerOffset(e, Engine.Participants[i].Id, progress) : Vector3.zero;
                markers[i].position = p;
                int other = i == actor ? receiver : actor;
                if (other < 0 || other == i) other = i == 0 ? 1 : 0;
                var facing = e.Type is MatchActionType.IrishWhip or MatchActionType.CornerWhip or MatchActionType.RopeRun or MatchActionType.Evade
                    ? frame.to[i] - frame.from[i] : frame.to[other] - frame.to[i];
                markers[i].rotation = Quaternion.LookRotation(FlatDirection(facing, i), Vector3.up);
                if (announceTable) markers[i].rotation = announceTable.PlayerRotation * markers[i].rotation;
                UpdateMarkerVisual(i);
                markerPostures[i] = Snapshot(progress < .65f ? e.Before : e.After, i).Posture;
            }
            if (needsContact && !chargingContact)
            {
                var contactPoint = markers[receiver].position;
                if (progress > .65f)
                {
                    var waiting = MatchMovement.Sample(e, e.ReceiverId, .5f);
                    contactPoint = ringCenter + new Vector3(waiting.X * halfRing, 0, waiting.Z * halfRing);
                }
                var direction = Vector3.ProjectOnPlane(contactPoint - markers[actor].position, Vector3.up);
                float gap = Mathf.Max(0, direction.magnitude - (markers[actor].localScale.x + markers[receiver].localScale.x) * .5f);
                float contact = Mathf.SmoothStep(0, 1, Mathf.Clamp01(1 - Mathf.Abs(progress - .65f) / .15f));
                var reach = direction.normalized * (gap * contact);
                markers[actor].position += reach;
                if (markerShadows != null && markerShadows[actor]) markerShadows[actor].position += reach;
            }
        }
        UpdateRopes();
        UpdateCommentary();
        UpdateFeedback();
        UpdateCrowdPanels();
        UpdateReferee();
        UpdateStates();
        UpdateFacingVisibility();
    }

    private void BuildReplayMotion()
    {
        replayMotions = Engine.Events.Select(e => new MatchEngineEvent
        {
            DurationSeconds = e.DurationSeconds, MotionSeconds = e.MotionSeconds,
            Motion = e.Motion?.Select(f => new MatchMotionFrame { Progress = f.Progress,
                Participants = f.Participants.Select(p => p.Copy()).ToList() }).ToList()
        }).ToList();
        if (Engine.EngineVersion < 3) return;
        for (int i = 0; i + 1 < replayMotions.Count; i++)
        {
            if (!Connects(i) || Engine.Events[i].Impact > 0 || IsHit(Engine.Events[i]) || !Preparation(Engine.Events[i].Type)) continue;
            var motion = replayMotions[i].Motion;
            // Use empty settling time for the approach when the same exchange continues.
            while (motion.Count > 2 && motion[^1].Participants.All(p => Same(p, motion[^2].Participants.Single(q => q.Id == p.Id))))
                motion.RemoveAt(motion.Count - 1);
            float finish = motion[^1].Progress;
            foreach (var frame in motion) frame.Progress /= finish;
        }
        for (int i = 0; i + 1 < replayMotions.Count; i++)
        {
            if (!Connects(i)) continue;
            var previous = replayMotions[i]; var next = replayMotions[i + 1];
            float previousRate = PlaybackRate(Engine.Events[i], 0), nextRate = PlaybackRate(Engine.Events[i + 1], 0);
            if (previousRate <= 0 || nextRate <= 0) continue;
            float leftSeconds = (1 - previous.Motion[^2].Progress) * previous.DurationSeconds / Mathf.Max(.0001f, previousRate);
            float rightSeconds = next.Motion[1].Progress * next.DurationSeconds / Mathf.Max(.0001f, nextRate);
            foreach (var end in previous.Motion[^1].Participants)
            {
                var start = next.Motion[0].Participants.Single(p => p.Id == end.Id);
                if (end.HasVelocity || start.HasVelocity || !Same(end, start)) continue;
                var before = previous.Motion[^2].Participants.Single(p => p.Id == end.Id);
                var after = next.Motion[1].Participants.Single(p => p.Id == end.Id);
                if (before.Posture != end.Posture || after.Posture != start.Posture) continue;
                var incoming = (Point(end) - Point(before)) / Mathf.Max(.0001f, leftSeconds);
                var outgoing = (Point(after) - Point(start)) / Mathf.Max(.0001f, rightSeconds);
                if (incoming.sqrMagnitude < .000001f || outgoing.sqrMagnitude < .000001f || Vector2.Dot(incoming.normalized, outgoing.normalized) < -.25f) continue;
                var velocity = (incoming.normalized + outgoing.normalized).normalized * Mathf.Min(incoming.magnitude, outgoing.magnitude);
                end.HasVelocity = start.HasVelocity = true;
                end.VelocityX = velocity.x / previousRate; end.VelocityZ = velocity.y / previousRate;
                start.VelocityX = velocity.x / nextRate; start.VelocityZ = velocity.y / nextRate;
            }
        }

        bool Connects(int index)
        {
            var previous = Engine.Events[index]; var next = Engine.Events[index + 1];
            return previous.Motion?.Count >= 2 && next.Motion?.Count >= 2 && previous.MotionSeconds <= 0 && next.MotionSeconds <= 0 &&
                !Pause(previous.Type) && !Pause(next.Type) &&
                previous.Result is not (MatchActionResult.Countered or MatchActionResult.Escaped or MatchActionResult.Interrupted or MatchActionResult.ExecutionFailure) &&
                MatchTechniques.Find(previous)?.Rules.Kind != MoveKind.Aerial && MatchTechniques.Find(next)?.Rules.Kind != MoveKind.Aerial &&
                (next.CauseEventIndex == index || previous.ExchangeId > 0 && previous.ExchangeId == next.ExchangeId || Preparation(previous.Type));
        }
        Vector2 Point(MatchMotionPose p) => new(p.X, p.Z);
        bool Same(MatchMotionPose a, MatchMotionPose b) => (Point(a) - Point(b)).sqrMagnitude < .00000001f &&
            Mathf.Abs(a.Height - b.Height) < .0001f && Mathf.Abs(a.Tilt - b.Tilt) < .0001f && Mathf.Abs(a.Grounded - b.Grounded) < .0001f &&
            a.Posture == b.Posture && a.Grip == b.Grip && a.GripTargetId == b.GripTargetId &&
            Mathf.Abs(a.FacingX - b.FacingX) < .0001f && Mathf.Abs(a.FacingZ - b.FacingZ) < .0001f;
        bool Preparation(MatchActionType type) => type is MatchActionType.Probe or MatchActionType.TieUp or MatchActionType.Chain or
            MatchActionType.Lift or MatchActionType.PrepareAttack or MatchActionType.Reposition or MatchActionType.TurnOver;
        bool Pause(MatchActionType type) => type is MatchActionType.Rest or MatchActionType.Hold or MatchActionType.StandingHold or
            MatchActionType.Submission or MatchActionType.Pin or MatchActionType.Finish or MatchActionType.Fall or MatchActionType.Recover or
            MatchActionType.RopeBreak or MatchActionType.Climb or MatchActionType.Tag or MatchActionType.Taunt or
            MatchActionType.ObjectSetup or MatchActionType.ObjectClimb or MatchActionType.ObjectFinish or MatchActionType.MedicalStop or MatchActionType.ExecutionStop;
    }

    public void UpdateFacingVisibility()
    {
        var chips = markers ?? new[] { blue, red };
        for (int i = 0; i < chips.Length + (refereeMarker ? 1 : 0); i++)
        {
            var marker = i < chips.Length ? chips[i] : refereeMarker;
            var facing = marker.Find("Facing"); var fill = marker.Find("Fill");
            if (!facing || !fill) continue;
            var renderer = facing.GetComponent<Renderer>();
            var symbol = facing.GetComponent<MeshFilter>();
            if (!chipFacingMaterial)
            {
                chipFacingMaterial = renderer.sharedMaterial; chipFacingCylinder = symbol.sharedMesh;
                chipTriangle = ChipSymbol(true); chipCircle = ChipSymbol(false);
            }
            var back = marker.Find("Back Fill");
            var backFacing = marker.Find("Back Facing");
            if (marker.GetComponent<MeshFilter>().sharedMesh.name != "Cylinder")
            {
                if (back) back.gameObject.SetActive(false);
                if (backFacing) backFacing.gameObject.SetActive(false);
                symbol.sharedMesh = chipFacingCylinder; renderer.sharedMaterial = chipFacingMaterial; renderer.enabled = true;
                continue;
            }
            if (!back)
            {
                back = Instantiate(fill, marker); back.name = "Back Fill";
                back.gameObject.hideFlags = HideFlags.DontSave;
            }
            back.gameObject.SetActive(true); back.localPosition = -fill.localPosition; back.localScale = fill.localScale;
            back.GetComponent<MeshFilter>().sharedMesh = fill.GetComponent<MeshFilter>().sharedMesh;
            var backRenderer = back.GetComponent<Renderer>(); var fillRenderer = fill.GetComponent<Renderer>();
            backRenderer.sharedMaterial = fillRenderer.sharedMaterial;
            chipFill ??= new MaterialPropertyBlock();
            fillRenderer.GetPropertyBlock(chipFill); backRenderer.SetPropertyBlock(chipFill);
            var posture = i == chips.Length ? refereeChipPosture : markerPostures == null ? MatchPosture.Standing : markerPostures[i];
            var mesh = posture == MatchPosture.Supine ? chipCircle : chipTriangle;
            if (symbol.sharedMesh != mesh)
            {
                symbol.sharedMesh = mesh;
                renderer.sharedMaterials = new[] { marker.GetComponent<Renderer>().sharedMaterial, chipFacingMaterial };
            }
            facing.localScale = Vector3.one;
            facing.localPosition = new Vector3(0, fill.localPosition.y + fill.localScale.y + .03f, 0);
            renderer.enabled = posture != MatchPosture.Prone && (!viewCamera || Vector3.Dot(marker.up, -viewCamera.transform.forward) > 0);
            if (!backFacing)
            {
                backFacing = Instantiate(facing, marker); backFacing.name = "Back Facing";
                backFacing.gameObject.hideFlags = HideFlags.DontSave;
            }
            backFacing.gameObject.SetActive(true);
            backFacing.localPosition = -facing.localPosition; backFacing.localScale = Vector3.one;
            // Flip the face normal while keeping the triangle pointed along the chip's forward axis.
            backFacing.localRotation = Quaternion.Euler(0, 0, 180);
            backFacing.GetComponent<MeshFilter>().sharedMesh = mesh;
            var reverseRenderer = backFacing.GetComponent<Renderer>();
            reverseRenderer.sharedMaterials = renderer.sharedMaterials;
            reverseRenderer.enabled = posture != MatchPosture.Prone && (!viewCamera || Vector3.Dot(marker.up, -viewCamera.transform.forward) < 0);
        }
    }

    private static Mesh ChipSymbol(bool triangle)
    {
        var points = triangle ? new[] { new Vector2(-.14f, .2f), new Vector2(0, .44f), new Vector2(.14f, .2f) } :
            Enumerable.Range(0, 24).Select(i => new Vector2(Mathf.Sin(i * Mathf.PI / 12), Mathf.Cos(i * Mathf.PI / 12)) * .15f).ToArray();
        var center = triangle ? new Vector2(0, .28f) : Vector2.zero;
        var vertices = new List<Vector3>();
        var mesh = new Mesh { name = triangle ? "Chip Triangle" : "Chip Supine", hideFlags = HideFlags.DontSave, subMeshCount = 2 };
        var indices = new int[2][];
        for (int layer = 0; layer < 2; layer++)
        {
            int start = vertices.Count; float height = layer * .02f;
            vertices.Add(new Vector3(center.x, height, center.y));
            foreach (var point in points)
            {
                var p = center + (point - center) * (layer == 0 ? 1 : .76f);
                vertices.Add(new Vector3(p.x, height, p.y));
            }
            indices[layer] = Enumerable.Range(0, points.Length).SelectMany(j => new[] { start, start + j + 1, start + (j + 1) % points.Length + 1 }).ToArray();
        }
        mesh.SetVertices(vertices); mesh.SetTriangles(indices[0], 0); mesh.SetTriangles(indices[1], 1);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    private static float MarkerSize(float height) => 1 + Mathf.Clamp(height, -.4f, 0) * .2f + Mathf.Clamp(height, 0, 1.2f) * .75f;

    private void UpdateMarkerVisual(int index)
    {
        float height = markers[index].position.y - ringCenter.y - .12f;
        height = height < 0 ? height / Mathf.Max(.01f, ringCenter.y) * .4f : height / halfRing;
        float elevation = Mathf.Clamp(height, 0, 1.2f);
        float size = MarkerSize(height);
        var original = markerBaseScales[index];
        markers[index].localScale = new Vector3(original.x * size, original.y, original.z * size);
        if (markerShadows == null || markerShadows.Length != markers.Length || markerShadows.Any(t => !t))
        {
            if (!shadowMaterial) shadowMaterial = TransparentMaterial(new Color(.01f, .015f, .025f, .3f));
            markerShadows = markers.Select(marker =>
            {
                var name = marker.name + " Shadow";
                var existing = transform.Find(name);
                if (existing) return existing;
                var shadow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                shadow.name = name; shadow.hideFlags = HideFlags.DontSave;
                shadow.transform.SetParent(transform, false);
                DestroyImmediate(shadow.GetComponent<Collider>());
                var renderer = shadow.GetComponent<Renderer>();
                renderer.sharedMaterial = shadowMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                return shadow.transform;
            }).ToArray();
        }
        markerShadows[index].gameObject.SetActive(true);
        var point = markers[index].position;
        bool outside = Mathf.Max(Mathf.Abs(point.x - ringCenter.x), Mathf.Abs(point.z - ringCenter.z)) > halfRing * 1.06f;
        float surface = outside ? 0 : ringCenter.y;
        var projection = new Vector3(-.25f, 0, .35f) * Mathf.Max(0, point.y - surface - .12f);
        markerShadows[index].position = new Vector3(point.x + projection.x, surface + .025f, point.z + projection.z);
        float shadowSize = (1 + Mathf.Clamp(height, -.4f, 0) * .2f) * (1 - elevation * .25f);
        markerShadows[index].localScale = new Vector3(original.x * shadowSize, .001f, original.z * shadowSize);
    }

    private void Update()
    {
        if (!paused && !ViewingProfile) crowdMotionTime += Time.deltaTime;
        if (showSimulationUI != null)
        {
            UpdateShowSimulation();
            if (showPromo || showSummary) return;
        }
        var display = ViewingProfile ? DisplayStyle.None : DisplayStyle.Flex;
        if (statesRow != null) statesRow.style.display = display;
        if (commentaryBar != null) commentaryBar.style.display = display;
        UpdateMatchReview(display);
        var key = Keyboard.current;
        if (ViewingProfile)
        {
            if (key?.escapeKey.wasPressedThisFrame == true) WrestlerProfileController.CloseOpenProfile(false);
            return;
        }
        AdvancePlayback(Time.deltaTime);
        if (key != null)
        {
            if (key.digit1Key.wasPressedThisFrame) SetView(0);
            if (key.digit2Key.wasPressedThisFrame) SetView(1);
            if (key.digit3Key.wasPressedThisFrame) SetView(2);
            if (showSimulationUI == null && key.spaceKey.wasPressedThisFrame) automaticCamera = !automaticCamera;
            if (key.pKey.wasPressedThisFrame) ToggleReviewPlayback();
            if (key.rKey.wasPressedThisFrame) { if (reviewControls != null) PauseReviewAt(0); else RestartDemo(); }
        }
        if (automaticCamera)
        {
            var snapshot = CurrentEvent == null ? null : clock < CurrentEvent.MatchTimeSeconds + CurrentEvent.DurationSeconds * .65f ? CurrentEvent.Before : CurrentEvent.After;
            var active = snapshot == null ? new[] { blue, red } : snapshot.Participants.Where(p => !p.IsEliminated &&
                (!Engine.Rules.Tag || Engine.Rules.Tornado || p.IsLegal)).Select(p => MarkerFor(p.Id)).ToArray();
            var actionCenter = active.Length == 0 ? ringCenter : active.Aggregate(Vector3.zero, (sum, marker) => sum + marker.position) / active.Length;
            target = crowdPanels.Length > 0 ? Vector3.Lerp(ringCenter, actionCenter, .25f) : actionCenter;
            targetSize = Mathf.Max(ringsideViewSize, active.Select(marker => Vector3.Distance(marker.position, target)).DefaultIfEmpty(0).Max() * 1.2f + 2.5f);
        }
        var mouse = Mouse.current;
        if (mouse != null && !PointerOverReviewUI(mouse.position.ReadValue()))
        {
            Vector2 position = mouse.position.ReadValue();
            if (mouse.rightButton.isPressed && !mouse.rightButton.wasPressedThisFrame)
            {
                Vector2 delta = position - previousMouse;
                float scale = viewCamera.orthographicSize * 2 / Mathf.Max(1, viewCamera.pixelHeight);
                target -= (viewCamera.transform.right * delta.x + viewCamera.transform.up * delta.y) * scale;
                automaticCamera = false;
            }
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > .01f) { targetSize = Mathf.Clamp(targetSize * Mathf.Exp(-scroll * .001f), 3.5f, 24); automaticCamera = false; }
            previousMouse = position;
        }
        target.x = Mathf.Clamp(target.x, -20, 20);
        target.z = Mathf.Clamp(target.z, -28, 28);
        viewCamera.transform.position -= cameraShake;
        viewCamera.transform.position = Vector3.Lerp(viewCamera.transform.position, new Vector3(target.x, 45, target.z), 1 - Mathf.Exp(-Time.deltaTime * 4));
        float shakeAge = Engine == null ? 0 : (clock - CurrentEvent.MatchTimeSeconds - CurrentEvent.DurationSeconds * .65f) / Mathf.Max(.01f, PlaybackRate(CurrentEvent));
        cameraShake = new Vector3(Mathf.Sin(shakeAge * 105), 0, Mathf.Cos(shakeAge * 135)) * HitEnvelope * .055f;
        viewCamera.transform.position += cameraShake;
        viewCamera.orthographicSize = Mathf.Lerp(viewCamera.orthographicSize, targetSize, 1 - Mathf.Exp(-Time.deltaTime * 4));
    }

    private void UpdateCrowdPanels()
    {
        if (Engine == null || markerColors == null || crowdPanels.Length == 0) return;
        crowdFill ??= new MaterialPropertyBlock();
        var neutral = new Color(.09f, .105f, .12f);
        int reactingTo = -1;
        for (int i = EventIndex; i >= 0; i--)
        {
            var e = Engine.Events[i];
            float reactionTime = e.MatchTimeSeconds + e.DurationSeconds * .65f;
            if (reactionTime > clock) continue;
            if (Mathf.Abs(e.CrowdReaction) < .01f) continue;
            reactingTo = Index(e.Result is MatchActionResult.Countered or MatchActionResult.Escaped ? e.ReceiverId : e.PerformerId);
            if (reactingTo >= 0) break;
        }
        var current = CurrentEvent;
        var snapshot = clock >= current.MatchTimeSeconds + current.DurationSeconds * .65f ? current.After : current.Before;
        float contactTime = current.MatchTimeSeconds + current.DurationSeconds * .65f;
        float reactionAge = (clock - contactTime) / Mathf.Max(.01f, PlaybackRate(current));
        bool majorReaction = current.CrowdReaction >= 5 || current.Impact >= 12;
        crowdAccent = majorReaction && reactionAge >= 0 ? Mathf.Clamp01(1 - reactionAge / 1.2f) : 0;
        if (showSimulationUI != null && showFanFrames != null)
        {
            int frame = clock >= current.MatchTimeSeconds + current.DurationSeconds * .65f ? EventIndex : EventIndex - 1;
            AnimateShowFanCrowd(frame >= 0 ? showFanFrames[frame] : new TopViewFanCrowdFrame());
            return;
        }
        int litCount = reactingTo < 0 ? 0 : Mathf.RoundToInt(Mathf.Clamp01(snapshot.CrowdHeat / 100f) * crowdPanels.Length);
        AnimateCrowdPanels(litCount, reactingTo < 0 ? neutral : markerColors[reactingTo]);
    }

    private void AnimateCrowdPanels(int litCount, Color activeColor)
    {
        AnimateCrowdPanels(i => i < litCount, _ => activeColor);
    }

    private void AnimateCrowdPanels(Func<int, bool> isCheering, Func<int, Color> activeColor)
    {
        if (crowdPanelRestPositions == null || crowdPanelRestPositions.Length != crowdPanels.Length)
            crowdPanelRestPositions = crowdPanels.Select(r => r.transform.position).ToArray();
        crowdFill ??= new MaterialPropertyBlock();
        var neutral = new Color(.09f, .105f, .12f);
        for (int i = 0; i < crowdPanels.Length; i++)
        {
            if (!crowdPanels[i]) continue;
            bool cheering = isCheering(i);
            float wave = .5f + .5f * Mathf.Sin(crowdMotionTime * 8.2f + i * 2.39996f);
            float brightness = .07f + .05f * wave + crowdAccent * .35f * wave;
            var color = cheering ? Color.Lerp(neutral, activeColor(i), brightness) : neutral;
            // Screen-up motion remains visible with the overhead camera.
            crowdPanels[i].transform.position = crowdPanelRestPositions[i] + (cheering ? viewCamera.transform.up * ((wave - .5f) * .14f * (.25f + crowdAccent * .75f)) : Vector3.zero);
            crowdFill.SetColor("_BaseColor", color);
            crowdPanels[i].SetPropertyBlock(crowdFill);
        }
    }

    private void CreateCommentary()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;
        narratedIndex = -1;
        root.Q("wrestler-states")?.RemoveFromHierarchy();
        root.Q("match-commentary")?.RemoveFromHierarchy();
        root.pickingMode = PickingMode.Ignore;
        CreateStates(root);
        var bar = commentaryBar = new VisualElement { name = "match-commentary", pickingMode = PickingMode.Ignore };
        bar.style.position = Position.Absolute;
        bar.style.left = bar.style.right = 24;
        bar.style.bottom = reviewControls != null && reviewControls.layout.height > 0 ? reviewControls.layout.height + 32 : 24;
        bar.style.paddingLeft = bar.style.paddingRight = 24;
        bar.style.paddingTop = bar.style.paddingBottom = 18;
        bar.style.flexDirection = FlexDirection.Row;
        bar.style.alignItems = Align.Center;
        bar.style.backgroundColor = new Color(.025f, .035f, .05f, .92f);
        bar.style.borderTopLeftRadius = bar.style.borderTopRightRadius = 8;
        bar.style.borderBottomLeftRadius = bar.style.borderBottomRightRadius = 8;
        matchTime = new Label { name = "match-time", pickingMode = PickingMode.Ignore };
        matchTime.style.fontSize = 20;
        matchTime.style.color = new Color(.65f, .7f, .78f);
        matchTime.style.marginRight = 24;
        matchTime.style.unityFont = commentaryFont;
        commentary = new Label { name = "commentary-text", enableRichText = true };
        commentary.style.fontSize = 26;
        commentary.style.color = Color.white;
        commentary.style.whiteSpace = WhiteSpace.Normal;
        commentary.style.flexGrow = 1;
        commentary.style.flexShrink = 1;
        commentary.style.unityFont = commentaryFont;
        bar.Add(matchTime); bar.Add(commentary); root.Add(bar);
        profileScreen?.parent?.BringToFront();
    }

    private void UpdateCommentary()
    {
        if (Engine == null) return;
        if (narratedIndex != EventIndex)
        {
            int index = MatchEventNarration.CommentaryIndex(narrationMatch, EventIndex);
            var text = MatchNarrationService.NarrateEngineEvent(narrationMatch, index,
                id => WrestlerNameText.DisplayName(Wrestlers?.FirstOrDefault(w => w?.Id == id)));
            if (!string.IsNullOrEmpty(text) && text != CommentaryText)
            {
                CommentaryText = text;
                if (commentary != null) WrestlerNameText.Set(commentary, CommentaryText, Wrestlers);
            }
            narratedIndex = EventIndex;
        }
        if (matchTime != null) matchTime.text = $"{(int)clock / 60:00}:{(int)clock % 60:00}";
    }
    public static bool IsHit(MatchEngineEvent e) => e != null &&
        (e.Impact > 0 || e.Type == MatchActionType.Chain &&
            e.Result is MatchActionResult.FullSuccess or MatchActionResult.PartialSuccess &&
            MatchTechniques.Find(e)?.Rules.MotionProfile == MoveMotionProfile.Takedown) &&
        MatchTechniques.Find(e)?.Rules.Kind != MoveKind.Submission &&
        MatchTechniques.Find(e)?.Rules.MotionProfile is not (MoveMotionProfile.Chain or MoveMotionProfile.Hold) &&
        e.Result is not (MatchActionResult.Countered or MatchActionResult.ExecutionFailure or MatchActionResult.Blocked or MatchActionResult.Escaped) &&
        e.Type is MatchActionType.BasicStrike or MatchActionType.HeavyStrike or MatchActionType.Grapple or MatchActionType.GroundAttack or
            MatchActionType.LimbAttack or MatchActionType.Signature or MatchActionType.Finisher or MatchActionType.Dive or MatchActionType.Counter or MatchActionType.DoubleTeam or MatchActionType.Chain or MatchActionType.RunningAttack or MatchActionType.CornerAttack or MatchActionType.TopRopeAttack or MatchActionType.RingsideAttack;

    private void UpdateFeedback()
    {
        if (Engine == null) return;
        var e = CurrentEvent;
        float age = (clock - e.MatchTimeSeconds - e.DurationSeconds * .65f) / Mathf.Max(.01f, PlaybackRate(e));
        HitEnvelope = IsHit(e) && age >= 0 && age < .3f ? (1 - age / .3f) * Mathf.Lerp(.4f, 1, Mathf.Clamp01(e.Impact / 15)) : 0;
        bool submission = e.Result is MatchActionResult.FullSuccess or MatchActionResult.PartialSuccess or MatchActionResult.Held &&
            (e.Type is MatchActionType.Submission or MatchActionType.StandingHold ||
             e.Type == MatchActionType.Hold && e.Detail is "hold" or "reach-ropes" ||
             e.Type is MatchActionType.Signature or MatchActionType.Finisher or MatchActionType.Counter &&
             MatchTechniques.Find(e)?.Rules.Kind == MoveKind.Submission);
        float holdAge = (clock - e.MatchTimeSeconds - (e.Type == MatchActionType.Hold ? 0 : e.DurationSeconds * .5f)) / Mathf.Max(.01f, PlaybackRate(e));
        float phase = Mathf.Repeat(Mathf.Max(0, holdAge), .55f);
        SubmissionEnvelope = submission && holdAge >= 0 && clock < e.MatchTimeSeconds + e.DurationSeconds && phase < .18f
            ? .75f * Mathf.Sin(Mathf.PI * phase / .18f) : 0;
        ControlEnvelope = 0;
        int controlled = -1;
        if (!submission && e.Type is MatchActionType.Chain or MatchActionType.TieUp && e.Motion != null &&
            e.Result is MatchActionResult.FullSuccess or MatchActionResult.PartialSuccess or MatchActionResult.Held &&
            clock < e.MatchTimeSeconds + e.DurationSeconds)
        {
            float progress = Mathf.Clamp01((clock - e.MatchTimeSeconds) / e.DurationSeconds);
            var motion = replayMotions[EventIndex];
            var grip = motion.Motion.FirstOrDefault(f => f.Participants.Any(p => p.Grip != MatchGrip.None && !string.IsNullOrEmpty(p.GripTargetId)));
            foreach (var participant in e.Before.Participants)
            {
                var pose = MatchMovement.Sample(motion, participant.Id, progress);
                if (pose.Grip != MatchGrip.None && !string.IsNullOrEmpty(pose.GripTargetId))
                { controlled = Index(pose.GripTargetId); break; }
            }
            // A preview has no continuing exchange, but a successful lock remains held to the end of its clip.
            var profile = MatchTechniques.Find(e)?.Rules.MotionProfile;
            if (controlled < 0 && progress >= .5f && profile is MoveMotionProfile.Chain or MoveMotionProfile.Hold or MoveMotionProfile.TieUp)
                controlled = Index(e.ReceiverId);
            float controlAge = (clock - e.MatchTimeSeconds - e.DurationSeconds * (grip?.Progress ?? .5f)) / Mathf.Max(.01f, PlaybackRate(e));
            float controlPhase = Mathf.Repeat(Mathf.Max(0, controlAge), .8f);
            if (controlled >= 0 && controlAge >= 0 && controlPhase < .24f)
                ControlEnvelope = .65f * Mathf.Sin(Mathf.PI * controlPhase / .24f);
        }
        flash ??= new MaterialPropertyBlock();
        int receiver = Index(e.ReceiverId);
        float blink = Mathf.Repeat(Mathf.Max(0, age), .09f) < .045f ? 1 : .12f;
        for (int i = 0; i < markers.Length; i++)
        {
            float feedback = i == receiver ? Mathf.Max(HitEnvelope * blink, SubmissionEnvelope) : 0;
            if (i == controlled) feedback = Mathf.Max(feedback, ControlEnvelope);
            flash.SetColor("_BaseColor", Color.Lerp(markerColors[i], Color.white, feedback));
            markerFills[i].SetPropertyBlock(flash);
        }
        UpdateImpactEffect(e, age, receiver);
    }

    private void UpdateImpactEffect(MatchEngineEvent e, float age, int receiver)
    {
        if (!Application.isPlaying || !impactPrefab) return;
        if (HitEnvelope <= 0 || receiver < 0 || clock >= e.MatchTimeSeconds + e.DurationSeconds)
        {
            if (impactEffect) impactEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return;
        }
        if (!impactEffect)
        {
            impactEffect = Instantiate(impactPrefab, transform).GetComponent<ParticleSystem>();
            int i = 0;
            foreach (var system in impactEffect.GetComponentsInChildren<ParticleSystem>())
            {
                system.useAutoRandomSeed = false;
                system.randomSeed = (uint)++i;
            }
        }
        // Anchor at contact, so the flash does not follow the receiver's recoil.
        var frame = frames[EventIndex];
        Vector3 contact = Vector3.Lerp(frame.from[receiver], frame.to[receiver], .65f) + Vector3.up * .14f;
        if (e.Motion != null)
        {
            var a = MatchMovement.Sample(e, e.PerformerId, .65f);
            var b = MatchMovement.Sample(e, e.ReceiverId, .65f);
            var rules = MatchTechniques.Find(e)?.Rules;
            int actor = Index(e.PerformerId);
            float radius = actor < 0 ? 0 : (markerBaseScales[actor].x * MarkerSize(a.Height) + markerBaseScales[receiver].x * MarkerSize(b.Height)) * .5f;
            if (rules?.Kind == MoveKind.Strike && rules.MotionProfile is MoveMotionProfile.Strike or MoveMotionProfile.Kick &&
                new Vector2(a.X - b.X, a.Z - b.Z).magnitude * halfRing > radius)
                b = MatchMovement.Sample(e, e.ReceiverId, .5f);
            contact = ringCenter + new Vector3(b.X * halfRing, WorldHeight(b.Height) + .22f, b.Z * halfRing);
            if (rules?.Kind == MoveKind.Strike)
                contact += new Vector3(a.X - b.X, 0, a.Z - b.Z).normalized * markerBaseScales[receiver].x * MarkerSize(b.Height) * .3f;
        }
        impactEffect.transform.position = contact;
        if (announceTable) impactEffect.transform.position += announceTable.PlayerOffset(e, e.ReceiverId, .65f);
        float strength = Mathf.Clamp01(e.Impact / 15);
        impactEffect.transform.localScale = Vector3.one * Mathf.Lerp(.28f, .5f, strength);
        foreach (var system in impactEffect.GetComponentsInChildren<ParticleSystem>())
        {
            var main = system.main;
            main.startColor = new Color(1, 1, 1, Mathf.Lerp(.72f, 1, strength));
        }
        // Manual simulation also covers pause, scrubbing and replay speed changes.
        impactEffect.Simulate(Mathf.Max(.001f, age), true, true, false);
        impactEffect.Pause(true);
    }

    private static Material TransparentMaterial(Color color)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { hideFlags = HideFlags.DontSave, renderQueue = 3000 };
        material.SetFloat("_Surface", 1); material.SetFloat("_ZWrite", 0);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetColor("_BaseColor", color);
        return material;
    }

    private void OnDestroy()
    {
        if (refereeStripeMaterial) DestroyImmediate(refereeStripeMaterial);
        if (refereeStripeTexture) DestroyImmediate(refereeStripeTexture);
        if (chipTriangle) DestroyImmediate(chipTriangle);
        if (chipCircle) DestroyImmediate(chipCircle);
        if (profileObject) Destroy(profileObject);
        if (shadowMaterial) DestroyImmediate(shadowMaterial);
        if (markerShadows != null) foreach (var shadow in markerShadows) if (shadow) DestroyImmediate(shadow.gameObject);
        if (previewSave != null && DashboardSession.ActiveSave == previewSave)
            DashboardSession.SetActiveSave(previousSave, previousTransient);
    }

    private void CreateStates(VisualElement root)
    {
        stateLabels = new Label[markers.Length]; stateBars = new ProgressBar[markers.Length][];
        damageIndicators = new LimbDamageIndicator[markers.Length];
        var row = statesRow = new VisualElement { name = "wrestler-states", pickingMode = PickingMode.Ignore };
        row.style.position = Position.Absolute;
        row.style.left = row.style.right = row.style.top = 24;
        row.style.flexDirection = FlexDirection.Row;
        root.Add(row);
        for (int i = 0; i < markers.Length; i++)
        {
            Color color = i < 2 ? i == 0 ? new Color(.41f, .73f, 1) : new Color(1, .49f, .53f) : markerColors[i];
            var card = new VisualElement { name = "state-" + i, pickingMode = PickingMode.Ignore };
            card.style.flexGrow = 1; card.style.flexBasis = 0;
            if (i < markers.Length - 1) card.style.marginRight = 12;
            card.style.paddingLeft = card.style.paddingRight = 20;
            card.style.paddingTop = card.style.paddingBottom = 16;
            card.style.backgroundColor = new Color(.025f, .035f, .05f, .94f);
            card.style.borderTopWidth = 3; card.style.borderTopColor = color;
            card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 8;
            card.style.unityFont = commentaryFont;
            var header = new VisualElement { pickingMode = PickingMode.Ignore };
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            if (markers.Length > 2) header.style.flexDirection = FlexDirection.Column;
            var wrestler = Wrestlers.Single(w => w.Id == Engine.Participants[i].Id);
            var name = WrestlerProfileController.CreateLink(wrestler);
            name.name = "wrestler-name-" + i;
            name.userData = wrestler.Id;
            name.style.backgroundColor = Color.clear;
            name.style.borderLeftWidth = name.style.borderRightWidth = name.style.borderTopWidth = name.style.borderBottomWidth = 0;
            name.style.marginLeft = name.style.marginRight = name.style.marginTop = name.style.marginBottom = 0;
            name.style.paddingLeft = name.style.paddingRight = name.style.paddingTop = name.style.paddingBottom = 0;
            name.style.unityTextAlign = TextAnchor.MiddleLeft;
            name.style.fontSize = 23; name.style.color = color;
            name.style.unityFont = strongFont; name.style.flexGrow = 1;
            stateLabels[i] = new Label { name = "posture-" + i, pickingMode = PickingMode.Ignore };
            stateLabels[i].style.fontSize = 18; stateLabels[i].style.color = Color.white;
            header.Add(name); header.Add(stateLabels[i]); card.Add(header);
            var body = new VisualElement { pickingMode = PickingMode.Ignore };
            body.style.flexDirection = FlexDirection.Row;
            body.style.alignItems = Align.Center;
            body.style.marginTop = 8;
            card.Add(body);
            var bars = new VisualElement { pickingMode = PickingMode.Ignore };
            bars.style.flexGrow = 1; bars.style.flexBasis = 0;
            bars.style.marginRight = markers.Length > 2 ? 12 : 24;
            body.Add(bars);
            stateBars[i] = new ProgressBar[3];
            var titles = new[] { "피로", "충격", "주도권" };
            for (int j = 0; j < 3; j++)
            {
                var bar = new ProgressBar { name = $"metric-{i}-{j}", title = titles[j], lowValue = 0, highValue = 100, pickingMode = PickingMode.Ignore };
                bar.style.height = 28; bar.style.fontSize = 16; bar.style.color = Color.white;
                if (j < 2) bar.style.marginBottom = 10;
                bar.Q<VisualElement>(className: "unity-progress-bar__background").style.backgroundColor = new Color(.12f, .15f, .2f);
                bar.Q<VisualElement>(className: "unity-progress-bar__progress").style.backgroundColor = j == 2 ? color : new Color(.62f, .42f, .2f);
                stateBars[i][j] = bar; bars.Add(bar);
            }
            var indicator = new LimbDamageIndicator { name = "limb-damage-" + i, pickingMode = PickingMode.Ignore };
            indicator.style.width = markers.Length > 2 ? 72 : 100; indicator.style.height = 132;
            indicator.style.flexShrink = 0;
            damageIndicators[i] = indicator;
            body.Add(indicator);
            row.Add(card);
        }
    }
    private void UpdateStates()
    {
        if (Engine == null || stateLabels == null || stateBars == null) return;
        bool applied = clock >= CurrentEvent.MatchTimeSeconds + CurrentEvent.DurationSeconds * .65f;
        var snapshot = applied ? CurrentEvent.After : CurrentEvent.Before;
        var titles = new[] { "피로", "충격", "주도권" };
        for (int i = 0; i < markers.Length; i++)
        {
            var p = Snapshot(snapshot, i);
            string area = p.Position switch { MatchPosition.Ropes => "로프", MatchPosition.Corner => "코너", MatchPosition.TopRope => "코너 위", MatchPosition.Apron => "에이프런", MatchPosition.Outside => "링 밖", _ => "링 중앙" };
            var pose = CurrentEvent.Motion != null ? MatchMovement.Sample(CurrentEvent, p.Id, Mathf.Clamp01((clock - CurrentEvent.MatchTimeSeconds) / CurrentEvent.DurationSeconds)).Posture : p.Posture;
            string posture = pose switch { MatchPosture.Supine => "바로 누움", MatchPosture.Prone => "엎드림", MatchPosture.Seated => "앉음", MatchPosture.Kneeling => "무릎을 짚음", _ => p.IsDowned ? "다운" : "서 있음" };
            if (showSimulationUI != null) posture = pose switch
            {
                MatchPosture.Supine or MatchPosture.Prone => "쓰러짐", MatchPosture.Seated => "앉아 있음",
                MatchPosture.Kneeling => "무릎을 짚음", _ => p.IsDowned ? "쓰러짐" : "서 있음"
            };
            stateLabels[i].text = (showSimulationUI == null ? area + " · " : "") + (p.IsEliminated ? "탈락" : Engine.Rules.Tag && !Engine.Rules.Tornado && !p.IsLegal ? "태그 대기" :
                Finished && Engine.WinnerId == p.Id ? "승리" : HitEnvelope > 0 && p.Id == CurrentEvent.ReceiverId ? "피격" : posture);
            if (showSimulationUI != null) stateLabels[i].text = posture;
            stateLabels[i].style.color = p.IsDowned ? new Color(1, .7f, .35f) : Color.white;
            damageIndicators[i].SetDamage(p.HeadDamage, p.BodyDamage, p.ArmDamage, p.LegDamage);
            if (showConditionGauges != null && i < showConditionGauges.Length)
            {
                float condition = Mathf.Clamp(100 - p.Fatigue, 0, 100);
                showConditionGauges[i].SetValue("컨디션", condition, $"{condition:0}%", new Color(.9f, .77f, .49f));
            }
            var values = new[] { p.Fatigue, p.Stun, p.MatchControl };
            for (int j = 0; j < values.Length; j++)
            {
                stateBars[i][j].value = values[j];
                stateBars[i][j].title = showSimulationUI == null ? $"{titles[j]} {values[j]:0}" : "";
                if (showSimulationUI != null) showStatCaptions[i][j].text = $"{titles[j]}  {values[j]:0}";
            }
        }
    }
    private void UpdateRopes()
    {
        if (ropes == null) return;
        for (int line = 0; line < ropes.Length; line++)
        {
            int side = line / 3;
            float height = ringCenter.y + .4855f + (line % 3) * .475f;
            int count = ropes[line].positionCount;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1), displacement = 0;
                foreach (var contact in contacts)
                    if (contact.side == side) displacement += RopeOffset(t, contact.along, (clock - contact.time) * contact.secondsPerEngineSecond, ropeStrength * contact.strength);
                float along = Mathf.Lerp(-ropeEnd, ropeEnd, t);
                Vector3 point = side == 0 ? new Vector3(along, height, -halfRing - displacement) : side == 1 ? new Vector3(halfRing + displacement, height, along) : side == 2 ? new Vector3(along, height, halfRing + displacement) : new Vector3(-halfRing - displacement, height, along);
                point.x += ringCenter.x; point.z += ringCenter.z;
                ropes[line].SetPosition(i, point);
            }
        }
    }
}

public sealed class LimbDamageIndicator : VisualElement
{
    public Vector4 Damage { get; private set; }

    public LimbDamageIndicator() => generateVisualContent += DrawBody;

    public void SetDamage(float head, float body, float arms, float legs)
    {
        var next = new Vector4(Mathf.Clamp(head, 0, 100), Mathf.Clamp(body, 0, 100), Mathf.Clamp(arms, 0, 100), Mathf.Clamp(legs, 0, 100));
        if (next == Damage) return;
        Damage = next;
        MarkDirtyRepaint();
    }

    public static Color DamageColor(float damage) => damage <= 0 ? new Color(.45f, .51f, .58f) :
        damage < 25 ? new Color(1, .83f, .24f) : damage < 60 ? new Color(1, .43f, .12f) : new Color(.95f, .16f, .22f);

    private void DrawBody(MeshGenerationContext context)
    {
        float scale = Mathf.Min(contentRect.width / 100, contentRect.height / 150);
        if (scale <= 0) return;
        Vector2 offset = new Vector2((contentRect.width - 100 * scale) * .5f, (contentRect.height - 150 * scale) * .5f);
        Vector2 At(float x, float y) => offset + new Vector2(x, y) * scale;
        var painter = context.painter2D;
        painter.strokeColor = new Color(.08f, .1f, .14f);
        painter.lineWidth = 1.5f * scale;
        painter.lineJoin = LineJoin.Round;
        void Part(float damage, params Vector2[] points)
        {
            painter.fillColor = DamageColor(damage);
            painter.BeginPath(); painter.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++) painter.LineTo(points[i]);
            painter.ClosePath(); painter.Fill(); painter.Stroke();
        }
        painter.fillColor = DamageColor(Damage.x);
        painter.BeginPath();
        painter.Arc(At(50, 16), 10 * scale, Angle.Degrees(0), Angle.Degrees(360));
        painter.ClosePath(); painter.Fill(); painter.Stroke();
        Part(Damage.y, At(34,31), At(66,31), At(70,46), At(64,70), At(61,88), At(39,88), At(36,70), At(30,46));
        Part(Damage.z, At(28,32), At(21,37), At(16,57), At(10,81), At(18,85), At(25,64), At(32,49));
        Part(Damage.z, At(72,32), At(79,37), At(84,57), At(90,81), At(82,85), At(75,64), At(68,49));
        Part(Damage.w, At(38,93), At(48,93), At(48,113), At(43,143), At(31,143), At(34,120));
        Part(Damage.w, At(52,93), At(62,93), At(66,120), At(69,143), At(57,143), At(52,113));
    }
}
