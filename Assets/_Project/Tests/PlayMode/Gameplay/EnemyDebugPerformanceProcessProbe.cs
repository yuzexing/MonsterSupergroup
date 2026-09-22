#if UNITY_EDITOR || MONSTER_MENU_VALIDATION
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat;
using Unity.Profiling;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Tests
{
    // Opt-in graphical Player fixture. Uses the production panel and real spawned agents;
    // extra agents have no AI/art so the experiment isolates the debug UI's enemy-count cost.
    public sealed class EnemyDebugPerformanceProcessProbe : MonoBehaviour
    {
        private string output;
        private bool finished;
        private EnemyDefinitionRuntimeFixture fixture;
        private double deadline;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string arg=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("--enemy-debug-benchmark="));
            if(arg==null)return;
            var root=new GameObject("Enemy debug performance validation"); DontDestroyOnLoad(root);
            root.AddComponent<EnemyDebugPerformanceProcessProbe>().output=arg.Substring("--enemy-debug-benchmark=".Length);
        }
        private IEnumerator Start()
        {
            Directory.CreateDirectory(output); deadline=Time.realtimeSinceStartupAsDouble+180;
            Application.runInBackground=true; QualitySettings.vSyncCount=0; Application.targetFrameRate=-1;
            Application.logMessageReceived+=Observe;
            var work=Run();
            while(!finished)
            {
                bool next; object current=null;
                try {next=work.MoveNext();if(next)current=work.Current;}
                catch(Exception e){Fail(e.ToString());yield break;}
                if(!next)break;yield return current;
            }
        }
        private void Update(){if(!finished && deadline>0 && Time.realtimeSinceStartupAsDouble>deadline)Fail("Benchmark timed out");}
        private void Observe(string text,string stack,LogType type){if(type==LogType.Exception)Fail(text+"\n"+stack);}
        private void Fail(string text){if(finished)return;finished=true;File.WriteAllText(Path.Combine(output,"failure.txt"),text);Application.Quit(1);}
        private IEnumerator Run()
        {
            var manager=FindFirstObjectByType<BootGameplayNetworkManager>();manager.ConfigurePreparationFlow(false);
            fixture=new EnemyDefinitionRuntimeFixture(manager,false);
            gameObject.AddComponent<WeaponAttackAdmissionFixtureGate>();
            if(!manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1",7989,false,out string error))throw new Exception(error);
            manager.StartHost();
            var g=GluttonyParameters.Defaults;g.Enabled=false;NetworkCombatWorld.Instance.ServerConfigureGluttony(g,true);
            while(NetworkClient.localPlayer==null || !manager.CanBeginRun(out _))yield return null;
            manager.BeginRun();while(!fixture.PairReady() || !fixture.PlaceInView())yield return null;
            var panel=FindFirstObjectByType<NetworkEnemyDebugPanel>();
            if(panel==null)throw new Exception("Production debug panel missing");
            panel.SetExpanded(false);
            var states=new CanonicalEntityState[400];
            for(int i=0;i<states.Length;i++)
            {
                var root=new GameObject("Debug benchmark enemy "+i);root.SetActive(false);
                root.AddComponent<CombatantBehaviour>().Initialize(100);
                var agent=root.AddComponent<NetworkEnemySimulationAgent>();root.SetActive(true);NetworkServer.Spawn(root);
                states[i]=new CanonicalEntityState {EntityId=agent.netId,Kind=(byte)CombatEntityKind.Enemy,Health=100,MaxHealth=100,Alive=true,StateVersion=1};
            }
            NetworkCombatWorld.Instance.Replica.Apply(new CanonicalWorldBatch {Entities=states});
            while(panel.Rows.Count<402)yield return null;
            var results=new List<Phase>();
            for(int phase=0;phase<4;phase++)
            {
                bool expanded=phase%2==1;panel.SetExpanded(expanded);yield return new WaitForSecondsRealtime(2);
                using var gui=ProfilerRecorder.StartNew(ProfilerCategory.Scripts,"EnemyDebug.OnGUI",16384);
                using var allocations=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",1);
                var samples=new List<ProfilerRecorderSample>(1);var frames=new List<double>();
                double guiSum=0,allocated=0,start=Time.realtimeSinceStartupAsDouble;
                while(Time.realtimeSinceStartupAsDouble-start<10)
                {
                    yield return null;frames.Add(Time.unscaledDeltaTime*1000d);
                    if(allocations.Valid)allocated+=allocations.LastValue;
                }
                gui.Stop();gui.CopyTo(samples,false);foreach(var sample in samples)guiSum+=sample.Value/1e6;
                frames.Sort();results.Add(new Phase {expanded=expanded,rows=panel.Rows.Count,frames=frames.Count,
                    meanMs=frames.Average(),p95Ms=frames[(int)((frames.Count-1)*.95)],maxMs=frames.Last(),
                    guiMeanMs=gui.Valid && !gui.WrappedAround?guiSum/frames.Count:-1,
                    allocatedBytesPerFrame=allocations.Valid?allocated/frames.Count:-1});
                File.WriteAllText(Path.Combine(output,"result.json"),JsonUtility.ToJson(new Report {buildGuid=Application.buildGUID,graphics=SystemInfo.graphicsDeviceType.ToString(),phases=results.ToArray()},true));
                if(phase==3){ScreenCapture.CaptureScreenshot(Path.Combine(output,"expanded.png"));yield return null;yield return null;}
            }
            panel.SetPage(panel.PageCount-1);
            yield return new WaitForSecondsRealtime(.5f);
            if(panel.LastDrawnRowCount!=2 || panel.PageIndex!=100 || panel.Rows.Count!=402)
                throw new Exception("Last page must draw its two rows without discarding any snapshots");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"last-page.png"));yield return null;yield return null;
            panel.SetPage(0);yield return new WaitForSecondsRealtime(.5f);
            if(panel.LastDrawnRowCount!=4)throw new Exception("First page must draw exactly four rows");
            panel.SetExpanded(false);yield return new WaitForSecondsRealtime(.5f);
            if(panel.LastDrawnRowCount!=0)throw new Exception("Collapsed panel rendered enemy rows");
            panel.SetExpanded(true);yield return new WaitForSecondsRealtime(.5f);
            if(panel.LastDrawnRowCount!=4)throw new Exception("Reopened panel lost its page");
            File.WriteAllText(Path.Combine(output,"paging-passed.txt"),"First/last page, collapsed/reopened, 402 retained snapshots: PASS");
            manager.StopHost();while(manager.IsGameplayLoaded || manager.IsGameplayTransitioning)yield return null;
            fixture.Dispose();fixture=null;finished=true;Application.logMessageReceived-=Observe;Application.Quit(0);
        }
        [Serializable]private class Report{public string buildGuid,graphics;public Phase[] phases;}
        [Serializable]private class Phase{public bool expanded;public int rows,frames;public double meanMs,p95Ms,maxMs,guiMeanMs,allocatedBytesPerFrame;}
        private void OnDestroy(){Application.logMessageReceived-=Observe;fixture?.Dispose();}
    }
}
#endif
