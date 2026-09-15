using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;
using UnityEngine;
using UnityEngine.Profiling;

namespace MonsterSupergroup.NetworkCombat
{
    // Opt-in rendered art audit only. Never drives enemies, damage, or animation.
    [DefaultExecutionOrder(31000)] // Observe the frame after the palette swapper's LateUpdate.
    public sealed class LimboArtObservation : MonoBehaviour
    {
        private StreamWriter log;
        private double next;
        private readonly HashSet<int> collapsedPanels = new HashSet<int>();
        private readonly HashSet<string> captures = new HashSet<string>();
        private readonly HashSet<string> deathFrames = new HashSet<string>();
        private void Start()
        {
            Directory.CreateDirectory(LimboReferenceLaunch.OutputDirectory);
            log = new StreamWriter(Path.Combine(LimboReferenceLaunch.OutputDirectory, "art-observation.jsonl")) { AutoFlush = true };
        }
        private void LateUpdate()
        {
            ObserveDeathFrames();
            if (Time.realtimeSinceStartupAsDouble < next) return;
            next = Time.realtimeSinceStartupAsDouble + 1;
            if ((LimboReferenceLaunch.Profile == "imp-v0" || LimboReferenceLaunch.Profile == "imp-v1") && NetworkCombatWorld.Instance != null)
            {
                var progress=NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>().Snapshot;
                foreach(int at in new[]{10,20,30,40,65,75,88})
                    if(progress.Elapsed>=at && captures.Add(progress.RunId+"/"+at))
                        ScreenCapture.CaptureScreenshot(Path.Combine(LimboReferenceLaunch.OutputDirectory,$"art-imp-{progress.RunId}-{at}.png"));
            }
            foreach (var panel in FindObjectsByType<NetworkEnemyDebugPanel>(FindObjectsSortMode.None))
                if (collapsedPanels.Add(panel.GetInstanceID())) panel.SetExpanded(false);
            foreach (var panel in FindObjectsByType<NetworkPlayerDebugPanel>(FindObjectsSortMode.None))
                if (collapsedPanels.Add(panel.GetInstanceID())) panel.SetExpanded(false);
            foreach (var identity in NetworkClient.spawned.Values)
            {
                if (identity == null || !identity.TryGetComponent<NetworkEnemySimulationAgent>(out var agent) || !agent.Birth.Enabled) continue;
                var e = identity.GetComponent<EnemyController>(); var renderer = e.spriteRenderer;
                if (renderer == null || renderer.sprite == null) continue;
                var m = renderer.sharedMaterial; var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                var state = e.enemyAnimator.animancer.Layers[0].CurrentState;
                log?.WriteLine(JsonUtility.ToJson(new BodyRow { kind = "body", real = Time.realtimeSinceStartupAsDouble,
                    combat = EnemySimulationClock.CombatNow, id = agent.netId, epoch = agent.Assignment.Epoch,
                    identity = agent.Birth.SourceEnemy, variant = agent.Birth.Variant, sprite = renderer.sprite.name,
                    texture = renderer.sprite.texture.name, format = renderer.sprite.texture.graphicsFormat.ToString(),
                    lut = e.enemyAnimator.PaletteSwapper?.ColorLut?.name, material = m?.name, shader = m?.shader.name,
                    keywords = m?.shaderKeywords, color = renderer.color, materialColor = m != null && m.HasProperty("_Color") ? m.GetColor("_Color") : Color.clear,
                    blockColor = block.GetColor("_Color"), hitColor = block.GetColor("_HitEffectColor"), hitBlend = block.GetFloat("_HitEffectBlend"),
                    clip = state?.Clip?.name, clipTime = state?.Time ?? 0, bounds = renderer.bounds, position = e.transform.position,
                    rotation=e.transform.eulerAngles.z, packed=renderer.sprite.packed }));
            }
            var particles = FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);
            log?.WriteLine(JsonUtility.ToJson(new FrameRow { kind="frame", real=Time.realtimeSinceStartupAsDouble,
                frameMs=Time.unscaledDeltaTime*1000, allocated=Profiler.GetTotalAllocatedMemoryLong(), reserved=Profiler.GetTotalReservedMemoryLong(),
                particleSystems=particles.Length, particles=particles.Sum(p=>p.particleCount), sprites=FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None).Length }));
        }
        private void ObserveDeathFrames()
        {
            foreach (var identity in NetworkClient.spawned.Values)
            {
                if (identity == null || !identity.TryGetComponent<NetworkEnemySimulationAgent>(out var agent) || !agent.Birth.Enabled) continue;
                var e=identity.GetComponent<EnemyController>();
                if (e.IsAlive || e.enemyAnimator?.animancer == null) continue;
                var state=e.enemyAnimator.animancer.Layers[0].CurrentState;
                if (state?.Clip == null || state.Clip != e.enemyAnimator.GetDeadClipTransition(e.FacingDirection.x,e.FacingDirection.y).Clip) continue;
                int band=e.DeathPresentationComplete?3:Mathf.Min(2,(int)(state.NormalizedTime*3));
                if (!deathFrames.Add(agent.netId+"/"+band)) continue;
                var shadow=e.enemyAnimator.transform.Find("ShadowCircle")?.GetComponent<SpriteRenderer>();
                int areas=e.GetComponentsInChildren<AstralShift.HellMaiden.Interactions.PlayerDamageInteraction>(true)
                    .Count(d=>d.isActiveAndEnabled && d.TryGetComponent<Collider2D>(out var c) && c.enabled);
                log?.WriteLine(JsonUtility.ToJson(new DeathRow{kind="death",real=Time.realtimeSinceStartupAsDouble,combat=EnemySimulationClock.CombatNow,
                    id=agent.netId,identity=agent.Birth.SourceEnemy,variant=agent.Birth.Variant,appearance=e.name,clip=state.Clip.name,
                    clipTime=state.Time,normalized=state.NormalizedTime,alive=agent.IsCanonicalAlive,complete=e.DeathPresentationComplete,
                    enabledDamageAreas=areas,shadowAlpha=shadow!=null?shadow.color.a:-1}));
                string shot=$"death-{e.name}-{agent.Birth.Variant}-{band}";
                if(captures.Add(shot))ScreenCapture.CaptureScreenshot(Path.Combine(LimboReferenceLaunch.OutputDirectory,shot+".png"));
            }
        }
        private void OnDestroy() { log?.Dispose(); log = null; }
        [Serializable] private class BodyRow
        {
            public string kind, identity, sprite, texture, format, lut, material, shader, clip;
            public string[] keywords;
            public uint id, epoch; public int variant; public double real, combat, clipTime;
            public Color color, materialColor, blockColor, hitColor; public float hitBlend;
            public Bounds bounds; public Vector3 position;
            public float rotation; public bool packed;
        }
        [Serializable] private class FrameRow
        { public string kind; public double real; public float frameMs; public long allocated, reserved; public int particleSystems, particles, sprites; }
        [Serializable] private class DeathRow
        { public string kind,identity,appearance,clip;public uint id;public int variant,enabledDamageAreas;public double real,combat,clipTime,normalized;public bool alive,complete;public float shadowAlpha; }
    }
}
