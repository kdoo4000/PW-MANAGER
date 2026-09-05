using System;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Domain.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public sealed class HighlightTestBootstrap : MonoBehaviour
    {
        public static readonly float[] CueTimes = { 0, 3, 5, 7, 8.1f, 9.5f, 12.5f };
        public const float Duration = 16;
        private GameSave previousSave, testSave;
        private bool previousTransient, initialized, paused, muted, reducedMotion;
        private float clock;
        private int cue = -1;
        private UIDocument document;
        private VisualElement stage, left, right, progress, profile;
        private Label headline, narration, count, position;
        private Button pauseButton;
        private AudioSource audioSource;
        private AudioClip impact, crowd;
        private WrestlerState attacker, defender;
        private NarrativeLineState[] lines;

        public static int CueAt(float seconds)
        {
            for (var i = CueTimes.Length - 1; i >= 0; i--)
                if (seconds >= CueTimes[i]) return i;
            return 0;
        }

        private void Start()
        {
            previousSave = DashboardSession.ActiveSave;
            previousTransient = DashboardSession.IsTransient;
            initialized = true;
            testSave = ShowTestSaveFactory.Create(Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog"), 20260903);
            DashboardSession.SetActiveSave(testSave, transient: true);
            attacker = testSave.Wrestlers[0];
            defender = testSave.Wrestlers[1];
            var a = WrestlerNameText.DisplayName(attacker);
            var d = WrestlerNameText.DisplayName(defender);
            // A fixed near-fall vignette previews timing without changing a booked match's outcome.
            lines = new[]
            {
                Line($"{d}의 공격! {a}, 몸을 틀어 피합니다."),
                Line($"{a}, 빈틈을 놓치지 않고 피니셔를 꽂습니다!"),
                Line($"{a}, 곧바로 커버! 두 어깨가 매트에 닿았습니다."),
                Line("하나!"), Line("둘!"),
                Line($"{d}, 어깨를 들어 올립니다! 카운트는 2!"),
                Line($"{a}, 믿기지 않는다는 표정입니다. 경기는 계속됩니다.")
            };
            document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = Resources.Load<PanelSettings>("PWManagerRuntime/PWManagerPanelSettings");
            BuildUI();
            gameObject.AddComponent<WrestlerProfileController>();
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0;
            impact = MakeSound(false);
            crowd = MakeSound(true);
            Restart();
        }

        private static NarrativeLineState Line(string text) => new() { Type = NarrativeLineType.Commentary, Text = text };

        private void BuildUI()
        {
            var root = document.rootVisualElement;
            root.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/HighlightTest"));
            var screen = Box(root, "highlight-screen");
            var header = Box(screen, "highlight-header");
            Text(header, "SHOW HIGHLIGHTS", "highlight-brand");
            Text(header, "SINGLES MATCH", "highlight-meta");
            position = Text(header, "01 / 07", "highlight-meta");
            stage = Box(screen, "highlight-stage");
            var top = Box(stage, "highlight-stage-top");
            Text(top, "하이라이트", "highlight-kicker");
            Text(top, "결정적 순간", "highlight-meta");
            var cast = Box(stage, "highlight-cast");
            left = WrestlerCard(cast, attacker, "공격", "attacker");
            var center = Box(cast, "highlight-center");
            count = Text(center, "VS", "highlight-count");
            Text(center, "PIN FALL", "highlight-meta");
            right = WrestlerCard(cast, defender, "수비", "defender");
            var caption = Box(stage, "highlight-caption");
            headline = Text(caption, "", "highlight-headline");
            narration = Text(caption, "", "highlight-narration");
            var track = Box(screen, "highlight-track");
            progress = Box(track, "highlight-progress");
            var footer = Box(screen, "highlight-footer");
            var controls = Box(footer, "highlight-controls");
            pauseButton = Control(controls, "일시정지", () => { paused = !paused; UpdatePause(); });
            Control(controls, "다시 재생", Restart);
            Control(controls, "다음 장면", NextCue);
            Control(controls, "끝으로", () => Seek(Duration));
            var options = Box(footer, "highlight-controls");
            var sound = new Toggle("음소거");
            sound.RegisterValueChangedCallback(e => { muted = e.newValue; audioSource.mute = muted; });
            options.Add(sound);
            var motion = new Toggle("움직임 줄이기");
            motion.RegisterValueChangedCallback(e => reducedMotion = e.newValue);
            options.Add(motion);
            var profileTree = Resources.Load<VisualTreeAsset>("PWManagerUI/WrestlerProfile");
            profileTree.CloneTree(root);
            profile = root.Q("wrestler-profile");
            // Keep the existing profile and its ID-based links above the isolated player.
            profile.style.position = Position.Absolute;
            profile.style.left = profile.style.right = profile.style.top = profile.style.bottom = 0;
        }

        private VisualElement WrestlerCard(VisualElement parent, WrestlerState wrestler, string role, string className)
        {
            var card = Box(parent, "highlight-wrestler");
            card.AddToClassList(className);
            Text(card, role, "highlight-role");
            var portrait = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            var path = wrestler.Presentation?.PortraitResourcePath;
            if (!string.IsNullOrEmpty(path)) portrait.image = Resources.Load<Texture2D>(path);
            if (portrait.image == null)
            {
                portrait.image = Resources.Load<Texture2D>("PWManagerUI/Wrestlers/Dummy/processed/single-1");
                portrait.tintColor = className == "attacker" ? new Color(.65f, .8f, 1) : new Color(1, .7f, .7f);
                if (portrait.image != null) portrait.image.filterMode = FilterMode.Point;
            }
            portrait.AddToClassList("highlight-portrait");
            card.Add(portrait);
            if (portrait.image == null) Text(card, "PW", "highlight-fallback");
            var link = WrestlerProfileController.CreateLink(wrestler);
            link.AddToClassList("highlight-name");
            card.Add(link);
            return card;
        }

        private static VisualElement Box(VisualElement parent, string className)
        {
            var item = new VisualElement(); item.AddToClassList(className); parent.Add(item); return item;
        }
        private static Label Text(VisualElement parent, string text, string className)
        {
            var item = new Label(text); item.AddToClassList(className); parent.Add(item); return item;
        }
        private static Button Control(VisualElement parent, string text, Action action)
        {
            var item = new Button(action) { text = text }; parent.Add(item); return item;
        }

        public void Restart() { paused = false; Seek(0); UpdatePause(); }
        public void NextCue() => Seek(cue + 1 < CueTimes.Length ? CueTimes[cue + 1] : Duration);
        public void Seek(float seconds)
        {
            clock = Mathf.Clamp(seconds, 0, Duration);
            audioSource.Stop();
            cue = -1;
            Render();
        }
        private void UpdatePause() => pauseButton.text = clock >= Duration ? "재생 완료" : paused ? "계속 재생" : "일시정지";

        private void Update()
        {
            if (stage == null) return;
            var profileOpen = profile.resolvedStyle.display != DisplayStyle.None;
            if (!paused && !profileOpen && clock < Duration) clock = Mathf.Min(Duration, clock + Time.unscaledDeltaTime);
            audioSource.mute = muted || paused || profileOpen;
            Render();
        }

        private void Render()
        {
            var next = CueAt(clock);
            if (cue != next)
            {
                cue = next;
                var titles = new[] { "한순간의 빈틈", "피니셔 작렬", "끝낼 수 있을까", "하나", "둘", "아직 끝나지 않았다", "경기는 계속된다" };
                headline.text = titles[cue];
                WrestlerNameText.Set(narration, lines[cue].Text, new[] { attacker, defender });
                count.text = cue == 3 ? "1" : cue == 4 ? "2" : cue == 5 ? "2.9" : cue == 6 ? "계속" : cue == 2 ? "PIN" : "VS";
                position.text = $"{cue + 1:00} / 07";
                stage.EnableInClassList("near-fall", cue >= 2 && cue <= 4);
                stage.EnableInClassList("kickout", cue == 5);
                left.EnableInClassList("focused", cue < 5);
                right.EnableInClassList("focused", cue >= 5);
                if (!muted && clock < Duration && (cue == 1 || cue == 3 || cue == 4 || cue == 5))
                    audioSource.PlayOneShot(cue == 5 ? crowd : impact, cue == 1 ? .65f : .4f);
            }
            var elapsed = clock - CueTimes[cue];
            var pulse = reducedMotion ? 0 : Mathf.Max(0, 1 - elapsed * 2);
            var zoom = 1 + pulse * .025f;
            (cue < 5 ? left : right).style.scale = new Scale(new Vector3(zoom, zoom, 1));
            (cue < 5 ? right : left).style.scale = new Scale(Vector3.one);
            var shake = !reducedMotion && (cue == 1 || cue == 5) ? Mathf.Sin(elapsed * 65) * pulse * 5 : 0;
            stage.style.translate = new Translate(shake, 0);
            progress.style.width = Length.Percent(clock / Duration * 100);
            pauseButton.SetEnabled(clock < Duration);
            UpdatePause();
        }

        private static AudioClip MakeSound(bool swell)
        {
            // ponytail: synthesized preview sounds; replace with recorded crowd and ring audio for production.
            const int rate = 22050;
            var samples = new float[swell ? rate * 2 : rate / 4];
            var random = new System.Random(43);
            float filtered = 0;
            for (var i = 0; i < samples.Length; i++)
            {
                var t = (float)i / rate;
                filtered = .88f * filtered + .12f * ((float)random.NextDouble() * 2 - 1);
                samples[i] = swell ? filtered * Mathf.Sin(Mathf.PI * i / samples.Length) * .8f
                    : (Mathf.Sin(t * 2 * Mathf.PI * 85) * .65f + filtered * .35f) * Mathf.Exp(-t * 23) * Mathf.Min(t * 200, 1);
            }
            var clip = AudioClip.Create(swell ? "Highlight crowd swell" : "Highlight ring impact", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void OnDestroy()
        {
            if (impact != null) Destroy(impact);
            if (crowd != null) Destroy(crowd);
            if (initialized && DashboardSession.ActiveSave == testSave) DashboardSession.SetActiveSave(previousSave, previousTransient);
        }
    }
}
