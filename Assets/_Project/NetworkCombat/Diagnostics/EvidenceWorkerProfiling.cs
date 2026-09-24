using System;
using System.Threading;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    // Installed only by the isolated allocation probe. No profiler dependency or work when unset.
    public static class EvidenceWorkerProfiling
    {
        public static Action<string, string> Started;
        public static Action Stopped, WorkStarted, WorkFinished;
        private static long failures;
        public static long FailureCount => Interlocked.Read(ref failures);
        public static string LastFailure { get; private set; }
        private static void Failed(Exception error) { LastFailure = error.GetType().FullName; Interlocked.Increment(ref failures); }
        internal static void Start(string role, string identity)
        { try { Started?.Invoke(role, identity); } catch (Exception error) { Failed(error); } }
        internal static void Stop() { try { Stopped?.Invoke(); } catch (Exception error) { Failed(error); } }
        internal static void BeginWork() { try { WorkStarted?.Invoke(); } catch (Exception error) { Failed(error); } }
        internal static void EndWork() { try { WorkFinished?.Invoke(); } catch (Exception error) { Failed(error); } }
    }
}
