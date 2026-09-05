using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LLMUnity;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;

namespace PWManager.Presentation
{
    public sealed class PromoNarrativeGenerationService
    {
        private const int MaximumResponseLength = 12000;
        private const string ResponseSchema = "{\"type\":\"object\",\"properties\":{\"Summary\":{\"type\":\"string\"},\"FulfilledBeatIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"Lines\":{\"type\":\"array\",\"minItems\":3,\"maxItems\":6,\"items\":{\"type\":\"object\",\"properties\":{\"Type\":{\"type\":\"string\",\"enum\":[\"Dialogue\",\"Action\",\"Crowd\"]},\"SpeakerId\":{\"type\":\"string\"},\"Text\":{\"type\":\"string\"},\"Intensity\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":5}},\"required\":[\"Type\",\"SpeakerId\",\"Text\",\"Intensity\"],\"additionalProperties\":false}}},\"required\":[\"Summary\",\"FulfilledBeatIds\",\"Lines\"],\"additionalProperties\":false}";
        private readonly LLMAgent agent;
        private readonly int timeoutMilliseconds;
        private bool generationTimedOut;

        public PromoNarrativeGenerationService(LLMAgent agent, int timeoutMilliseconds = 15000)
        {
            if (timeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            this.agent = agent;
            this.timeoutMilliseconds = timeoutMilliseconds;
        }

        public async Task<PromoScriptState> Generate(PromoGenerationContext context)
        {
            if (context?.Result?.Narrative != null) return context.Result.Narrative;
            var fallback = PromoFallbackNarration.Create(context);
            if (generationTimedOut || agent == null || agent.llm == null || string.IsNullOrWhiteSpace(agent.llm.model)) return fallback;
            try
            {
                agent.systemPrompt = PromoPromptBuilder.SystemPrompt;
                agent.grammar = ResponseSchema;
                using var timeout = new CancellationTokenSource();
                var generation = agent.Chat(PromoPromptBuilder.Build(context), addToHistory: false);
                if (await Task.WhenAny(generation, Task.Delay(timeoutMilliseconds, timeout.Token)) != generation)
                {
                    generationTimedOut = true;
                    // Observe a late failure without waiting for a stalled native request to finish.
                    _ = generation.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                    if (agent != null) agent.CancelRequests();
                    return fallback;
                }
                timeout.Cancel();
                var response = await generation;
                return TryParse(response, context, out var script) ? script : fallback;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Promo narrative generation failed: {exception.Message}");
                return fallback;
            }
        }

        private static bool TryParse(string json, PromoGenerationContext context, out PromoScriptState script)
        {
            script = null;
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumResponseLength) return false;
            json = ExtractJson(json);
            PromoResponse response;
            try { response = JsonUtility.FromJson<PromoResponse>(json); }
            catch { return false; }
            if (response?.Lines == null || response.Lines.Count == 0) return false;
            var allowedSpeakers = new HashSet<string>(context.Wrestlers.Select(x => x.Id), StringComparer.Ordinal);
            var lines = new List<NarrativeLineState>();
            foreach (var line in response.Lines)
            {
                if (line == null || string.IsNullOrWhiteSpace(line.Text) || !Enum.TryParse(line.Type, true, out NarrativeLineType type) ||
                    type == NarrativeLineType.Commentary ||
                    (type == NarrativeLineType.Dialogue && !allowedSpeakers.Contains(line.SpeakerId)) ||
                    (type != NarrativeLineType.Dialogue && !string.IsNullOrEmpty(line.SpeakerId) && !allowedSpeakers.Contains(line.SpeakerId))) return false;
                lines.Add(new NarrativeLineState { Type = type, SpeakerId = line.SpeakerId, Text = line.Text.Trim(), Intensity = Math.Max(0, Math.Min(5, line.Intensity)) });
            }
            var fulfilled = response.FulfilledBeatIds ?? new List<string>();
            if (context.MandatoryBeatIds.Any(x => !fulfilled.Contains(x))) return false;
            script = new PromoScriptState { Summary = response.Summary?.Trim(), Lines = lines, FulfilledBeatIds = fulfilled };
            return true;
        }

        private static string ExtractJson(string value)
        {
            var start = value.IndexOf('{'); var end = value.LastIndexOf('}');
            return start >= 0 && end > start ? value.Substring(start, end - start + 1) : value;
        }

        [Serializable] private sealed class PromoResponse
        {
            public string Summary;
            public List<string> FulfilledBeatIds = new();
            public List<PromoLineResponse> Lines = new();
        }

        [Serializable] private sealed class PromoLineResponse
        {
            public string Type;
            public string SpeakerId;
            public string Text;
            public int Intensity;
        }
    }
}
