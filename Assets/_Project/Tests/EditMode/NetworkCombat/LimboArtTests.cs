using System;
using System.Linq;
using System.Reflection;
using System.IO;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.AI;
using AstralShift.Rendering;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class LimboArtTests
    {
        [Test]
        public void DyingReferenceBodyKeepsItsPresentedDirectionDespiteLateNavigation()
        {
            var root=new GameObject("reference-death-quadrant");
            try
            {
                var animator=root.AddComponent<EnemyAnimator>();
                var f=BindingFlags.Instance|BindingFlags.NonPublic;
                void Set(string key,object value)=>typeof(EnemyAnimator).GetField(key,f).SetValue(animator,value);
                var left=new Animancer.ClipTransition();var right=new Animancer.ClipTransition();
                Set("deadLeftDown",left);Set("deadRightDown",right);
                Set("useRecoveredMovement",true);Set("_blockAnimations",true);
                Set("bodyPresentationFacing",new Vector2(1,-1));
                Assert.That(animator.GetDeadClipTransition(-1,-1),Is.SameAs(right));
                Set("useRecoveredMovement",false);
                Assert.That(animator.GetDeadClipTransition(-1,-1),Is.SameAs(left),"ordinary game keeps its existing selection");
            }
            finally{Object.DestroyImmediate(root);}
        }

        [Test]
        public void ReplicaAttackFacingAlsoSelectsSubsequentDeathDirection()
        {
            var root=new GameObject("reference-replica-facing");
            try
            {
                var enemy=root.AddComponent<EnemyController>();
                enemy.enemyAnimator=root.AddComponent<EnemyAnimator>();
                enemy._currentMovementScript=root.AddComponent<EnemyDefaultMovement>();
                enemy.hasAttackAnimation=true;
                typeof(EnemyController).GetField("animateMovementOnlyDeath",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(enemy,true);
                foreach(var facing in new[]{new Vector2(1,1),new Vector2(-1,1),new Vector2(-1,-1),new Vector2(1,-1)})
                {
                    enemy.ApplyReplicatedAttackPresentation(EnemyAttackPresentationPhase.Recovery,facing,.1);
                    Assert.That(enemy.FacingDirection,Is.EqualTo(facing.normalized));
                }
            }
            finally{Object.DestroyImmediate(root);}
        }

        [Test]
        public void PredictedLethalPresentationStartsOnceAfterOutermostDamageScope()
        {
            var root=new GameObject("reference-deferred-death");
            try
            {
                var e=root.AddComponent<EnemyController>();
                var f=BindingFlags.Instance|BindingFlags.NonPublic;
                void Set(string name,object value)=>typeof(EnemyController).GetField(name,f).SetValue(e,value);
                int started=0;
                e.Moving=new AstralShift.FSM.State("Moving");e.Dead=new AstralShift.FSM.State("Dead");
                e.Dead.onEnter=()=>started++;
                var machine=new AstralShift.FSM.StateMachine("Death fixture");
                machine.AddAnyTransition(e.Dead);machine.SetInitialState(e.Moving);
                Set("_stateMachine",machine);Set("animateMovementOnlyDeath",true);
                Set("_damageResolutionDepth",2);Set("_predictedDeath",true);
                var finish=typeof(EnemyController).GetMethod("EndDamageResolution",f);
                finish.Invoke(e,null);Assert.That(started,Is.Zero,"nested damage must finish first");
                finish.Invoke(e,null);Assert.That(started,Is.EqualTo(1),"prediction cannot wait for a deduplicated kill echo");
                finish.Invoke(e,null);Assert.That(started,Is.EqualTo(1),"no replay of the same death");
            }
            finally{Object.DestroyImmediate(root);}
        }

        [Test]
        public void AllApprovedBodiesBindSourceTransitionsAndKeepGameplayGeometry()
        {
            var data=JsonUtility.FromJson<LimboArtAssets.Source>(File.ReadAllText(LimboArtAssets.Root+"/ArtSource.json"));
            Assert.That(data.bodies.Length,Is.EqualTo(8));
            foreach(var body in data.bodies)
            {
                var root=AssetDatabase.LoadAssetAtPath<GameObject>(body.target);
                Assert.That(root,Is.Not.Null,body.target);
                var enemy=root.GetComponent<EnemyController>(); var so=new SerializedObject(enemy.enemyAnimator);
                Assert.That(enemy.enemyAnimator.GetComponent<Animator>().runtimeAnimatorController,Is.Null,body.group);
                Assert.That(root.transform.localScale,Is.EqualTo(Vector3.one),body.group);
                Assert.That(enemy.spriteRenderer.sprite.name,Is.Not.EqualTo("Circle"),body.group+" reset must restore a recovered body frame");
                Assert.That(new SerializedObject(root.GetComponent<NetworkEnemyServerDriver>()).FindProperty("waitForDeathPresentation").boolValue,Is.True,body.group+" canonical death must retain its visual tail");
                Assert.That(new SerializedObject(root.GetComponent<EnemyController>()).FindProperty("animateMovementOnlyDeath").boolValue,Is.True,body.group+" contact bodies need presentation without an attack FSM");
                foreach(var binding in body.bindings)
                {
                    var actual=so.FindProperty(binding.field).FindPropertyRelative("_Clip").objectReferenceValue;
                    Assert.That(actual,Is.SameAs(AssetDatabase.LoadAssetAtPath<AnimationClip>(binding.path)),body.group+"/"+binding.field);
                    foreach(var callback in binding.visualCallbacks ?? System.Array.Empty<LimboArtAssets.Callback>())
                    {
                        var slot=so.FindProperty(binding.field).FindPropertyRelative("_Events").FindPropertyRelative("_Callbacks").GetArrayElementAtIndex(callback.index);
                        var restored=slot.managedReferenceValue as UnityEngine.Events.UnityEvent;
                        Assert.That(restored,Is.Not.Null,body.group+"/"+binding.field+" visual callback");
                        Assert.That(restored.GetPersistentEventCount(),Is.EqualTo(1));
                        Assert.That(restored.GetPersistentMethodName(0),Is.EqualTo(callback.method));
                        Assert.That(restored.GetPersistentTarget(0),Is.SameAs(enemy.enemyAnimator));
                    }
                }
                if(body.contact) Assert.That(root.GetComponent<Rigidbody2D>().freezeRotation,Is.True,body.group+" must not rotate its offset contact circle");
            }
        }

        [Test]
        public void EveryPaletteAnimationAtlasHasOriginalRuntimeBake()
        {
            var data=JsonUtility.FromJson<LimboArtAssets.Source>(File.ReadAllText(LimboArtAssets.Root+"/ArtSource.json"));
            var bakes=JsonUtility.FromJson<LimboArtAssets.Bakes>(File.ReadAllText(LimboArtAssets.Root+"/BakedPalettes.json"));
            foreach(var body in data.bodies)
            foreach(var palette in data.palettes.Where(p=>p.identity==body.identity&&!string.IsNullOrEmpty(p.texture)))
            {
                var textures=body.bindings.Where(b=>!b.field.StartsWith("shadow")).SelectMany(b=>
                {
                    var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(b.path);
                    return AnimationUtility.GetObjectReferenceCurveBindings(clip).SelectMany(binding=>AnimationUtility.GetObjectReferenceCurve(clip,binding))
                        .Select(k=>k.value).OfType<Sprite>().Select(s=>AssetDatabase.GetAssetPath(s.texture));
                }).Distinct();
                foreach(var texture in textures)Assert.That(bakes.pairs.Any(b=>b.original==texture&&b.lut==palette.texture),Is.True,body.group+"/"+texture);
            }
        }

        [Test]
        public void EliteKeepsShortRightVisualAndLongLogicalRecovery()
        {
            var enemy=AssetDatabase.LoadAssetAtPath<GameObject>(LimboStage2Assets.Root+"/ReferenceElite_Skeleton.prefab").GetComponent<EnemyController>();
            Assert.That(enemy.enemyAnimator.RecoveryTime,Is.EqualTo(.43f).Within(.00001));
            Assert.That(enemy.enemyAnimator.RecoveryRightUp.Length,Is.EqualTo(.28f).Within(.00001));
            Assert.That(enemy.attackScript.GetComponent<EnemyAttackMelee>(),Is.Not.Null);
        }

        [Test]
        public void FireAndDeferredDisplaysHaveOnlyApprovedVisualComponents()
        {
            var fire=AssetDatabase.LoadAssetAtPath<GameObject>(LimboArtAssets.Root+"/Effects/ReferenceFireParticles.prefab");
            Assert.That(fire.GetComponentsInChildren<ParticleSystem>(true).Length,Is.EqualTo(6));
            foreach(var name in new[]{"Ghoul_Warning","soul enemy warning","Enemy_Bomb_ExplosionAttack 1"})
            {
                var effect=AssetDatabase.LoadAssetAtPath<GameObject>(LimboArtAssets.Root+"/Effects/"+name+".prefab");
                Assert.That(effect.GetComponentsInChildren<EnemyController>(true),Is.Empty);
                Assert.That(effect.GetComponentsInChildren<Collider2D>(true),Is.Empty);
                Assert.That(effect.GetComponentsInChildren<AstralShift.HellMaiden.Interactions.PlayerDamageInteraction>(true),Is.Empty);
            }
            var rules=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/Full.asset");
            Assert.That(rules.TryCapture(out var full,out var error),Is.True,error);
            Assert.That(full.Reference.ReadinessError(),Is.Not.Null,"Art import must not enable LostSoul/Ghoul or Full");
            var display=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/ArtEffects.asset");
            Assert.That(display.TryCapture(out var visual,out error),Is.True,error);
            Assert.That(visual.Reference.ReadinessError(),Is.Null);
            Assert.That(visual.Reference.Clips.All(c=>c.Start>=24),Is.True,"Visual-only fixture must not spawn enemies");
        }
        [TestCase("Stage2/ReferenceBrotchi.prefab", "Brotchi_Walk_LeftDown", "brotchi_walk")]
        [TestCase("Dash/ReferenceBrotchiDash.prefab", "brochipink_walk_left", "brotchi_walk_pink")]
        public void OpeningBodiesUseSourceSpritesAndMovement(string path, string clip, string sprite)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(LimboReferenceAssets.Root + "/" + path);
            var enemy = root.GetComponent<EnemyController>();
            Assert.That(enemy.spriteRenderer.sprite.name, Does.StartWith(sprite));
            Assert.That(enemy.enemyAnimator.MoveLeftDown.Clip.name, Is.EqualTo(clip));
            Assert.That(root.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(enemy.enemyAnimator.PaletteSwapper, Is.Not.Null);
            Assert.That(enemy.enemyAnimator.GetComponent<Animator>().runtimeAnimatorController, Is.Null);
        }

        [Test]
        public void RestoredArrowHasSourceLayersAndNoDamageComponents()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(LimboDashAssets.ArrowPath);
            Assert.That(root.transform.Find("SlimePath"), Is.Not.Null);
            Assert.That(root.GetComponentsInChildren<SpriteRenderer>(true).Length, Is.EqualTo(6));
            Assert.That(root.GetComponentsInChildren<ParticleSystem>(true).Length, Is.EqualTo(3));
            Assert.That(root.GetComponentsInChildren<Collider2D>(true), Is.Empty);
            Assert.That(root.GetComponent<EnemyAttackPrefab>().damageInteraction, Is.Null);
        }

        [Test]
        public void PartiallyVisibleBodyIsNotRetiredJustBecauseItsFeetAreOffscreen()
        {
            var views = new[] { new Bounds(Vector3.zero, new Vector3(20, 12, 0)), new Bounds(new Vector3(30, 0), new Vector3(20, 12, 0)) };
            var body = new Bounds(new Vector3(41, 0), new Vector3(4, 2, 0));
            Assert.That(GameplayCameraGeometry.MinimumOutsideDistance(body, views), Is.Zero);
            body.center = new Vector3(44, 0);
            Assert.That(GameplayCameraGeometry.MinimumOutsideDistance(body, views), Is.EqualTo(2));
        }

        [Test]
        public void SharedBakedPaletteSurvivesAnotherEnemyDestructionAndSameAtlasVariantSwitch()
        {
            var original = new Texture2D(4, 4); var lutA = new Texture2D(2, 2); var lutB = new Texture2D(2, 2);
            var bakedA = new Texture2D(4, 4); var bakedB = new Texture2D(4, 4);
            var sprite = Sprite.Create(original, new Rect(0,0,4,4), Vector2.one*.5f);
            var a = new GameObject("palette-owner-a"); var b = new GameObject("palette-owner-b");
            var tick = typeof(SpriteRendererPaletteSwapper).GetMethod("LateUpdate", BindingFlags.Instance|BindingFlags.NonPublic);
            try
            {
                PaletteSwapSpriteManager.RegisterBakedTexture(original,lutA,bakedA);
                PaletteSwapSpriteManager.RegisterBakedTexture(original,lutB,bakedB);
                var sa = a.AddComponent<SpriteRenderer>(); sa.sprite = sprite;
                var sb = b.AddComponent<SpriteRenderer>(); sb.sprite = sprite;
                var pa = a.AddComponent<SpriteRendererPaletteSwapper>(); pa.Renderer = sa;
                var pb = b.AddComponent<SpriteRendererPaletteSwapper>(); pb.Renderer = sb;
                pa.ColorLut=lutA; pb.ColorLut=lutA; tick.Invoke(pa,null); tick.Invoke(pb,null);
                var shared=sb.sprite; Assert.That(sa.sprite,Is.SameAs(shared));
                tick.Invoke(pb,null); tick.Invoke(pb,null); // An unchanged animation frame must stay mapped.
                Assert.That(pb.ModifiedTexture,Is.SameAs(bakedA));
                Object.DestroyImmediate(a); Assert.That(shared!=null,Is.True); Assert.That(sb.sprite.texture,Is.SameAs(bakedA));
                pb.ColorLut=lutB; tick.Invoke(pb,null); Assert.That(sb.sprite.texture,Is.SameAs(bakedB));
                pb.ColorLut=null; Assert.That(sb.sprite,Is.SameAs(sprite));
                PaletteSwapSpriteManager.ClearAll(); Assert.That(bakedA!=null && bakedB!=null,Is.True);
            }
            finally
            {
                if(a!=null)Object.DestroyImmediate(a); Object.DestroyImmediate(b); PaletteSwapSpriteManager.ClearAll();
                foreach(var obj in new Object[]{sprite,original,lutA,lutB,bakedA,bakedB})Object.DestroyImmediate(obj);
            }
        }
    }
}
