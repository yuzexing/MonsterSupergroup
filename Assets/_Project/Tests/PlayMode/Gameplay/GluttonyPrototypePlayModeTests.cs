#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.Options;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Timeline;
namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class GluttonyPrototypePlayModeTests
    {
        private BootGameplayNetworkManager manager;
        private EnemyDefinitionRuntimeFixture isolated;
        private GameObject[] roots;
        private GameObject gate;
        private int pickupSpawns, experienceCollections;
        private void ObservePickup(string kind, string run, ulong drop, string detail)
        {
            if (kind == "spawn") pickupSpawns++;
            if (kind == "xp-collected") experienceCollections++;
        }
        private readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();
        private IEnumerator Start(bool controlled)
        {
            PickupAudit.Recorded += ObservePickup;
            if (!Application.isBatchMode) UnityEditor.EditorApplication.ExecuteMenuItem("Window/General/Game");
            GameOptionsService.EnsureInitialized();
            yield return EnemyDefinitionRuntimeFixture.Wait(() => GameLocalization.IsReady, "localization startup");
            // Let initial dynamic-font imports finish before Boot loads Rewired and gameplay assets.
            yield return null;
            yield return null;
            const string boot="Assets/_Project/Scenes/Boot.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(boot,new LoadSceneParameters(LoadSceneMode.Single));
            roots=BootSceneFixtureObjects.Capture(boot); manager=(BootGameplayNetworkManager)NetworkManager.singleton;
            manager.ConfigurePreparationFlow(true);
            if (controlled)
            {
                isolated=new EnemyDefinitionRuntimeFixture(manager);
                gate=new GameObject("Gluttony isolated-test weapon gate"); gate.AddComponent<WeaponAttackAdmissionFixtureGate>();
            }
            else SceneManager.sceneLoaded+=ConfigureNormalBattle;
            Assert.That(manager.TryStartOfflineRoom(out string error),Is.True,error);
            yield return EnemyDefinitionRuntimeFixture.Wait(()=>manager.RoomSnapshot.Phase==PreparationPhase.Preparing,"preparation");
            manager.SetOwnLoadout(6);
            yield return EnemyDefinitionRuntimeFixture.Wait(()=>manager.RoomSnapshot.Members[0].WeaponId==6,"weapon loadout");
            manager.StartPreparedGame();
            yield return EnemyDefinitionRuntimeFixture.Wait(()=>manager.RoomSnapshot.Phase==PreparationPhase.InGame && NetworkClient.localPlayer!=null,"gameplay");
            var skill=NetworkClient.localPlayer.GetComponent<NetworkPlayerGluttony>();
            Assert.That(skill,Is.Not.Null,"Install the prototype on the production Player prefab first.");
            var settings=GluttonyParameters.Defaults; settings.Enabled=true; settings.PassiveEnabled=!controlled;
            NetworkCombatWorld.Instance.ServerConfigureGluttony(settings,true);
            yield return EnemyDefinitionRuntimeFixture.Wait(()=>skill.OwnerReady,"owner baseline");
        }
        [UnityTest]
        public IEnumerator ProductionHost_PassiveAndMarkedCollectionShareCanonicalKillWithoutSharingCooldown()
        {
            yield return Start(true);
            yield return EnemyDefinitionRuntimeFixture.Wait(()=>isolated.PairReady() && isolated.PlaceInView(),"fixture placement");
            var owner=NetworkClient.localPlayer; var skill=owner.GetComponent<NetworkPlayerGluttony>();
            var settings=skill.Parameters; settings.ActiveEnabled=false; settings.PassiveEnabled=true;
            NetworkCombatWorld.Instance.ServerConfigureGluttony(settings,true);
            var target=isolated.Agents().OrderBy(a=>Vector2.Distance(a.transform.position,owner.transform.position)).First();
            Assert.That(skill.RequestDevour(target.netId,false));
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(skill.State.PassiveKills,Is.Zero,"Out-of-range request must not execute.");
            Assert.That(skill.State.PassiveReadyAt,Is.Zero,"Failed request must not consume cooldown.");
            yield return ApproachUntil(()=>skill.State.PassiveKills==1,()=>target!=null ? (Vector2)target.transform.position : (Vector2)owner.transform.position,15);
            double passiveDeadline=skill.State.PassiveReadyAt;
            settings.ActiveEnabled=true; settings.Length=20; settings.Width=15;
            NetworkCombatWorld.Instance.ServerConfigureGluttony(settings,false);
            var remaining=isolated.Agents().First(a=>NetworkCombatWorld.Instance.Gateway.Ledger.IsAlive(a.netId));
            Assert.That(skill.RequestMark((Vector2)remaining.transform.position-(Vector2)owner.transform.position));
            yield return EnemyDefinitionRuntimeFixture.Wait(()=>skill.State.Marked==1,"actual rectangle collision mark");
            Assert.That(skill.State.ActiveReadyAt,Is.GreaterThan(NetworkTime.time+18));
            yield return ApproachUntil(()=>skill.State.TotalCollected==1,()=>remaining!=null ? (Vector2)remaining.transform.position : (Vector2)owner.transform.position,5);
            Assert.That(skill.State.PassiveReadyAt,Is.EqualTo(passiveDeadline));
            Assert.That(skill.State.PassiveKills,Is.EqualTo(1));
            Assert.That(NetworkCombatWorld.Instance.Gateway.Metrics.ConfirmedKills,Is.EqualTo(2));
            Assert.That(skill.RemainingMarks,Is.Zero);
        }
        [UnityTest]
        public IEnumerator MultipleMarkedEnemiesCollectWithoutConsumingPassiveCooldown()
        {
            yield return Start(true);
            var owner = NetworkClient.localPlayer; var skill = owner.GetComponent<NetworkPlayerGluttony>();
            var settings = skill.Parameters; settings.PassiveEnabled = false;
            settings.Length = 20; settings.Width = 15; settings.MarkDuration = 10;
            NetworkCombatWorld.Instance.ServerConfigureGluttony(settings, true);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => isolated.PairReady() && isolated.PlaceInView(), "two live enemies");
            Assert.That(skill.RequestMark(new Vector2(5, 3)));
            yield return EnemyDefinitionRuntimeFixture.Wait(() => skill.State.Marked == 2, "two rectangle marks");
            Assert.That(skill.State.ActiveReadyAt, Is.GreaterThan(NetworkTime.time + 18));
            yield return ApproachUntil(() => skill.State.TotalCollected == 2, () =>
            {
                var target = isolated.Agents().Where(a => skill.HasMark(a.netId))
                    .OrderBy(a => Vector2.Distance(a.transform.position, owner.transform.position)).FirstOrDefault();
                return target != null ? (Vector2)target.transform.position : (Vector2)owner.transform.position;
            }, 8);
            Assert.That(skill.State.PassiveReadyAt, Is.Zero);
            Assert.That(skill.State.PassiveKills, Is.Zero);
            Assert.That(skill.RemainingMarks, Is.Zero);
            Assert.That(NetworkCombatWorld.Instance.Gateway.Metrics.ConfirmedKills, Is.EqualTo(2));
        }
        [UnityTest]
        public IEnumerator NormalBattle_WeaponsMovementDropsAndPrototypeRunTogether()
        {
            yield return Start(false);
            var skill=NetworkClient.localPlayer.GetComponent<NetworkPlayerGluttony>();
            var player=NetworkClient.localPlayer.GetComponent<PlayerMovement>();
            double end=Time.realtimeSinceStartupAsDouble+28, nextCast=0;
            int casts=0, maximumMarked=0; bool captured=false;
            Directory.CreateDirectory("Logs/GluttonyPrototype");
            while (Time.realtimeSinceStartupAsDouble<end && !BootGameplayNetworkManager.CombatHasEnded)
            {
                ChooseOfferedUpgrade();
                var enemy=NetworkClient.spawned.Values.Where(x=>x!=null).Select(x=>x.GetComponent<NetworkEnemySimulationAgent>())
                    .Where(x=>x!=null && x.ProductEnemyInitialized && x.GetComponent<CombatantBehaviour>().IsAlive)
                    .OrderBy(x=>Vector2.Distance(x.transform.position,player.transform.position)).FirstOrDefault();
                if (enemy!=null && skill.OwnerReady)
                {
                    Vector2 direction=(Vector2)enemy.transform.position-(Vector2)player.transform.position;
                    player.SetDirection(direction.normalized);
                    if (NetworkTime.time>=nextCast && direction.magnitude<=skill.Parameters.Length &&
                        direction.magnitude>skill.Parameters.Radius+.25f && skill.RequestMark(direction))
                    { casts++; nextCast=NetworkTime.time+skill.Parameters.ActiveCooldown; }
                }
                maximumMarked=Math.Max(maximumMarked,skill.State.Marked);
                if (!captured && skill.RemainingMarks>0)
                { ScreenCapture.CaptureScreenshot("Logs/GluttonyPrototype/normal-battle.png"); captured=true; }
                yield return null;
            }
            player.SetDirection(Vector2.zero);
            long damage=NetworkCombatWorld.Instance.Gateway.Metrics.AcceptedCombatResults;
            File.WriteAllText("Logs/GluttonyPrototype/normal-battle.txt",$"casts={casts} maxMarked={maximumMarked} passiveKills={skill.State.PassiveKills} collected={skill.State.TotalCollected} normalDamageResults={damage} kills={NetworkCombatWorld.Instance.Gateway.Metrics.ConfirmedKills} pickupSpawns={pickupSpawns} experienceCollections={experienceCollections}");
            Assert.That(casts,Is.GreaterThan(0)); Assert.That(maximumMarked,Is.GreaterThan(0));
            Assert.That(damage,Is.GreaterThan(0),"Real weapon damage must remain enabled in the normal battle check.");
            Assert.That(skill.State.PassiveKills+skill.State.TotalCollected,Is.GreaterThan(0),"The prototype must actually devour during normal combat.");
            Assert.That(pickupSpawns,Is.GreaterThan(0),"The production pickup world must spawn a real drop.");
            Assert.That(experienceCollections,Is.GreaterThan(0),"A real XP pickup must be collected.");
        }
        private IEnumerator ApproachUntil(Func<bool> condition,Func<Vector2> target,float seconds)
        {
            var player=NetworkClient.localPlayer.GetComponent<PlayerMovement>(); float end=Time.realtimeSinceStartup+seconds;
            while(!condition() && Time.realtimeSinceStartup<end)
            { ChooseOfferedUpgrade(); player.SetDirection((target()-(Vector2)player.transform.position).normalized); yield return null; }
            player.SetDirection(Vector2.zero); Assert.That(condition(),Is.True,"Timed out while approaching a real enemy.");
        }
        private void ChooseOfferedUpgrade()
        {
            var s=NetworkClient.localPlayer.GetComponent<NetworkModifierSelection>();
            if (s.IsSelecting && s.PendingEventId!=0 && s.ServerOffers.Count>0)
                s.ServerSelect(NetworkClient.localPlayer.connectionToClient,s.PendingEventId,0,out _);
        }
        private static void Set(object obj,string field,object value) => obj.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(obj,value);
        private void ConfigureNormalBattle(Scene scene,LoadSceneMode mode)
        {
            if (scene.path!=manager.GameplayScene) return;
            var definition=manager.EnemyCatalog.Definitions.Where(d=>d.Prefab.name=="ReferenceBrotchi").OrderByDescending(d=>d.Stats.Capture().Health).First();
            var timeline=ScriptableObject.CreateInstance<TimelineAsset>(); temporary.Add(timeline);
            timeline.durationMode=TimelineAsset.DurationMode.FixedLength; timeline.fixedDuration=90;
            var track=timeline.CreateTrack<NetworkEnemySpawnTrack>(null,"Prototype normal combat validation"); temporary.Add(track);
            var clip=track.CreateClip<NetworkEnemySpawnClip>(); clip.start=1; clip.duration=30;
            var spawn=(NetworkEnemySpawnClip)clip.asset; temporary.Add(spawn);
            Set(spawn,"enemy",definition); Set(spawn,"authoringVersion",1);
            spawn.referenceMode=ReferenceSpawnMode.CurveBudget; spawn.count=36;
            spawn.spawnCurve=AnimationCurve.Constant(0,1,1); spawn.speedMultipliers=Vector2.one;
            spawn.contactRadius=.6f; spawn.expiresOffscreen=false;
            var rules=ScriptableObject.CreateInstance<GameplayWaveRules>(); temporary.Add(rules);
            Set(rules,"timeline",timeline); Set(rules,"referenceStage",true); Set(rules,"referenceEndTime",90d);
            Set(rules,"referenceSourceDuration",90d); Set(rules,"referenceXpAmplitude",0f); Set(rules,"positionAttempts",100);
            foreach(var spawner in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true))) spawner.ConfigureWaveRules(rules);
        }
        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            PickupAudit.Recorded -= ObservePickup;
            SceneManager.sceneLoaded-=ConfigureNormalBattle;
            if (manager!=null)
            {
                manager.LeavePreparationRoom();
                yield return EnemyDefinitionRuntimeFixture.Wait(()=>!NetworkClient.active && !NetworkServer.active && !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning,"cleanup");
            }
            isolated?.Dispose(); if(gate!=null) UnityEngine.Object.Destroy(gate);
            for(int i=temporary.Count-1;i>=0;i--) if(temporary[i]!=null) UnityEngine.Object.Destroy(temporary[i]); temporary.Clear();
            BootSceneFixtureObjects.Destroy(roots); yield return null; NetworkManager.ResetStatics();
        }
    }
}
#endif
