using System;
using System.Collections.Generic;
using System.IO;
using AstralShift.HellMaiden.Player.Attacks;
using FMODUnity;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Tests
{
    // Opt-in audit in the existing real Host/Client probes. Never shipped in the manual package.
    public sealed class WeaponAudioProcessAudit : MonoBehaviour
    {
        private StreamWriter writer;
        private readonly HashSet<string> starts = new();
        private int errors, duplicates, activeAtExit;
        private string role;
        public void Initialize(string directory, string side)
        {
            role=side;
            writer=new StreamWriter(Path.Combine(directory,side+"-audio.jsonl"));
            AttackAudioAudit.Changed+=Record;
        }
        private void Record(AttackAudioAudit.Entry entry)
        {
            writer?.WriteLine(JsonUtility.ToJson(entry));
            if(entry.Result != FMOD.RESULT.OK) errors++;
            if(entry.Operation=="start" || entry.Operation=="start-shot")
                if(!starts.Add(entry.Instance+"/"+entry.Generation)) duplicates++;
        }
        private void OnApplicationQuit()
        {
            // Stop/release are queued commands. Inspect FMOD after they have been consumed,
            // rather than reporting managed teardown's same-frame queue as residual playback.
            RuntimeManager.StudioSystem.flushCommands();
            foreach(string path in new[]{"event:/sx/plr/Sx_plr_slowprojectile_shot","event:/sx/plr/Sx_plr_slowprojectile_loop","event:/sx/plr/Sx_plr_dragonbreath"})
            {
                var d=RuntimeManager.GetEventDescription(path);
                if(d.getInstanceList(out var instances)==FMOD.RESULT.OK)
                    foreach(var instance in instances)
                        if(instance.getPlaybackState(out var state)==FMOD.RESULT.OK && state!=FMOD.Studio.PLAYBACK_STATE.STOPPED) activeAtExit++;
            }
            string result=errors==0 && duplicates==0 && activeAtExit==0 && starts.Count>0?"PASS":"FAIL";
            Debug.Log($"[WeaponAudioProcess] role={role} result={result} starts={starts.Count} duplicateStarts={duplicates} errors={errors} activeAtExit={activeAtExit}");
            writer?.WriteLine($"{{\"summary\":\"{result}\",\"starts\":{starts.Count},\"duplicates\":{duplicates},\"errors\":{errors},\"activeAtExit\":{activeAtExit}}}");
            writer?.Flush();
        }
        private void OnDestroy() { AttackAudioAudit.Changed-=Record; writer?.Dispose(); writer=null; }
    }
}
