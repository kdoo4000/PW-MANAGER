using System.Collections.Generic;
using PWManager.Domain.Services;
using UnityEngine;

public sealed partial class TopViewArenaTest
{
    private Transform refereeMarker;
    private Material refereeStripeMaterial;
    private Texture2D refereeStripeTexture;
    private MatchPosture refereeChipPosture;
    private MatchEngineState refereeReplay;
    private readonly List<Vector3> refereePath = new();
    private const float RefereeStep = .1f;
    public string RefereePosture { get; private set; }
    public Vector3 RefereePosition => refereeMarker ? refereeMarker.position : ringCenter;

    private void UpdateReferee()
    {
        if (Engine == null || markers == null) return;
        if (!refereeMarker)
        {
            foreach (var candidate in gameObject.scene.GetRootGameObjects())
                foreach (var child in candidate.GetComponentsInChildren<Transform>(true))
                    if (child.name == "Referee") { refereeMarker = child; break; }
            if (!refereeMarker) return;
            if (!refereeMarker.Find("Facing"))
            {
                var direction = Instantiate(blue.Find("Facing"), refereeMarker);
                direction.name = "Facing";
                direction.gameObject.hideFlags = HideFlags.DontSave;
            }
            var fill = refereeMarker.Find("Fill").GetComponent<Renderer>();
            refereeStripeTexture = new Texture2D(16, 16) { name = "Referee stripes", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.DontSave };
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    refereeStripeTexture.SetPixel(x, y, x / 2 % 2 == 0 ? Color.white : new Color(.04f, .04f, .04f));
            refereeStripeTexture.Apply();
            refereeStripeMaterial = new Material(fill.sharedMaterial) { name = "Referee stripes", hideFlags = HideFlags.DontSave };
            refereeStripeMaterial.SetColor("_BaseColor", Color.white);
            refereeStripeMaterial.SetTexture("_BaseMap", refereeStripeTexture);
            fill.sharedMaterial = refereeStripeMaterial;
        }
        refereeMarker.gameObject.SetActive(!showPromo && !showSummary);
        if (showPromo || showSummary) return;
        var e = CurrentEvent;
        float progress = Mathf.Clamp01((clock - e.MatchTimeSeconds) / Mathf.Max(1, e.DurationSeconds));
        if (refereeReplay != Engine) BuildRefereePath();
        float sample = clock / RefereeStep;
        int frame = Mathf.Min(Mathf.FloorToInt(sample), refereePath.Count - 1);
        refereeMarker.position = Vector3.Lerp(refereePath[frame], refereePath[Mathf.Min(frame + 1, refereePath.Count - 1)], sample - frame);
        int receiver = Index(e.ReceiverId);
        var focus = receiver >= 0 ? markers[receiver].position : (markers[0].position + markers[1].position) * .5f;
        var facing = Vector3.ProjectOnPlane(focus - refereeMarker.position, Vector3.up);
        if (facing.sqrMagnitude > .001f) refereeMarker.rotation = Quaternion.LookRotation(facing);
        bool pin = e.Type is MatchActionType.Pin or MatchActionType.Cover || e.Result is MatchActionResult.Pinfall or MatchActionResult.RollUp;
        bool checking = e.Type == MatchActionType.Submission || e.Type == MatchActionType.Hold && e.Detail is "hold" or "reach-ropes";
        RefereePosture = pin ? "카운트" : checking ? "상태 확인" : e.Type == MatchActionType.RopeBreak ? "브레이크 지시" : "서 있음";
        refereeChipPosture = (pin || checking) && progress >= .3f ? MatchPosture.Kneeling : MatchPosture.Standing;
        float diameter = Mathf.Min(markerBaseScales[0].x, markerBaseScales[1].x) * .88f;
        refereeMarker.localScale = new Vector3(diameter, markerBaseScales[0].y, diameter);
    }

    private static bool RefereeChecksCover(MatchEngineEvent e) =>
        e.Type is MatchActionType.Pin or MatchActionType.Cover or MatchActionType.Submission ||
        e.Type == MatchActionType.Hold && e.Detail is "hold" or "reach-ropes" ||
        e.Result is MatchActionResult.Pinfall or MatchActionResult.RollUp;

