using System.Linq;
using MonsterSupergroup.NetworkCombat.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Mirror;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class LimboFullTests
    {
        [Test]
        public void FullBoundaryMustWaitForParticipantDecisionInsteadOfCompleting()
        {
            var rules = AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot + "/Full.asset");
            Assert.That(rules.TryCapture(out var settings, out var error), Is.True, error);
            settings.Reference.FlowReadiness = ReferenceEnemyReadiness.Ready;
            settings.Reference.FlowReadinessNote = "";
            var schedule = new ServerWaveSchedule("boundary-repro", settings, 0);
            schedule.TickReference(721, 1, true, 3, 3, new int[31], out _);
            Assert.That(schedule.State.Phase, Is.Not.EqualTo(WavePhase.Completed),
                "A timestamp alone must not trigger CompleteReferenceStage/ServerCancelPending before the server checks participants.");
        }
        private static WaveParameters Captured(bool preview = false)
        {
            var r = AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot + "/Full.asset");
            Assert.That(r.TryCapture(out var p, out var error), Is.True, error);
            p.Reference.FlowReadiness = ReferenceEnemyReadiness.Ready; p.Reference.FlowReadinessNote = "";
            p.Reference.EndPolicy = preview ? ReferenceEndPolicy.ImmediatePreview : ReferenceEndPolicy.WaitForParticipants;
            return p;
        }
        [Test] public void All31ClipsAndBudgetsKeepIndependentCountSemantics()
        {
            var p = Captured(); var c = p.Reference.Clips;
            Assert.That(c.Length, Is.EqualTo(31)); Assert.That(p.Reference.EndTime, Is.EqualTo(720.9));
            Assert.That(p.Reference.SourceDuration, Is.EqualTo(841.5766649882));
            Assert.That(c.Count(x => x.Mode == ReferenceSpawnMode.CurveBudget), Is.EqualTo(14));
            Assert.That(c.Count(x => x.Mode == ReferenceSpawnMode.AliveTarget), Is.EqualTo(10));
            Assert.That(c.Count(x => x.Mode == ReferenceSpawnMode.FormationBurst), Is.EqualTo(7));
            Assert.That(c.Where(x => x.Mode == ReferenceSpawnMode.CurveBudget).Sum(x => x.Count), Is.EqualTo(1139));
            Assert.That(c.Where(x => x.Mode == ReferenceSpawnMode.FormationBurst).Sum(x => x.Count), Is.EqualTo(60));
            Assert.That(p.Reference.Barriers[1].start, Is.EqualTo(599.9666666666667));
            Assert.That(c[22].Stats.BaseHealth, Is.EqualTo(2600)); Assert.That(c[30].Count, Is.EqualTo(10));
        }
        [Test] public void RequestSurvivesPauseAndCompletesOnlyOnceWithSameRun()
        {
            var s = new ServerWaveSchedule("run", Captured(), 0); var alive = new int[31];
            s.TickReference(721, 1, true, 3, 3, alive, out _);
            Assert.That(s.State.TransitionRequestedAt, Is.EqualTo(721));
            s.SetReferenceWait(1, "selection");
            s.TickReference(800, 2, false, 3, 3, alive, out _);
            Assert.That(s.State.Phase, Is.EqualTo(WavePhase.TransitionPending)); Assert.That(s.State.Elapsed, Is.EqualTo(721));
            s.TickReference(801, 3, true, 3, 3, alive, out _);
            Assert.That(s.CompleteReferenceTransition("old"), Is.False);
            Assert.That(s.CompleteReferenceTransition("run"), Is.True);
            Assert.That(s.CompleteReferenceTransition("run"), Is.False);
            Assert.That(s.State.Alive, Is.EqualTo(3), "Completion never waits for a clear field.");
            Assert.That(s.State.TransitionWaitCount, Is.Zero);
        }
        [Test] public void PendingWaitFinishesAnExistingCurveWithoutReplayingOtherClips()
        {
            var p = Captured(); var s = new ServerWaveSchedule("run", p, 0); var alive = new int[31];
            s.TickReference(1.01, 1, true, 0, 0, alive, out _); // Start only opening C.
            s.TickReference(721, 2, true, 0, 0, alive, out _);
            Assert.That(s.TickReference(721.1, 3, true, 0, 0, alive, out var birth), Is.True);
            Assert.That(birth.ClipIndex, Is.Zero); s.Resolve(birth, true);
            Assert.That(s.State.Phase, Is.EqualTo(WavePhase.TransitionPending));
            Assert.That(s.State.ActiveClips, Is.EqualTo(1));
        }
        [Test] public void OldPreviewStillClampsAndCompletesWithoutARequest()
        {
            var s = new ServerWaveSchedule("run", Captured(true), 0);
            s.TickReference(721, 1, true, 3, 3, new int[31], out _);
            Assert.That(s.State.Phase, Is.EqualTo(WavePhase.Completed)); Assert.That(s.State.Elapsed, Is.EqualTo(720.9));
            Assert.That(s.State.TransitionRequestedAt, Is.Zero);
        }
        [Test] public void BusyDeadDisconnectedAndRestoringMembersHaveDistinctOutcomes()
        {
            var session = new RunSession(); var ledger = new CombatLedger();
            session.TryConnect("host", 0, out _, out _); session.AttachAvatar(0, 10, 1);
            session.TryConnect("client", 1, out _, out _); session.AttachAvatar(1, 11, 1);
            ledger.RegisterEntity(10, 500, CombatEntityKind.Player, CombatEntityAuthority.OwnerFinal, 10);
            ledger.RegisterEntity(11, 500, CombatEntityKind.Player, CombatEntityAuthority.OwnerFinal, 11);
            session.BeginRun();
            var status = session.EvaluateReferenceTransition(ledger, id => id == 11 ? ReferenceParticipantReadiness.Selecting : ReferenceParticipantReadiness.Ready);
            Assert.That(status.CanComplete, Is.False); Assert.That(status.Selecting, Is.EqualTo(1));
            session.Disconnect(1, null);
            Assert.That(session.EvaluateReferenceTransition(ledger, _ => ReferenceParticipantReadiness.Ready).CanComplete, Is.True);
            session.TryConnect("client", 2, out _, out _);
            Assert.That(session.EvaluateReferenceTransition(ledger, _ => ReferenceParticipantReadiness.Ready).Unready, Is.EqualTo(1));
            session.AttachAvatar(2, 12, 2);
            Assert.That(session.EvaluateReferenceTransition(ledger, _ => ReferenceParticipantReadiness.Ready).CanComplete, Is.False);
            session.Disconnect(2, null); session.Disconnect(0, null);
            Assert.That(session.EvaluateReferenceTransition(ledger, _ => ReferenceParticipantReadiness.Ready).CanComplete, Is.False);
        }
        [Test] public void SnapshotProtocolRetainsOldEnumValuesAndWaitingReason()
        {
            Assert.That((int)WavePhase.Completed, Is.EqualTo(5)); Assert.That((int)WavePhase.TransitionPending, Is.EqualTo(6));
            var state = new WaveProgressSnapshot { RunId = "run", Phase = WavePhase.TransitionPending,
                TransitionRequestedAt = 720.915, TransitionWaitCount = 2, TransitionWaitReason = "选卡 / 恢复" };
            using (var writer = NetworkWriterPool.Get())
            {
                writer.Write(state); var read = new NetworkReader(writer.ToArraySegment()).Read<WaveProgressSnapshot>();
                Assert.That(read.TransitionRequestedAt, Is.EqualTo(state.TransitionRequestedAt));
                Assert.That(read.TransitionWaitCount, Is.EqualTo(2)); Assert.That(read.TransitionWaitReason, Is.EqualTo(state.TransitionWaitReason));
            }
        }
        [Test] public void AllConnectedDownedWinsAndCannotAlsoCompleteReferenceFlow()
        {
            var session = new RunSession(); var ledger = new CombatLedger();
            session.TryConnect("host", 0, out _, out _); session.AttachAvatar(0, 10, 1);
            ledger.RegisterEntity(10, 500, CombatEntityKind.Player, CombatEntityAuthority.OwnerFinal, 10);
            session.BeginRun();
            ledger.TryGetState(10, out var dead); dead.Health = 0; dead.Alive = false;
            ledger.RestoreEntityState(10, new ServerEntityCheckpoint(dead, false));
            Assert.That(session.EvaluateReferenceTransition(ledger, _ => ReferenceParticipantReadiness.Ready).CanComplete, Is.False);
            Assert.That(session.TryEndRun(ledger), Is.True);
            Assert.That(session.TryCompleteStage(), Is.False);
            Assert.That(session.TryEndRun(ledger), Is.False);
        }
        [Test] public void FlowGateCheckCannotDemoteAnAcceptedProgram()
        {
            var p = Captured();
            LimboFullAssets.VerifyIndependentFlowGate(p.Reference);
            Assert.That(p.Reference.FlowReadiness, Is.EqualTo(ReferenceEnemyReadiness.Ready));
            Assert.That(p.Reference.ReadinessError(), Is.Null);
        }
        [Test] public void StoppedOldRunCannotCompleteAndNewRunHasNoWaitingState()
        {
            var old = new ServerWaveSchedule("old", Captured(), 0);
            old.TickReference(721, 1, true, 9, 9, new int[31], out _);
            old.Stop();
            Assert.That(old.CompleteReferenceTransition("old"), Is.False);
            var current = new ServerWaveSchedule("new", Captured(), 900);
            Assert.That(current.State.TransitionRequestedAt, Is.Zero);
            Assert.That(current.State.TransitionWaitCount, Is.Zero);
            Assert.That(current.CompleteReferenceTransition("old"), Is.False);
        }
        [Test] public void StopDisableAndDestroyCannotPublishThreeCancellations()
        {
            var go = new GameObject("Reference cancel lifecycle");
            try
            {
                var spawner = go.AddComponent<NetworkGameplayEnemySpawner>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(NetworkGameplayEnemySpawner).GetField("referenceRequestPublished", flags).SetValue(spawner, true);
                typeof(NetworkGameplayEnemySpawner).GetField("boundRunId", flags).SetValue(spawner, "cancel-test");
                var cancel = typeof(NetworkGameplayEnemySpawner).GetMethod("EndReferenceTrace", flags);
                UnityEngine.TestTools.LogAssert.Expect(LogType.Log, "[LimboTransition] cancelled run=cancel-test elapsed=");
                cancel.Invoke(spawner, null); cancel.Invoke(spawner, null); cancel.Invoke(spawner, null);
                UnityEngine.TestTools.LogAssert.NoUnexpectedReceived();
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
