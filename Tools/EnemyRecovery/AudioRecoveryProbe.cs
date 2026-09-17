using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FMODUnity;
using Newtonsoft.Json;
using UnityEngine;
using Object = UnityEngine.Object;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Data.Cards;

// Instrumentation only: compiled against the original package, never shipped in the game.
public sealed partial class RecoveryProbe
{
    static bool audioCapture;
    static string audioCase = "configuration";
    static readonly Dictionary<long,FMOD.Studio.EventInstance> audioInstances=new Dictionary<long,FMOD.Studio.EventInstance>();
    float audioSampleAt;
    public static void AudioParameter(FMOD.Studio.EventInstance sound, FMOD.Studio.PARAMETER_ID id, float value)
    {
        if(!audioCapture)return;
        FMOD.Studio.EventDescription d;FMOD.Studio.PARAMETER_DESCRIPTION p;
        string name=sound.getDescription(out d)==FMOD.RESULT.OK && d.getParameterDescriptionByID(id,out p)==FMOD.RESULT.OK?(string)p.name:"unknown-id";
        AudioEvent(sound,"setParameterByID",name,value);
    }
    public static void AudioEvent(FMOD.Studio.EventInstance sound, string operation, string parameter, float value)
    {
        if (!audioCapture) return;
        try
        {
            FMOD.Studio.EventDescription description; string path;
            if (sound.getDescription(out description) != FMOD.RESULT.OK || description.getPath(out path) != FMOD.RESULT.OK ||
                path.IndexOf("/plr/", StringComparison.OrdinalIgnoreCase) < 0) return;
            FMOD.ATTRIBUTES_3D position; sound.get3DAttributes(out position);
            audioInstances[sound.handle.ToInt64()]=sound;
            Log("audio-call", new { test=audioCase, handle=sound.handle.ToInt64(), path=path, operation=operation,
                parameter=parameter, value=value, position=new[]{position.position.x,position.position.y,position.position.z} });
        }
        catch(Exception e) { Log("audio-probe-error",e.ToString()); }
    }
    void Update()
    {
        if(!audioCapture || Time.realtimeSinceStartup<audioSampleAt)return;
        audioSampleAt=Time.realtimeSinceStartup+.25f;
        var states=new List<object>();
        foreach(var pair in audioInstances.ToArray())
        {
            FMOD.Studio.PLAYBACK_STATE state;
            if(pair.Value.getPlaybackState(out state)!=FMOD.RESULT.OK){audioInstances.Remove(pair.Key);continue;}
            states.Add(new{handle=pair.Key,state=state.ToString()});
        }
        FMOD.ChannelGroup master; FMOD.DSP meter; FMOD.DSP_METERING_INFO levels;
        float peak=0,rms=0;
        if(RuntimeManager.CoreSystem.getMasterChannelGroup(out master)==FMOD.RESULT.OK &&
            master.getDSP(FMOD.CHANNELCONTROL_DSP_INDEX.HEAD,out meter)==FMOD.RESULT.OK)
        {
            meter.setMeteringEnabled(false,true);
            if(meter.getMeteringInfo(IntPtr.Zero,out levels)==FMOD.RESULT.OK)
                for(int i=0;i<levels.numchannels;i++){peak=Mathf.Max(peak,levels.peaklevel[i]);rms=Mathf.Max(rms,levels.rmslevel[i]);}
        }
        Log("audio-sample",new{test=audioCase,peak=peak,rms=rms,instances=states,timeScale=Time.timeScale});
    }
    public static void AudioComponent(object sender, string method)
    {
        if(!audioCapture) return;
        var component=sender as Component;
        Log("audio-attack-stage",new {test=audioCase,method=method,id=component==null?0:component.GetInstanceID(),path=ObjectPath(component)});
    }
    IEnumerator CaptureAudio(object progression)
    {
        phase="weapon-audio";
        var director=Singleton("AstralShift.HellMaiden.GameDirector");
        var db=Get(director,"runtimeDB");
        var weapons=new[]{(WeaponData)Invoke(db,"GetWeaponData",2u),(WeaponData)Invoke(db,"GetWeaponData",3u)};
        var graph=new AssetGraph();
        foreach(var data in weapons) graph.Reference(data);
        File.WriteAllText(Path.Combine(Output,"audio-assets.json"),JsonConvert.SerializeObject(new{objects=graph.Complete()},Formatting.Indented));
        var descriptions=new List<object>();
        foreach(var bank in new[]{"plr"})
        {
            FMOD.Studio.Bank b; RuntimeManager.StudioSystem.getBank("bank:/"+bank,out b);
            FMOD.Studio.EventDescription[] eventsInBank; b.getEventList(out eventsInBank);
            foreach(var d in eventsInBank ?? new FMOD.Studio.EventDescription[0])
            {
                string path; FMOD.GUID id; bool oneshot, is3d; float min,max; int count,length;
                d.getPath(out path); d.getID(out id); d.isOneshot(out oneshot); d.is3D(out is3d);
                d.getMinMaxDistance(out min,out max); d.getParameterDescriptionCount(out count); d.getLength(out length);
                var parameters=new List<object>();
                for(int i=0;i<count;i++){FMOD.Studio.PARAMETER_DESCRIPTION p;d.getParameterDescriptionByIndex(i,out p);parameters.Add(new{name=(string)p.name,min=p.minimum,max=p.maximum,initial=p.defaultvalue,id=p.id});}
                descriptions.Add(new{path=path,id=id,oneshot=oneshot,is3d=is3d,min=min,max=max,length=length,parameters=parameters});
            }
        }
        File.WriteAllText(Path.Combine(Output,"audio-bank.json"),JsonConvert.SerializeObject(descriptions,Formatting.Indented));
        var master=Singleton("AstralShift.HellMaiden.Scenes.SceneMaster");
        float deadline=Time.realtimeSinceStartup+60;
        while(Get(master,"_mainOperationCoroutine")!=null && Time.realtimeSinceStartup<deadline) yield return null;
        if(Get(master,"_mainOperationCoroutine")!=null){File.WriteAllText(Path.Combine(Output,"audio.failed"),"Scene initialization timeout");Application.Quit();yield break;}
        var timeline=Get(progression,"MainProgressionTimeline") as MonoBehaviour;
        Invoke(timeline,"Pause");
        foreach(var spawner in timeline.GetComponentsInChildren<MonoBehaviour>())
            if(spawner.GetType().Namespace=="AstralShift.HellMaiden.Combat.Spawners")spawner.StopAllCoroutines();
        Invoke(Singleton("AstralShift.HellMaiden.Combat.Hand.PlayerHand"),"DeactivateWeapons");
        var player=(Component)Get(director,"Player");
        foreach(var enemy in Object.FindObjectsByType(TypeNamed("AstralShift.HellMaiden.AI.Enemy.EnemyController"),FindObjectsSortMode.None).Cast<Component>())Invoke(enemy,"Kill",true,false);
        Log("audio-fixture",new{note="Original weapon methods; automatic cooldown disabled; controlled elements/counts/cancel; no pressure evidence.",
            listeners=Object.FindObjectsByType<StudioListener>(FindObjectsSortMode.None).Select(l=>new{path=ObjectPath(l),position=Vec(l.transform.position)}).ToArray()});
        audioCapture=true;
        foreach(string name in new[]{"Master","SFX","Music"})
        {
            var vca=RuntimeManager.GetVCA("vca:/"+name);float before,final;vca.getVolume(out before,out final);
            Log("audio-gain",new{name=name,before=before,actual=final,fixture=name=="Music"?0:1});vca.setVolume(name=="Music"?0:1);
        }
        bool supplement=Environment.GetCommandLineArgs().Contains("--audio-supplement=true");
        foreach(var data in weapons)
        foreach(var element in data.ID==2?new[]{"Default","Fire","Poison"}:new[]{"Fire","Poison"})
        {
            var weapon=Object.Instantiate(data.WeaponPrefab,player.transform);
            weapon.enabled=false;
            // Only the count is varied; keep referenced source configuration untouched.
            var stats=(AttackStats)typeof(object).GetMethod("MemberwiseClone",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(data.BaseStats,null);
            weapon.Init(data.ID,stats);
            Set(weapon,"_equipmentModifiers",new RuntimeEquipmentModifiers());
            Set(weapon,"IsFireType",element=="Fire");Set(weapon,"IsPoisonType",element=="Poison");
            for(int cycle=0;cycle<(supplement?1:3);cycle++)
            {
                audioCase=data.ID+"/"+element+"/"+(supplement?"controlled-hit-pause":cycle==0?"single":cycle==1?"multi":"cancel");
                stats.projectileCount=cycle==1?3:1;
                weapon.Init(data.ID,stats);
                Set(player,"attackDirection",Vector2.right);
                Log("audio-case-start",new{test=audioCase,id=data.ID,count=stats.projectileCount,duration=stats.duration});
                weapon.Attack();
                if(supplement)
                {
                    yield return new WaitForSecondsRealtime(.3f);
                    Time.timeScale=0;Log("audio-timescale",new{value=0});
                    yield return new WaitForSecondsRealtime(.5f);
                    Time.timeScale=.25f;Log("audio-timescale",new{value=.25f});
                    yield return new WaitForSecondsRealtime(.5f);
                    Time.timeScale=1;Log("audio-timescale",new{value=1});
                    if(data.ID==2)
                    {
                        foreach(var projectile in Object.FindObjectsByType<ProjectileAttack>(FindObjectsSortMode.None))
                        {
                            // Controlled interface callback, not a physical collision or pressure run.
                            Set(projectile,"hitMaxCount",2);Set(projectile,"_hitCount",0);
                            Log("audio-controlled-hit",new{part="intermediate",id=projectile.GetInstanceID()});
                            Invoke(projectile,"OnHit",new AudioTarget());
                            yield return new WaitForSecondsRealtime(.3f);
                            Log("audio-controlled-hit",new{part="final",id=projectile.GetInstanceID()});
                            Invoke(projectile,"OnHit",new AudioTarget());
                        }
                    }
                }
                yield return new WaitForSecondsRealtime(cycle==2?.25f:5f);
                // Explicit cancellation of outstanding effects is separately labelled, not natural expiry.
                weapon.Deactivate();
                Log("audio-case-cleanup",new{test=audioCase});
                yield return new WaitForSecondsRealtime(1);
                if(cycle<2){weapon.gameObject.SetActive(true);weapon.enabled=false;}
            }
            Object.Destroy(weapon.gameObject);
        }
        audioCapture=false;
        File.WriteAllText(Path.Combine(Output,"audio.complete"),"Configuration and bounded three-attack observations complete; hit/pause coverage must be audited separately.");
        events.Flush();Application.Quit();
    }
    sealed class AudioTarget : IDamageable
    {
        public int GetID(){return 987654;}
        public Vector2 GetPosition(){return Vector2.zero;}
        public bool IsActive(){return true;}
        public void Damage(Vector2 p,WeaponBehaviour w,DamageType d){}
        public void Damage(int v,DamageType d){}
    }
}
