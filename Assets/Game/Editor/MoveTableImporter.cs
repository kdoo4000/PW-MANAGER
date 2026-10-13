using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PWManager.Data.Catalogs;
using PWManager.Data.Definitions;
using PWManager.Data.Loading;
using PWManager.Domain.Services;
using UnityEditor;
using UnityEngine;

namespace PWManager.Editor
{
    public static class MoveTableImporter
    {
        private const string Folder = "Assets/Game/Data/Static/Wrestlers/Moves";
        private static readonly string[] CatalogPaths =
        {
            "Assets/Game/Data/Static/GameStaticContentCatalog.asset",
            "Assets/Game/Presentation/Resources/PWManagerRuntime/GameStaticContentCatalog.asset"
        };

        [MenuItem("PW Manager/Import Move Table")]
        public static void Import()
        {
            var rows = MoveTable.Load();
            var catalogs = CatalogPaths.Select(AssetDatabase.LoadAssetAtPath<StaticContentCatalog>).ToArray();
            if (catalogs.Any(x => x == null)) throw new InvalidOperationException("Both static content catalogs are required.");
            foreach (var catalog in catalogs)
            {
                var styles = catalog.WrestlingStyles.Select(x => x.Id).ToHashSet();
                foreach (var row in rows)
                    if (row.AllowedStyleIds.Any(x => !styles.Contains(x)))
                        throw new InvalidOperationException($"Move {row.Id} references an unknown style.");
            }
            var moves = CreateMoves(rows);
            foreach (var catalog in catalogs)
            {
                Undo.RecordObject(catalog, "Import move table");
                catalog.Moves = moves;
                EditorUtility.SetDirty(catalog);
            }
            AssetDatabase.SaveAssets();
            MatchTechniques.Configure(rows);
            Debug.Log($"Imported {rows.Count} moves ({rows.Count(x => x.Enabled)} enabled).");
        }

        public static List<MoveDefinition> CreateMoves() => CreateMoves(MoveTable.Load());

        private static List<MoveDefinition> CreateMoves(IReadOnlyList<MatchTechnique> rows)
        {
            var existing = AssetDatabase.FindAssets("t:MoveDefinition", new[] { Folder })
                .Select(x => AssetDatabase.LoadAssetAtPath<MoveDefinition>(AssetDatabase.GUIDToAssetPath(x))).ToDictionary(x => x.Id);
            if (existing.Keys.Any(id => !rows.Any(row => row.Id == id)))
                throw new InvalidOperationException("The move table cannot silently remove an existing move ID.");
            Directory.CreateDirectory(Folder);
            var moves = new List<MoveDefinition>();
            foreach (var row in rows)
            {
                if (!existing.TryGetValue(row.Id, out var move))
                {
                    move = ScriptableObject.CreateInstance<MoveDefinition>();
                    AssetDatabase.CreateAsset(move, $"{Folder}/{row.Id}_{row.Key.Replace('-', '_')}.asset");
                }
                Undo.RecordObject(move, "Import move table");
                move.SetEditorIdentity(row.Id, row.Rules.Name, row.EnglishName);
                move.TechniqueKey = row.Key; move.Action = row.Action; move.Situation = row.Rules.Situation;
                move.Kind = row.Rules.Kind; move.TargetRegion = row.Rules.TargetRegion;
                move.AllowedStyleIds = row.AllowedStyleIds; move.Enabled = row.Enabled;
                move.CanUseGenerally = row.CanUseGenerally; move.CanBeSignature = row.CanBeSignature; move.CanBeFinisher = row.CanBeFinisher;
                move.BrawlingWeight = row.Rules.BrawlingWeight; move.PowerWeight = row.Rules.PowerWeight;
                move.TechnicalWeight = row.Rules.TechnicalWeight; move.HighFlyingWeight = row.Rules.HighFlyingWeight;
                move.ExecutionDifficulty = row.Rules.ExecutionDifficulty; move.SellingDifficulty = (MoveSellingDifficulty)row.Rules.SellingDifficulty;
                move.RequiredStat = Enum.Parse<MoveRequiredStat>(row.RequiredStat); move.RequiredStatValue = row.RequiredStatValue;
                move.MotionProfile = row.Rules.MotionProfile; move.EndPosture = row.Rules.EndPosture; move.TravelDistance = row.Rules.TravelDistance;
                move.ExecutionStyle = row.Rules.ExecutionStyle;
                EditorUtility.SetDirty(move); moves.Add(move);
            }
            return moves;
        }
    }
}
