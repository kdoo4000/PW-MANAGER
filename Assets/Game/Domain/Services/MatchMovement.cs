using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace PWManager.Domain.Services
{
    public enum MatchPosture { Standing, Supine, Prone, Seated, Kneeling }
    public enum MatchGrip { None, Wrist, Head, Waist, Arm, Leg, Body }
    public enum MoveMotionProfile { Automatic, Circle, HandFight, TieUp, Chain, Strike, Kick, Slam, Suplex,
        Snapmare, Bulldog, Powerbomb, Takedown, LimbControl, Hold, GroundDrop, Dive }

    [Serializable]
    public sealed class MatchMotionPose
    {
        public string Id;
        public float X, Z, Height, FacingX, FacingZ;
        public float Tilt, Grounded;
        public bool HasVelocity;
        public float VelocityX, VelocityZ;
        public MatchPosture Posture;
        public MatchGrip Grip;
        public string GripTargetId;
        public MatchMotionPose Copy() => (MatchMotionPose)MemberwiseClone();
    }

    [Serializable]
    public sealed class MatchMotionFrame
    {
        public float Progress;
        public List<MatchMotionPose> Participants;
    }

    public static class MatchMovement
    {
        public static void Initialize(MatchEngineState state)
        {
            if (state.SpatialInitialized) return;
            for (int i = 0; i < state.Participants.Count; i++)
            {
                var p = state.Participants[i];
                p.RingX = i % 2 == 0 ? -.4f : .4f;
                p.RingZ = i < 2 ? 0 : (i / 2) * .15f;
                p.FacingX = -Math.Sign(p.RingX); p.FacingZ = 0;
                NormalizePosture(p);
                if (p.Position != MatchPosition.Center) SetBoundary(p, p.Position);
            }
            state.SpatialInitialized = true;
        }

        public static bool IsBehind(MatchEngineParticipant a, MatchEngineParticipant b) =>
            Vector2.Dot(Direction(new Vector2(a.RingX - b.RingX, a.RingZ - b.RingZ)), new Vector2(b.FacingX, b.FacingZ)) < -.35f;

        public static float Distance(MatchEngineParticipant a, MatchEngineParticipant b) =>
            Vector2.Distance(new Vector2(a.RingX, a.RingZ), new Vector2(b.RingX, b.RingZ));

        public static float DistanceFromCenter(MatchEngineParticipant p) => Math.Max(Math.Abs(p.RingX), Math.Abs(p.RingZ));

        public static bool CanReachRopes(MatchEngineParticipant p) => MatchPhysics.InRing(p) &&
            (p.Position == MatchPosition.Ropes || DistanceFromCenter(p) >= .8f);

        public static void RecoverPosture(MatchEngineParticipant p)
        {
            p.Posture = p.Posture switch
            {
                MatchPosture.Standing or MatchPosture.Supine or MatchPosture.Prone => MatchPosture.Seated,
                MatchPosture.Seated => MatchPosture.Kneeling,
                _ => MatchPosture.Standing
            };
            p.IsDowned = p.Posture != MatchPosture.Standing;
        }

        public static void Apply(MatchEngineState state, MatchEngineEvent e, MatchEngineSnapshot before)
        {
            var start = before.Participants.Select(Pose).ToList();
            var poses = start.Select(x => x.Copy()).ToList();
            foreach (var p in state.Participants)
            {
                NormalizePosture(p);
                var prior = before.Participants.Single(x => x.Id == p.Id);
                if (p.Position != prior.Position && p.Position != MatchPosition.Center) SetBoundary(p, p.Position);
                if (p.Position == MatchPosition.Center && prior.Position is MatchPosition.Outside or MatchPosition.Apron)
                { p.RingX = Math.Clamp(p.RingX, -.7f, .7f); p.RingZ = Math.Clamp(p.RingZ, -.7f, .7f); }
                if (p.Position != MatchPosition.TopRope) p.Height = p.Position == MatchPosition.Outside ? -.4f : 0;
                Write(poses.Single(x => x.Id == p.Id), p);
            }
            var a = poses.FirstOrDefault(x => x.Id == e.PerformerId);
            var b = poses.FirstOrDefault(x => x.Id == e.ReceiverId);
            var moveId = e.MoveId ?? state.Exchange?.MoveId;
            var rule = MatchTechniques.Find(e)?.Rules ?? MatchTechniques.Find(moveId)?.Rules ?? (string.IsNullOrEmpty(moveId) ? null : state.FindMove?.Invoke(moveId));
            var profile = rule?.MotionProfile ?? MoveMotionProfile.Automatic;
            var contact = start.Select(x => x.Copy()).ToList();
            var apex = start.Select(x => x.Copy()).ToList();
            List<MatchMotionPose> release = null;
            List<MatchMotionPose> lift = null, landing = null;
            if (a != null && b != null)
            {
                var initialA = start.Single(x => x.Id == a.Id);
                var initialB = start.Single(x => x.Id == b.Id);
                var away = Direction(Point(initialA) - Point(initialB));
                if (e.Type == MatchActionType.TopRopeAttack && profile == MoveMotionProfile.Suplex)
                    away = Direction(-Point(initialB));
                var side = new Vector2(-away.Y, away.X);
                bool failed = e.Result is MatchActionResult.Countered or MatchActionResult.ExecutionFailure or MatchActionResult.Blocked or MatchActionResult.Escaped or MatchActionResult.Interrupted;
                bool attacking = e.Type is MatchActionType.BasicStrike or MatchActionType.HeavyStrike or MatchActionType.Grapple or
                    MatchActionType.GroundAttack or MatchActionType.LimbAttack or MatchActionType.Signature or MatchActionType.Finisher or
                    MatchActionType.Dive or MatchActionType.RunningAttack or MatchActionType.CornerAttack or MatchActionType.RingsideAttack or
                    MatchActionType.TopRopeAttack or MatchActionType.Counter or MatchActionType.DoubleTeam or MatchActionType.Submission or MatchActionType.StandingHold || e.Type == MatchActionType.Chain && e.Impact > 0;
                if (e.Type is MatchActionType.IrishWhip or MatchActionType.CornerWhip)
                {
                    var runner = e.Result == MatchActionResult.Countered ? a : b;
                    var sender = runner == a ? b : a;
                    var origin = start.Single(x => x.Id == runner.Id);
                    var destination = e.Type == MatchActionType.CornerWhip ? Corner(Point(origin), e.ExchangeId, Point(origin) - Point(sender)) : Boundary(Point(origin) - Point(sender), .92f);
                    Set(runner, destination); runner.Height = 0;
                    Set(sender, Clamp(Point(origin) + Direction(Point(sender) - Point(origin)) * .25f));
                    Face(runner, destination - Point(origin));
                    Face(sender, destination - Point(sender));
                }
                else if (e.Type == MatchActionType.RopeRun)
                {
                    Set(a, e.Detail == "outside-run" ? Boundary(rule?.Situation == MoveSituation.RingsideDive ? -Point(initialB) : Point(initialB), .92f) :
                        e.Detail == "rebound" ? Clamp(Point(initialB) + away * .6f) : Boundary(Point(initialA) - Point(initialB), .92f));
                    Face(a, e.Detail == "outside-run" ? Point(b) - Point(a) : Point(a) - Point(initialA));
                }
                else if (e.Type == MatchActionType.Evade)
                {
                    Set(a, Clamp(Point(initialA) + side * .42f));
                    Set(b, Boundary(Point(initialB) - Point(initialA), .92f));
                }
                else if (e.Detail == "missed-charge")
                {
                    Set(a, Point(initialB));
                    var inward = Direction(-Point(initialB));
                    Set(b, Clamp(Point(initialB) + inward * .33f + side * .2f));
                    Face(a, Point(initialB));
                    Face(b, Point(a) - Point(b));
                }
                else if (e.Detail == "corner-run-up")
                { Set(a, Clamp(Point(initialA) + Direction(-Point(initialB)) * .5f)); Face(a, Point(b) - Point(a)); }
                else if (e.Detail == "rear-setup")
                { Set(a, Clamp(Point(initialB) - new Vector2(initialB.FacingX, initialB.FacingZ) * (rule?.Situation == MoveSituation.RunningRearStanding ? .75f : .25f))); Face(a, new Vector2(initialB.FacingX, initialB.FacingZ)); }
                else if (e.Detail == "ringside-stall")
                { Set(a, Boundary(Point(initialA) + side * .25f, 1.3f)); a.Height = -.4f; }
                else if (e.Type is MatchActionType.Retreat or MatchActionType.Rest ||
                    e.Type == MatchActionType.Chain && e.Result == MatchActionResult.Escaped)
                {
                    if (e.Detail is not ("delayed-cover" or "double-down")) Set(a, Clamp(Point(initialA) + away * .25f));
                    if (e.Type == MatchActionType.Chain) Set(b, Clamp(Point(initialB) - away * .25f));
                }
                else if (e.Type == MatchActionType.Probe)
                {
                    if (profile == MoveMotionProfile.HandFight) Set(a, Clamp(Point(initialB) + away * .25f));
                    else
                    {
                        Set(a, Clamp(Point(initialA) + side * .18f));
                        Set(b, Clamp(Point(initialB) - side * .18f));
                    }
                }
                else if (e.Type == MatchActionType.Pursue)
                { Set(a, Point(initialB) + side * .3f); a.Height = -.4f; }
                else if (e.Detail == "aerial-interrupted")
                { Set(a, Point(initialB) + Direction(-Point(initialB)) * .18f); a.Height = initialB.Height; }
                else if (e.Type == MatchActionType.Climb)
                { Set(a, Corner(e.Detail == "outside-climb" ? Point(initialB) : Point(initialA), e.ExchangeId, Point(initialA) - Point(initialB))); a.Height = .5f; Face(a, Point(b) - Point(a)); }
                else if (e.Type == MatchActionType.Taunt)
                {
                    Set(a, Point(initialA));
                    if (e.KayfabeAction == "stall") Set(a, Clamp(Point(initialA) + away * .18f));
                    if (e.Detail == "taunt-counter") Set(b, Clamp(Point(initialA) - away * .28f));
                    else if (e.Detail == "taunt-approach") Set(b, Clamp(Point(initialB) + away * .2f));
                    Face(a, e.KayfabeAction == "showboat" ? Direction(Point(a)) : Point(b) - Point(a));
                }
                else if (e.Type == MatchActionType.CornerAttack)
                {
                    Set(b, Point(initialB));
                    Set(a, Point(b) + Direction(-Point(b)) * .27f);
                }
                else if (e.Type == MatchActionType.RopeBreak)
                { Set(b, Clamp(Point(a) + Direction(-Point(a)) * .25f)); }
                else if (attacking || e.Type is MatchActionType.PrepareAttack or MatchActionType.TieUp or MatchActionType.Chain or
                    MatchActionType.Hold or MatchActionType.Lift or MatchActionType.TurnOver or MatchActionType.Cover or MatchActionType.Pin)
                {
                    var travel = rule?.TravelDistance ?? (e.Impact >= 6 ? .14f : .07f);
                    if (attacking && !failed && e.Impact > 0)
                    {
                        var displacement = profile == MoveMotionProfile.Suplex ? away * travel : -away * travel;
                        if (e.Type == MatchActionType.TopRopeAttack) displacement = Direction(-Point(initialB)) * .65f;
                        Set(b, state.GetParticipant(b.Id).Position == MatchPosition.Outside ? Point(initialB) + displacement : Clamp(Point(initialB) + displacement));
                    if (state.GetParticipant(b.Id).IsDowned && rule?.EndPosture != MatchPosture.Standing &&
                        (rule?.Kind is MoveKind.Grapple or MoveKind.Submission || before.Participants.Single(x => x.Id == b.Id).IsDowned == false))
                        b.Posture = rule?.EndPosture ?? MatchPosture.Supine;
                    }
                    var gap = e.Type is MatchActionType.Cover or MatchActionType.Pin ? .1f : .25f;
                    if (profile is MoveMotionProfile.Hold or MoveMotionProfile.LimbControl || e.Type == MatchActionType.Hold)
                        gap = .16f;
                    Set(a, Point(b) + away * gap);
                    if (state.GetParticipant(a.Id).Position != MatchPosition.Outside) Set(a, Clamp(Point(a)));
                    a.Height = state.GetParticipant(a.Id).Position == MatchPosition.Outside ? -.4f : 0;
                    if (e.Type == MatchActionType.TopRopeAttack) a.Posture = MatchPosture.Supine;
                    if (failed)
                    {
                        Set(b, e.Detail == "outside-dive-evaded" ? Boundary(Point(initialB) + side * .35f, 1.3f) : Point(initialB));
                        Set(a, state.GetParticipant(a.Id).Position == MatchPosition.Outside ?
                            Boundary(Point(initialB) + away * .4f, 1.3f) : Clamp(Point(initialB) + away * .4f));
                    }
                }
                if (e.Type is not (MatchActionType.IrishWhip or MatchActionType.CornerWhip or MatchActionType.RopeRun or MatchActionType.Taunt) && e.Detail != "missed-charge")
                {
                    Face(a, Point(b) - Point(a));
                    if (!MatchTechniques.IsRear(rule?.Situation ?? MoveSituation.Automatic) && e.Detail != "rear-setup")
                        Face(b, Point(a) - Point(b));
                }
                var ca = contact.Single(x => x.Id == a.Id); var cb = contact.Single(x => x.Id == b.Id);
                var aa = apex.Single(x => x.Id == a.Id); var ab = apex.Single(x => x.Id == b.Id);
                if (attacking || e.Type is MatchActionType.PrepareAttack or MatchActionType.TieUp or MatchActionType.Lift or MatchActionType.Chain or MatchActionType.Cover)
                {
                    Set(ca, Point(initialB) + away * (profile == MoveMotionProfile.Kick ? .3f : .23f));
                    Face(ca, Point(cb) - Point(ca));
                    if (initialA.Height > 0) ca.Height = initialA.Height + .2f;
                    var grappling = profile is MoveMotionProfile.Slam or MoveMotionProfile.Suplex or MoveMotionProfile.Powerbomb or MoveMotionProfile.Snapmare or
                        MoveMotionProfile.Bulldog or MoveMotionProfile.Takedown or MoveMotionProfile.Hold or MoveMotionProfile.LimbControl;
                    if (grappling || e.Type is MatchActionType.PrepareAttack or MatchActionType.TieUp or MatchActionType.Lift or MatchActionType.Chain)
                    { ca.Grip = GripFor(e.TargetRegion); ca.GripTargetId = b.Id; }
                    Set(aa, Vector2.Lerp(Point(ca), Point(a), .5f));
                    Set(ab, Vector2.Lerp(Point(initialB), Point(b), .5f));
                    aa.Grip = ca.Grip; aa.GripTargetId = ca.GripTargetId;
                    if (!failed && profile is MoveMotionProfile.Slam or MoveMotionProfile.Suplex or MoveMotionProfile.Powerbomb)
                        ab.Height += profile == MoveMotionProfile.Powerbomb ? .55f : .35f;
                    if (initialA.Height > 0 || profile == MoveMotionProfile.Dive) aa.Height = Math.Max(.35f, initialA.Height + .28f);
                    if (profile == MoveMotionProfile.GroundDrop) { aa.Height += .14f; aa.Posture = MatchPosture.Kneeling; }
                    if (profile == MoveMotionProfile.Takedown) Set(aa, Point(aa) + side * .15f);
                    if (profile == MoveMotionProfile.LimbControl) Set(aa, Point(aa) + side * .07f);
                    if (profile == MoveMotionProfile.Snapmare)
                    { ab.FacingX = -cb.FacingX; ab.FacingZ = -cb.FacingZ; }
                    if (failed) Set(ab, Point(initialB));
                    if (!failed && attacking && e.Impact > 0)
                    {
                        if (profile is MoveMotionProfile.Suplex or MoveMotionProfile.Snapmare)
                        {
                            var travel = Math.Max(profile == MoveMotionProfile.Suplex ? .3f : .23f, rule.TravelDistance);
                            var pivot = Point(ca);
                            var destination = pivot + away * (.12f + travel);
                            var correction = state.GetParticipant(b.Id).Position == MatchPosition.Outside ? Vector2.Zero : Clamp(destination) - destination;
                            pivot += correction;
                            Set(ca, pivot); Set(cb, Point(initialB) + correction);
                            Set(a, pivot + away * .12f); Set(b, Point(a) + away * travel);
                            // Leave room behind the throw before the lift, including near a rope.
                            Face(ca, -away); Face(a, profile == MoveMotionProfile.Snapmare ? away : -away);
                            ca.Height = initialA.Height; cb.Height = initialB.Height;
                            lift = contact.Select(x => x.Copy()).ToList();
                            var la = lift.Single(x => x.Id == a.Id); var lb = lift.Single(x => x.Id == b.Id);
                            Set(lb, pivot - away * .04f);
                            lb.Height = Math.Max(ca.Height, cb.Height) + (profile == MoveMotionProfile.Suplex ? .62f : .28f);
                            lb.Tilt = 55;
                            la.Tilt = profile == MoveMotionProfile.Suplex ? -12 : 12;
                            Set(aa, pivot + away * .065f); Set(ab, Point(aa) + away * .15f);
                            aa.Height = profile == MoveMotionProfile.Suplex ? Math.Max(0, initialA.Height * .5f) + .08f : 0;
                            ab.Height = Math.Max(initialA.Height, initialB.Height) * .5f + (profile == MoveMotionProfile.Suplex ? .45f : .2f);
                            aa.Tilt = profile == MoveMotionProfile.Suplex ? -40 : 15;
                            ab.Tilt = e.Type == MatchActionType.DoubleTeam && profile == MoveMotionProfile.Suplex ? 120 : -55;
                            Face(aa, -away);
                            if (profile == MoveMotionProfile.Suplex)
                            { ab.FacingX = b.FacingX = initialB.FacingX; ab.FacingZ = b.FacingZ = initialB.FacingZ; }
                            else { Face(ab, away); Face(b, away); }
                            if (profile == MoveMotionProfile.Snapmare) b.Posture = MatchPosture.Seated;
                        }
                        else if (profile is MoveMotionProfile.Slam or MoveMotionProfile.Powerbomb)
                        {
                            Set(ab, Point(ca) - away * .1f);
                            ab.Height = Math.Max(initialB.Height, initialA.Height) + (profile == MoveMotionProfile.Powerbomb ? .65f : .42f);
                            ab.Tilt = profile == MoveMotionProfile.Powerbomb ? 15 : 45;
                            aa.Tilt = -12;
                        }
                        else if (profile is MoveMotionProfile.Bulldog or MoveMotionProfile.Takedown)
                        { aa.Tilt = 22; ab.Tilt = 35; ab.Height += .12f; }
                        else if (profile is MoveMotionProfile.Strike or MoveMotionProfile.Kick)
                        { Set(aa, Point(ca) + away * .09f); aa.Tilt = -15; }
                        else if (profile is MoveMotionProfile.Dive or MoveMotionProfile.GroundDrop)
                        { aa.Tilt = 35; aa.Grounded = .6f; }
                    }
                }
                else
                {
                    contact = Interpolate(start, poses, .3f / .65f);
                    apex = Interpolate(start, poses, .5f / .65f);
                    if (e.Type is MatchActionType.IrishWhip or MatchActionType.CornerWhip)
                    {
                        var runnerId = e.Result == MatchActionResult.Countered ? a.Id : b.Id;
                        var senderId = runnerId == a.Id ? b.Id : a.Id;
                        var holder = contact.Single(x => x.Id == senderId);
                        Set(holder, Point(poses.Single(x => x.Id == senderId)));
                        Set(contact.Single(x => x.Id == runnerId), Point(start.Single(x => x.Id == runnerId)));
                        holder.Grip = MatchGrip.Wrist; holder.GripTargetId = runnerId;
                        release = contact.Select(x => x.Copy()).ToList();
                        var hand = release.Single(x => x.Id == senderId);
                        hand.Grip = MatchGrip.None; hand.GripTargetId = null;
                        apex = Interpolate(release, poses, .5f);
                    }
                    if (e.Type == MatchActionType.Evade && e.Detail == "leapfrog") apex.Single(x => x.Id == a.Id).Height += .3f;
                }
                if (e.Type == MatchActionType.Reposition && e.Detail == "center" &&
                    before.Participants.Single(x => x.Id == a.Id).Position is MatchPosition.Center or MatchPosition.Ropes or MatchPosition.Corner &&
                    before.Participants.Single(x => x.Id == b.Id).Position is MatchPosition.Center or MatchPosition.Ropes or MatchPosition.Corner)
                {
                    Set(b, Vector2.Zero);
                    Set(a, Clamp(Point(b) + away * .25f));
                    Face(a, Point(b) - Point(a));
                    var holder = contact.Single(x => x.Id == a.Id);
                    var held = contact.Single(x => x.Id == b.Id);
                    Set(holder, Point(initialB) + away * .16f); Set(held, Point(initialB));
                    holder.Grip = initialB.Posture == MatchPosture.Standing ? MatchGrip.Wrist : MatchGrip.Leg;
                    holder.GripTargetId = b.Id;
                    // The opponent starts moving after the grip, rather than sliding toward center alone.
                    apex = Interpolate(contact, poses, (.5f - .3f) / (.65f - .3f));
                }
                if (e.Type is MatchActionType.Cover or MatchActionType.Pin ||
                    e.Type is MatchActionType.Finish or MatchActionType.Fall && e.Result is MatchActionResult.Pinfall or MatchActionResult.RollUp)
                {
                    ca = contact.Single(x => x.Id == a.Id); cb = contact.Single(x => x.Id == b.Id);
                    aa = apex.Single(x => x.Id == a.Id); ab = apex.Single(x => x.Id == b.Id);
                    bool kickout = e.Type == MatchActionType.Pin && e.Result is MatchActionResult.OneCount or MatchActionResult.TwoCount or MatchActionResult.NearFall or MatchActionResult.Interrupted;
                    a.Height = kickout ? b.Height : b.Height + .06f;
                    Set(ca, e.Type == MatchActionType.Cover ? Point(initialB) + away * .15f : Point(initialA));
                    Set(aa, e.Type == MatchActionType.Cover ? Vector2.Lerp(Point(ca), Point(a), .5f) : Point(initialA));
                    ca.Height = cb.Height + .06f; aa.Height = ab.Height + .06f;
                    if (e.Detail == "roll-up")
                    {
                        ca.Grip = MatchGrip.Waist; ca.GripTargetId = b.Id;
                        cb.Posture = initialB.Posture;
                        ab.Posture = MatchPosture.Seated; ab.Grounded = .5f; ab.Tilt = 75;
                        aa.Posture = MatchPosture.Kneeling; aa.Grip = MatchGrip.Waist; aa.GripTargetId = b.Id;
                        Set(ab, Vector2.Lerp(Point(initialB), Point(b), .5f) + side * .08f);
                        b.Posture = MatchPosture.Supine;
                    }
                    if (e.Detail == "bridge")
                    {
                        foreach (var frame in new[] { contact, apex, poses })
                            for (int i = 0; i < frame.Count; i++)
                                if (frame[i].Id == a.Id || frame[i].Id == b.Id) frame[i] = start[i].Copy();
                        a = poses.Single(x => x.Id == e.PerformerId); b = poses.Single(x => x.Id == e.ReceiverId);
                        if (kickout) a.Height = b.Height;
                    }
                    if (kickout) Set(a, Clamp(Point(initialA) + away * .25f + side * .1f));
                }
            }
            foreach (var p in state.Participants)
            {
                var pose = poses.Single(x => x.Id == p.Id);
                if (p.Position == MatchPosition.Outside && Math.Max(Math.Abs(pose.X), Math.Abs(pose.Z)) < 1.15f)
                    Set(pose, Boundary(Point(pose), 1.15f));
                bool retains = state.Exchange?.ActorId == p.Id && (state.Exchange.Kind is ExchangeKind.Chain or ExchangeKind.Hold or ExchangeKind.Preparation or ExchangeKind.GripCombo or ExchangeKind.Cover ||
                    state.Exchange.Kind == ExchangeKind.FollowUp && MatchMoveMotion.IsBridgingSuplex(state.Exchange.MoveId));
                pose.Grip = retains ? MatchMoveMotion.IsBridgingSuplex(state.Exchange.MoveId) ? MatchGrip.Waist : state.Exchange.Kind == ExchangeKind.Chain ? ChainGrip(state.Exchange.ChainPosition) : GripFor(state.Exchange.Region) : MatchGrip.None;
                pose.GripTargetId = retains ? state.Exchange.TargetId : null;
                p.RingX = pose.X; p.RingZ = pose.Z; p.Height = pose.Height;
                p.FacingX = pose.FacingX; p.FacingZ = pose.FacingZ;
                p.Posture = pose.Posture; p.IsDowned = p.Posture != MatchPosture.Standing;
                p.Grip = pose.Grip; p.GripTargetId = pose.GripTargetId;
                pose.Grounded = Grounded(pose.Posture);
            }
            landing = poses.Select(x => x.Copy()).ToList();
            if (a != null && b != null && e.Type == MatchActionType.Pin && e.Result is MatchActionResult.OneCount or MatchActionResult.TwoCount or MatchActionResult.NearFall or MatchActionResult.Interrupted)
            {
                var coverer = landing.Single(x => x.Id == a.Id);
                var prior = start.Single(x => x.Id == a.Id);
                Set(coverer, Point(prior)); coverer.Height = prior.Height;
                if (e.Detail == "bridge") { coverer.Grip = prior.Grip; coverer.GripTargetId = prior.GripTargetId; }
            }
            if (a != null && b != null && e.Impact > 0 && e.Result is not (MatchActionResult.Countered or MatchActionResult.ExecutionFailure or MatchActionResult.Blocked or MatchActionResult.Escaped or MatchActionResult.Interrupted))
            {
                var landedA = landing.Single(x => x.Id == a.Id);
                var landedB = landing.Single(x => x.Id == b.Id);
                if (profile == MoveMotionProfile.Suplex)
                {
                    landedA.Posture = a.Posture = MatchPosture.Supine;
                    landedA.Grounded = a.Grounded = 1; landedA.Tilt = a.Tilt = -180;
                    var performer = state.GetParticipant(a.Id);
                    performer.Posture = MatchPosture.Supine; performer.IsDowned = true;
                }
                else if (profile is MoveMotionProfile.Powerbomb or MoveMotionProfile.Bulldog or MoveMotionProfile.GroundDrop or MoveMotionProfile.Dive)
                { landedA.Grounded = .65f; landedA.Tilt = 20; }
                if (landedB.Posture == MatchPosture.Standing) landedB.Tilt = 20;
                if (e.Type == MatchActionType.DoubleTeam && profile == MoveMotionProfile.Suplex) landedB.Tilt = 180;
            }
            e.Motion = new List<MatchMotionFrame>
            {
                new() { Progress = 0, Participants = start }, new() { Progress = .3f, Participants = contact },
                new() { Progress = .5f, Participants = apex }, new() { Progress = .65f, Participants = landing },
                new() { Progress = 1, Participants = poses }
            };
            if (release != null) e.Motion.Insert(2, new MatchMotionFrame { Progress = .35f, Participants = release });
            if (lift != null) e.Motion.Insert(2, new MatchMotionFrame { Progress = .42f, Participants = lift });
            if (a != null && b != null && e.Type == MatchActionType.Pin &&
                e.Result is MatchActionResult.OneCount or MatchActionResult.TwoCount or MatchActionResult.NearFall)
            {
                var recoil = poses.Select(p => p.Copy()).ToList();
                var coverer = recoil.Single(p => p.Id == a.Id);
                var covered = recoil.Single(p => p.Id == b.Id);
                Set(coverer, Clamp(Point(a) + Direction(Point(a) - Point(b)) * .02f));
                coverer.Height += .025f; coverer.Tilt = 10;
                covered.Height += .035f; covered.Tilt = -16;
                e.Motion.Insert(e.Motion.Count - 1, new MatchMotionFrame { Progress = .78f, Participants = recoil });
            }
            MatchMoveMotion.Apply(state, e, before);
            if (e.Detail == "desperation-strike")
            {
                foreach (var frame in e.Motion.Where(f => f.Progress >= .65f))
                foreach (var pose in frame.Participants.Where(p => p.Id == e.PerformerId || p.Id == e.ReceiverId))
                { pose.Posture = MatchPosture.Supine; pose.Grounded = 1; pose.Height = 0; }
                foreach (var p in state.Participants.Where(p => p.Id == e.PerformerId || p.Id == e.ReceiverId))
                { p.Posture = MatchPosture.Supine; p.IsDowned = true; p.Height = 0; }
            }
            string helperId = e.Type == MatchActionType.DoubleTeam ? e.PartnerId : e.Type == MatchActionType.BreakUp && e.Detail != "aerial-interrupted" ? e.PerformerId : null;
            if (!string.IsNullOrEmpty(helperId) && !string.IsNullOrEmpty(e.ReceiverId))
            {
                var initial = e.Motion[0].Participants.Single(p => p.Id == helperId);
                bool suplexPartner = e.Type == MatchActionType.DoubleTeam && profile == MoveMotionProfile.Suplex &&
                    e.Impact > 0 && e.Result is MatchActionResult.FullSuccess or MatchActionResult.PartialSuccess or MatchActionResult.ReceptionFailure;
                foreach (var frame in e.Motion.Where(f => f.Progress > 0 && (f.Progress < 1 || suplexPartner)))
                {
                    var helper = frame.Participants.Single(p => p.Id == helperId);
                    var receiver = frame.Participants.Single(p => p.Id == e.ReceiverId);
                    var approach = Direction(Point(initial) - Point(receiver));
                    bool outside = before.Participants.Single(p => p.Id == helperId).Position == MatchPosition.Outside;
                    Set(helper, outside ? Boundary(Point(receiver) + approach * .2f, 1.3f) : Clamp(Point(receiver) + approach * .2f));
                    helper.Height = outside ? -.4f : 0;
                    Face(helper, suplexPartner ? Point(e.Motion[0].Participants.Single(p => p.Id == e.ReceiverId)) - Point(initial) : Point(receiver) - Point(helper));
                    if (suplexPartner)
                    {
                        var performer = frame.Participants.Single(p => p.Id == e.PerformerId);
                        helper.Tilt = performer.Tilt;
                        if (frame.Progress >= .65f)
                        { helper.Posture = MatchPosture.Supine; helper.Grounded = 1; }
                    }
                }
                if (suplexPartner)
                {
                    var partner = state.GetParticipant(helperId);
                    var final = e.Motion[^1].Participants.Single(p => p.Id == helperId);
                    partner.RingX = final.X; partner.RingZ = final.Z; partner.Height = final.Height;
                    partner.FacingX = final.FacingX; partner.FacingZ = final.FacingZ;
                    partner.Posture = MatchPosture.Supine; partner.IsDowned = true;
                }
            }
            ApplyRunningMotion(state, e);
        }

        private static void ApplyRunningMotion(MatchEngineState state, MatchEngineEvent e)
        {
            var previous = state.Events.LastOrDefault();
            if (e.Type is MatchActionType.IrishWhip or MatchActionType.RopeRun ||
                e.Type == MatchActionType.Evade && e.Detail is "leapfrog" or "duck-under")
            {
                string id = e.Type == MatchActionType.IrishWhip ? e.Result == MatchActionResult.Countered ? e.PerformerId : e.ReceiverId :
                    e.Type == MatchActionType.Evade ? e.ReceiverId : e.PerformerId;
                var start = e.Motion[0].Participants.Single(p => p.Id == id);
                var end = e.Motion[^1].Participants.Single(p => p.Id == id);
                var origin = Point(start); var destination = Point(end);
                var direction = Direction(destination - origin);
                var runner = state.GetParticipant(id);
                float speed = (1.35f + runner.Attributes.HighFlying * .0325f) *
                    Math.Max(.4f, 1 - runner.Fatigue * .005f - runner.LegDamage * .004f);
                var prior = previous?.Motion?.Last().Participants.FirstOrDefault(p => p.Id == id);
                var incoming = prior?.HasVelocity == true ? new Vector2(prior.VelocityX, prior.VelocityZ) : Vector2.Zero;
                float preparation = e.Type == MatchActionType.IrishWhip ? .26f : 0;
                var frames = new List<MatchMotionFrame> { e.Motion[0] };
                Velocity(start, incoming);
                if (e.Type == MatchActionType.RopeRun && e.Detail == "rebound")
                {
                    if (incoming.LengthSquared() < .0001f) incoming = Direction(origin) * speed;
                    Velocity(start, incoming);
                    // The runner yields into the rope before accelerating in the opposite direction.
                    var compressed = Vector2.Clamp(origin + incoming * .06f, new Vector2(-.98f), new Vector2(.98f));
                    preparation = Math.Max(.025f, 2 * Vector2.Distance(origin, compressed) / incoming.Length());
                    e.MotionSeconds = preparation + 1.5f * Vector2.Distance(compressed, destination) / speed;
                    Add(preparation / e.MotionSeconds, compressed, Vector2.Zero);
                    direction = Direction(destination - compressed);
                }
                else
                {
                    float distance = Vector2.Distance(origin, destination);
                    bool moving = incoming.LengthSquared() > .0001f;
                    e.MotionSeconds = Math.Max(.35f, preparation + (moving ? 1 : 1.5f) * distance / speed);
                    if (preparation > 0)
                    {
                        var grip = e.Motion.Single(f => f.Progress == .3f);
                        grip.Progress = preparation * .7f / e.MotionSeconds;
                        Velocity(grip.Participants.Single(p => p.Id == id), Vector2.Zero);
                        Face(grip.Participants.Single(p => p.Id == id), direction);
                        frames.Add(grip);
                        var release = e.Motion.Single(f => f.Progress == .35f);
                        release.Progress = preparation / e.MotionSeconds;
                        Velocity(release.Participants.Single(p => p.Id == id), Vector2.Zero);
                        Face(release.Participants.Single(p => p.Id == id), direction);
                        frames.Add(release);
                    }
                }
                Velocity(end, direction * speed);
                Face(end, direction);
                if (e.Detail == "outside-run" && MatchTechniques.Find(e.TechniqueId ?? e.MoveId ?? state.Exchange?.MoveId)?.Rules.Situation == MoveSituation.RingsideDive)
                {
                    Velocity(end, Vector2.Zero);
                    Face(end, Point(e.Motion[0].Participants.Single(p => p.Id == e.ReceiverId)) - destination);
                }
                frames.Add(e.Motion[^1]);
                e.Motion = frames;

                void Add(float progress, Vector2 point, Vector2 velocity)
                {
                    var poses = e.Motion[0].Participants.Select(p => p.Copy()).ToList();
                    var pose = poses.Single(p => p.Id == id);
                    Set(pose, point); Velocity(pose, velocity);
                    Face(pose, direction);
                    frames.Add(new MatchMotionFrame { Progress = progress, Participants = poses });
                }
            }
            else if (e.Type == MatchActionType.RunningAttack && previous?.Motion != null)
            {
                // A rebound may feed either the attacker or the opponent being caught.
                foreach (string id in new[] { e.PerformerId, e.ReceiverId })
                {
                    var prior = previous.Motion[^1].Participants.Single(p => p.Id == id);
                    if (!prior.HasVelocity || prior.VelocityX * prior.VelocityX + prior.VelocityZ * prior.VelocityZ < .0001f) continue;
                    var incoming = new Vector2(prior.VelocityX, prior.VelocityZ);
                    var entry = e.Motion[1];
                    e.MotionSeconds = 1.1f;
                    var start = e.Motion[0].Participants.Single(p => p.Id == id);
                    Velocity(start, incoming);
                    if (id == e.ReceiverId)
                    {
                        var offset = incoming * (entry.Progress * e.MotionSeconds * .5f);
                        // Translate the move as a pair so its authored grips, lifts and landing stay intact.
                        foreach (var frame in e.Motion.Skip(1))
                            foreach (var pose in frame.Participants.Where(p => p.Id == e.PerformerId || p.Id == e.ReceiverId))
                                Set(pose, Point(pose) + offset);
                        Velocity(entry.Participants.Single(p => p.Id == id), Vector2.Zero);
                    }
                }
            }
            var rules = MatchTechniques.Find(e)?.Rules;
            var approaching = previous?.Motion?.Last().Participants.FirstOrDefault(p => p.Id == e.ReceiverId);
            bool catching = e.Type == MatchActionType.RunningAttack && approaching?.HasVelocity == true &&
                approaching.VelocityX * approaching.VelocityX + approaching.VelocityZ * approaching.VelocityZ > .0001f;
            if (!catching && rules?.ExecutionStyle == MoveExecutionStyle.Running && rules.Kind == MoveKind.Strike &&
                e.Result is not (MatchActionResult.Countered or MatchActionResult.ExecutionFailure or MatchActionResult.Blocked or
                    MatchActionResult.Escaped or MatchActionResult.Interrupted))
                ApplyCharge(e);
            foreach (var p in state.Participants)
            {
                var final = e.Motion[^1].Participants.Single(pose => pose.Id == p.Id);
                p.RingX = final.X; p.RingZ = final.Z; p.FacingX = final.FacingX; p.FacingZ = final.FacingZ;
            }
        }

        private static void ApplyCharge(MatchEngineEvent e)
        {
            float contactProgress = MatchTechniques.Find(e)?.Id == "move_004" ? .42f : .65f;
            var start = e.Motion[0].Participants.Single(p => p.Id == e.PerformerId);
            var contact = e.Motion.Single(f => f.Progress == contactProgress).Participants.Single(p => p.Id == e.PerformerId);
            var origin = Point(start); var destination = Point(contact); var delta = destination - origin;
            if (delta.LengthSquared() < .0001f) return;
            if (e.MotionSeconds <= 0) e.MotionSeconds = e.Type is MatchActionType.Signature or MatchActionType.Finisher ? 1.1f : .8f;
            var incoming = start.HasVelocity ? new Vector2(start.VelocityX, start.VelocityZ) : Vector2.Zero;
            float entrySpeed = incoming.Length();
            if (entrySpeed > .01f) e.MotionSeconds = Math.Min(e.MotionSeconds, delta.Length() / (contactProgress * entrySpeed));
            float contactSeconds = e.MotionSeconds * contactProgress, accelerationSeconds = contactSeconds * .3f;
            var cruise = (delta / contactSeconds - incoming * .15f) / .85f;
            var acceleration = (cruise - incoming) / accelerationSeconds;
            var accelerated = Frame(contactProgress * .3f);
            var finish = e.Motion[^1].Participants.Single(p => p.Id == e.PerformerId);
            bool braking = Vector2.Dot(Point(finish) - destination, cruise) <= 0;
            var brake = braking ? Frame(.72f) : null;
            e.Motion.Add(accelerated);
            if (brake != null) e.Motion.Add(brake);
            e.Motion = e.Motion.OrderBy(f => f.Progress).ToList();
            foreach (var frame in e.Motion.Where(f => f.Progress <= contactProgress))
            {
                var pose = frame.Participants.Single(p => p.Id == e.PerformerId);
                float time = frame.Progress * e.MotionSeconds;
                if (time <= accelerationSeconds)
                {
                    Set(pose, origin + incoming * time + acceleration * (.5f * time * time));
                    Velocity(pose, incoming + acceleration * time);
                }
                else
                {
                    Set(pose, destination - cruise * (contactSeconds - time));
                    Velocity(pose, cruise);
                }
            }
            if (brake != null)
            {
                var pose = brake.Participants.Single(p => p.Id == e.PerformerId);
                Set(pose, destination + cruise * (.035f * e.MotionSeconds)); Velocity(pose, Vector2.Zero);
            }

            MatchMotionFrame Frame(float progress) => new() { Progress = progress,
                Participants = e.Motion[0].Participants.Select(p => Sample(e, p.Id, progress)).ToList() };
        }

        private static void Velocity(MatchMotionPose pose, Vector2 velocity)
        { pose.HasVelocity = true; pose.VelocityX = velocity.X; pose.VelocityZ = velocity.Y; }

        public static MatchMotionPose Sample(MatchEngineEvent e, string id, float progress)
        {
            if (e.Motion == null || e.Motion.Count < 2) throw new ArgumentException("The event has no recorded motion.");
            progress = Math.Clamp(progress, 0, 1);
            int upper = e.Motion.FindIndex(x => x.Progress >= progress);
            if (upper <= 0) return e.Motion[0].Participants.Single(x => x.Id == id).Copy();
            var first = e.Motion[upper - 1]; var last = e.Motion[upper];
            var a = first.Participants.Single(x => x.Id == id); var b = last.Participants.Single(x => x.Id == id);
            float t = (progress - first.Progress) / (last.Progress - first.Progress);
            var pose = Lerp(a, b, t * t * (3 - 2 * t));
            var previous = upper > 1 ? e.Motion[upper - 2] : null;
            var next = upper + 1 < e.Motion.Count ? e.Motion[upper + 1] : null;
            var before = previous?.Participants.Single(p => p.Id == id);
            var after = next?.Participants.Single(p => p.Id == id);
            pose.X = Position(true); pose.Z = Position(false);
            return pose;

            float Position(bool x)
            {
                float Value(MatchMotionPose p) => x ? p.X : p.Z;
                float Tangent(float left, float right) => left * right <= 0 ? 0 : 2 * left * right / (left + right);
                float duration = last.Progress - first.Progress;
                float slope = (Value(b) - Value(a)) / duration;
                float start = before == null ? 0 : Tangent((Value(a) - Value(before)) / (first.Progress - previous.Progress), slope);
                float end = after == null ? 0 : Tangent(slope, (Value(after) - Value(b)) / (next.Progress - last.Progress));
                float seconds = e.MotionSeconds > 0 ? e.MotionSeconds : Math.Max(1, e.DurationSeconds);
                if (a.HasVelocity) start = (x ? a.VelocityX : a.VelocityZ) * seconds;
                if (b.HasVelocity) end = (x ? b.VelocityX : b.VelocityZ) * seconds;
                float t2 = t * t, t3 = t2 * t;
                return (2 * t3 - 3 * t2 + 1) * Value(a) + (t3 - 2 * t2 + t) * duration * start +
                    (-2 * t3 + 3 * t2) * Value(b) + (t3 - t2) * duration * end;
            }
        }

        private static MatchMotionPose Pose(MatchParticipantSnapshot p) => new() { Id = p.Id, X = p.RingX, Z = p.RingZ,
            Height = p.Height, FacingX = p.FacingX, FacingZ = p.FacingZ, Posture = p.Posture, Grounded = Grounded(p.Posture), Grip = p.Grip, GripTargetId = p.GripTargetId };
        private static void Write(MatchMotionPose pose, MatchEngineParticipant p)
        { pose.X = p.RingX; pose.Z = p.RingZ; pose.Height = p.Height; pose.Posture = p.Posture; }
        private static void NormalizePosture(MatchEngineParticipant p)
        { if (!p.IsDowned) p.Posture = MatchPosture.Standing; else if (p.Posture == MatchPosture.Standing) p.Posture = MatchPosture.Supine; }
        private static Vector2 Point(MatchMotionPose p) => new(p.X, p.Z);
        private static float Grounded(MatchPosture posture) => posture switch { MatchPosture.Supine or MatchPosture.Prone => 1, MatchPosture.Seated => .5f, MatchPosture.Kneeling => .3f, _ => 0 };
        private static void Set(MatchMotionPose p, Vector2 v)
        { p.X = Math.Clamp(v.X, -1.4f, 1.4f); p.Z = Math.Clamp(v.Y, -1.4f, 1.4f); }
        private static Vector2 Direction(Vector2 v) => v.LengthSquared() < .0001f ? Vector2.UnitX : Vector2.Normalize(v);
        private static Vector2 Clamp(Vector2 v) => new(Math.Clamp(v.X, -.84f, .84f), Math.Clamp(v.Y, -.84f, .84f));
        private static Vector2 Corner(Vector2 v, int choice = 0, Vector2? approach = null)
        {
            float Sign(float coordinate, float direction, int bit) => Math.Abs(coordinate) > .0001f ? Math.Sign(coordinate) :
                Math.Abs(direction) > .0001f ? Math.Sign(direction) : (choice & bit) == 0 ? -1 : 1;
            var preferred = approach ?? Vector2.Zero;
            return new Vector2(Sign(v.X, preferred.X, 1), Sign(v.Y, preferred.Y, 2)) * .92f;
        }
        private static Vector2 Boundary(Vector2 v, float radius) => Direction(v) * (radius / Math.Max(Math.Abs(Direction(v).X), Math.Abs(Direction(v).Y)));
        private static void Face(MatchMotionPose p, Vector2 v) { v = Direction(v); p.FacingX = v.X; p.FacingZ = v.Y; }
        private static MatchGrip GripFor(BodyRegion r) => r switch { BodyRegion.Head => MatchGrip.Head, BodyRegion.Arms => MatchGrip.Arm, BodyRegion.Legs => MatchGrip.Leg, _ => MatchGrip.Body };
        private static MatchGrip ChainGrip(ChainPosition p) => p switch { ChainPosition.Headlock => MatchGrip.Head, ChainPosition.WaistLock => MatchGrip.Waist, ChainPosition.Hammerlock => MatchGrip.Arm, _ => MatchGrip.Wrist };
        private static void SetBoundary(MatchEngineParticipant p, MatchPosition position)
        {
            var point = new Vector2(p.RingX, p.RingZ);
            point = position is MatchPosition.Corner or MatchPosition.TopRope ? Corner(point, p.Id.Sum(c => (int)c), new Vector2(p.FacingX, p.FacingZ)) : Boundary(point, position == MatchPosition.Outside ? 1.3f : position == MatchPosition.Apron ? 1.12f : .92f);
            p.RingX = point.X; p.RingZ = point.Y; p.Height = position == MatchPosition.TopRope ? .5f : position == MatchPosition.Outside ? -.4f : 0;
        }
        private static List<MatchMotionPose> Interpolate(List<MatchMotionPose> a, List<MatchMotionPose> b, float t) => a.Select(p => Lerp(p, b.Single(q => q.Id == p.Id), t)).ToList();
        private static MatchMotionPose Lerp(MatchMotionPose a, MatchMotionPose b, float t)
        {
            if (t <= 0) return a.Copy();
            if (t >= 1) return b.Copy();
            var pose = a.Copy();
            pose.X = a.X + (b.X - a.X) * t; pose.Z = a.Z + (b.Z - a.Z) * t; pose.Height = a.Height + (b.Height - a.Height) * t;
            pose.Tilt = a.Tilt + (b.Tilt - a.Tilt) * t; pose.Grounded = a.Grounded + (b.Grounded - a.Grounded) * t;
            float angle = (float)Math.Atan2(a.FacingZ, a.FacingX);
            float turn = (float)Math.Atan2(a.FacingX * b.FacingZ - a.FacingZ * b.FacingX,
                a.FacingX * b.FacingX + a.FacingZ * b.FacingZ);
            pose.FacingX = (float)Math.Cos(angle + turn * t); pose.FacingZ = (float)Math.Sin(angle + turn * t);
            return pose;
        }
    }
}
