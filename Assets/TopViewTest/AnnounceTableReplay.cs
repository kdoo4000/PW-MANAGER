using System.Linq;
using PWManager.Domain.Services;
using UnityEngine;

public sealed class AnnounceTableReplay : MonoBehaviour
{
    private Renderer[] body, equipment;
    private bool[] bodyEnabled;
    private Vector3[] equipmentPositions, equipmentCenters;
    private Quaternion[] equipmentRotations;
    private float[] equipmentDrops;
    private Transform[] legs;
    private Transform tabletop, cover, debris;
    private Vector3 tabletopPosition;
    private Quaternion tabletopRotation;
    private TopViewArenaTest arena;
    private MatchEngineEvent setup, impact, returning;
    private Vector3 center, size, anchorOffset, front;
    private Quaternion tableFacing = Quaternion.identity;
    private float floor, sequenceWeight, landingWeight, tabletopDrop, rebound;
    public float Cleared { get; private set; }
    public float Collapsed { get; private set; }
    public Quaternion PlayerRotation => Quaternion.Slerp(Quaternion.identity, tableFacing, sequenceWeight);
    public float PlayerRebound(string id) => id == impact?.ReceiverId ? rebound * landingWeight * sequenceWeight : 0;

    public void Load(TopViewArenaTest playback)
    {
        arena = playback;
        setup = arena.Engine.Events.FirstOrDefault(e => e.Detail == "announce-table-setup");
        impact = arena.Engine.Events.FirstOrDefault(e => e.Detail == "announce-table-break");
        returning = arena.Engine.Events.FirstOrDefault(e => setup != null && e.MatchTimeSeconds > setup.MatchTimeSeconds &&
            e.Detail is "ringside-return" or "ringside-wait");
        if (body == null && (setup != null || impact != null))
        {
            var renderers = gameObject.scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>()).ToArray();
            var top = renderers.FirstOrDefault(r => r.name.Contains("AnnounceTop"));
            if (!top) return;
            center = top.bounds.center; size = top.bounds.size;
            body = renderers.Where(r => r.name.Contains("AnnounceTop") || r.name.Contains("AnnounceBody")).ToArray();
            floor = body.Min(r => r.bounds.min.y);
            tabletopDrop = Mathf.Max(0, center.y - floor - size.y * .5f - size.z * .5f * Mathf.Sin(6 * Mathf.Deg2Rad) - .08f);
            bodyEnabled = body.Select(r => r.enabled).ToArray();
            equipment = renderers.Where(r => r.name.Contains("AnnounceEquipment")).ToArray();
            equipmentPositions = equipment.Select(r => r.transform.position).ToArray();
            equipmentCenters = equipment.Select(r => r.bounds.center).ToArray();
            equipmentRotations = equipment.Select(r => r.transform.rotation).ToArray();
            equipmentDrops = equipment.Select(r => Mathf.Max(0, r.bounds.min.y - floor - .02f)).ToArray();
            debris = new GameObject("Announce Table Debris").transform;
            debris.gameObject.hideFlags = HideFlags.DontSave;
            debris.SetParent(transform, false);
            tabletop = Instantiate(top.gameObject, top.transform.position, top.transform.rotation, debris).transform;
            tabletop.name = "Collapsed Announce Top"; tabletop.gameObject.hideFlags = HideFlags.DontSave;
            tabletop.localScale = top.transform.lossyScale;
            tabletopPosition = tabletop.position; tabletopRotation = tabletop.rotation;
            legs = Enumerable.Range(0, 4).Select(i => Part("Folding Leg " + i,
                new Vector3(.08f, .8f, .08f), top.sharedMaterial)).ToArray();
            var trim = body.FirstOrDefault(r => r.name.Contains("PaintedSteel")) ?? top;
            cover = Part("Removable Desk Cover", new Vector3(size.x, .025f, size.z), trim.sharedMaterial);
        }
        var anchor = impact ?? setup;
        anchorOffset = Vector3.zero;
        tableFacing = Quaternion.identity;
        if (body != null)
        {
            var direction = Vector3.ProjectOnPlane(arena.ringCenter - center, Vector3.up).normalized;
            front = direction * ((Mathf.Abs(direction.x) * size.x + Mathf.Abs(direction.z) * size.z) * .5f + .45f);
        }
        if (anchor != null && body != null)
        {
            var pose = impact != null ? MatchMovement.Sample(impact, impact.ReceiverId, .65f) :
                MatchMovement.Sample(setup, setup.ReceiverId, 1);
            anchorOffset = center - arena.ringCenter - new Vector3(pose.X * arena.halfRing, 0, pose.Z * arena.halfRing);
            anchorOffset.y = 0;
            var actor = MatchMovement.Sample(anchor, anchor.PerformerId, impact != null ? .65f : 1);
            tableFacing = Quaternion.FromToRotation(new Vector3(actor.FacingX, 0, actor.FacingZ), -front.normalized);
        }
    }

    private Transform Part(string name, Vector3 scale, Material material)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name; part.hideFlags = HideFlags.DontSave;
        part.transform.SetParent(debris, false); part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = material;
        DestroyImmediate(part.GetComponent<Collider>());
        return part.transform;
    }

    public void Sample(float clock)
    {
        if (body == null) return;
        float setupProgress = setup == null ? impact == null ? 0 : 1 :
            Mathf.Clamp01((clock - setup.MatchTimeSeconds) / setup.DurationSeconds);
        Cleared = Mathf.SmoothStep(0, 1, Mathf.Clamp01(setupProgress / .6f));
        Collapsed = impact == null ? 0 : Mathf.SmoothStep(0, 1,
            Mathf.Clamp01(((clock - impact.MatchTimeSeconds) / impact.DurationSeconds - .65f) / .16f));
        float reaction = impact == null ? 0 : Mathf.Clamp01(((clock - impact.MatchTimeSeconds) / impact.DurationSeconds - .68f) / .2f);
        rebound = Mathf.Sin(reaction * Mathf.PI) * (1 - reaction) * .45f;
        sequenceWeight = setup == null ? impact == null || clock < impact.MatchTimeSeconds ? 0 : 1 :
            Mathf.SmoothStep(0, 1, Mathf.Clamp01(setupProgress / .3f));
        landingWeight = impact == null ? 0 : Mathf.SmoothStep(0, 1,
            Mathf.InverseLerp(.5f, .65f, (clock - impact.MatchTimeSeconds) / impact.DurationSeconds));
        if (returning != null)
            sequenceWeight *= 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((clock - returning.MatchTimeSeconds) / returning.DurationSeconds));
        for (int i = 0; i < equipment.Length; i++)
        {
            float clear = Mathf.SmoothStep(0, 1, Mathf.Clamp01((setupProgress - i * .06f) / .45f));
            var rotation = equipmentRotations[i] * Quaternion.Euler(0, clear * -35, 0);
            var delta = rotation * Quaternion.Inverse(equipmentRotations[i]);
            equipment[i].transform.position = equipmentCenters[i] + new Vector3(size.x * .7f * clear,
                -equipmentDrops[i] * clear, .5f * clear) - delta * (equipmentCenters[i] - equipmentPositions[i]);
            equipment[i].transform.rotation = rotation;
        }
        float coverProgress = Mathf.SmoothStep(0, 1, Mathf.Clamp01((setupProgress - .35f) / .4f));
        cover.position = center + new Vector3(-size.x * 1.2f * coverProgress,
            size.y + .02f - .65f * coverProgress, -.6f * coverProgress);
        cover.rotation = Quaternion.Euler(coverProgress * 35, 0, 0);
        cover.gameObject.SetActive(setup != null || impact != null);
        bool broken = Collapsed > 0;
        for (int i = 0; i < body.Length; i++) body[i].enabled = bodyEnabled[i] && !broken;
        tabletop.gameObject.SetActive(broken);
        var tilt = Quaternion.Euler(6 * Collapsed + 14 * Mathf.Sin(Collapsed * Mathf.PI), 0, 0);
        tabletop.position = center + new Vector3(0, -tabletopDrop * Collapsed, .1f * Collapsed) - tilt * (center - tabletopPosition);
        tabletop.rotation = tilt * tabletopRotation;
        for (int i = 0; i < legs.Length; i++)
        {
            float side = i < 2 ? -1 : 1;
            legs[i].gameObject.SetActive(broken);
            legs[i].position = new Vector3(center.x + side * size.x * .38f,
                Mathf.Lerp(center.y - .4f, floor + .04f, Collapsed), center.z + (i % 2 == 0 ? -1 : 1) * size.z * .35f);
            legs[i].position += new Vector3(side * .18f, 0, i % 2 == 0 ? -.12f : .12f) * Collapsed;
            legs[i].rotation = Quaternion.Euler(0, 0, side * 90 * Collapsed);
            var position = legs[i].position;
            float angle = Collapsed * Mathf.PI * .5f;
            position.y = Mathf.Max(position.y, floor + .4f * Mathf.Cos(angle) + .04f * Mathf.Sin(angle) + .02f);
            legs[i].position = position;
        }
    }

    public Vector3 PlayerOffset(MatchEngineEvent e, string id, float progress)
    {
        if (body == null) return Vector3.zero;
        var offset = (anchorOffset + front) * sequenceWeight;
        if (impact != null && e.Motion != null)
        {
            var pose = MatchMovement.Sample(e, id, progress);
            var contact = MatchMovement.Sample(impact, id, .65f);
            var relative = new Vector3(pose.X - contact.X, 0, pose.Z - contact.Z) * arena.halfRing;
            // Keep the lifter at the table edge while the receiver moves onto the top.
            var position = center + front + tableFacing * relative * (id == impact.PerformerId ? .2f : .25f);
            if (e == impact && id == impact.ReceiverId)
                position += Vector3.Cross(Vector3.up, front.normalized) * (.7f * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .42f, progress))));
            offset = (position - arena.ringCenter - new Vector3(pose.X * arena.halfRing, 0, pose.Z * arena.halfRing)) * sequenceWeight;
            offset.y = 0;
        }
        if (impact != null && id == impact.ReceiverId)
        {
            offset -= front * landingWeight * sequenceWeight;
            offset.y += (center.y + size.y * .5f - floor - tabletopDrop * Collapsed + rebound) * landingWeight * sequenceWeight;
        }
        if (e == setup && id == e.PerformerId)
            offset.x += Mathf.Sin(progress * Mathf.PI * 2) * .55f;
        return offset;
    }

    private void OnDestroy()
    {
        if (body == null) return;
        for (int i = 0; i < body.Length; i++) if (body[i]) body[i].enabled = bodyEnabled[i];
        for (int i = 0; i < equipment.Length; i++) if (equipment[i])
        { equipment[i].transform.position = equipmentPositions[i]; equipment[i].transform.rotation = equipmentRotations[i]; }
        if (debris) DestroyImmediate(debris.gameObject);
    }
}
