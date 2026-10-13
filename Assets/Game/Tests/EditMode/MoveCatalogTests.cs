using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PWManager.Data.Catalogs;
using PWManager.Data.Definitions;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Tests
{
    public sealed class MoveCatalogTests
    {
        [Test]
        public void MovesEditorFiltersEachRoleAndKeepsOnlyItsExistingIneligibleAssignment()
        {
            var previous = DashboardSession.ActiveSave;
            var transient = DashboardSession.IsTransient;
            var host = new GameObject("Move choices test");
            host.SetActive(false);
            try
            {
                DashboardSession.SetActiveSave(null, true);
                var moves = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog").Moves;
                var existing = moves.First(x => !x.Enabled).Id;
                var wrestler = new WrestlerState { Presentation = new WrestlerPresentationState {
                    WrestlingStyleId = "style_006", SignatureMoveIds = new() { existing } },
                    Attributes = new WrestlerAttributesState { Brawling = 20, Power = 20, Technical = 20, HighFlying = 20 } };
                var controller = host.AddComponent<WrestlerProfileController>();
                var root = new VisualElement();
                var type = typeof(WrestlerProfileController);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                type.GetField("root", flags).SetValue(controller, root);
                type.GetField("boundWrestler", flags).SetValue(controller, wrestler);
                type.GetField("canSaveStyles", flags).SetValue(controller, true);
                type.GetMethod("OpenMovesEditor", flags).Invoke(controller, null);
                var fields = root.Query<DropdownField>().ToList();
                Assert.That(fields.Count, Is.EqualTo(4));
                for (int i = 0; i < fields.Count; i++)
                {
                    var ids = ((System.Collections.Generic.List<string>)fields[i].userData).Where(x => x != null);
                    var expected = moves.Where(x => x.CanAssign(wrestler, i >= 2) || i == 0 && x.Id == existing).Select(x => x.Id);
                    Assert.That(ids, Is.EquivalentTo(expected), fields[i].label);
                }
                Assert.That(((System.Collections.Generic.List<string>)fields[0].userData)[fields[0].index], Is.EqualTo(existing));
                Assert.That(moves.Any(x => x.CanAssign(wrestler, false) && !x.CanAssign(wrestler, true)), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                DashboardSession.SetActiveSave(previous, transient);
            }
        }

        [Test]
        public void TableRejectsDuplicatesBadNumbersAndPrematureSituationsAndReadsExcelQuotes()
        {
            var text = Resources.Load<TextAsset>(MoveTable.ResourcePath).text;
            var rows = MoveTable.Parse(text);
            Assert.That(rows.Count, Is.GreaterThan(100));
            Assert.That(rows.Count(x => !x.Enabled), Is.GreaterThan(0));
            var first = text.Split('\n')[1];
            Assert.Throws<FormatException>(() => MoveTable.Parse(text + "\n" + first));
            Assert.Throws<FormatException>(() => MoveTable.Parse(text.Replace(",10.0,7.0,", ",NaN,7.0,")));
            Assert.Throws<FormatException>(() => MoveTable.Parse(text.Replace("SpringboardStanding,Aerial,Body,style_004;style_005;style_007,False", "SpringboardStanding,Aerial,Body,style_004;style_005;style_007,True")));
            var quoted = text.Replace("스피닝 백피스트", "\"스피닝, \"\"백피스트\"\"\"");
            Assert.That(MoveTable.Parse(quoted).Single(x => x.Id == "move_003").Rules.Name, Is.EqualTo("스피닝, \"백피스트\""));
        }

        [Test]
        public void StylesLimitGeneralPoolsAndBothDivingPosturesRemainAvailable()
        {
            MoveTable.Initialize();
            var actor = new MatchEngineParticipant { Id = "a", WrestlingStyleId = "style_006" };
            var target = new MatchEngineParticipant { Id = "b" };
            Assert.That(MatchTechniques.Candidates(MatchActionType.HeavyStrike, actor, target).Any(x => x.Key == "dropkick"), Is.False);
            actor.WrestlingStyleId = "style_004";
            Assert.That(MatchTechniques.Candidates(MatchActionType.HeavyStrike, actor, target).Any(x => x.Key == "dropkick"), Is.True);
            Assert.That(MatchTechniques.Candidates(MatchActionType.Dive, actor, target).All(x => x.Rules.Situation == MoveSituation.DivingStanding), Is.True);
            Assert.That(MatchTechniques.Candidates(MatchActionType.Dive, actor, target), Is.Not.Empty);
            target.IsDowned = true;
            Assert.That(MatchTechniques.Candidates(MatchActionType.Dive, actor, target).All(x => x.Rules.Situation == MoveSituation.DivingGrounded), Is.True);
            Assert.That(MatchTechniques.Candidates(MatchActionType.Dive, actor, target), Is.Not.Empty);
            Assert.That(MatchTechniques.Find("running-powerslam"), Is.Not.Null);
            Assert.That(MatchTechniques.All.Where(x => !x.Enabled).All(x => !x.CanBeSignature && !x.CanBeFinisher), Is.True);
        }

        [Test]
        public void EveryStyleFinishesMatchesUsingOnlyItsAllowedGeneralTechniques()
        {
            MoveTable.Initialize();
            for (int style = 1; style <= 7; style++)
            for (int seed = 0; seed < 4; seed++)
            {
                var a = new MatchEngineParticipant { Id = "a", WrestlingStyleId = $"style_{style:000}",
                    Attributes = new WrestlerAttributesState { Brawling = 12, Power = 12, Technical = 12, HighFlying = 12, Selling = 12, Stamina = 12 } };
                var b = new MatchEngineParticipant { Id = "b", WrestlingStyleId = "style_007",
                    Attributes = new WrestlerAttributesState { Brawling = 12, Power = 12, Technical = 12, HighFlying = 12, Selling = 12, Stamina = 12 } };
                var match = new MatchSimulator(seed).Simulate(new[] { a, b }, 600,
                    new MatchEngineBooking { WinnerId = "a", LoserId = "b" });
                Assert.That(match.IsFinished && !match.IsAborted, Is.True, $"Style {style}, seed {seed}: {match.FailureReason}");
                foreach (var e in match.Events.Where(x => !string.IsNullOrEmpty(x.TechniqueId)))
                {
                    var technique = MatchTechniques.Find(e.TechniqueId);
                    Assert.That(technique, Is.Not.Null, e.TechniqueId);
                    Assert.That(technique.Enabled && technique.CanUseGenerally, Is.True, e.TechniqueId);
                    Assert.That(technique.AllowsStyle(match.GetParticipant(e.PerformerId).WrestlingStyleId), Is.True, e.TechniqueId);
                }
            }
        }

        [Test]
        public void AssignmentChecksRoleStyleAndAbilityIndependently()
        {
            var move = ScriptableObject.CreateInstance<MoveDefinition>();
            var wrestler = new WrestlerState { Presentation = new WrestlerPresentationState { WrestlingStyleId = "style_004" } };
            try
            {
                move.CanBeSignature = true; move.CanBeFinisher = false;
                Assert.That(move.CanAssign(wrestler, false), Is.True);
                Assert.That(move.CanAssign(wrestler, true), Is.False);
                move.AllowedStyleIds = new[] { "style_006" };
                Assert.That(move.CanAssign(wrestler, false), Is.False);
                move.AllowedStyleIds = new[] { "style_004" };
                move.RequiredStat = MoveRequiredStat.HighFlying; move.RequiredStatValue = 10;
                wrestler.Attributes.HighFlying = 9;
                Assert.That(move.CanAssign(wrestler, false), Is.False);
                wrestler.Attributes.HighFlying = 10;
                Assert.That(move.CanAssign(wrestler, false), Is.True);
                move.Enabled = false;
                Assert.That(move.CanAssign(wrestler, false), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(move); }
        }

        [Test]
        public void MoveRolesAreIndependentAndLegacyMovesKeepTheirEligibility()
        {
            var move = ScriptableObject.CreateInstance<MoveDefinition>();
            try
            {
                var signature = typeof(MoveDefinition).GetField("CanBeSignature");
                var finisher = typeof(MoveDefinition).GetField("CanBeFinisher");
                Assert.That(signature, Is.Not.Null, "Signature eligibility must be explicit.");
                Assert.That(finisher, Is.Not.Null, "Finisher eligibility must be explicit.");
                Assert.That(signature.GetValue(move), Is.EqualTo(true));
                Assert.That(finisher.GetValue(move), Is.EqualTo(true));
                signature.SetValue(move, false);
                Assert.That(finisher.GetValue(move), Is.EqualTo(true));
            }
            finally { UnityEngine.Object.DestroyImmediate(move); }
        }

        [Test]
        public void EditableMoveTablePreservesExistingIdsAndContainsSituationalGeneralMoves()
        {
            var table = Resources.Load<TextAsset>("PWManagerRuntime/MatchMoves");
            Assert.That(table, Is.Not.Null, "The editable move table must ship with the game.");
            var lines = table.text.Split('\n').Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            Assert.That(lines[0], Does.Contain("CanBeSignature"));
            Assert.That(lines[0], Does.Contain("CanBeFinisher"));
            for (var i = 1; i <= 24; i++)
                Assert.That(lines.Any(x => x.StartsWith($"move_{i:000},", StringComparison.Ordinal)), Is.True);
            Assert.That(lines.Any(x => x.Contains("DivingStanding")), Is.True);
            Assert.That(lines.Any(x => x.Contains("DivingGrounded")), Is.True);
        }
    }
}
