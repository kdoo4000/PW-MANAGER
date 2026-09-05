using System;
using System.Threading.Tasks;
using LLMUnity;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Presentation;
using UnityEngine;

namespace PWManager.Tests
{
    public sealed class PromoNarrativeGenerationServiceTests
    {
        private GameObject owner;
        private DelayedPromoAgent agent;

        [SetUp]
        public void SetUp()
        {
            owner = new GameObject("Promo timeout test");
            owner.SetActive(false);
            agent = owner.AddComponent<DelayedPromoAgent>();
            var llm = owner.AddComponent<LLM>();
            typeof(LLM).GetField("_model", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(llm, "test-model");
            agent.SetTestModel(llm);
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(owner);

        [Test]
        public async Task Generate_StalledRequestFallsBackAndSkipsRemainingRequests()
        {
            var service = new PromoNarrativeGenerationService(agent, 20);
            var generation = service.Generate(Context());
            Assert.That(await Task.WhenAny(generation, Task.Delay(2000)), Is.SameAs(generation));
            var fallback = await generation;
            Assert.That(fallback.UsedFallback, Is.True);
            Assert.That(fallback.FulfilledBeatIds, Is.EqualTo(new[] { "opening" }));
            Assert.That((await service.Generate(Context())).UsedFallback, Is.True);
            Assert.That(agent.RequestCount, Is.EqualTo(1));
            agent.Response.SetResult("late response");
            Assert.That(fallback.Lines[0].Text, Is.EqualTo("도전을 선언한다"));
        }

        [Test]
        public async Task Generate_ResponseBeforeDeadlineUsesGeneratedScript()
        {
            agent.Response.SetResult("{\"Summary\":\"도전\",\"FulfilledBeatIds\":[\"opening\"],\"Lines\":[{\"Type\":\"Action\",\"SpeakerId\":\"\",\"Text\":\"도전을 선언한다\",\"Intensity\":2}]}");
            var script = await new PromoNarrativeGenerationService(agent).Generate(Context());
            Assert.That(script.UsedFallback, Is.False);
            Assert.That(script.Summary, Is.EqualTo("도전"));
        }

        private static PromoGenerationContext Context() => new()
        {
            Plan = new PromoPlanState(), Result = new PromoResultState(), ShowEvent = new ShowEventState { PlannedDuration = 5 },
            MandatoryBeatIds = { "opening" }, MandatoryBeats = { "도전을 선언한다" }
        };
    }

    public sealed class DelayedPromoAgent : LLMAgent
    {
        public readonly TaskCompletionSource<string> Response = new();
        public int RequestCount;
        public override void Awake() { }
        public void SetTestModel(LLM model) => _llm = model;
        public override Task<string> Chat(string query, Action<string> callback = null,
            Action completionCallback = null, bool addToHistory = true)
        {
            RequestCount++;
            return Response.Task;
        }
    }
}
