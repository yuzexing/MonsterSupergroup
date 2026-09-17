using System;
using System.Linq;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using MonsterSupergroup.NetworkCombat.Editor;
using NUnit.Framework;
using Mirror;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class LimboGhoulTests
    {
        private static EnemyActionState Combo()=>new EnemyActionState
        {
            Sequence=true,ActionId=43,ComboStartedAt=10,Phase=EnemyAttackPresentationPhase.Warning,
            SequenceWarnings=new(.292857170f,.378571451f,.207142860f),SequenceActives=new(.235714287f,.2f,.207142860f),
            RecoveryUntil=11.6428572,NextAttackAt=12.1428572,LockedStrikeMask=1,PoseStrikeIndex=0
        };
        [TestCase(.1,0,EnemyAttackPresentationPhase.Warning)]
        [TestCase(.4,0,EnemyAttackPresentationPhase.Active)]
        [TestCase(.6,1,EnemyAttackPresentationPhase.Warning)]
        [TestCase(1,1,EnemyAttackPresentationPhase.Active)]
        [TestCase(1.2,2,EnemyAttackPresentationPhase.Warning)]
        [TestCase(1.4,2,EnemyAttackPresentationPhase.Active)]
        [TestCase(1.6,2,EnemyAttackPresentationPhase.Recovery)]
        [TestCase(1.7,2,EnemyAttackPresentationPhase.Inactive)]
        public void HandoffResolvesWholeComboWithoutIntermediateRecovery(double elapsed,int index,EnemyAttackPresentationPhase phase)
        {
            var resolved=EnemySequenceTimeline.Resolve(Combo(),10+elapsed);
            Assert.That(resolved.StrikeIndex,Is.EqualTo(index));Assert.That(resolved.Phase,Is.EqualTo(phase));
            Assert.That(resolved.ActionId,Is.EqualTo(43));Assert.That(resolved.ComboStartedAt,Is.EqualTo(10));
        }
        [Test] public void ExpiredPoseCannotCreateHitForAnotherStrike()
        {
            var state=EnemySequenceTimeline.Resolve(Combo(),11.4);
            Assert.That(EnemySequenceTimeline.HasPose(state),Is.False);
            state.PoseStrikeIndex=2;state.LockedStrikeMask=5;Assert.That(EnemySequenceTimeline.HasPose(state),Is.True);
            state.Phase=EnemyAttackPresentationPhase.Cancelled;
            Assert.That(state.PhaseAt(11.41),Is.EqualTo(EnemyAttackPresentationPhase.Cancelled));
        }
        [Test] public void ClockCorrectionCannotRewindAnAcceptedStrike()
        {
            var active=EnemySequenceTimeline.Resolve(Combo(),11.4);
            var corrected=EnemySequenceTimeline.Resolve(active,10.3);
            Assert.That(corrected.StrikeIndex,Is.EqualTo(2));Assert.That(corrected.Phase,Is.EqualTo(EnemyAttackPresentationPhase.Active));
        }
        [Test] public void SourceBindingsAndGeometryAreIndependent()
        {
            var p=AssetDatabase.LoadAssetAtPath<GameObject>(LimboGhoulAssets.EnemyPath);
            var c=p.GetComponent<EnemyController>();var attack=p.GetComponent<SequenceEnemyAttack>();attack.enemyAnimator=c.enemyAnimator;
            Assert.That(c.attackDistance,Is.EqualTo(2.5f));Assert.That(c.attackCooldown,Is.EqualTo(.5f));Assert.That(attack.consecutiveAttacks,Is.EqualTo(2));
            var so=new SerializedObject(c.enemyAnimator);var sets=so.FindProperty("attackSets");Assert.That(sets.arraySize,Is.EqualTo(3));
            Assert.That(sets.GetArrayElementAtIndex(2).FindPropertyRelative("attackWarningLeftUp").FindPropertyRelative("_Clip").objectReferenceValue.name,Is.EqualTo("Enemy_Ghoul_Attack_Right 2"));
            for(int i=0;i<3;i++){attack.currentAttackCount=i;Assert.That(attack.WarningTime,Is.EqualTo(Combo().SequenceWarnings[i]).Within(.000001));Assert.That(attack.AttackTime,Is.EqualTo(Combo().SequenceActives[i]).Within(.000001));}
            Assert.That(attack.RecoveryTime,Is.EqualTo(.121428572f).Within(.000001));
            var polygon=attack.attackPrefab.damageInteraction.GetComponent<PolygonCollider2D>();Assert.That(polygon.points.Length,Is.EqualTo(6));
            Assert.That(polygon.points[0],Is.EqualTo(new Vector2(1.56064153f,2.31846976f)));
            Assert.That(((CircleCollider2D)c.collider).radius,Is.EqualTo(.46f));Assert.That(c.collider.offset,Is.EqualTo(new Vector2(0,.3f)));
            Assert.That(p.GetComponent<EnemyContactDamage>(),Is.Null);
            Assert.That(p.GetComponent<EnemyWarningStep>(),Is.Not.Null);
            Assert.That(p.GetComponent<EnemyWarningStep>().stepDistance,Is.EqualTo(.4f));
            Assert.That(p.GetComponent<NetworkIdentity>().assetId,Is.EqualTo(NetworkIdentity.AssetGuidToUint(new Guid(AssetDatabase.AssetPathToGUID(LimboGhoulAssets.EnemyPath)))));
        }
        [Test] public void PreviewKeepsExactCutoffCountsAndFullGate()
        {
            var r=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/GhoulValidation.asset");
            Assert.That(r.TryCapture(out var p,out var error),Is.True,error);Assert.That(p.Reference.ReadinessError(),Is.Null);
            Assert.That(p.Reference.EndTime,Is.EqualTo(599.9666666666667));var clips=p.Reference.Clips.Where(c=>c.Start<p.Reference.EndTime).ToArray();
            Assert.That(clips.Length,Is.EqualTo(22));Assert.That(clips.Count(c=>c.Mode==ReferenceSpawnMode.CurveBudget),Is.EqualTo(12));
            Assert.That(clips.Where(c=>c.Mode==ReferenceSpawnMode.CurveBudget).Sum(c=>c.Count),Is.EqualTo(1077));
            Assert.That(clips.Count(c=>c.Mode==ReferenceSpawnMode.AliveTarget),Is.EqualTo(8));Assert.That(clips.Count(c=>c.Mode==ReferenceSpawnMode.FormationBurst),Is.EqualTo(2));
            var late=clips.Single(c=>Math.Abs(c.Start-509.65)<.00001);Assert.That(late.Variant,Is.EqualTo(1));Assert.That(late.Stats.BaseHealth,Is.EqualTo(150));
            var full=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/Full.asset");Assert.That(full.TryCapture(out var f,out _),Is.True);
            f.Reference.FlowReadiness = ReferenceEnemyReadiness.ImplementationPending; Assert.That(f.Reference.ReadinessError(),Does.Contain("flow gate"));
        }
        [Test] public void CheckpointProtocolRoundTripsComboAndRejectsInvalidIndices()
        {
            var action=EnemySequenceTimeline.Resolve(Combo(),11.4);action.ExecutedStrikeMask=7;action.LockedStrikeMask=7;action.PoseStrikeIndex=2;
            action.WarningStep=new EnemyWarningStepState{Enabled=true,StartedMask=7,CompletedMask=3,SampledAt=11.3,
                Pending=true,RequestedPosition=new Vector2(2,3),Facing=Vector2.left};
            var snapshot=new EnemySimulationSnapshot{EnemyEntityId=4,AssignmentEpoch=9,Sequence=1,Runtime=new EnemySimulationRuntimeState{Action=action}};
            Assert.That(snapshot.IsFinite,Is.True);
            using(var writer=NetworkWriterPool.Get())
            {
                writer.Write(snapshot);
                var restored=new NetworkReader(writer.ToArraySegment()).Read<EnemySimulationSnapshot>();
                Assert.That(restored.Runtime.Action.ActionId,Is.EqualTo(action.ActionId));Assert.That(restored.Runtime.Action.StrikeIndex,Is.EqualTo(2));
                Assert.That(restored.Runtime.Action.ExecutedStrikeMask,Is.EqualTo(7));Assert.That(restored.Runtime.Action.ComboStartedAt,Is.EqualTo(10));
                Assert.That(restored.Runtime.Action.WarningStep,Is.EqualTo(action.WarningStep));
                TestContext.WriteLine("Ghoul full checkpoint bytes="+writer.Position);
            }
            snapshot.Runtime.Action.StrikeIndex=3;Assert.That(snapshot.IsFinite,Is.False);
            snapshot.Runtime.Action=action;snapshot.Runtime.Action.SequenceWarnings.x=float.NaN;Assert.That(snapshot.IsFinite,Is.False);
            snapshot.Runtime.Action=action;snapshot.Runtime.Action.WarningStep.CompletedMask=8;Assert.That(snapshot.IsFinite,Is.False);
            snapshot.Runtime.Action=action;snapshot.Runtime.Action.WarningStep.SampledAt=double.NaN;Assert.That(snapshot.IsFinite,Is.False);
            snapshot.Runtime.Action=action;snapshot.Runtime.Action.WarningStep.RequestedPosition.x=float.PositiveInfinity;Assert.That(snapshot.IsFinite,Is.False);
        }
        [Test] public void AdmittedCancellationRejectsOldWindowsWithoutBlockingANewCombo()
        {
            var parent=new GameObject("Inactive protocol fixture");parent.SetActive(false);
            try
            {
                var copy=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(LimboGhoulAssets.EnemyPath),parent.transform);
                var agent=copy.GetComponent<NetworkEnemySimulationAgent>();
                var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                typeof(NetworkEnemySimulationAgent).GetMethod("ResolveReferences",flags).Invoke(agent,null);
                var validate=typeof(NetworkEnemySimulationAgent).GetMethod("ValidateSequenceAction",flags);
                bool Valid(EnemyActionState value)=>(bool)validate.Invoke(agent,new object[]{value});
                var active=EnemySequenceTimeline.Resolve(Combo(),10.4);Assert.That(Valid(active),Is.True);
                var malformed=active;malformed.NextAttackAt-=.2;Assert.That(Valid(malformed),Is.False);
                malformed=active;malformed.ActiveUntil+=.2;Assert.That(Valid(malformed),Is.False);
                var cancelled=active;cancelled.Phase=EnemyAttackPresentationPhase.Cancelled;
                typeof(NetworkEnemySimulationAgent).GetMethod("RememberSequenceCancellation",flags).Invoke(agent,new object[]{cancelled});
                Assert.That(Valid(active),Is.False);Assert.That(Valid(cancelled),Is.True);
                active.ActionId++;Assert.That(Valid(active),Is.True);
                var notify=typeof(NetworkEnemySimulationAgent).GetMethod("TryMarkSequenceInterruptBroadcast",flags);
                Assert.That((bool)notify.Invoke(agent,new object[]{active.ActionId}),Is.True);
                Assert.That((bool)notify.Invoke(agent,new object[]{active.ActionId}),Is.False);
            }
            finally{UnityEngine.Object.DestroyImmediate(parent);}
        }
    }
}
