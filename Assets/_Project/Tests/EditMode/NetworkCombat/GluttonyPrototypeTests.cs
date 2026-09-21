using System;
using System.Linq;
using MonsterSupergroup.GAS;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class GluttonyPrototypeTests
    {
        private static GluttonyParameters Enabled()
        { var p = GluttonyParameters.Defaults; p.Enabled = true; return p; }
        [Test] public void DefaultsAreValidAndOptIn()
        { Assert.That(GluttonyParameters.Defaults.IsValid); Assert.That(GluttonyParameters.Defaults.Enabled, Is.False); }
        [Test] public void PassiveStartsReadyAndCommitsOnlyOnSuccess()
        {
            var r = new GluttonyPrototypeRuntime(); var p = Enabled();
            Assert.That(r.CanConsume(1,false,0,p,0)); Assert.That(r.State.PassiveReadyAt, Is.Zero);
            r.CommitConsume(1,false,p,2);
            Assert.That(r.State.PassiveReadyAt, Is.EqualTo(12));
            Assert.That(r.CanConsume(2,false,0,p,11.999), Is.False);
            Assert.That(r.CanConsume(2,false,0,p,12));
        }
        [Test] public void MultipleFreeCollectionsDoNotChangePassiveDeadline()
        {
            var r = new GluttonyPrototypeRuntime(); var p = Enabled(); r.CommitConsume(9,false,p,0);
            Assert.That(r.TryCast(100,new uint[]{1,2,3},p,1));
            foreach (uint id in new uint[]{1,2,3})
            { Assert.That(r.CanConsume(id,true,100,p,2)); r.CommitConsume(id,true,p,2); }
            Assert.That(r.State.PassiveReadyAt, Is.EqualTo(10));
            Assert.That(r.State.Collected, Is.EqualTo(3)); Assert.That(r.Marks, Is.Empty);
        }
        [Test] public void EmptyCastConsumesActiveCooldownButNotPassive()
        {
            var r = new GluttonyPrototypeRuntime(); var p = Enabled();
            Assert.That(r.TryCast(100,Array.Empty<uint>(),p,0));
            Assert.That(r.CanCast(p,19.99),Is.False); Assert.That(r.CanCast(p,20));
            Assert.That(r.State.PassiveReadyAt,Is.Zero);
        }
        [Test] public void CandidatesAreDeduplicatedAndCappedInInputOrder()
        {
            var r = new GluttonyPrototypeRuntime(); var p=Enabled(); p.MaximumTargets=3;
            r.TryCast(1,new uint[]{0,5,5,3,4,2},p,0);
            Assert.That(r.Marks.OrderBy(x=>x),Is.EqualTo(new uint[]{3,4,5}));
            Assert.That(r.State.Marked,Is.EqualTo(3));
        }
        [Test] public void MarksAreOwnedByOneRuntimeAndOneCast()
        {
            var a=new GluttonyPrototypeRuntime(); var b=new GluttonyPrototypeRuntime(); var p=Enabled();
            a.TryCast(100,new uint[]{1},p,0);
            Assert.That(a.CanConsume(1,true,99,p,1),Is.False);
            Assert.That(b.CanConsume(1,true,100,p,1),Is.False);
            Assert.That(a.CanConsume(1,false,0,p,1),Is.False);
            Assert.That(a.CanConsume(1,true,100,p,1));
        }
        [Test] public void ExactExpiryRejectsFreeConsumeWithoutFallingBack()
        {
            var r=new GluttonyPrototypeRuntime(); var p=Enabled(); r.TryCast(100,new uint[]{1},p,0);
            Assert.That(r.HasMark(1,100,5.999)); Assert.That(r.CanConsume(1,true,100,p,6),Is.False);
            Assert.That(r.State.PassiveReadyAt,Is.Zero); Assert.That(r.Expire(6));
            Assert.That(r.State.Expired,Is.EqualTo(1)); Assert.That(r.Expire(6),Is.False);
        }
        [Test] public void LostAndCancelledTargetsAreCountedOnce()
        {
            var r=new GluttonyPrototypeRuntime(); var p=Enabled(); r.TryCast(100,new uint[]{1,2,3},p,0);
            Assert.That(r.ForgetKilledTarget(1)); Assert.That(r.ForgetKilledTarget(1),Is.False);
            r.CancelMarks(); r.CancelMarks();
            Assert.That(r.State.Lost,Is.EqualTo(1)); Assert.That(r.State.Cancelled,Is.EqualTo(2));
        }
        [Test] public void NewCastReplacesPreviousBatch()
        {
            var r=new GluttonyPrototypeRuntime(); var p=Enabled(); p.ActiveCooldown=.1f;
            r.TryCast(100,new uint[]{1,2},p,0); r.TryCast(101,new uint[]{3},p,1);
            Assert.That(r.HasMark(1,100,2),Is.False); Assert.That(r.HasMark(3,101,2));
            Assert.That(r.State.Marked,Is.EqualTo(1));
        }
        [Test] public void DisabledAndInvalidParametersCannotExecute()
        {
            var r=new GluttonyPrototypeRuntime(); var p=Enabled(); p.Enabled=false;
            Assert.That(r.CanCast(p,0),Is.False); Assert.That(r.CanConsume(1,false,0,p,0),Is.False);
            p=Enabled(); p.Radius=float.NaN; Assert.That(p.IsValid,Is.False);
            p=Enabled(); Assert.That(r.CanCast(p,double.NaN),Is.False);
        }
        [TestCase(2,0,true)] [TestCase(-1,0,false)] [TestCase(6,0,false)] [TestCase(2,2,false)]
        public void RectangleIsForwardOnly(float x,float y,bool expected)
        { Assert.That(GluttonyGeometry.InRectangle(new Vector2(x,y),Vector2.zero,Vector2.zero,Vector2.right,5,2,0),Is.EqualTo(expected)); }
        [Test] public void CircleUsesHurtboxEdgeNotOnlyCenter()
        {
            Assert.That(GluttonyGeometry.InCircle(new Vector2(2,0),Vector2.one,Vector2.zero,1));
            Assert.That(GluttonyGeometry.InCircle(new Vector2(2.1f,0),Vector2.one,Vector2.zero,1),Is.False);
        }
        private static GluttonyReplica Reply(uint revision, uint[] targets, int collected = 0) =>
            new GluttonyReplica { State = new GluttonySnapshot { Revision = revision, CastId = 100,
                MarkExpiresAt = 6, ActiveReadyAt = 20, PassiveReadyAt = 10, Collected = collected }, Targets = targets };
        [Test] public void MarkReceiptContainsMembershipBeforeReplicationArrives()
        {
            var view = GluttonyReplica.Latest(default, Reply(2, new uint[] {20, 30}));
            Assert.That(view.HasMark(20, 1)); Assert.That(view.HasMark(30, 1));
            Assert.That(view.State.ActiveReadyAt, Is.EqualTo(20));
        }
        [Test] public void StaleReplicationCannotRestoreAConsumedMark()
        {
            var view = GluttonyReplica.Latest(Reply(2, new uint[] {20}), Reply(3, Array.Empty<uint>(), 1));
            Assert.That(view.HasMark(20, 1), Is.False);
            Assert.That(view.State.Collected, Is.EqualTo(1));
            Assert.That(view.State.PassiveReadyAt, Is.EqualTo(10));
        }
        [Test] public void NewerReplicationWinsOverAnOlderReceipt()
        {
            var view = GluttonyReplica.Latest(Reply(4, new uint[] {30}), Reply(3, new uint[] {20}));
            Assert.That(view.HasMark(20, 1), Is.False); Assert.That(view.HasMark(30, 1));
        }
        [Test] public void ReplicaRevisionHandlesWrapWithoutAcceptingZero()
        {
            Assert.That(GluttonyReplica.IsNewer(1, uint.MaxValue));
            Assert.That(GluttonyReplica.IsNewer(uint.MaxValue, 1), Is.False);
            Assert.That(GluttonyReplica.IsNewer(0, uint.MaxValue), Is.False);
        }
        [Test] public void ReceiptDoesNotExtendExpiredMarks()
        {
            var view = Reply(2, new uint[] {20});
            Assert.That(view.HasMark(20, 5.999)); Assert.That(view.HasMark(20, 6), Is.False);
            Assert.That(view.HasMark(20, double.NaN), Is.False);
        }
        [Test] public void CompetingPlayersProduceOneCanonicalKill()
        {
            var g = World();
            g.Ledger.RegisterEntity(11, 100, CombatEntityKind.Player, CombatEntityAuthority.OwnerFinal, 11);
            g.Ledger.RegisterSource(11, 11); g.RegisterClientIdentity(11, 13, 1);
            var a = new SequentialCombatEventIdSource(12, 1); var b = new SequentialCombatEventIdSource(13, 1);
            Assert.That(g.ProcessGluttonyDevour(10, 10, 20, a.Next().Value, 1, out _).Accepted);
            Assert.That(g.ProcessGluttonyDevour(11, 11, 20, b.Next().Value, 1, out _).Rejection,
                Is.EqualTo(CombatRejectionReason.TargetCanonicalDead));
            Assert.That(g.Metrics.ConfirmedKills, Is.EqualTo(1));
        }
        private static ServerCombatGateway World(int hp=100)
        {
            var g=new ServerCombatGateway();
            g.Ledger.RegisterEntity(10,100,CombatEntityKind.Player,CombatEntityAuthority.OwnerFinal,10);
            g.Ledger.RegisterSource(10,10); g.RegisterClientIdentity(10,12,1);
            g.Ledger.RegisterEntity(20,hp,CombatEntityKind.Enemy,CombatEntityAuthority.ServerCanonical);
            return g;
        }
        [Test] public void CanonicalDevourConfirmsOnceAndDoesNotUseDamageCap()
        {
            var g=World(int.MaxValue); var ids=new SequentialCombatEventIdSource(12,1); ulong id=ids.Next().Value;
            int kills=0; g.ConfirmedKillProduced+=_=>kills++;
            var first=g.ProcessGluttonyDevour(10,10,20,id,0,out var batch);
            Assert.That(first.Accepted); Assert.That(first.State.Alive,Is.False);
            Assert.That(batch.ConfirmedKills.Length,Is.EqualTo(1)); Assert.That(batch.EnemyHitPresentations,Is.Empty);
            var duplicate=g.ProcessGluttonyDevour(10,10,20,id,.01,out _);
            Assert.That(duplicate.Rejection,Is.EqualTo(CombatRejectionReason.DuplicateEvent));
            var another=g.ProcessGluttonyDevour(10,10,20,ids.Next().Value,.02,out _);
            Assert.That(another.Rejection,Is.EqualTo(CombatRejectionReason.TargetCanonicalDead)); Assert.That(kills,Is.EqualTo(1));
        }
        [Test] public void CanonicalDevourRejectsImmunityAndWrongSource()
        {
            var g=World(); var ids=new SequentialCombatEventIdSource(12,1);
            g.Ledger.SetAbsoluteInvulnerable(20,true);
            Assert.That(g.ProcessGluttonyDevour(10,10,20,ids.Next().Value,0,out _).Accepted,Is.False);
            g.Ledger.SetAbsoluteInvulnerable(20,false);
            Assert.That(g.ProcessGluttonyDevour(10,99,20,ids.Next().Value,0,out _).Accepted,Is.False);
            Assert.That(g.Ledger.IsAlive(20));
        }
        [Test] public void CanonicalDevourRejectsStoppedRun()
        {
            var g=World(); var ids=new SequentialCombatEventIdSource(12,1); g.StopCombat();
            Assert.That(g.ProcessGluttonyDevour(10,10,20,ids.Next().Value,0,out _).Accepted,Is.False);
            Assert.That(g.Ledger.IsAlive(20));
        }
    }
}
