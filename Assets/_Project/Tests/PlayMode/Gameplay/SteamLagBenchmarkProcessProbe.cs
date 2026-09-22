#if UNITY_EDITOR || MONSTER_MENU_VALIDATION
using System;
using System.Collections;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.NetworkCombat;
using Unity.Profiling;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class SteamLagBenchmarkProcessProbe : MonoBehaviour
    {
        private string output;
        private bool enabledCapture, finished;
        private double deadline;
        private EnemyDefinitionRuntimeFixture fixture;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string mode=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("--lag-benchmark="));
            if(mode==null) return;
            var root=new GameObject("Steam diagnostics overhead benchmark"); DontDestroyOnLoad(root);
            var probe=root.AddComponent<SteamLagBenchmarkProcessProbe>(); probe.enabledCapture=mode.EndsWith("=on");
            probe.output=Environment.GetCommandLineArgs().First(a=>a.StartsWith("--lag-output=")).Substring("--lag-output=".Length);
        }
        private IEnumerator Start()
        {
            deadline=Time.realtimeSinceStartupAsDouble+100;
            Application.runInBackground=true; QualitySettings.vSyncCount=0;
            Application.targetFrameRate=Array.IndexOf(Environment.GetCommandLineArgs(),"--lag-uncapped") >= 0 ? -1 : 144;
            Application.logMessageReceived+=Observe;
            var task=Run();
            while(!finished)
            {
                bool next; object current=null;
                try { next=task.MoveNext(); if(next) current=task.Current; }
                catch(Exception e) { Fail(e.ToString()); yield break; }
                if(!next) break;
                yield return current;
            }
        }
        private void Update() { if(!finished && deadline>0 && Time.realtimeSinceStartupAsDouble>deadline) Fail("Benchmark timeout"); }
        private void Observe(string message,string stack,LogType type) { if(type==LogType.Exception) Fail(message+"\n"+stack); }
        private void Fail(string message) { if(finished)return; finished=true; Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output,"failure.txt"),message); Application.Quit(1); }
        private IEnumerator Run()
        {
            Directory.CreateDirectory(output);
            var manager=FindFirstObjectByType<BootGameplayNetworkManager>();
            manager.ConfigurePreparationFlow(false); fixture=new EnemyDefinitionRuntimeFixture(manager,false);
            gameObject.AddComponent<WeaponAttackAdmissionFixtureGate>();
            if(!manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1",7988,false,out string error)) throw new Exception(error);
            manager.StartHost();
            var g=GluttonyParameters.Defaults; g.Enabled=false; NetworkCombatWorld.Instance.ServerConfigureGluttony(g,true);
            while(NetworkClient.localPlayer==null || !manager.CanBeginRun(out _)) yield return null;
            manager.BeginRun();
            while(!fixture.PairReady() || !fixture.PlaceInView()) yield return null;
            NetworkClient.localPlayer.GetComponent<PlayerMovement>().SetDirection(Vector2.zero);
            yield return new WaitForSecondsRealtime(5);
            // No diagnostic component in the off run; always-on measurement is identical in both groups.
            GameObject observation=null;
            if(enabledCapture) {observation=new GameObject("Benchmark diagnostics on");observation.AddComponent<NetworkDiagnosticsObservation>();}
            yield return new WaitForSecondsRealtime(2);
            if(Array.IndexOf(Environment.GetCommandLineArgs(), "--lag-profiler-probe") >= 0)
            {
                // Explicit validation-only hitch: exercise the real Player trigger and file writer.
                while(Time.realtimeSinceStartupAsDouble < 61) yield return null;
                System.Threading.Thread.Sleep(150);
                yield return null;
                yield return new WaitForSecondsRealtime(4);
                string directory=Environment.GetCommandLineArgs().First(a=>a.StartsWith("--network-diagnostics-output=")).Substring("--network-diagnostics-output=".Length);
                string[] profiles=Directory.GetFiles(directory,"profile-*.raw");
                if(profiles.Length != 1 || new FileInfo(profiles[0]).Length == 0 || UnityEngine.Profiling.Profiler.enabled)
                    throw new Exception("Bounded Profiler capture did not write one nonempty file and stop.");
                File.WriteAllText(Path.Combine(output,"profiler-validation.json"),JsonUtility.ToJson(new ProfileResult {
                    path=profiles[0],bytes=new FileInfo(profiles[0]).Length,stopped=true}));
            }
            var main=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Main Thread",1);
            int frames=0; double sum=0,maximum=0,mainSum=0;
            double start=Time.realtimeSinceStartupAsDouble;
            while(Time.realtimeSinceStartupAsDouble-start<20)
            {
                yield return null;
                double ms=Time.unscaledDeltaTime*1000d; sum+=ms; maximum=Math.Max(maximum,ms);frames++;
                if(main.Valid) mainSum+=main.LastValue/1e6;
            }
            bool mainValid=main.Valid;main.Dispose();
            File.WriteAllText(Path.Combine(output,"benchmark.json"),JsonUtility.ToJson(new Result {
                diagnostics=enabledCapture,frames=frames,seconds=Time.realtimeSinceStartupAsDouble-start,meanMs=sum/frames,
                maxMs=maximum,mainMeanMs=mainValid?mainSum/frames:-1,enemies=fixture.Agents().Length,
                graphics=SystemInfo.graphicsDeviceType.ToString(),buildGuid=Application.buildGUID}));
            if(observation!=null) Destroy(observation);
            yield return null; manager.StopHost();
            while(manager.IsGameplayLoaded || manager.IsGameplayTransitioning) yield return null;
            fixture.Dispose(); fixture=null;
            finished=true; Application.logMessageReceived-=Observe; Application.Quit(0);
        }
        [Serializable] private class Result { public bool diagnostics; public int frames,enemies; public double seconds,meanMs,maxMs,mainMeanMs; public string graphics,buildGuid; }
        [Serializable] private class ProfileResult { public string path; public long bytes; public bool stopped; }
        private void OnDestroy() { Application.logMessageReceived-=Observe; fixture?.Dispose(); }
    }
}
#endif
