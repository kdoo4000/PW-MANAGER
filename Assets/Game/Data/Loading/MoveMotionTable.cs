using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PWManager.Domain.Services;
using UnityEngine;

namespace PWManager.Data.Loading
{
    public static class MoveMotionTable
    {
        private const string Header = "MoveId,Progress,ActorForward,ActorSide,ActorHeight,ActorYaw,TargetForward,TargetSide,TargetHeight,TargetYaw,ActorRoll,TargetRoll";
        public static void Initialize(IReadOnlyList<MatchTechnique> moves)
        {
            var asset = Resources.Load<TextAsset>("PWManagerRuntime/MatchMoveMotions");
            if (!asset) throw new InvalidOperationException("MatchMoveMotions.csv is required.");
            MatchMoveMotion.Configure(Parse(asset.text, moves));
        }

        public static IReadOnlyDictionary<string, List<MoveMotionBeat>> Parse(string csv, IReadOnlyList<MatchTechnique> moves)
        {
            var lines = csv.TrimStart('\uFEFF').Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2 || lines[0] != Header && lines[0] != Header + ",ActorPosture,TargetPosture") throw new FormatException("Invalid motion table header.");
            int columns = lines[0].Split(',').Length;
            var table = new Dictionary<string, List<MoveMotionBeat>>();
            foreach (var line in lines.Skip(1))
            {
                var c = line.Split(',');
                if (c.Length != columns || !moves.Any(m => m.Id == c[0])) throw new FormatException("Invalid motion row: " + line);
                var beat = new MoveMotionBeat { Progress = Number(c[1]), ActorForward = Number(c[2]), ActorSide = Number(c[3]),
                    ActorHeight = Height(c[4]), ActorYaw = Number(c[5]), TargetForward = Number(c[6]), TargetSide = Number(c[7]),
                    TargetHeight = Height(c[8]), TargetYaw = Number(c[9]), ActorRoll = Number(c[10]), TargetRoll = Number(c[11]) };
                if (columns == 14) { beat.ActorPosture = Posture(c[12]); beat.TargetPosture = Posture(c[13]); }
                if (!table.TryGetValue(c[0], out var beats)) table[c[0]] = beats = new();
                if (beat.Progress <= (beats.Count == 0 ? 0 : beats[^1].Progress) || beat.Progress > 1 ||
                    Math.Abs(beat.ActorForward) > 1.5f || Math.Abs(beat.TargetForward) > 1.5f ||
                    Math.Abs(beat.ActorSide) > 1.5f || Math.Abs(beat.TargetSide) > 1.5f ||
                    beat.ActorHeight is < 0 or > 1.2f || beat.TargetHeight is < 0 or > 1.2f)
                    throw new FormatException("Invalid motion beat: " + line);
                beats.Add(beat);
            }
            foreach (var move in moves.Where(m => m.Enabled && m.Action != MatchActionType.DoubleTeam))
                if (!table.TryGetValue(move.Id, out var beats) || beats.Count < 4 || beats[^1].Progress != 1 ||
                    !RestingHeight(beats[^1].ActorHeight, move) || !RestingHeight(beats[^1].TargetHeight, move) || !beats.Any(b => b.Progress == .65f))
                    throw new FormatException("Missing complete motion: " + move.Id);
            return table;
        }

        private static float Number(string value)
        {
            float number = float.Parse(value, CultureInfo.InvariantCulture);
            if (float.IsNaN(number) || float.IsInfinity(number)) throw new FormatException("Non-finite motion value.");
            return number;
        }
        private static float? Height(string value) => value == "Start" ? null : Number(value);
        private static bool RestingHeight(float? height, MatchTechnique move) => height == 0 ||
            (move.Rules.MotionProfile == MoveMotionProfile.Hold || move.Id == "move_023") && height is >= 0 and <= .06f;
        private static MatchPosture? Posture(string value)
        {
            if (value == "Start") return null;
            if (!Enum.TryParse<MatchPosture>(value, out var posture) || !Enum.IsDefined(typeof(MatchPosture), posture) || value != posture.ToString())
                throw new FormatException("Invalid motion posture: " + value);
            return posture;
        }
    }
}
