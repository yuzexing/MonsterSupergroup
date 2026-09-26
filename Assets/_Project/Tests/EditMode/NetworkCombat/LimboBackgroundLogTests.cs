using System;
using System.IO;
using System.Text;
using System.Threading;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class LimboBackgroundLogTests
    {
        private sealed class BlockedStream : MemoryStream
        {
            public readonly ManualResetEventSlim Entered = new(), Release = new();
            public override void Write(byte[] buffer, int offset, int count)
            { Entered.Set(); Release.Wait(); base.Write(buffer, offset, count); }
        }
        [Test]
        public void BlockedDiskDoesNotBlockGameplayAndOverflowIsExplicit()
        {
            var stream = new BlockedStream();
            var log = new LimboObservationLog(stream, () => 10);
            try
            {
                log.WriteLine(new string('a', 20000));
                Assert.That(stream.Entered.Wait(2000), Is.True);
                // Disk is still blocked: all calls below must return without waiting for it.
                for (int i = 0; i < 250; i++) log.WriteLine(new string('b', 10000));
                Assert.That(log.Failure, Does.Contain("4 MiB"));
                Assert.That(LimboObservationLog.PendingBytes, Is.LessThanOrEqualTo(LimboObservationLog.MaximumPendingBytes));
            }
            finally { stream.Release.Set(); log.Dispose(); }
            Assert.That(log.IsComplete, Is.False);
        }
        [Test]
        public void FileStatusStaysIncompleteUntilTheTailIsClosed()
        {
            string path = Path.Combine(Path.GetTempPath(), "limbo-background-" + Guid.NewGuid() + ".jsonl");
            try
            {
                using (var log = new LimboObservationLog(path))
                {
                    log.WriteLine("first"); log.Flush();
                    Assert.That(File.ReadAllText(path + ".status.json"), Does.Contain("\"complete\":false"));
                    log.WriteLine("last");
                }
                Assert.That(File.ReadAllLines(path), Is.EqualTo(new[] { "first", "last" }));
                Assert.That(File.ReadAllText(path + ".status.json"), Does.Contain("\"complete\":true"));
            }
            finally { File.Delete(path); File.Delete(path + ".status.json"); }
        }
        private sealed class BrokenStream : MemoryStream
        { public override void Write(byte[] buffer, int offset, int count) => throw new IOException("simulated disk failure"); }
        [Test]
        public void WriterFailureIsReportedAndNeverMarkedComplete()
        {
            var log = new LimboObservationLog(new BrokenStream(), () => 0);
            log.WriteLine("event"); log.Flush(); log.Dispose();
            Assert.That(log.Failure, Does.Contain("simulated disk failure"));
            Assert.That(log.IsComplete, Is.False);
        }
        [Test]
        public void AsyncReadinessAndCloseWaitForActualFlushWithoutBlockingCaller()
        {
            var stream = new BlockedStream();
            var log = new LimboObservationLog(stream, () => 0);
            try
            {
                log.WriteLine("header");
                log.RequestReadyCheck();
                Assert.That(stream.Entered.Wait(2000), Is.True);
                Assert.That(log.IsReady, Is.False, "An enqueued header is not a persisted header.");
                log.BeginClose();
                Assert.That(log.CloseCompleted, Is.False);
                Assert.That(log.IsComplete, Is.False, "Requesting close is not successful completion.");
                stream.Release.Set();
                Assert.That(SpinWait.SpinUntil(() => log.CloseCompleted, 2000), Is.True);
                Assert.That(log.IsReady, Is.True);
                Assert.That(log.IsComplete, Is.True);
                Assert.That(Encoding.UTF8.GetString(stream.ToArray()).Trim(), Is.EqualTo("header"));
            }
            finally { stream.Release.Set(); log.Dispose(); }
        }
        [Test]
        public void HeaderFlushAndCloseCannotHideAnUnwritableStatusFile()
        {
            string path = Path.Combine(Path.GetTempPath(), "limbo-status-failure-" + Guid.NewGuid() + ".jsonl");
            Directory.CreateDirectory(path + ".status.json");
            try
            {
                using var log = new LimboObservationLog(path);
                log.WriteLine("header"); log.RequestReadyCheck(); log.BeginClose();
                Assert.That(SpinWait.SpinUntil(() => log.CloseCompleted, 2000), Is.True);
                Assert.That(log.Failure, Is.Not.Null);
                Assert.That(log.IsReady, Is.False);
                Assert.That(log.IsComplete, Is.False);
            }
            finally { File.Delete(path); Directory.Delete(path + ".status.json"); }
        }
    }
}
