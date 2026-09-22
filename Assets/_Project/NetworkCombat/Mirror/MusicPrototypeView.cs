using System.Collections.Generic;
using MonsterSupergroup.Gameplay.Options;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    // Local rhythm presentation; shared effects never schedule another player's beat sounds.
    public sealed partial class MusicPrototypeView : MonoBehaviour
    {
        [SerializeField] private Material prototypeMaterial;
        [SerializeField, Range(-.2f, .2f)] private float inputCalibrationSeconds;
        private NetworkPlayerMusic skill;
        private GameObject root;
        private Material fallbackMaterial;
        private LineRenderer judgementRing, timingRing;
        private readonly List<AudioSource> scheduledSources = new List<AudioSource>();
        private readonly List<AudioClip> tones = new List<AudioClip>();
        private MusicParameters parameters;
        private sbyte[] judgements;
        private ulong castId;
        private double dspStart;
        private bool performing;
        private string feedback = "";
        private float feedbackUntil;
        private float presentationUntil;
        private float optionsVolume = 1;
        private GUIStyle statusStyle;

        public float InputCalibrationSeconds
        {
            get => inputCalibrationSeconds;
            set => inputCalibrationSeconds = Mathf.Clamp(value, -.2f, .2f);
        }
        public bool IsPresenting => performing;
        public int ScheduledSourceCount => scheduledSources.Count;
        public string PerformanceStatus
        {
            get
            {
                if (judgements == null) return "";
                int hits = 0, resolved = 0;
                foreach (var judgement in judgements)
                {
                    if (judgement != 0) resolved++;
                    if (judgement > 0) hits++;
                }
                if (!performing) return $"Music complete | Hits {hits}/{judgements.Length}";
                double elapsed = AudioSettings.dspTime - dspStart;
                if (elapsed < 0) return "Get ready | Space on the beat";
                double lead = parameters.CountInBeats * BeatSeconds;
                if (elapsed < lead) return "Count in: " + Mathf.CeilToInt((float)((lead - elapsed) / BeatSeconds)) + " | Space on the beat";
                return $"Music {resolved}/{judgements.Length} | Hits {hits} | Space on the beat";
            }
        }
        private double BeatSeconds => 60d / parameters.Bpm;

        private void Awake() => skill = GetComponent<NetworkPlayerMusic>();
        public void Begin(ulong id, double start, MusicParameters settings)
        {
            if (!isActiveAndEnabled || skill == null || !skill.isClient || !skill.isOwned) return;
            if (performing && castId == id) return;
            End();
            StopScheduledAudio();
            parameters = settings;
            castId = id;
            dspStart = start;
            judgements = new sbyte[settings.BeatCount];
            performing = true;
            feedback = "Get ready";
            feedbackUntil = Time.unscaledTime + .6f;
            EnsureVisuals();
            EnsureTones();
            // Every short tone has its own source so all beats are scheduled against the audio clock.
            int count = settings.CountInBeats + settings.BeatCount;
            while (scheduledSources.Count < count)
            {
                var source = root.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0;
                source.loop = false;
                scheduledSources.Add(source);
            }
            for (int i = 0; i < count; i++)
            {
                var source = scheduledSources[i];
                source.clip = i < settings.CountInBeats ? tones[0] : tones[1 + (i - settings.CountInBeats) % (tones.Count - 1)];
                source.volume = settings.FeedbackVolume * optionsVolume;
                double when = start + i * BeatSeconds;
                if (when >= AudioSettings.dspTime) source.PlayScheduled(when);
            }
            judgementRing.enabled = timingRing.enabled = true;
        }

        public void ShowJudgement(int index, bool hit)
        {
            if (!performing || judgements == null || index < 0 || index >= judgements.Length || judgements[index] != 0) return;
            judgements[index] = hit ? (sbyte)1 : (sbyte)-1;
            feedback = hit ? "HIT" : "MISS";
            feedbackUntil = Time.unscaledTime + .4f;
        }

        public void End()
        {
            if (performing) presentationUntil = Time.unscaledTime + .6f;
            performing = false;
            if (judgementRing != null) judgementRing.enabled = false;
            if (timingRing != null) timingRing.enabled = false;
        }

        public void Clear()
        {
            End();
            StopScheduledAudio();
            castId = 0;
            judgements = null;
            effects.Clear();
            scheduledSources.Clear();
            foreach (var tone in tones) if (tone != null) Destroy(tone);
            tones.Clear();
            if (root != null) Destroy(root);
            root = null;
            if (fallbackMaterial != null) Destroy(fallbackMaterial);
            fallbackMaterial = null;
            statusStyle = null;
        }

        private void StopScheduledAudio()
        {
            foreach (var source in scheduledSources) if (source != null) source.Stop();
        }

        private void ApplyOptionsVolume()
        {
            var options = GameOptionsService.Instance?.Current;
            optionsVolume = options == null ? 1 : options.MasterVolume * options.EffectsVolume;
            foreach (var source in scheduledSources)
                if (source != null) source.volume = parameters.FeedbackVolume * optionsVolume;
        }

        private void EnsureVisuals()
        {
            if (root != null) return;
            root = new GameObject("Music prototype visuals") { hideFlags = HideFlags.DontSave };
            if (prototypeMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader != null) fallbackMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            }
            judgementRing = MakeLine("Music judgement ring", .035f, true);
            timingRing = MakeLine("Music timing ring", .05f, true);
            judgementRing.enabled = timingRing.enabled = false;
        }

        private LineRenderer MakeLine(string label, float width, bool loop)
        {
            var visual = new GameObject(label);
            visual.transform.SetParent(root.transform, false);
            var line = visual.AddComponent<LineRenderer>();
            line.sharedMaterial = prototypeMaterial != null ? prototypeMaterial : fallbackMaterial;
            line.useWorldSpace = true;
            line.widthMultiplier = width;
            line.loop = loop;
            line.sortingLayerID = SortingLayer.NameToID("Foreground");
            line.sortingOrder = 32000;
            return line;
        }

        private void EnsureTones()
        {
            if (tones.Count != 0) return;
            tones.Add(Tone("Music count in", 1100, .04f));
            tones.Add(Tone("Music C", 523.25f, .10f));
            tones.Add(Tone("Music E", 659.25f, .10f));
            tones.Add(Tone("Music G", 783.99f, .10f));
            tones.Add(Tone("Music E reprise", 659.25f, .10f));
        }

        private static AudioClip Tone(string label, float frequency, float duration)
        {
            const int sampleRate = 44100;
            var samples = new float[Mathf.RoundToInt(sampleRate * duration)];
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)sampleRate;
                float envelope = Mathf.Min(1, time / .003f) * Mathf.Pow(1 - i / (float)samples.Length, 2);
                samples[i] = Mathf.Sin(time * frequency * Mathf.PI * 2) * envelope * .45f;
            }
            var clip = AudioClip.Create(label, samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void LateUpdate()
        {
            UpdateEffects();
            if (!performing || skill == null || !skill.isOwned) return;
            double elapsed = AudioSettings.dspTime - dspStart;
            double first = parameters.CountInBeats * BeatSeconds;
            double beat = (elapsed - first) / BeatSeconds;
            float phase = Mathf.Repeat((float)beat, 1);
            float radius = Mathf.Lerp(1.7f, .85f, phase);
            Color color = Color.cyan;
            if (Time.unscaledTime < feedbackUntil) color = feedback == "MISS" ? new Color(1, .35f, .4f) : new Color(.3f, 1, .7f);
            SetCircle(judgementRing, transform.position, .85f, new Color(.55f, .85f, 1, .8f));
            SetCircle(timingRing, transform.position, radius, color);
        }

        private static void SetCircle(LineRenderer line, Vector3 center, float radius, Color color)
        {
            const int points = 48;
            line.positionCount = points;
            line.startColor = line.endColor = color;
            for (int i = 0; i < points; i++)
            {
                float angle = i * Mathf.PI * 2 / points;
                line.SetPosition(i, center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius);
            }
        }

        private void OnGUI()
        {
            if ((!performing && Time.unscaledTime >= presentationUntil) || skill == null || !skill.isOwned || Camera.main == null || judgements == null) return;
            Vector3 screen = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 1.8f);
            if (screen.z < 0) return;
            if (statusStyle == null) statusStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14, fontStyle = FontStyle.Bold };
            var rect = new Rect(Mathf.Clamp(screen.x - 150, 0, Mathf.Max(0, Screen.width - 300)), Mathf.Clamp(Screen.height - screen.y - 60, 0, Mathf.Max(0, Screen.height - 82)), 300, 82);
            GUI.Box(rect, GUIContent.none);
            GUI.Label(new Rect(rect.x, rect.y + 2, rect.width, 24), PerformanceStatus, statusStyle);
            float totalWidth = judgements.Length * 18;
            Color previous = GUI.color;
            for (int i = 0; i < judgements.Length; i++)
            {
                GUI.color = judgements[i] > 0 ? new Color(.3f, 1, .65f) : judgements[i] < 0 ? new Color(1, .35f, .4f) : new Color(.6f, .65f, .75f);
                GUI.Label(new Rect(rect.center.x - totalWidth / 2 + i * 18, rect.y + 28, 18, 20), judgements[i] > 0 ? "+" : judgements[i] < 0 ? "x" : "o", statusStyle);
            }
            GUI.color = previous;
            if (Time.unscaledTime < feedbackUntil) GUI.Label(new Rect(rect.x, rect.y + 54, rect.width, 22), feedback, statusStyle);
        }

        private void OnEnable()
        {
            GameOptionsService.Changed += ApplyOptionsVolume;
            ApplyOptionsVolume();
        }
        private void OnDisable()
        {
            GameOptionsService.Changed -= ApplyOptionsVolume;
            Clear();
        }
        private void OnDestroy() => Clear();
    }
}
