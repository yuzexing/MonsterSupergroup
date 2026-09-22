using System;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;

namespace MonsterSupergroup.NetworkCombat
{
    // Explicit opt-in, one bounded capture. Ordinary diagnostics never enable the Profiler.
    public sealed class NetworkDiagnosticsProfilerCapture : MonoBehaviour
    {
        private double duration, until;
        private bool started, done, previousEnabled, previousBinary;
        private string previousFile;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            foreach(string arg in Environment.GetCommandLineArgs())
                if(arg.StartsWith("--network-profiler-seconds=", StringComparison.Ordinal) &&
                   int.TryParse(arg.Substring("--network-profiler-seconds=".Length), out int seconds) && seconds > 0 && seconds <= 60)
                {
                    if (!Debug.isDebugBuild) { Debug.LogWarning("[NetworkDiagnostics] Short Profiler capture requires a Development Player."); return; }
                    var root = new GameObject("Short diagnostics Profiler capture"); DontDestroyOnLoad(root);
                    root.AddComponent<NetworkDiagnosticsProfilerCapture>().duration = seconds; return;
                }
        }
        private void LateUpdate()
        {
            if(done) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if(!started && now >= 60 && Time.unscaledDeltaTime > .1f)
            {
                // Never take over an existing profiler session.
                if(Profiler.enabled) { Debug.LogWarning("[NetworkDiagnostics] Profiler already enabled; automatic capture skipped."); done=true; return; }
                string directory = Path.Combine(Application.persistentDataPath, "NetworkDiagnostics");
                foreach(string arg in Environment.GetCommandLineArgs())
                    if(arg.StartsWith("--network-diagnostics-output=", StringComparison.Ordinal)) directory=arg.Substring("--network-diagnostics-output=".Length);
                Directory.CreateDirectory(directory);
                previousEnabled=Profiler.enabled; previousBinary=Profiler.enableBinaryLog; previousFile=Profiler.logFile;
                string path=Path.Combine(directory, "profile-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".raw");
                Profiler.logFile=path; Profiler.enableBinaryLog=true; Profiler.enabled=true;
                started=true; until=now+duration;
                Debug.Log("[NetworkDiagnostics] Short Profiler capture started: " + path);
            }
            if(started && now >= until) Stop();
        }
        private void Stop()
        {
            if(done || !started) return;
            Profiler.enabled=previousEnabled; Profiler.enableBinaryLog=previousBinary; Profiler.logFile=previousFile;
            done=true; Debug.Log("[NetworkDiagnostics] Short Profiler capture finished.");
        }
        private void OnApplicationQuit() => Stop();
        private void OnDestroy() => Stop();
    }
}
