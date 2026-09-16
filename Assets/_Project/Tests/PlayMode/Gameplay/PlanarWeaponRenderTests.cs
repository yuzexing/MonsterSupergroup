#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Data.Cards;
using MonsterSupergroup.Gameplay.Combat;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    // Isolated visual sampling: slash animation sampling and the existing Wisp
    // presentation replica. This does not replace network/damage acceptance.
    public sealed class PlanarWeaponRenderTests
    {
        [UnityTest]
        public IEnumerator MigratedWeaponsRenderInEightDirectionsThroughTheNordicPerspective()
        {
            var holder = new GameObject("inactive visual fixture"); holder.SetActive(false);
            var cameraRoot = new GameObject("Nordic perspective visual fixture");
            var camera = cameraRoot.AddComponent<Camera>();
            camera.orthographic = false; camera.fieldOfView = 80; camera.transform.position = new Vector3(0, 0, -10);
            camera.GetUniversalAdditionalCameraData().SetRenderer(1); // Gameplay.unity's NordicRenderer2D.
            camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            var target = new RenderTexture(1280, 720, 24); camera.targetTexture = target;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            GameObject ownedPool = null;
            if (PoolManager.Instance == null) { ownedPool = new GameObject("visual test pool"); ownedPool.AddComponent<PoolManager>().Init(); }
            var ownerRoot = new GameObject("inactive presentation owner"); ownerRoot.SetActive(false);
            var owner = ownerRoot.AddComponent<PlayerMovement>();
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/MonoBehaviour/WeaponData_Dante_SlowProjectile.asset");
            Assert.That(weapon, Is.Not.Null);
            Assert.That(weapon.ID, Is.EqualTo(2));
            var emitter = Object.Instantiate(weapon.WeaponPrefab.gameObject).GetComponent<ProjectileAttackBehaviour>();
            emitter.InitializePresentationReplica(2, owner);
            ulong action = 1000;
            var rows = new List<string> { "asset,visualScale,directionDegrees,projection,pixels,minX,minY,maxX,maxY" };
            string output = "Logs/ManualPlayFix/weapon-directions"; Directory.CreateDirectory(output);
            var paths = new[] {
                "Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Melee/GameObject/PlayerAttack_Dante_Slash.prefab",
                "Assets/_Project/GameObject/PlayerAttack_Dante_Projectile.prefab",
                "Assets/_Project/GameObject/PlayerAttack_Dante_Projectile_Fire Variant.prefab",
                "Assets/_Project/GameObject/PlayerAttack_Dante_Projectile_Poison Variant.prefab" };
            int Capture(string name, float size, int angle, string projection)
            {
                camera.Render();
                RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                var pixels = image.GetPixels32(); int count = 0, minX = 1280, minY = 720, maxX = -1, maxY = -1;
                for (int y = 0; y < 720; y++) for (int x = 0; x < 1280; x++)
                {
                    var color = pixels[y * 1280 + x]; if (Mathf.Max(color.r, Mathf.Max(color.g, color.b)) < 20) continue;
                    count++; minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }
                File.WriteAllBytes(Path.Combine(output, $"{name}-size{size}-angle{angle}-{projection}.png"), image.EncodeToPNG());
                rows.Add($"{name},{size},{angle},{projection},{count},{minX},{minY},{maxX},{maxY}");
                return count;
            }
            try
            {
                foreach (string path in paths)
                foreach (float size in new[] { 1f, 2f })
                for (int angle = 0; angle < 360; angle += 45)
                {
                    holder.SetActive(false);
                    bool wisp = path.Contains("Projectile");
                    ProjectileAttack projectile = null;
                    GameObject root;
                    if (wisp)
                    {
                        var element = path.Contains("Poison") ? AttackElement.Poison : path.Contains("Fire") ? AttackElement.Fire : AttackElement.Default;
                        projectile = emitter.PlayPresentation(new ProjectilePresentationSpawn(2, new ProjectilePresentationKey(action++, 0),
                            Vector3.zero, new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)), element, true,
                            new ProjectilePresentationStats { SizeMultiplierSum = size - 1, EffectiveSpeed = .4f, Duration = 10, ProjectileCount = 1, BaseProjectileCount = 1 }), 0, playLaunchSound: false);
                        root = projectile.gameObject;
                    }
                    else root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), holder.transform);
                    try
                    {
                        if (!wisp)
                        {
                        root.transform.localPosition = Vector3.zero;
                        var attack = root.GetComponent<AnimatedAttack>();
                        attack.UpdateRotation(new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)));
                        var clip = attack.attackAnim?.Clip ?? attack.attackStartAnim?.Clip;
                        var animationRoot = attack.animancer.Animator.gameObject;
                        foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(behaviour);
                        foreach (var animator in root.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);
                        foreach (var collider in root.GetComponentsInChildren<Collider2D>(true)) Object.DestroyImmediate(collider);
                        foreach (var body in root.GetComponentsInChildren<Rigidbody2D>(true)) Object.DestroyImmediate(body);
                        foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 30;
                        if (clip != null) clip.SampleAnimation(animationRoot, .15f);
                        root.transform.localScale *= size;
                        holder.SetActive(true);
                        uint seed = 14301;
                        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                        {
                            ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                            ps.useAutoRandomSeed = false; ps.randomSeed = seed++;
                            ps.Simulate(.15f, false, true, true); ps.Pause(false);
                        }
                        GameplayPlanarEffect.Attach(root);
                        }
                        else
                        {
                            foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 30;
                            yield return new WaitForSeconds(.5f);
                        }
                        yield return null;
                        // Visual fixture only; presentation collision filtering is tested elsewhere.
                        foreach (var collider in root.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
                        root.GetComponent<GameplayPlanarEffect>().RefreshBounds();
                        string name = Path.GetFileNameWithoutExtension(path);
                        int count = Capture(name, size, angle, "planar");
                        Assert.That(count, Is.GreaterThan(30), $"Invisible effect: {name} size={size} angle={angle}");
                        foreach (var collider in root.GetComponentsInChildren<Collider2D>(true)) Assert.That(collider.enabled, Is.False);
                        var saved = new Dictionary<Renderer, Material[]>();
                        var temporary = new List<Material>();
                        var projection = root.GetComponent<GameplayPlanarEffect>();
                        try
                        {
                            projection.enabled = false;
                            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                            {
                                var materials = renderer.sharedMaterials; saved.Add(renderer, (Material[])materials.Clone());
                                for (int i = 0; i < materials.Length; i++)
                                {
                                    if (materials[i] == null || !materials[i].HasProperty("_GameplayPlanar")) continue;
                                    string original = materials[i].shader.name == "MonsterSupergroup/PlanarSprite" ? "AllIn1SpriteShader/AllIn1SpriteShader" : "AllIn1Vfx/AllIn1VfxURPCompat";
                                    var copy = new Material(materials[i]) { shader = Shader.Find(original) }; temporary.Add(copy); materials[i] = copy;
                                }
                                renderer.sharedMaterials = materials;
                            }
                            Capture(name, size, angle, "legacy-depth");
                        }
                        finally
                        {
                            foreach (var pair in saved) pair.Key.sharedMaterials = pair.Value;
                            foreach (var material in temporary) Object.DestroyImmediate(material);
                            projection.enabled = true;
                        }
                    }
                    finally { if (projectile != null) projectile.TerminatePresentation(ProjectilePresentationPhase.Cancelled, projectile.transform.position); else Object.DestroyImmediate(root); }
                }
            }
            finally
            {
                File.WriteAllLines(Path.Combine(output, "pixels.csv"), rows);
                RenderTexture.active = previous; camera.targetTexture = null;
                Object.DestroyImmediate(image); Object.DestroyImmediate(target); Object.DestroyImmediate(cameraRoot); Object.DestroyImmediate(holder);
                emitter.DisposePresentationReplica(); Object.DestroyImmediate(emitter.gameObject); Object.DestroyImmediate(ownerRoot); if (ownedPool != null) Object.DestroyImmediate(ownedPool);
            }
        }
    }
}
#endif
