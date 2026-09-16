#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Traps;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Rendering.Universal;
using UnityEditor;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class PlanarBarrierRenderTests
    {
        [UnityTest]
        public IEnumerator RealFireRingRemainsVisibleAtWorldOriginAndAwayFromIt()
        {
            var pool = new GameObject("planar fire pool"); pool.AddComponent<PoolManager>().Init();
            var cameraRoot = new GameObject("production perspective"); var camera = cameraRoot.AddComponent<Camera>();
            camera.orthographic = false; camera.fieldOfView = 80; camera.cullingMask = 1 << 30;
            camera.backgroundColor = Color.black; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.GetUniversalAdditionalCameraData().SetRenderer(1); // Gameplay.unity's NordicRenderer2D.
            var target = new RenderTexture(1280, 720, 24); camera.targetTexture = target;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var barrier = Object.Instantiate(Resources.Load<GameplayWaveRules>("LimboReference/SpatialBarrier").ReferenceBarrier);
            var mapParent = new GameObject("inactive map fixture"); mapParent.SetActive(false);
            var map = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Content/Nordic/NordicStaticMap.prefab"), mapParent.transform);
            foreach (var script in map.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(script);
            foreach (var t in map.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 30;
            mapParent.SetActive(true);
            var light = new GameObject("global map light").AddComponent<Light2D>(); light.lightType = Light2D.LightType.Global;
            var lightData = new SerializedObject(light);
            var layers = lightData.FindProperty("m_ApplyToSortingLayers"); layers.arraySize = SortingLayer.layers.Length;
            for (int i = 0; i < layers.arraySize; i++) layers.GetArrayElementAtIndex(i).intValue = SortingLayer.layers[i].id;
            lightData.ApplyModifiedPropertiesWithoutUndo();
            var report = new System.Collections.Generic.List<string>();
            Directory.CreateDirectory("Logs/ManualPlayFix/fire-render");
            try
            {
                foreach (float centerY in new[] { 0f, -12f })
                foreach (float radius in new[] { 10f, 20f })
                {
                    camera.transform.position = new Vector3(0, centerY, radius == 20 ? -20 : -10);
                    barrier.ApplyNetworkPresentation(new Vector3(0, centerY), BarrierPhase.Shrinking, radius, 90, 90, .25f, true);
                    foreach (var t in barrier.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 30;
                    yield return new WaitForSeconds(.5f);
                    var renderers = barrier.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
                    foreach (var r in renderers) r.enabled = false;
                    camera.Render(); RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                    var background = image.GetPixels32();
                    foreach (var r in renderers) r.enabled = true;
                    camera.Render(); RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                    var fire = image.GetPixels32(); int visible = 0;
                    for (int i = 0; i < fire.Length; i++)
                        if (Mathf.Abs(fire[i].r - background[i].r) + Mathf.Abs(fire[i].g - background[i].g) + Mathf.Abs(fire[i].b - background[i].b) > 60) visible++;
                    File.WriteAllBytes($"Logs/ManualPlayFix/fire-render/center{centerY}-radius{radius}.png", image.EncodeToPNG());
                    report.Add($"center={centerY} radius={radius} pixels={visible}");
                    foreach (var ps in barrier.GetComponentsInChildren<ParticleSystem>())
                    {
                        if (ps.particleCount == 0) continue;
                        var r = ps.GetComponent<ParticleSystemRenderer>();
                        report.Add($"{ps.name} particles={ps.particleCount} position={ps.transform.position} bounds={r.bounds} shader={r.sharedMaterial?.shader.name}");
                        // The single stationary glow reproduced the Host bug: its renderer
                        // stayed at an old ring position after the actual particle moved.
                        if (ps.name == "Glow" && ps.particleCount == 1)
                        {
                            var particle = new ParticleSystem.Particle[1]; ps.GetParticles(particle);
                            var position = ps.main.simulationSpace == ParticleSystemSimulationSpace.Local
                                ? ps.transform.TransformPoint(particle[0].position) : particle[0].position;
                            Assert.That(Vector2.Distance(r.bounds.center, position), Is.LessThan(.1f),
                                "Automatic particle bounds must keep updating between camera renders.");
                        }
                    }
                    Assert.That(visible, Is.GreaterThan(100), "The real fire ring must be visible, not just its collider.");
                }
            }
            finally
            {
                File.WriteAllLines("Logs/ManualPlayFix/fire-render/diagnostics.txt", report);
                barrier.CancelNetworkLifecycle(); Object.DestroyImmediate(barrier.gameObject);
                RenderTexture.active = null; camera.targetTexture = null;
                Object.DestroyImmediate(target); Object.DestroyImmediate(image); Object.DestroyImmediate(cameraRoot); Object.DestroyImmediate(pool);
                Object.DestroyImmediate(mapParent); Object.DestroyImmediate(light.gameObject);
            }
        }
    }
}
#endif
