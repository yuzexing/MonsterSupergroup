using System;
using System.Collections.Generic;
using System.Linq;
using MonsterSupergroup.GAS;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class CombatEvidenceIntegrityTests
    {
        [TearDown] public void ResetSink() => CombatEvidence.Sink = null;

        [Test] public void FailedCaptureUsesIndependentIntegrityChannelAndRetainsIdentity()
        {
            var sink = new IntegritySink { throwWrites = true }; CombatEvidence.Sink = sink;
            var original = new DiagnosticRecord { stage = "stats.damage", engine = "output-3", role = "Owner", operation = "ApplyDamage",
                eventId = "17", rootEventId = "11", parentEventId = "15", source = 2, target = 7, stateVersion = 9,
                input = new object(), before = new object(), after = new object(), inputRef = "stale", critical = false };
            Assert.That(CombatEvidence.Write(original), Is.False);
            Assert.That(sink.writes, Is.EqualTo(1));
            var gap = sink.failures.Single();
            Assert.That(gap.stage, Is.EqualTo("evidence.gap")); Assert.That(gap.outcome, Is.EqualTo("CaptureFailed"));
            Assert.That(gap.reason, Is.EqualTo("stats.damage:InvalidOperationException"));
            Assert.That(gap.engine, Is.EqualTo("output-3")); Assert.That(gap.operation, Is.EqualTo("ApplyDamage"));
            Assert.That(gap.eventId, Is.EqualTo("17")); Assert.That(gap.rootEventId, Is.EqualTo("11")); Assert.That(gap.parentEventId, Is.EqualTo("15"));
            Assert.That(gap.source, Is.EqualTo(2)); Assert.That(gap.target, Is.EqualTo(7)); Assert.That(gap.stateVersion, Is.EqualTo(9));
            Assert.That(gap.input, Is.Null); Assert.That(gap.before, Is.Null); Assert.That(gap.after, Is.Null); Assert.That(gap.inputRef, Is.Null);
            Assert.That(gap.critical, Is.True); Assert.That(original.stage, Is.EqualTo("stats.damage"));
        }

        [Test] public void OldSinkReceivesFallbackGapAndBrokenIntegritySinkCannotThrowOrRecurse()
        {
            var old = new LegacySink(); CombatEvidence.Sink = old;
            CombatEvidence.ReportCaptureFailure(new DiagnosticRecord { stage = "owner.attack_stats" }, new ArgumentException());
            Assert.That(old.records.Single().reason, Is.EqualTo("owner.attack_stats:ArgumentException"));
            var broken = new IntegritySink { throwWrites = true, throwIntegrity = true }; CombatEvidence.Sink = broken;
            Assert.DoesNotThrow(() => CombatEvidence.Write(new DiagnosticRecord { stage = "stats.damage" }));
            Assert.That(broken.writes, Is.EqualTo(1)); Assert.That(broken.integrityCalls, Is.EqualTo(1));
        }

        [Test] public void UnknownEngineRegistrationFailureRemainsConservativeAndDoesNotSuppressLaterCalls()
        {
            var sink = new IntegritySink { throwRegistration = true }; CombatEvidence.Sink = sink;
            var engine = new object();
            using (CombatEvidence.Begin(engine, "gateway", "StopCombat", Array.Empty<object>(), _ => new object())) { }
            var gap = sink.failures.Single();
            Assert.That(gap.engine, Is.Null); Assert.That(gap.reason, Is.EqualTo("replay.engine_checkpoint:InvalidOperationException"));
            Assert.That(CombatEvidence.CurrentEngine, Is.Null);
            sink.throwRegistration = false;
            using (var operation = CombatEvidence.Begin(engine, "gateway", "StopCombat", Array.Empty<object>(), _ => new object())) operation.Complete();
            Assert.That(sink.records.Select(r => r.stage), Is.EqualTo(new[] { "replay.input", "replay.output" }));
            Assert.That(CombatEvidence.CurrentEngine, Is.Null);
        }

        [Test] public void FailedCompactInputAndCompletionRemainReportedWithoutChangingStatusAdvance()
        {
            var sink = new IntegritySink { throwAdvances = true }; CombatEvidence.Sink = sink;
            var status = new StatusController(_ => { });
            Assert.DoesNotThrow(() => status.Advance(.125f));
            Assert.That(sink.failures.Select(r => r.reason), Is.EqualTo(new[] { "replay.input:InvalidOperationException", "replay.output:InvalidOperationException" }));
            Assert.That(sink.failures.All(r => r.engine == "status-1" && r.operation == "Advance"), Is.True);
            Assert.That(CombatEvidence.CurrentEngine, Is.Null);
        }

        [Test] public void ExplicitFailureRetainsTheDamageContextAndSuppressionPreventsRecursiveEvidence()
        {
            var sink = new IntegritySink(); CombatEvidence.Sink = sink;
            var context = CombatContext.CreateRoot(CombatEventId.Compose(2, 4, 5), 2, 3, 7, CombatTags.Attack)
                .CreateChild(CombatEventId.Compose(2, 4, 6), CombatTags.Damage, 19, 11);
            CombatEvidence.ReportCaptureFailure("Owner", "owner.damage_calculation", new ArgumentException(), context, "damage-1");
            var gap = sink.failures.Single();
            Assert.That(gap.eventId, Is.EqualTo(context.EventId.Value.ToString()));
            Assert.That(gap.rootEventId, Is.EqualTo(context.RootEventId.Value.ToString()));
            Assert.That(gap.parentEventId, Is.EqualTo(context.ParentEventId.Value.ToString()));
            Assert.That(gap.source, Is.EqualTo(2)); Assert.That(gap.target, Is.EqualTo(19)); Assert.That(gap.engine, Is.EqualTo("damage-1"));
            using (CombatEvidence.Suppress()) CombatEvidence.ReportCaptureFailure("Owner", "owner.damage_calculation", new Exception(), context);
            Assert.That(sink.failures, Has.Count.EqualTo(1));
        }

        private sealed class LegacySink : IDiagnosticSink
        {
            public readonly List<DiagnosticRecord> records = new();
            public bool TryWrite(DiagnosticRecord record) { records.Add(record); return true; }
            public string RegisterEngine(object engine, string domain, Func<object, object> capture) => domain + "-1";
        }
        private sealed class IntegritySink : IDiagnosticSink, IDiagnosticIntegritySink, IDiagnosticAdvanceSink
        {
            public bool throwWrites, throwIntegrity, throwRegistration, throwAdvances;
            public int writes, integrityCalls;
            public readonly List<DiagnosticRecord> records = new(), failures = new();
            public bool TryWrite(DiagnosticRecord record)
            {
                writes++; if (throwWrites) throw new InvalidOperationException("injected capture failure");
                records.Add(record); return true;
            }
            public string RegisterEngine(object engine, string domain, Func<object, object> capture)
            { if (throwRegistration) throw new InvalidOperationException("injected registration failure"); return domain + "-1"; }
            public bool TryWriteAdvance(string role, string engine, string operation, float delta, StatusReplayBoundary boundary, int phase)
            { if (throwAdvances) throw new InvalidOperationException("injected advance failure"); return true; }
            public void ReportCaptureFailure(DiagnosticRecord record)
            {
                integrityCalls++;
                if (throwIntegrity) { CombatEvidence.ReportCaptureFailure(record, new Exception()); throw new InvalidOperationException("injected integrity failure"); }
                failures.Add(record);
            }
        }
    }
}
