using System.Collections.Generic;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class GhoulAttackLifetimeTests
    {
        private readonly List<GameObject> owned=new();
        private EnemyController controller;
        private SequenceEnemyAttack attack;
        private GameObject Create(string name,bool active=true){var go=new GameObject(name);go.SetActive(active);owned.Add(go);return go;}
        [SetUp] public void Setup()
        {
            Create("Ghoul test pool").AddComponent<PoolManager>().Init();
            var rules=Resources.Load<GameplayWaveRules>("LimboReference/GhoulFixture");Assert.That(rules.TryCapture(out var plan,out var error),Is.True,error);
            var source=plan.Prefabs[plan.Reference.Clips[0].PrefabIndex].GetComponent<EnemyController>();
            var root=Create("Same Ghoul owner",false);controller=root.AddComponent<EnemyController>();controller.enabled=false;
            controller.stats=new EnemyStats();controller.stats.Init(new EnemyStatsValues{Health=50,Damage=50,Speed=6,XP=9,KnockBackMultiplier=1,WindMultiplier=1});
            attack=root.AddComponent<SequenceEnemyAttack>();attack.controller=controller;attack.enemyAnimator=source.enemyAnimator;controller.attackScript=attack;
            attack.attackPrefab=((SequenceEnemyAttack)source.attackScript).attackPrefab;root.SetActive(true);
        }
        private static EnemyActionState State(double now)
        {
            var s=new EnemyActionState{Sequence=true,ActionId=42,ComboStartedAt=1,Phase=EnemyAttackPresentationPhase.Warning,
                SequenceWarnings=new(.292857170f,.378571451f,.207142860f),SequenceActives=new(.235714287f,.2f,.207142860f),
                RecoveryUntil=2.6428572,NextAttackAt=3.1428572,Facing=Vector2.right,LockedStrikeMask=7};
            s=EnemySequenceTimeline.Resolve(s,now);s.PoseStrikeIndex=s.StrikeIndex;return s;
        }
        [TearDown] public void Cleanup(){attack?.CancelAttack();for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null)Object.DestroyImmediate(owned[i]);owned.Clear();PoolManager.Instance=null;}
        [Test] public void PoolReuseRebindsStatsAndKeepsAlternatingParentLocalOffsets()
        {
            EnemyAttackPrefab previous=null;
            foreach(double now in new[]{1.4,2.0,2.4})
            {
                var s=State(now);attack.RestoreSequence(s,now,false);var current=attack.SimulationAttackInstance;
                Assert.That(current.damageInteraction.enemyStats,Is.SameAs(controller.stats));
                Assert.That(current.transform.localPosition,Is.EqualTo((s.StrikeIndex%2==0?-1:1)*attack.areaSideWarpDistance));
                Assert.That(current.damageInteraction.isActiveAndEnabled,Is.True);
                if(previous!=null)Assert.That(current,Is.SameAs(previous));previous=current;
                attack.SuspendSimulation();Assert.That(current.damageInteraction.gameObject.activeSelf,Is.False);
                controller.stats=new EnemyStats();controller.stats.Init(new EnemyStatsValues{Health=50,Damage=50,Speed=6,XP=9});
            }
        }
        [TestCase(1.1,false)] [TestCase(1.4,true)] [TestCase(2.6,false)] [TestCase(3.0,false)]
        public void ActualColliderOnlyExistsInsideCurrentActiveWindow(double now,bool hit)
        {
            var s=State(now);attack.RestoreSequence(s,now,false);
            Assert.That(attack.SimulationAttackInstance!=null&&attack.SimulationAttackInstance.damageInteraction.isActiveAndEnabled,Is.EqualTo(hit));
            if(hit){var p=attack.SimulationAttackInstance.damageInteraction.GetComponent<PolygonCollider2D>();Assert.That(p.points.Length,Is.EqualTo(6));}
        }
        [Test] public void CancelledOrMissingPoseNeverRestoresHit()
        {
            var s=State(2.4);s.PoseStrikeIndex=0;attack.RestoreSequence(s,2.4,false);Assert.That(attack.SimulationAttackInstance,Is.Null);
            s.PoseStrikeIndex=2;s.Phase=EnemyAttackPresentationPhase.Cancelled;attack.RestoreSequence(s,2.4,false);Assert.That(attack.SimulationAttackInstance,Is.Null);
        }
        [Test] public void ThirdRecoveryKeepsDisplayIndexAfterLogicalCounterResets()
        {
            var s=State(2.6);attack.RestoreSequence(s,2.6,false);
            Assert.That(s.StrikeIndex,Is.EqualTo(2));Assert.That(attack.currentAttackCount,Is.Zero);
            Assert.That(attack.SimulationAttackInstance,Is.Null);
        }
    }
}
