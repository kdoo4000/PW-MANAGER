using System;
using System.IO;
using System.Linq;
using PWManager.Data.Loading;
using UnityEngine;

public static class MatchImpactVisibilityCheck
{
    [Serializable] private sealed class Sample
    { public float strength, age; public int visiblePixels; public float maxContrast; }
    [Serializable] private sealed class Report { public Sample[] samples; }

    public static string Check(string label, bool requireVisible = true)
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run the arena or preview first.");
        var arena = UnityEngine.Object.FindAnyObjectByType<TopViewArenaTest>();
        var original = arena.Engine; var wrestlers = arena.Wrestlers;
        var preview = UnityEngine.Object.FindAnyObjectByType<MovePreviewScene>();
        bool enabled = preview && preview.enabled, paused = arena.paused;
        float clock = arena.clock;
        var camera = arena.viewCamera;
        var position = camera.transform.position; var rect = camera.rect; var size = camera.orthographicSize;
        var target = camera.targetTexture; var active = RenderTexture.active;
        var texture = RenderTexture.GetTemporary(960, 540, 24);
        var image = new Texture2D(960, 540, TextureFormat.RGB24, false);
        var samples = new System.Collections.Generic.List<Sample>();
        string folder = "artifacts/vfx-visibility-20261009"; Directory.CreateDirectory(folder);
        Color32[] Capture(string file)
        {
            camera.Render(); RenderTexture.active = texture;
            image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); image.Apply();
            if (file != null) File.WriteAllBytes(file, image.EncodeToPNG());
            return image.GetPixels32();
        }
        try
        {
            if (preview) preview.enabled = false;
            arena.paused = true; camera.targetTexture = texture; camera.rect = new Rect(0, 0, 1, 1);
            camera.orthographicSize = 5; camera.transform.position = new Vector3(arena.ringCenter.x, 45, arena.ringCenter.z);
            var state = MovePreviewScene.BuildPreview(MoveTable.Load().Single(m => m.Key == "superkick"));
            var hit = state.Events.Single();
            foreach (float strength in new[] { 2f, 15f })
            foreach (float age in new[] { .02f, .06f, .12f })
            {
                hit.Impact = strength; arena.LoadMatch(state);
                arena.Seek(3.9f + age * arena.PlaybackRate(hit));
                var renderers = arena.GetComponentsInChildren<ParticleSystemRenderer>();
                var withEffect = Capture($"{folder}/{label}-{strength:00}-{age:F2}.png");
                foreach (var renderer in renderers) renderer.enabled = false;
                var withoutEffect = Capture(null);
                foreach (var renderer in renderers) renderer.enabled = true;
                var sample = new Sample { strength = strength, age = age };
                for (int i = 0; i < withEffect.Length; i++)
                {
                    int contrast = Math.Max(Math.Abs(withEffect[i].r - withoutEffect[i].r),
                        Math.Max(Math.Abs(withEffect[i].g - withoutEffect[i].g), Math.Abs(withEffect[i].b - withoutEffect[i].b)));
                    if (contrast >= 32) sample.visiblePixels++;
                    sample.maxContrast = Math.Max(sample.maxContrast, contrast / 255f);
                }
                samples.Add(sample);
            }
            File.WriteAllText($"{folder}/{label}.json", JsonUtility.ToJson(new Report { samples = samples.ToArray() }, true));
            if (requireVisible && (samples.Single(s => s.strength == 2 && s.age == .06f).visiblePixels < 30 ||
                samples.Single(s => s.strength == 15 && s.age == .06f).visiblePixels < 120 ||
                samples.Single(s => s.strength == 15 && s.age == .12f).visiblePixels < 60))
                throw new InvalidOperationException("Impact is not visible at the normal arena camera distance.");
            return string.Join("; ", samples.Select(s => $"{s.strength}/{s.age:F2}s: {s.visiblePixels}px contrast>=32"));
        }
        finally
        {
            typeof(TopViewArenaTest).GetProperty("Wrestlers").SetValue(arena, wrestlers);
            arena.LoadMatch(original); arena.Seek(clock); arena.paused = paused;
            if (preview) preview.enabled = enabled;
            camera.targetTexture = target; camera.rect = rect; camera.orthographicSize = size; camera.transform.position = position;
            RenderTexture.active = active; RenderTexture.ReleaseTemporary(texture);
            UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
