using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace PWManager.Domain.Services
{
    public sealed class MoveMotionBeat
    {
        public float Progress, ActorForward, ActorSide, TargetForward, TargetSide, ActorYaw, TargetYaw, ActorRoll, TargetRoll;
        public float? ActorHeight, TargetHeight;
        public MatchPosture? ActorPosture, TargetPosture;
    }

    public static class MatchMoveMotion
    {
        private static IReadOnlyDictionary<string, List<MoveMotionBeat>> table;
        public static void Configure(IReadOnlyDictionary<string, List<MoveMotionBeat>> value) => table = value;

        public static bool IsBridgingSuplex(string id) => MatchTechniques.Find(id)?.Id == "move_023";

        public static MatchPosture ActorEndPosture(string id) => id != null && table != null && table.TryGetValue(id, out var beats)
            ? beats.LastOrDefault(b => b.ActorPosture.HasValue)?.ActorPosture ?? MatchPosture.Standing : MatchPosture.Standing;

        public static bool Apply(MatchEngineState state, MatchEngineEvent e, MatchEngineSnapshot before)
        {
            if (e.Type is not (MatchActionType.Probe or MatchActionType.TieUp or MatchActionType.Chain or
                MatchActionType.BasicStrike or MatchActionType.HeavyStrike or MatchActionType.Grapple or
                MatchActionType.GroundAttack or MatchActionType.LimbAttack or MatchActionType.Signature or MatchActionType.Finisher or
                MatchActionType.Dive or MatchActionType.RunningAttack or MatchActionType.CornerAttack or MatchActionType.RingsideAttack or
                MatchActionType.TopRopeAttack or MatchActionType.Counter or MatchActionType.Submission or MatchActionType.StandingHold)) return false;
            var move = MatchTechniques.Find(e);
            if (move == null || table == null || !table.TryGetValue(move.Id, out var beats) ||
                e.Result is MatchActionResult.Countered or MatchActionResult.ExecutionFailure or MatchActionResult.Blocked or
                    MatchActionResult.Escaped or MatchActionResult.Interrupted) return false;
            var a = before.Participants.FirstOrDefault(p => p.Id == e.PerformerId);
            var b = before.Participants.FirstOrDefault(p => p.Id == e.ReceiverId);
            if (a == null || b == null) return false;
            var forward = new Vector2(b.RingX - a.RingX, b.RingZ - a.RingZ);
            forward = forward.LengthSquared() < .0001f ? Vector2.UnitX : Vector2.Normalize(forward);
            var side = new Vector2(-forward.Y, forward.X);
            var origin = new Vector2(b.RingX, b.RingZ);
            bool outsideDive = MatchTechniques.IsOutsideDive(move.Rules.Situation);
            bool outside = b.Position == MatchPosition.Outside && (a.Position == MatchPosition.Outside || outsideDive);
            bool runningDive = move.Rules.Situation == MoveSituation.RingsideDive;
            var launch = runningDive ? origin * (.92f / Math.Max(Math.Abs(origin.X), Math.Abs(origin.Y))) : new Vector2(a.RingX, a.RingZ);
            if (runningDive)
            {
                forward = Vector2.Normalize(origin - launch);
                side = new Vector2(-forward.Y, forward.X);
            }
            float launchScale = outsideDive ? Vector2.Distance(launch, origin) /
                Math.Max(.01f, Math.Abs(beats[0].ActorForward)) : 1;
            float limit = a.Position is MatchPosition.TopRope or MatchPosition.Corner || b.Position is MatchPosition.TopRope or MatchPosition.Corner ? .92f : .84f;
            var points = beats.SelectMany(x => new[] { origin + forward * x.ActorForward + side * x.ActorSide,
                origin + forward * x.TargetForward + side * x.TargetSide }).ToArray();
            if (!outside)
            {
                float minX = points.Min(p => p.X), maxX = points.Max(p => p.X);
                float minZ = points.Min(p => p.Y), maxZ = points.Max(p => p.Y);
                origin += new Vector2(minX < -limit ? -limit - minX : maxX > limit ? limit - maxX : 0,
                    minZ < -limit ? -limit - minZ : maxZ > limit ? limit - maxZ : 0);
            }
            var frames = new List<MatchMotionFrame> { e.Motion[0] };
            foreach (var beat in beats)
            {
                var poses = state.Participants.Select(p => MatchMovement.Sample(e, p.Id, beat.Progress)).ToList();
                Set(poses.Single(p => p.Id == a.Id), a, beat.ActorForward * launchScale, beat.ActorSide, beat.ActorHeight, beat.ActorYaw, beat.ActorRoll);
                Set(poses.Single(p => p.Id == b.Id), b, beat.TargetForward, beat.TargetSide, beat.TargetHeight, beat.TargetYaw, beat.TargetRoll);
                if (runningDive && beat.Progress <= .34f)
                {
                    var actor = poses.Single(p => p.Id == a.Id);
                    var point = Vector2.Lerp(new Vector2(a.RingX, a.RingZ), launch, beat.Progress / .34f);
                    actor.X = point.X; actor.Z = point.Y; actor.Height = a.Height;
                }
                SetPosture(poses.Single(p => p.Id == a.Id), beat.ActorPosture);
                SetPosture(poses.Single(p => p.Id == b.Id), beat.TargetPosture);
                if (move.Id == "move_023" && e.Result == MatchActionResult.FullSuccess && beat.Progress >= .3f)
                {
                    var actor = poses.Single(p => p.Id == a.Id);
                    actor.Grip = MatchGrip.Waist; actor.GripTargetId = b.Id;
                }
                if (move.Id == "move_023" && e.Result == MatchActionResult.ReceptionFailure && beat.Progress >= .65f)
                {
                    Set(poses.Single(p => p.Id == a.Id), a, beat.ActorForward, beat.ActorSide, 0, beat.ActorYaw, 0);
                    Set(poses.Single(p => p.Id == b.Id), b, beat.TargetForward - .17f, beat.TargetSide, 0, beat.TargetYaw, beat.TargetRoll);
                }
                if (move.Rules.MotionProfile == MoveMotionProfile.Hold && beat.Progress < .5f)
                {
                    var hold = beats.Last();
                    var destination = origin + forward * hold.ActorForward + side * hold.ActorSide;
                    destination = Vector2.Clamp(destination, new Vector2(outside ? -1.4f : -limit), new Vector2(outside ? 1.4f : limit));
                    float approach = Math.Clamp(beat.Progress / .5f, 0, 1);
                    approach = approach * approach * (3 - 2 * approach);
                    var point = Vector2.Lerp(new Vector2(a.RingX, a.RingZ), destination, approach);
                    var actor = poses.Single(p => p.Id == a.Id);
                    actor.X = point.X; actor.Z = point.Y;
                }
                frames.Add(new MatchMotionFrame { Progress = beat.Progress, Participants = poses });
            }
            e.Motion = frames;
            if (move.Id == "move_023" && e.Result == MatchActionResult.FullSuccess) e.Detail = "bridge";
            foreach (var p in state.Participants)
            {
                var pose = frames[^1].Participants.Single(x => x.Id == p.Id);
                p.RingX = pose.X; p.RingZ = pose.Z; p.Height = pose.Height;
                p.FacingX = pose.FacingX; p.FacingZ = pose.FacingZ;
                p.Posture = pose.Posture; p.IsDowned = pose.Posture != MatchPosture.Standing;
                p.Grip = pose.Grip; p.GripTargetId = pose.GripTargetId;
            }
            return true;

            void SetPosture(MatchMotionPose pose, MatchPosture? posture)
            {
                if (!posture.HasValue) return;
                pose.Posture = posture.Value;
                pose.Grounded = posture.Value switch { MatchPosture.Supine or MatchPosture.Prone => 1, MatchPosture.Seated => .5f, MatchPosture.Kneeling => .3f, _ => 0 };
            }

            void Set(MatchMotionPose pose, MatchParticipantSnapshot initial, float f, float s, float? height, float yaw, float roll)
            {
                var point = origin + forward * f + side * s;
                point = Vector2.Clamp(point, new Vector2(outside ? -1.4f : -limit), new Vector2(outside ? 1.4f : limit));
                if (outside && (!outsideDive || initial.Id == b.Id || height == 0) && Math.Max(Math.Abs(point.X), Math.Abs(point.Y)) < 1.15f)
                {
                    if (Math.Abs(origin.X) >= Math.Abs(origin.Y)) point.X = Math.Sign(origin.X) * 1.15f;
                    else point.Y = Math.Sign(origin.Y) * 1.15f;
                }
                pose.X = point.X; pose.Z = point.Y;
                pose.Height = height.HasValue ? (outside ? -.4f : 0) + height.Value : initial.Height;
                pose.Tilt = roll;
                float radians = yaw * (float)Math.PI / 180;
                var facing = forward * (float)Math.Cos(radians) + side * (float)Math.Sin(radians);
                pose.FacingX = facing.X; pose.FacingZ = facing.Y;
            }
        }
    }
}