    private Vector3 RefereePlayerPosition(int eventIndex, int player, float progress)
    {
        var e = Engine.Events[eventIndex];
        if (e.Motion != null)
        {
            var pose = MatchMovement.Sample(replayMotions[eventIndex], Engine.Participants[player].Id, progress);
            return ringCenter + new Vector3(pose.X * halfRing, .12f, pose.Z * halfRing);
        }
        return Vector3.Lerp(frames[eventIndex].from[player], frames[eventIndex].to[player], Mathf.SmoothStep(0, 1, Mathf.Clamp01(progress / .65f)));
    }

    private Vector3 ClampReferee(Vector3 point)
    {
        float edge = halfRing - .45f;
        return new Vector3(Mathf.Clamp(point.x, ringCenter.x - edge, ringCenter.x + edge), ringCenter.y + .12f,
            Mathf.Clamp(point.z, ringCenter.z - edge, ringCenter.z + edge));
    }

    private static Vector3 ClosestOnAction(Vector3 point, Vector3 a, Vector3 b)
    {
        var axis = b - a; axis.y = 0;
        return a + axis * Mathf.Clamp01(Vector3.Dot(point - a, axis) / Mathf.Max(.0001f, axis.sqrMagnitude));
    }

    private void BuildRefereePath()
    {
        refereeReplay = Engine;
        refereePath.Clear();
        var position = ClampReferee(ringCenter + new Vector3(-1.8f, .12f, -1.8f));
        var watchPoint = position;
        int eventIndex = 0;
        float bodyClearance = Mathf.Max(markerBaseScales[0].x, markerBaseScales[1].x) * .94f + .12f;
        var players = new Vector3[markers.Length];
        // Fixed match-time samples preserve the same route when pausing or seeking backwards.
        for (int tick = 0; tick <= Mathf.CeilToInt(Engine.ElapsedSeconds / RefereeStep); tick++)
        {
            float time = tick * RefereeStep;
            while (eventIndex + 1 < Engine.Events.Count && Engine.Events[eventIndex + 1].MatchTimeSeconds <= time) eventIndex++;
            var e = Engine.Events[eventIndex];
            float progress = Mathf.Clamp01((time - e.MatchTimeSeconds) / Mathf.Max(1, e.DurationSeconds));
            for (int i = 0; i < players.Length; i++) players[i] = RefereePlayerPosition(eventIndex, i, progress);
            int actor = Mathf.Max(0, Index(e.PerformerId)), receiver = Index(e.ReceiverId);
            if (receiver < 0 || receiver == actor) receiver = (actor + 1) % players.Length;
            var a = players[actor]; var b = players[receiver];
            bool close = RefereeChecksCover(e);
            var focus = close ? b : (a + b) * .5f;
            var axis = Vector3.ProjectOnPlane(b - a, Vector3.up).normalized;
            if (axis.sqrMagnitude < .01f) axis = Vector3.forward;
            var normal = Vector3.Cross(Vector3.up, axis);
            float distance = close ? bodyClearance + .25f : 1.65f;
            var left = ClampReferee(focus + normal * distance);
            var right = ClampReferee(focus - normal * distance);
            float Score(Vector3 point)
            {
                float clearance = Vector3.Distance(point, ClosestOnAction(point, a, b));
                return (point - position).sqrMagnitude + Mathf.Max(0, distance - clearance) * 12;
            }
            var targetPosition = close ? (Score(left) <= Score(right) ? left : right) : watchPoint;
            position = Vector3.MoveTowards(position, targetPosition, RefereeStep * 1.35f);
            var beforeAvoidance = position;
            // Keep the official out of the action corridor, including while crossing to a new viewing angle.
            for (int pass = 0; pass < 3; pass++)
            {
                var nearest = ClosestOnAction(position, a, b);
                var away = Vector3.ProjectOnPlane(position - nearest, Vector3.up);
                float clearance = close ? bodyClearance : Mathf.Max(1.05f, bodyClearance + .25f);
                if (away.magnitude < clearance)
                    position = nearest + (away.sqrMagnitude > .001f ? away.normalized : normal) * clearance;
                foreach (var player in players)
                {
                    away = Vector3.ProjectOnPlane(position - player, Vector3.up);
                    if (away.magnitude < bodyClearance)
                        position = player + (away.sqrMagnitude > .001f ? away.normalized : normal) * bodyClearance;
                }
                position = ClampReferee(position);
            }
            // Ordinary action only changes the observation spot when the wrestlers force an evasive step.
            if (!close && (position - beforeAvoidance).sqrMagnitude > .0001f) watchPoint = position;
            refereePath.Add(position);
        }
    }
}
