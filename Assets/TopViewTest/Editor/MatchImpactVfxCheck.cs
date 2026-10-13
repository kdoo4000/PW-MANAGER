using System;
using System.Linq;
using PWManager.Data.Loading;
using PWManager.Domain.Services;
using UnityEditor;
using UnityEngine;

public static class MatchImpactVfxCheck
{
    [MenuItem("PWManager/Top View/Check Impact VFX")]
    public static string Check()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run the arena or move preview first.");
        var arena = UnityEngine.Object.FindFirstObjectByType<TopViewArenaTest>();
        if (!arena || !arena.impactPrefab) throw new InvalidOperationException("Impact prefab is not assigned.");
        var preview = UnityEngine.Object.FindFirstObjectByType<MovePreviewScene>();
        bool enabled = preview && preview.enabled, paused = arena.paused;
        var original = arena.Engine; var wrestlers = arena.Wrestlers; float clock = arena.clock;
        if (preview) preview.enabled = false;
        arena.paused = true;
        int Count() => arena.GetComponentsInChildren<ParticleSystem>().Sum(p => p.particleCount);
        float Rate(MatchEngineEvent e) => arena.PlaybackRate(e);
        void Require(bool valid, string detail) { if (!valid) throw new InvalidOperationException(detail); }
        string Fingerprint() => string.Join(";", arena.GetComponentsInChildren<ParticleSystem>().Select(p =>
        {
            var particles = new ParticleSystem.Particle[p.particleCount]; p.GetParticles(particles);
            return string.Join(",", particles.Select(x => $"{x.position:R}/{x.remainingLifetime:R}/{x.rotation:R}/{x.startColor}"));
        }));
        try
        {
            int checkedMoves = 0;
            foreach (var move in MoveTable.Load().Where(MovePreviewScene.CanPreview))
            {
                var state = MovePreviewScene.BuildPreview(move); var e = state.Events.Single();
                arena.LoadMatch(state);
                arena.Seek(6 * .65f - .001f);
                Require(Count() == 0, "Premature impact: " + move.Id);
                float hitTime = 6 * .65f + .02f * Rate(e);
                arena.Seek(hitTime);
                Require((Count() > 0) == TopViewArenaTest.IsHit(e), "Wrong hit classification: " + move.Id);
                string fingerprint = Fingerprint();
                arena.Seek(0); arena.Seek(hitTime);
                Require(Fingerprint() == fingerprint, "Scrubbing changed particles: " + move.Id);
                arena.Seek(Mathf.Min(6, 6 * .65f + .31f * Rate(e)));
                Require(Count() == 0, "Impact did not clear: " + move.Id);
                arena.Seek(6);
                Require(Count() == 0, "Impact remained at the clip end: " + move.Id);
                checkedMoves++;
            }
            var strike = MovePreviewScene.BuildPreview(MoveTable.Load().First(m => m.Key == "superkick"));
            var hit = strike.Events.Single(); float previousScale = 0, previousAlpha = 0;
            foreach (float strength in new[] { 2f, 6f, 9f, 15f })
            {
                hit.Impact = strength; arena.LoadMatch(strike); arena.Seek(3.9f + .02f * Rate(hit));
                var effect = arena.GetComponentInChildren<ParticleSystem>();
                float scale = effect.transform.localScale.x, alpha = effect.main.startColor.color.a;
                Require(scale > previousScale && alpha > previousAlpha, "Strength did not increase size and opacity.");
                previousScale = scale; previousAlpha = alpha;
                var contact = effect.transform.position;
                arena.Seek(3.9f + .09f * Rate(hit));
                Require(Vector3.Distance(effect.transform.position, contact) < .001f, "Effect followed recoil.");
            }
            var storedMotion = hit.Motion; hit.Motion = null;
            arena.LoadMatch(strike); arena.Seek(3.9f + .02f * Rate(hit));
            Require(Count() > 0, "Legacy replay lost its hit effect.");
            hit.Motion = storedMotion;
            foreach (var result in new[] { MatchActionResult.Countered, MatchActionResult.ExecutionFailure, MatchActionResult.Blocked, MatchActionResult.Escaped })
            {
                hit.Result = result; arena.LoadMatch(strike); arena.Seek(3.9f + .02f * Rate(hit));
                Require(Count() == 0, "Effect on failed hit: " + result);
            }
            var hold = MovePreviewScene.BuildPreview(MoveTable.Load().First(m => m.Key == "crossface"));
            var submission = hold.Events.Single(); submission.Type = MatchActionType.Finisher;
            submission.TechniqueId = ""; submission.Impact = 15;
            arena.LoadMatch(hold); arena.Seek(3.9f + .02f * Rate(submission));
            Require(Count() == 0 && !TopViewArenaTest.IsHit(submission), "Loaded submission finisher became a strike.");
            Require(arena.GetComponentsInChildren<ParticleSystemRenderer>().All(r => r.sharedMaterial.shader.isSupported), "Unsupported particle shader.");
            Require(!arena.GetComponentsInChildren<LineRenderer>().Any(l => l.name.StartsWith("Impact ")), "Old line VFX still present.");
            return $"{checkedMoves} techniques: impact timing, hold exclusion, clearing and deterministic scrubbing passed; 4 strengths, recoil anchor and failed hits passed.";
        }
        finally
        {
            typeof(TopViewArenaTest).GetProperty("Wrestlers").SetValue(arena, wrestlers);
            arena.LoadMatch(original); arena.Seek(clock); arena.paused = paused;
            if (preview) preview.enabled = enabled;
        }
    }
}
