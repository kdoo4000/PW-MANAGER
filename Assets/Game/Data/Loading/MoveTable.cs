using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PWManager.Data.Definitions;
using EntityId = PWManager.Domain.Identifiers.EntityId;
using PWManager.Domain.Services;
using UnityEngine;

namespace PWManager.Data.Loading
{
    public static class MoveTable
    {
        public const string ResourcePath = "PWManagerRuntime/MatchMoves";
        private const string Header = "Id,Key,KoreanName,EnglishName,Action,Situation,Kind,TargetRegion,AllowedStyleIds,Enabled,CanUseGenerally,CanBeSignature,CanBeFinisher,BrawlingWeight,PowerWeight,TechnicalWeight,HighFlyingWeight,ExecutionDifficulty,SellingDifficulty,RequiredStat,RequiredStatValue,MotionProfile,EndPosture,TravelDistance";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        public static void Initialize()
        {
            var moves = Load();
            MatchTechniques.Configure(moves);
            MoveMotionTable.Initialize(moves);
        }

        public static IReadOnlyList<MatchTechnique> Load()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null) throw new InvalidOperationException("MatchMoves.csv is required.");
            return Parse(asset.text);
        }

        public static IReadOnlyList<MatchTechnique> Parse(string csv)
        {
            var records = ReadCsv(csv).ToList();
            if (records.Count < 2 || string.Join(",", records[0]) != Header && string.Join(",", records[0]) != Header + ",ExecutionStyle")
                throw new FormatException("MatchMoves.csv has a missing or incorrect header.");
            int columns = records[0].Length;
            var rows = new List<MatchTechnique>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var cells in records.Skip(1))
            {
                if (cells.Length != columns) throw new FormatException($"Move row {rows.Count + 2} must have {columns} columns.");
                var id = cells[0];
                if (!EntityId.IsValidStaticId(id) || !id.StartsWith("move_", StringComparison.Ordinal) || !ids.Add(id))
                    throw new FormatException($"Invalid or duplicate move ID: {id}");
                if (string.IsNullOrWhiteSpace(cells[1]) || !keys.Add(cells[1]))
                    throw new FormatException($"Missing or duplicate technique key: {id}");
                if (string.IsNullOrWhiteSpace(cells[2]) || string.IsNullOrWhiteSpace(cells[3]))
                    throw new FormatException($"Move names are required: {id}");
                var action = Value<MatchActionType>(cells[4]);
                var situation = Value<MoveSituation>(cells[5]);
                var row = new MatchTechnique(id, cells[2], action, MatchTechniques.IsGrounded(situation), Value<MoveKind>(cells[6]), Value<BodyRegion>(cells[7]))
                {
                    Key = cells[1], EnglishName = cells[3],
                    AllowedStyleIds = cells[8].Length == 0 ? Array.Empty<string>() : cells[8].Split(';'),
                    Enabled = bool.Parse(cells[9]), CanUseGenerally = bool.Parse(cells[10]),
                    CanBeSignature = bool.Parse(cells[11]), CanBeFinisher = bool.Parse(cells[12]),
                    RequiredStat = Value<MoveRequiredStat>(cells[19]).ToString(), RequiredStatValue = Number(cells[20])
                };
                row.Rules.Situation = situation;
                row.Rules.MotionProfile = Value<MoveMotionProfile>(cells[21]);
                row.Rules.ExecutionStyle = columns == 25 ? Value<MoveExecutionStyle>(cells[24]) :
                    situation is MoveSituation.RunningStanding or MoveSituation.RunningGrounded or MoveSituation.RunningRearStanding or MoveSituation.CornerStanding || id is "move_001" or "move_004"
                        ? MoveExecutionStyle.Running : situation is MoveSituation.DivingStanding or MoveSituation.DivingGrounded or MoveSituation.TopRopeGrapple
                            ? MoveExecutionStyle.Aerial : MoveExecutionStyle.Stationary;
                row.Rules.EndPosture = Value<MatchPosture>(cells[22]);
                row.Rules.TravelDistance = Number(cells[23]);
                if (row.Rules.TravelDistance < 0 || row.Rules.TravelDistance > .8f || row.Enabled && row.Rules.MotionProfile == MoveMotionProfile.Automatic)
                    throw new FormatException($"A move needs an explicit motion and travel of 0-0.8 ring half-widths: {id}");
                row.Rules.BrawlingWeight = Number(cells[13]); row.Rules.PowerWeight = Number(cells[14]);
                row.Rules.TechnicalWeight = Number(cells[15]); row.Rules.HighFlyingWeight = Number(cells[16]);
                row.Rules.ExecutionDifficulty = Number(cells[17]); row.Rules.SellingDifficulty = Number(cells[18]);
                var weights = new[] { row.Rules.BrawlingWeight, row.Rules.PowerWeight, row.Rules.TechnicalWeight, row.Rules.HighFlyingWeight };
                if (weights.Any(x => x < 0 || x > 1) || Math.Abs(weights.Sum() - 1) > .001f)
                    throw new FormatException($"Move weights must be 0-1 and total 1: {id}");
                if (row.Rules.ExecutionDifficulty < 1 || row.Rules.ExecutionDifficulty > 20 ||
                    !Enum.IsDefined(typeof(MoveSellingDifficulty), (int)row.Rules.SellingDifficulty) || row.Rules.SellingDifficulty % 1 != 0)
                    throw new FormatException($"Invalid difficulty: {id}");
                if (row.RequiredStat == "None" ? row.RequiredStatValue != 0 : row.RequiredStatValue < 1 || row.RequiredStatValue > 20)
                    throw new FormatException($"Invalid required ability: {id}");
                if (row.AllowedStyleIds.Distinct().Count() != row.AllowedStyleIds.Length || row.AllowedStyleIds.Any(x =>
                    !EntityId.IsValidStaticId(x) || !x.StartsWith("style_", StringComparison.Ordinal)))
                    throw new FormatException($"Invalid style IDs: {id}");
                if (row.Enabled && !MatchTechniques.IsSupported(situation))
                    throw new FormatException($"Situation is not supported yet; disable this row: {id}");
                if (row.Enabled && !MatchesAction(action, situation))
                    throw new FormatException($"Action and situation disagree: {id}");
                rows.Add(row);
            }
            return rows.AsReadOnly();
        }

        private static bool MatchesAction(MatchActionType action, MoveSituation s) => action switch
        {
            MatchActionType.Probe => s == MoveSituation.Probe,
            MatchActionType.TieUp => s == MoveSituation.TieUp,
            MatchActionType.Chain => s == MoveSituation.Chain,
            MatchActionType.BasicStrike => s == MoveSituation.StandingStrike,
            MatchActionType.HeavyStrike => s is MoveSituation.StandingHeavyStrike or MoveSituation.StandingRearStrike,
            MatchActionType.Grapple => s is MoveSituation.StandingGrapple or MoveSituation.StandingRearGrapple,
            MatchActionType.GroundAttack => s is MoveSituation.GroundStrike or MoveSituation.GroundProne or MoveSituation.GroundSeated or MoveSituation.GroundKneeling,
            MatchActionType.LimbAttack => s is MoveSituation.StandingLimb or MoveSituation.GroundLimb,
            MatchActionType.Submission => s is MoveSituation.GroundSubmission or MoveSituation.GroundProne,
            MatchActionType.StandingHold => s is MoveSituation.StandingSubmission or MoveSituation.StandingRearSubmission,
            MatchActionType.Dive => s is MoveSituation.DivingStanding or MoveSituation.DivingGrounded || MatchTechniques.IsOutsideDive(s),
            MatchActionType.RunningAttack => s is MoveSituation.RunningStanding or MoveSituation.RunningGrounded or MoveSituation.RunningRearStanding,
            MatchActionType.CornerAttack => s == MoveSituation.CornerStanding,
            MatchActionType.RingsideAttack => s is MoveSituation.RingsideStanding or MoveSituation.RingsideGrounded or MoveSituation.Table,
            MatchActionType.TopRopeAttack => s == MoveSituation.TopRopeGrapple,
            MatchActionType.DoubleTeam => s is MoveSituation.DoubleTeamStanding or MoveSituation.DoubleTeamGrounded,
            MatchActionType.Signature or MatchActionType.Finisher => MatchTechniques.IsSupported(s),
            _ => false
        };

        private static T Value<T>(string text) where T : struct, Enum =>
            Enum.TryParse<T>(text, false, out var value) && Enum.IsDefined(typeof(T), value) ? value :
                throw new FormatException($"Unknown {typeof(T).Name}: {text}");

        private static float Number(string text) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
            !float.IsNaN(value) && !float.IsInfinity(value) ? value : throw new FormatException($"Invalid number: {text}");

        private static IEnumerable<string[]> ReadCsv(string csv)
        {
            if (csv == null) throw new ArgumentNullException(nameof(csv));
            var cells = new List<string>(); var field = new StringBuilder();
            bool quoted = false, closed = false;
            for (int i = 0; i <= csv.Length; i++)
            {
                char c = i < csv.Length ? csv[i] : '\n';
                if (i == 0 && c == '\uFEFF') continue;
                if (quoted)
                {
                    if (i == csv.Length) throw new FormatException("Unclosed CSV quote.");
                    if (c == '"')
                    {
                        if (i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; }
                        else { quoted = false; closed = true; }
                    }
                    else field.Append(c);
                    continue;
                }
                if (c is ',' or '\r' or '\n')
                {
                    cells.Add(field.ToString()); field.Clear(); closed = false;
                    if (c != ',')
                    {
                        if (cells.Count > 1 || cells[0].Length > 0) yield return cells.ToArray();
                        cells.Clear();
                        if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                    }
                }
                else if (c == '"' && field.Length == 0 && !closed) quoted = true;
                else if (closed || c == '"') throw new FormatException("Invalid CSV quoting.");
                else field.Append(c);
            }
        }
    }
}
