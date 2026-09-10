using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Text;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace PWManager.Tests
{
    public sealed class MatchEnginePreviewTests
    {
        [UnityTest]
        public IEnumerator Preview_GeneratesTwoWrestlers_ReplaysSingles_AndKeepsSeedDeterministic()
        {
            var type = Assembly.Load("PWMANAGER.Editor").GetType("PWManager.Editor.MatchEnginePreviewWindow", true);
            var window = (EditorWindow)ScriptableObject.CreateInstance(type);
            var exportRoot = Path.Combine(Application.temporaryCachePath, "match-log-test-" + Guid.NewGuid().ToString("N"));
            T Field<T>(string name) => (T)type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
            void Click(string name) => typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window.rootVisualElement.Q<Button>(name).clickable, new object[] { null });
            try
            {
                window.position = new Rect(100, 100, 900, 680);
                window.Show();
                yield return null;
                yield return null;
                var root = window.rootVisualElement;
                Assert.That(root.Q<Button>("run").enabledSelf, Is.False);
                Assert.That(root.Q<Button>("export").enabledSelf, Is.False);
                Click("generate");
                var save = Field<GameSave>("save");
                Assert.That(save.Wrestlers.Count, Is.EqualTo(2));
                Assert.That(save.Wrestlers.Select(x => x.Id).Distinct().Count(), Is.EqualTo(2));
                var roster = JsonUtility.ToJson(save);
                Click("generate");
                Assert.That(JsonUtility.ToJson(Field<GameSave>("save")), Is.EqualTo(roster));
                root.Q<DropdownField>("winner").index = 1;
                root.Q<IntegerField>("minutes").value = 5;
                Click("run");
                var result = Field<MatchResultState>("result");
                Assert.That(result.EngineState.IsFinished, Is.True);
                Assert.That(result.WinnerId, Is.EqualTo(Field<GameSave>("save").Wrestlers[1].Id));
                Assert.That(result.EngineState.ElapsedSeconds, Is.EqualTo(300));
                Assert.That(root.Q<Button>("export").enabledSelf, Is.True);
                var snapshot = JsonUtility.ToJson(result.EngineState);
                Click("next");
                Assert.That(root.Q<ScrollView>("match-log").contentContainer.childCount, Is.EqualTo(1));
                var cells = root.Q<ScrollView>("match-log").Query<Label>().ToList().Select(x => x.text).ToList();
                Assert.That(cells, Does.Contain("피로").And.Contain("주도권").And.Contain("행동 준비도").And.Contain("관중 열기"));
                var firstEvent = result.EngineState.Events[0];
                Assert.That(cells.Any(x => x.Contains($"[{firstEvent.Before.Phase} → {firstEvent.After.Phase}]")), Is.True);
                var before = firstEvent.Before.Participants[0].Fatigue;
                var after = firstEvent.After.Participants[0].Fatigue;
                Assert.That(cells, Does.Contain($"{before:F1} → {after:F1}\n({after - before:+0.0;-0.0;0.0})"));
                Assert.That(root.Q<Label>("summary").text, Is.Empty);
                for (var i = 1; i < result.EngineState.Events.Count; i++) Click("next");
                Assert.That(root.Q<ScrollView>("match-log").contentContainer.childCount, Is.EqualTo(result.EngineState.Events.Count));
                Assert.That(root.Q<Label>("summary").text, Does.Contain("경기 평가"));
                Assert.That(root.Q<Button>("next").enabledSelf, Is.False);
                Click("run");
                Assert.That(JsonUtility.ToJson(Field<MatchResultState>("result").EngineState), Is.EqualTo(snapshot));
                Click("play");
                var deadline = EditorApplication.timeSinceStartup + 3;
                while (Field<int>("eventIndex") == 0 && EditorApplication.timeSinceStartup < deadline) yield return null;
                Click("play");
                Assert.That(Field<int>("eventIndex"), Is.GreaterThan(0));
                Assert.That(Field<bool>("playing"), Is.False);
                root.Q<DropdownField>("finish").index = (int)MatchFinishType.Draw;
                Assert.That(root.Q<DropdownField>("winner").enabledSelf, Is.False);
                Click("run");
                result = Field<MatchResultState>("result");
                Assert.That(result.WinnerId, Is.Null);
                Assert.That(MatchNarrationService.NarrateEngineEvent(result, result.EngineState.Events.Count - 1, _ => "선수"), Does.Contain("무승부"));
                root.Q<IntegerField>("generation-seed").value = 999;
                Field<GameSave>("save").Wrestlers[0].Identity.RingName = "=SUM(1,2)\"";
                var exportMethod = type.GetMethod("ExportLogs", BindingFlags.Instance | BindingFlags.NonPublic);
                var directory = (string)exportMethod.Invoke(window, new object[] { exportRoot });
                var json = File.ReadAllText(Path.Combine(directory, "match.json"));
                Assert.That(json, Does.Contain("\"GenerationSeed\": 12345").And.Contain("\"Commentary\"").And.Contain("\"Before\"").And.Contain("\"After\""));
                var exported = JsonUtility.FromJson(json, type.GetNestedType("PreviewLog", BindingFlags.NonPublic));
                var restored = (MatchResultState)exported.GetType().GetField("Match").GetValue(exported);
                Assert.That(JsonUtility.ToJson(restored.EngineState), Is.EqualTo(JsonUtility.ToJson(result.EngineState)));
                var csvPath = Path.Combine(directory, "events.csv");
                Assert.That(File.ReadAllBytes(csvPath).Take(3), Is.EqualTo(new UTF8Encoding(true).GetPreamble()));
                var csv = File.ReadAllText(csvPath);
                Assert.That(csv, Does.Contain("\"피로 증감\"").And.Contain("\"'="));
                Assert.That(File.ReadAllLines(csvPath).Length, Is.EqualTo(1 + result.EngineState.Events.Count * 2));
                var secondDirectory = (string)exportMethod.Invoke(window, new object[] { exportRoot });
                Assert.That(secondDirectory, Is.Not.EqualTo(directory));
                Assert.That(File.ReadAllText(Path.Combine(directory, "match.json")), Is.EqualTo(json));
                yield return null;
                Assert.That(root.Q<ScrollView>("match-log").worldBound.height, Is.GreaterThan(100));
                Assert.That(root.Q<Label>("summary").worldBound.yMax, Is.LessThanOrEqualTo(root.worldBound.yMax + 1));
                Assert.That(root.Q<Button>("run").worldBound.xMax, Is.LessThan(root.Q<Button>("next").worldBound.xMin));
            }
            finally
            {
                window.Close(); UnityEngine.Object.DestroyImmediate(window);
                if (Directory.Exists(exportRoot)) Directory.Delete(exportRoot, true);
            }
        }
    }
}
