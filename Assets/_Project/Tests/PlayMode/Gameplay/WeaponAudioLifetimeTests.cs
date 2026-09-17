#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class WeaponAudioLifetimeTests
    {
        private GameObject pool, ownerObject;
        private PlayerMovement owner;
        private WeaponBehaviour emitter;
        private readonly List<AttackAudioAudit.Entry> trace = new();
        private static ProjectilePresentationStats Stats => new() { DamageMultiplierSum=1, SpeedMultiplierSum=1, SizeMultiplierSum=1,
            DurationMultiplierSum=1, EffectiveSpeed=.4f, Duration=10, ProjectileCount=1, BaseProjectileCount=1 };
        [SetUp] public void Setup()
        {
            Time.timeScale=1;
            pool=new GameObject("Audio pool");pool.AddComponent<PoolManager>().Init();
            ownerObject=new GameObject("Audio owner");ownerObject.SetActive(false);owner=ownerObject.AddComponent<PlayerMovement>();
            AttackAudioAudit.Changed+=trace.Add;
        }
        [TearDown] public void Teardown()
        {
            if(emitter is ProjectileAttackBehaviour p)p.DisposePresentationReplica();
            if(emitter is PlayerBeamAttackBehaviour b)b.DisposePresentationReplica();
            if(emitter!=null)Object.DestroyImmediate(emitter.gameObject);
            Object.DestroyImmediate(pool);Object.DestroyImmediate(ownerObject);PoolManager.Instance=null;
            AttackAudioAudit.Changed-=trace.Add;trace.Clear();Time.timeScale=1;
        }
        [UnityTest] public IEnumerator WispOnlyShootsAndFinalHits_ReuseAndLateReplayDoNotAddFlightOrPiercingAudio()
        {
            var p=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/GameObject/Dante_SlowProjectile_Behaviour.prefab")).GetComponent<ProjectileAttackBehaviour>();
            emitter=p;p.InitializePresentationReplica(2,owner);
            foreach(var element in new[]{AttackElement.Default,AttackElement.Fire,AttackElement.Poison})
            {
                trace.Clear();
                var spawn=new ProjectilePresentationSpawn(2,new ProjectilePresentationKey(42,0),Vector3.zero,Vector2.right,element,true,Stats);
                var shot=p.PlayPresentation(spawn,0);
                Assert.That(trace.Count(e=>e.Operation=="start-shot"),Is.EqualTo(1));
                shot.TerminatePresentation(ProjectilePresentationPhase.Impact,Vector3.one);
                Assert.That(trace.Any(e=>e.Operation=="final-hit"),Is.False);
                shot.TerminatePresentation(ProjectilePresentationPhase.Hit,Vector3.one);
                Assert.That(trace.Count(e=>e.Operation=="final-hit"),Is.EqualTo(1));
                Assert.That(trace.Where(e=>e.Operation=="final-hit").Single().Event,Does.EndWith("slowprojectile_shot"));
                shot.TerminatePresentation(ProjectilePresentationPhase.Hit,Vector3.one);
                Assert.That(trace.Count(e=>e.Operation=="final-hit"),Is.EqualTo(1));
                trace.Clear();var late=p.PlayPresentation(spawn,3,playLaunchSound:false);
                Assert.That(trace,Is.Empty,"State restoration cannot replay the expired shot.");
                late.TerminatePresentation(ProjectilePresentationPhase.Cancelled,Vector3.zero);
                yield return null;
            }
        }
        [UnityTest] public IEnumerator BreathPhaseChangesAtExitCompletion_CancellationAndReuseReleaseOneInstance()
        {
            var description=FMODUnity.RuntimeManager.GetEventDescription("event:/sx/plr/Sx_plr_dragonbreath");
            description.getInstanceCount(out int initialInstances);
            var data=AssetDatabase.LoadAssetAtPath<AstralShift.HellMaiden.Data.Cards.WeaponData>("Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Beam/MonoBehaviour/WeaponData_Dante_DragonsBreath.asset");
            var beam=Object.Instantiate((PlayerBeamAttackBehaviour)data.WeaponPrefab);emitter=beam;beam.InitializePresentationReplica(3,owner);
            foreach(var element in new[]{AttackElement.Fire,AttackElement.Poison})
            {
                trace.Clear();var key=new BeamPresentationKey(50,0);
                var shot=beam.PlayPresentation(new BeamPresentationSpawn(3,key,1,Vector2.right,element,.15f,Stats),0);
                Assert.That(trace.Count(e=>e.Operation=="start"),Is.EqualTo(1));
                yield return new WaitForSeconds(.2f);
                Assert.That(trace.Any(e=>e.Operation=="completion-parameter"),Is.False,"Not at the damage/entry boundary.");
                float deadline=Time.realtimeSinceStartup+4;
                while(!trace.Any(e=>e.Operation=="completion-parameter")&&Time.realtimeSinceStartup<deadline)yield return null;
                Assert.That(trace.Count(e=>e.Operation=="completion-parameter"),Is.EqualTo(1));
                Assert.That(trace.Count(e=>e.Operation=="release"),Is.EqualTo(1));
                Assert.That(trace.All(e=>e.Result==FMOD.RESULT.OK),Is.True);
                trace.Clear();shot=beam.PlayPresentation(new BeamPresentationSpawn(3,new BeamPresentationKey(51,0),1,Vector2.right,element,2,Stats),1);
                Assert.That(trace.Single(e=>e.Operation=="start").Value,Is.EqualTo(1));
                FMODUnity.RuntimeManager.StudioSystem.flushCommands();
                var resumedSound=new FMOD.Studio.EventInstance(new System.IntPtr(trace.Single(e=>e.Operation=="start").Handle));
                Assert.That(resumedSound.getTimelinePosition(out int timeline),Is.EqualTo(FMOD.RESULT.OK));
                Assert.That(timeline,Is.InRange(950,1300),"Restoration seeks before playback; it must not restart the intro at zero.");
                Time.timeScale=0;yield return null;
                Assert.That(trace.Count(e=>e.Operation=="start"),Is.EqualTo(1));
                Time.timeScale=1;beam.TerminatePresentation(new BeamPresentationKey(51,0));
                Assert.That(trace.Count(e=>e.Operation=="release"),Is.EqualTo(1));
                Assert.That(trace.Any(e=>e.Operation=="completion-parameter"),Is.False,"Cancellation is not a normal exit.");
            }
            float releaseDeadline=Time.realtimeSinceStartup+3;
            int remaining;
            do { yield return null; description.getInstanceCount(out remaining); }
            while(remaining>initialInstances && Time.realtimeSinceStartup<releaseDeadline);
            Assert.That(remaining,Is.LessThanOrEqualTo(initialInstances),"Submitted release must finish, not just clear managed handles.");
        }
    }
}
#endif
