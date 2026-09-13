using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.CameraFX;
using AstralShift.HellMaiden.Player;
using Com.LuisPedroFonseca.ProCamera2D;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Tests
{
    [DefaultExecutionOrder(500)]
    public sealed class NordicGameplayProcessProbe : MonoBehaviour
    {
        [Serializable] private sealed class Report { public bool passed; public float loadSeconds; public List<string> checks = new List<string>(); public List<string> failures = new List<string>(); public List<Performance> performance = new List<Performance>(); }
        [Serializable] private sealed class Performance { public string phase; public int target, frames, enemies; public float meanMs, p95Ms, maxMs; public long memory; }
        private string output;
        private readonly Report report = new Report();
        private PlayerMovement player;
        private NordicPlayerAnimator animator;
        private GameplayMapContext map;
        private GameplayCameraRig cameraRig;
        private Vector2 direction;
        private bool finished;
        private float deadline;
        private bool drive;
        private bool protect = true;
        private Vector2 crossStart, crossDirection, retreatStart, retreatDirection;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--nordic-gameplay-check="));
            if (arg == null) return;
            var probe = new GameObject("Nordic Gameplay acceptance").AddComponent<NordicGameplayProcessProbe>();
            probe.output = arg.Substring("--nordic-gameplay-check=".Length); DontDestroyOnLoad(probe.gameObject);
        }
        private IEnumerator Start()
        {
            Directory.CreateDirectory(output); deadline = Time.realtimeSinceStartup + 340; Application.runInBackground = true;
            var stack = new Stack<IEnumerator>(); stack.Push(Run());
            while (stack.Count > 0)
            {
                object next;
                try { if (!stack.Peek().MoveNext()) { stack.Pop(); continue; } next = stack.Peek().Current;
                    if (next is IEnumerator nested) { stack.Push(nested); continue; } }
                catch (Exception error) { report.failures.Add(error.ToString()); Debug.LogException(error); Finish(); yield break; }
                yield return next;
            }
            report.passed = true; Finish();
        }
        private void Update()
        {
            if (protect && NetworkClient.localPlayer != null && NetworkCombatWorld.Instance != null)
            {
                NetworkClient.localPlayer.GetComponent<CombatantBehaviour>().SetCanonicalInvulnerable(true);
                NetworkCombatWorld.Instance.Gateway.Ledger.SetAbsoluteInvulnerable(NetworkClient.localPlayer.netId,true);
            }
            if (!finished && deadline > 0 && Time.realtimeSinceStartup > deadline) { report.failures.Add("Timed out"); Finish(); }
        }
        private void LateUpdate() { if (drive && player != null) player.SetDirection(direction); }
        private IEnumerator Run()
        {
            var manager = FindFirstObjectByType<BootGameplayNetworkManager>();
            Check(manager != null, "Formal Boot loaded");
            Check(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", 7917, false, out string error), "KCP prepared: " + error);
            manager.ConfigurePreparationFlow(true);
            manager.StartHost();
            while (manager.RoomSnapshot.Phase != PreparationPhase.Preparing) yield return null;
            manager.SetOwnReady(true);
            yield return new WaitForSeconds(.25f);
            float loadStart = Time.realtimeSinceStartup; manager.StartPreparedGame();
            while (NetworkClient.localPlayer == null || manager.IsLoadingLocked || FindFirstObjectByType<GameplayCameraRig>()?.BoundPlayer == null) yield return null;
            player = NetworkClient.localPlayer.GetComponent<PlayerMovement>(); animator = player.GetComponentInChildren<NordicPlayerAnimator>();
            map = GameplayMapContext.Active; cameraRig = FindFirstObjectByType<GameplayCameraRig>();
            report.loadSeconds = Time.realtimeSinceStartup - loadStart;
            // Test fixtures are runtime only: isolate geometry from enemy pressure and upgrade popups.
            gameObject.AddComponent<WeaponAttackAdmissionFixtureGate>();
            player.GetComponent<CombatantBehaviour>().SetUltimateInvulnerable(true);
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 120;
            yield return new WaitForSeconds(10);
            var waveFrames = new List<float>(); float waveUntil = Time.realtimeSinceStartup + 5;
            while (Time.realtimeSinceStartup < waveUntil) { yield return null; waveFrames.Add(Time.unscaledDeltaTime * 1000); }
            RecordPerformance("authored waves, weapons gated for measurement",120,waveFrames);
            if (Environment.GetCommandLineArgs().Contains("--nordic-full-performance"))
            {
                // Retain the authored wave clock and cap. Invulnerability and the weapon gate
                // only keep the measurement actor alive and prevent kills/upgrade interruptions.
                for (int window=1; window<=5; window++)
                {
                    var frames=new List<float>(); float until=Time.realtimeSinceStartup+30;
                    while (Time.realtimeSinceStartup<until) { yield return null; frames.Add(Time.unscaledDeltaTime*1000); }
                    RecordPerformance("authored density, elapsed approximately "+(15+30*window)+"s",120,frames);
                }
                Check(report.performance.Max(p=>p.enemies)>=30,"Full authored live-enemy cap measured without changing waves");
                yield return Shot("authored-full-density");
            }
            yield return Shot("formal-waves");
            foreach (var spawner in FindObjectsByType<NetworkGameplayEnemySpawner>(FindObjectsSortMode.None)) spawner.enabled = false;
            foreach (var enemy in FindObjectsByType<NetworkEnemySimulationAgent>(FindObjectsSortMode.None)) NetworkServer.Destroy(enemy.gameObject);
            yield return null;
            Check(map != null && map.IsReady, "Baked navigation loaded before ready");
            Check(Mathf.Abs(map.Bounds.size.x - 119.3386f) < .001f && Mathf.Abs(map.Bounds.size.y - 67.128f) < .001f, "Four by four screen map dimensions");
            Check(animator != null && player.GetComponent<CircleCollider2D>().radius == .1f && player.transform.Find("HitBox").GetComponent<CircleCollider2D>().radius == .13f, "Axeldor and unchanged footprints");
            Check(!FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Any(c => c.GetType().Name == "NordicSamplePreview"), "No preview controller in formal scene");
            Check(cameraRig.BoundPlayer == player && player.IsLocalOwnerBound, "Formal local player binding retained");
            Move(map.Bounds.center); yield return new WaitForSeconds(.8f); yield return Shot("center");
            var bodyRenderers = player.transform.Find("Nordic Visual").GetComponentsInChildren<SpriteRenderer>();
            Bounds body = bodyRenderers.First(r => r.name == "Cuerpo").bounds;
            foreach (var r in bodyRenderers.Where(r => r.name != "Shadow")) body.Encapsulate(r.bounds);
            Check(body.size.y > 2 && body.size.y < 2.4f, "Axeldor original world scale");
            foreach (var resolution in new[] { new Vector2Int(1920,1080), new Vector2Int(1440,1080), new Vector2Int(1920,180) })
            {
                Screen.SetResolution(resolution.x, resolution.y, false); yield return new WaitForSeconds(.4f);
                CheckCamera("Viewport " + resolution);
            }
            Screen.SetResolution(1920,1080,false); yield return new WaitForSeconds(.4f);
            foreach (var d in new[] { Vector2.left, Vector2.right, Vector2.up, Vector2.down, new Vector2(1,1).normalized, new Vector2(-1,-1).normalized })
            {
                Vector2 edge = new Vector2(d.x < 0 ? map.Bounds.min.x + .11f : d.x > 0 ? map.Bounds.max.x - .11f : 0,
                    d.y < 0 ? map.Bounds.min.y + .11f : d.y > 0 ? map.Bounds.max.y - .11f : .99f);
                Move(edge); drive = true; direction = d;
                yield return new WaitForSeconds(.35f); direction = Vector2.zero; yield return new WaitForSeconds(.1f);
                Check(Vector2.Distance(map.Clamp(player.body.position, .1f), player.body.position) < .002f, "Movement boundary " + d);
                CheckCamera("Boundary view " + d);
            }
            yield return Shot("boundary"); drive = false;
            CheckDashGeometry();
            yield return CheckActualDash(crossStart, crossDirection, "cross ordinary obstacle");
            yield return CheckActualDash(retreatStart, retreatDirection, "retreat occupied endpoint");
            Vector2 lane = FindLane();
            foreach (int fps in new[] { 60,120 })
            {
                QualitySettings.vSyncCount = 0; Application.targetFrameRate = fps;
                Move(lane); direction = Vector2.right; drive = true; yield return new WaitForSeconds(.2f);
                var times = new List<float>(); var motion = new List<string> { "time,x,cameraX" };
                double start = Time.realtimeSinceStartupAsDouble; float previous = player.transform.position.x; int reversed = 0;
                while (Time.realtimeSinceStartupAsDouble - start < 1)
                {
                    yield return null; times.Add(Time.unscaledDeltaTime * 1000);
                    float x = player.transform.position.x; if (x < previous - .0001f) reversed++; previous = x;
                    motion.Add(FormattableString.Invariant($"{Time.realtimeSinceStartupAsDouble},{x},{cameraRig.transform.position.x}"));
                }
                Check(reversed == 0, "No backwards render frames at " + fps);
                Check(!animator.FacingLeft && animator.Motion == NordicMotion.Walk, "Walk right facing at " + fps);
                direction = Vector2.zero; drive = false; player.StopMovement();
                RecordPerformance("isolated movement",fps,times);
                File.WriteAllLines(Path.Combine(output,"motion-"+fps+".csv"),motion);
            }
            Move(lane + Vector2.right * 2); drive = true; direction = Vector2.left; yield return new WaitForSeconds(.2f);
            Check(animator.FacingLeft, "Walk left facing"); direction = Vector2.zero; yield return new WaitForSeconds(.1f); drive = false; player.StopMovement();
            yield return Shot("facing-left");
            int shakeCount = 0; cameraRig.ShakePlayed += _ => shakeCount++;
            cameraRig.PlayShake(player, 2); yield return new WaitForSeconds(.1f); CheckCamera("Shaken view"); Check(shakeCount == 1,"Existing shake callback");
            yield return new WaitForSeconds(.7f);
            protect=false;
            var combatant = player.GetComponent<CombatantBehaviour>(); combatant.SetUltimateInvulnerable(false); combatant.SetCanonicalInvulnerable(false);
            NetworkCombatWorld.Instance.Gateway.Ledger.SetAbsoluteInvulnerable(NetworkClient.localPlayer.netId,false);
            int healthBefore = combatant.CurrentHealth;
            player.DecreaseHealth(1); yield return null;
            Check(combatant.CurrentHealth < healthBefore, "Damage follows formal owner health path");
            yield return null;
            Check(player.GetComponentsInChildren<SpriteRenderer>().Where(r => r.name != "Shadow").Count(r => r.color.g < .5f) >= 5,"Whole body hit feedback");
            yield return Shot("hurt");
            protect=true;
            yield return new WaitForSeconds(.5f);
            Check(FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Any(p => p.isPlaying && p.transform.IsChildOf(map.transform)), "Torch particles running");
            var tree = map.GetComponentsInChildren<Collider2D>().Where(c => map.Bounds.Contains(c.bounds.min) && map.Bounds.Contains(c.bounds.max) &&
                c.GetComponentsInParent<Transform>().Any(t => t.name.StartsWith("Tree_"))).OrderBy(c => c.bounds.center.sqrMagnitude).First();
            Move((Vector2)tree.bounds.center - Vector2.up * (tree.bounds.extents.y + .3f));
            drive=true; direction=Vector2.up; yield return new WaitForSeconds(1);
            drive=false; direction=Vector2.zero; player.StopMovement();
            Check(player.body.position.y < tree.bounds.max.y && Physics2D.Distance(player.GetComponent<CircleCollider2D>(),tree).distance >= -.015f,
                "Tree roots block formal walking; tree="+tree.bounds+" player="+player.body.position);
            Move((Vector2)tree.bounds.center + Vector2.up * (tree.bounds.extents.y + .25f)); yield return new WaitForSeconds(.5f); yield return Shot("occlusion-behind");
            Move((Vector2)tree.bounds.center - Vector2.up * (tree.bounds.extents.y + .25f)); yield return new WaitForSeconds(.5f); yield return Shot("occlusion-front");
            yield return CheckEnemyRoute(manager, tree);
            foreach (var d in new[] { Vector2.one, -Vector2.one, new Vector2(-1,1), new Vector2(1,-1) })
            {
                map.Place(player.body,player.GetComponent<CircleCollider2D>(),d*1000);
                Check(map.IsFree(player.body.position,.1f),"Teleport safe at corner "+d);
            }
            Move(map.Bounds.center); yield return new WaitForSeconds(.6f); yield return Shot("final-gameplay");
            yield return CheckLifePresentation();
            manager.StopHost();
            while (manager.IsGameplayLoaded || manager.IsGameplayTransitioning) yield return null;
            Check(GameplayMapContext.Active == null, "Map released on room exit");
        }
        private IEnumerator CheckEnemyRoute(BootGameplayNetworkManager manager, Collider2D tree)
        {
            var prefab = manager.spawnPrefabs.First(p => p.name == "NetworkEnemyBase");
            var foot = (CircleCollider2D)prefab.GetComponent<AstralShift.HellMaiden.AI.Enemy.EnemyController>().collider;
            float radius = GameplayMapContext.Radius(foot);
            Vector2 start = map.FindSpawn((Vector2)tree.bounds.center - Vector2.right * (tree.bounds.extents.x + 2), radius);
            Vector2 end = map.FindSpawn((Vector2)tree.bounds.center + Vector2.right * (tree.bounds.extents.x + 2), radius);
            Check(Physics2D.CircleCast(start, radius, (end-start).normalized, Vector2.Distance(start,end), LayerMask.GetMask("Obstacles")).collider != null,
                "Enemy route fixture crosses an authored obstacle");
            Check(map.IsReachable(start,end), "Enemy route connected in baked graph");
            Move(end);
            var root = Instantiate(prefab,start,Quaternion.identity);
            var agent = root.GetComponent<NetworkEnemySimulationAgent>();
            agent.ConfigureInitialServerTarget(NetworkClient.localPlayer.netId);
            NetworkServer.Spawn(root);
            float until = Time.realtimeSinceStartup + 20;
            while (!agent.ProductEnemyInitialized && Time.realtimeSinceStartup < until) yield return null;
            Check(agent.ProductEnemyInitialized,"Route enemy uses production initialization");
            var enemy = root.GetComponent<AstralShift.HellMaiden.AI.Enemy.EnemyController>();
            var trace = new List<string> { "time,x,y,distanceToTree" };
            float minimum = float.PositiveInfinity;
            while (Vector2.Distance(enemy.MovementCenterPosition,end) > 1.25f && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForFixedUpdate();
                var ownFoot = (CircleCollider2D)enemy.collider;
                float distance = Physics2D.Distance(ownFoot,tree).distance; minimum = Mathf.Min(minimum,distance);
                trace.Add(FormattableString.Invariant($"{Time.realtimeSinceStartup},{enemy.MovementCenterPosition.x},{enemy.MovementCenterPosition.y},{distance}"));
            }
            File.WriteAllLines(Path.Combine(output,"enemy-route.csv"),trace);
            Check(Vector2.Distance(enemy.MovementCenterPosition,end) <= 1.25f && minimum >= -.015f,
                "Ground enemy physically routed around tree; separation="+minimum+" remaining="+Vector2.Distance(enemy.MovementCenterPosition,end));
            yield return Shot("enemy-route");
            NetworkServer.Destroy(root);
        }
        private IEnumerator CheckLifePresentation()
        {
            // Isolate the visual contract. Do not create a new gameplay revival or end the canonical run.
            player.enabled=false;
            var health = player.GetComponent<CombatantBehaviour>(); int before=health.CurrentHealth;
            ApplyPresentationHealth(health,0);
            yield return null; yield return null;
            Check(animator.Motion==NordicMotion.Dead,"Canonical zero health selects original Die");
            yield return new WaitForSeconds(1.15f);
            var state=animator.GetComponent<Animancer.AnimancerComponent>().States.Current;
            Check(state.Speed==0 && Mathf.Abs(state.Time-state.Length)<.001f,"Die holds its final frame");
            yield return Shot("axeldor-dead");
            ApplyPresentationHealth(health,before);
            yield return null; yield return null;
            state=animator.GetComponent<Animancer.AnimancerComponent>().States.Current;
            Check(animator.Motion==NordicMotion.Revive && state.Speed<0,"Existing alive restoration reverses Die");
            yield return new WaitForSeconds(1.15f);
            Check(animator.Motion==NordicMotion.Idle && health.CurrentHealth==before,"Visual restoration finishes without changing health; motion="+animator.Motion+" health="+health.CurrentHealth+" expected="+before+" clipTime="+state.Time);
            player.enabled=true;
        }
        internal static void ApplyPresentationHealth(CombatantBehaviour health,int value)
        {
            // Mirror's canonical receive path uses the same guard. A direct fixture health edit
            // must not report a real owner death to the server while testing presentation alone.
            var adapter=health.GetComponent<NetworkCombatantAdapter>();
            var guard=typeof(NetworkCombatantAdapter).GetField("applyingCanonical",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            guard.SetValue(adapter,true);
            try { health.ApplyCanonicalHealth(value,health.MaxHealth,health.StateVersion+1); }
            finally { guard.SetValue(adapter,false); }
        }
        private void RecordPerformance(string phase,int fps,List<float> times)
        {
            times.Sort(); report.performance.Add(new Performance { phase=phase,target=fps,frames=times.Count,
                enemies=FindObjectsByType<NetworkEnemySimulationAgent>(FindObjectsSortMode.None).Length,
                meanMs=times.Average(),p95Ms=times[Mathf.Min(times.Count-1,Mathf.FloorToInt(times.Count*.95f))],maxMs=times.Last(),
                memory=UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() });
        }
        private void CheckDashGeometry()
        {
            int count = 0; bool crossed = false, retreated = false;
            for (int x = -8; x <= 8; x++) for (int y = -5; y <= 5; y++)
            {
                Vector2 p = new Vector2(x*2,y*2);
                if (!map.IsFree(p,.1f)) continue;
                foreach (Vector2 d in new[] { Vector2.right,Vector2.left,Vector2.up,Vector2.down })
                {
                    Check(player.TryGetDashMotionParameters(d,p,out var dash),"Dash parameters " + count);
                    Check(map.IsFree(p+d*dash.Distance,.1f),"Legal dash endpoint " + count++);
                    var cast = Physics2D.CircleCast(p,.1f,d,dash.Distance,LayerMask.GetMask("Obstacles"));
                    bool crossing = cast.collider != null && cast.distance < 4 && dash.Distance > 5.9f;
                    bool retreating = dash.Distance > 1 && dash.Distance < 5.9f;
                    if (!crossed && crossing) { crossStart=p; crossDirection=d; }
                    if (!retreated && retreating) { retreatStart=p; retreatDirection=d; }
                    crossed |= crossing; retreated |= retreating;
                    if (count >= 64 && crossed && retreated) { Check(true,"Dash can cross obstacles and retreat from blocked endpoint"); return; }
                }
            }
            Check(crossed && retreated,"Dash crossing and blocked endpoint cases present");
        }
        private IEnumerator CheckActualDash(Vector2 start, Vector2 heading, string name)
        {
            Move(start); direction=heading; drive=true; player.SetDirection(heading);
            yield return new WaitForSeconds(.05f);
            player.Dash(); float until=Time.realtimeSinceStartup+3;
            while (player.CurrentDashUseId==0 && Time.realtimeSinceStartup<until) yield return null;
            Check(player.CurrentDashUseId!=0,"Formal dash admitted: "+name);
            drive=false; direction=Vector2.zero; player.SetDirection(Vector2.zero);
            yield return null;
            Check(animator.Motion==NordicMotion.Dash,"Axeldor source Dash: "+name);
            while (player.CurrentDashUseId!=0 && Time.realtimeSinceStartup<until) yield return null;
            Check(player.CurrentDashUseId==0 && map.IsFree(player.body.position,.1f),"Physical dash ended free: "+name);
            yield return new WaitForSeconds(.3f);
        }
        private Vector2 FindLane()
        {
            for (int y = -12; y <= 12; y++) for (int x = -22; x <= 10; x++)
            {
                var p = new Vector2(x,y); bool clear = true;
                for (int i = 0; i <= 150; i++) if (!map.IsFree(p + Vector2.right * (i*.05f),.1f)) { clear = false; break; }
                if (clear) return p;
            }
            throw new InvalidOperationException("No movement test corridor found.");
        }
        private void Move(Vector2 point)
        {
            point = map.FindSpawn(point,.1f); player.StopMovement();
            player.body.position = point; player.transform.position = point; Physics2D.SyncTransforms();
            cameraRig.GetComponent<ProCamera2D>().Reset();
        }
        private void CheckCamera(string name)
        {
            Camera c = cameraRig.GameCamera; Bounds b = GameplayCameraGeometry.ViewBounds(c);
            Check(!c.orthographic && Mathf.Abs(c.fieldOfView-80)<.001f && Mathf.Abs(c.transform.position.z+10)<.001f, name+" lens");
            Check(b.min.x >= map.Bounds.min.x-.03f && b.max.x <= map.Bounds.max.x+.03f && b.min.y >= map.Bounds.min.y-.03f && b.max.y <= map.Bounds.max.y+.03f,name+" bounds");
        }
        private IEnumerator Shot(string name) { yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png")); yield return null; }
        private void Check(bool pass,string name) { if (!pass) throw new InvalidOperationException(name); report.checks.Add(name); }
        private void Finish()
        {
            if (finished) return; finished = true; drive = false;
            File.WriteAllText(Path.Combine(output,"acceptance.json"),JsonUtility.ToJson(report,true));
            Debug.Log("[NordicGameplay] result="+(report.passed ? "PASS" : "FAIL")+" checks="+report.checks.Count);
            Application.Quit(report.passed?0:1);
        }
    }
}
