using System;
using System.IO;
using System.Reflection;
using System.Threading;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class AutomaticCaptureSessionTests
    {
        private string root;
        [SetUp] public void Setup() { root = Path.Combine(Path.GetTempPath(), "auto-capture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
        [TearDown] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }
        [Test] public void PrefersGameDirectoryAndLeavesNoProbe()
        {
            string target = AutomaticCaptureSession.CreateDirectory(Path.Combine(root, "game"), Path.Combine(root, "fallback"), "one", 60, out string reason);
            Assert.That(target, Is.EqualTo(Path.Combine(root, "game", "one")));
            Assert.That(reason, Is.Null); Assert.That(Directory.GetFiles(target), Is.Empty);
        }
        [Test] public void TooLongPrimaryFallsBackBeforeCreatingPartialDirectory()
        {
            string target = AutomaticCaptureSession.CreateDirectory(Path.Combine(root, new string('x', 190)), Path.Combine(root, "fallback"), "one", 60, out string reason);
            Assert.That(target, Is.EqualTo(Path.Combine(root, "fallback", "one")));
            Assert.That(reason, Does.Contain("PathTooLong"));
        }
        [Test] public void ExistingSessionIsNeverOverwritten()
        {
            string primary = Path.Combine(root, "game"); Directory.CreateDirectory(Path.Combine(primary, "one"));
            string original = Path.Combine(primary, "one", "original.txt"); File.WriteAllText(original, "keep");
            string target = AutomaticCaptureSession.CreateDirectory(primary, Path.Combine(root, "fallback"), "one", 60, out string reason);
            Assert.That(target, Does.Contain("fallback")); Assert.That(reason, Is.Not.Null);
            Assert.That(File.ReadAllText(original), Is.EqualTo("keep"));
        }
        [Test] public void CombatPathReserveRejectsDirectoryThatOnlyFitsMenuRecords()
        {
            string primary = Path.Combine(root, new string('x', Math.Max(1, 96 - root.Length)));
            string fallback = Path.Combine(Path.GetTempPath(), "ce-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            try
            {
                string target = AutomaticCaptureSession.CreateDirectory(primary, fallback, "s", 180, out string reason);
                Assert.That(target, Does.StartWith(fallback)); Assert.That(reason, Does.Contain("PathTooLong"));
                Assert.That(target.Length + 177, Is.LessThan(260));
            }
            finally { if (Directory.Exists(fallback)) Directory.Delete(fallback, true); }
        }
        [Test] public void FinalStatusWaitingForDiskDoesNotLockUiState()
        {
            var owner = new GameObject("automatic capture test");
            var session = owner.AddComponent<AutomaticCaptureSession>();
            var type = typeof(AutomaticCaptureSession);
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var singleton = type.GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);
            object previous = singleton.GetValue(null);
            type.GetField("root", flags).SetValue(session, root);
            type.GetField("source", flags).SetValue(session, "network");
            type.GetField("mode", flags).SetValue(session, "Network");
            singleton.SetValue(null, session);
            object fileGate = type.GetField("fileGate", flags).GetValue(session);
            var returned = new ManualResetEventSlim();
            var finalizer = new Thread(() => AutomaticCaptureSession.ReportClosed("network", true));
            bool responsive = false;
            Monitor.Enter(fileGate);
            try
            {
                finalizer.Start();
                var observer = new Thread(() => { _ = AutomaticCaptureSession.HasFailure; AutomaticCaptureSession.ReportSaving("network", 10); returned.Set(); });
                observer.Start(); responsive = returned.Wait(1000);
            }
            finally
            {
                Monitor.Exit(fileGate); finalizer.Join(2000);
                singleton.SetValue(null, previous); UnityEngine.Object.DestroyImmediate(owner);
            }
            Assert.That(responsive, Is.True, "UI must remain responsive while the finalizer waits for disk.");
            Assert.That(File.Exists(Path.Combine(root, "session.json")), Is.True);
        }
    }
}
