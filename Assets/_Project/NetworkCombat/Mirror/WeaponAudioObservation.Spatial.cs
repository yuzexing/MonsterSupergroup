using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using AstralShift.HellMaiden.CameraFX;
using AstralShift.HellMaiden.Player.Attacks;
using FMODUnity;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.Options;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class WeaponAudioObservation
    {
        private bool EdgeDiagnostics => (LimboReferenceLaunch.Argument("--limbo-audio-case=") ?? "").StartsWith("edge", StringComparison.Ordinal);
        private string edgeLocation = "manual";
        private bool fixtureFraming;

        private void MoveToEdge(int index)
        {
            if (owner == null || GameplayMapContext.Active == null) return;
            Cancel();
            Bounds map = GameplayMapContext.Active.Bounds;
            Vector2[] offsets = { Vector2.zero, Vector2.left, Vector2.right, Vector2.up, Vector2.down,
                new(-1,1), new(1,1), new(-1,-1), new(1,-1) };
            Vector2 desired = (Vector2)map.center + Vector2.Scale(offsets[index], (Vector2)map.extents - Vector2.one);
            Vector2 position = GameplayMapContext.Active.FindSpawn(desired, .5f);
            owner.StopMovement(); owner.SetDirection(Vector2.zero);
            owner.transform.position = new Vector3(position.x, position.y, owner.transform.position.z);
            owner.GetComponent<Rigidbody2D>().position = position;
            edgeLocation = new[] { "center", "left", "right", "top", "bottom", "top-left", "top-right", "bottom-left", "bottom-right" }[index];
            Write("test-teleport", $"location={edgeLocation};position={position};presentation-only=true");
        }

        private void DrawEdgeControls()
        {
            GUILayout.Label("EDGE COMPARISON: explicit test positioning; no pressure evidence.");
            string[] labels = { "Center", "Left", "Right", "Top", "Bottom", "Top-left", "Top-right", "Bottom-left", "Bottom-right" };
            for (int row=0; row<3; row++)
            {
                GUILayout.BeginHorizontal();
                for (int i=row*3; i<row*3+3; i++) if (GUILayout.Button(labels[i])) MoveToEdge(i);
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button(fixtureFraming ? "Clear test camera framing" : "Test barrier camera framing"))
            {
                var camera = FindFirstObjectByType<GameplayCameraRig>();
                fixtureFraming = !fixtureFraming;
                if (fixtureFraming) camera.SetReferenceTrapFraming(Vector2.zero, 20, 1);
                else camera.ClearReferenceTrapFraming();
                Write("test-framing", fixtureFraming.ToString());
            }
        }

        // Opt-in production-build diagnostic. Real local binding and FMOD events; no audio RPC,
        // forced volume, automatic screenshots, or synthetic health/weapon admission.
        private IEnumerator EdgeSmoke()
        {
            Write("test-assistance", "edge-smoke: teleports local owner through nine legal map positions, then camera framing. Distance checks and event/DSP samples, not listening acceptance.");
            bool host = LimboReferenceLaunch.Argument("--limbo-role=") == "host";
            for (int step=0; step<9; step++)
            {
                int index = step == 0 ? 0 : (host ? step : 9-step);
                MoveToEdge(index);
                yield return new WaitForSeconds(.4f);
                var camera = FindFirstObjectByType<GameplayCameraRig>();
                var expected = owner.transform.position - Vector3.forward*10;
                bool valid = camera.BoundPlayer == owner && StudioListener.ListenerCount == 1 &&
                    Vector3.Distance(expected, camera.LocalAudioListener.transform.position)<.005f &&
                    Quaternion.Angle(Quaternion.identity,camera.LocalAudioListener.transform.rotation)<.005f;
                if (!valid) errors++;
                Write("edge-check", $"location={edgeLocation};passed={valid};owner={owner.transform.position};camera={camera.transform.position};listener={camera.LocalAudioListener.transform.position}");
                element = Wisp ? (step%3 == 0 ? AttackElement.Default : step%3 == 1 ? AttackElement.Fire : AttackElement.Poison) : (step%2 == 0 ? AttackElement.Fire : AttackElement.Poison);
                Fire(1);
                yield return new WaitForSeconds(.2f); SampleSpatial(camera); // Capture short wisp events.
                yield return new WaitForSeconds(.35f); SampleSpatial(camera);
            }
            Cancel();
            var view = FindFirstObjectByType<GameplayCameraRig>();
            view.SetReferenceTrapFraming(Vector2.zero,20,1);
            yield return new WaitForSeconds(.3f);
            bool fixedListener = Vector3.Distance(owner.transform.position-Vector3.forward*10, view.LocalAudioListener.transform.position)<.005f;
            if (!fixedListener) errors++;
            Write("framing-check", $"passed={fixedListener};listener={view.LocalAudioListener.transform.position};camera={view.transform.position}");
            Fire(1); yield return new WaitForSeconds(.4f); SampleSpatial(view);
            view.ClearReferenceTrapFraming(); Cancel();
        }

        [Serializable] private sealed class SpatialSample
        {
            public string location, element, guid, path, state, distanceResult, phaseResult, groupResult, meterResult, attributesResult;
            public long handle;
            public Vector3 player, camera, listener, fmodListener, source;
            public float geometricDistance, oldCameraDistance, distance, finalDistance, phase, finalPhase, eventVolume, eventFinalVolume;
            public float userMaster, userEffects, peak, rms;
            public int listeners, timelineMs;
            public List<string> dsp = new();
        }

        private static Vector3 Position(FMOD.VECTOR value) => new(value.x,value.y,value.z);
        private void SampleSpatial(GameplayCameraRig camera)
        {
            if (owner == null || camera == null || camera.LocalAudioListener == null) return;
            var options = GameOptionsService.EnsureInitialized().Current;
            foreach (var pair in sounds)
            {
                var instance = pair.Value;
                if (instance.getPlaybackState(out var state) != FMOD.RESULT.OK || state == FMOD.Studio.PLAYBACK_STATE.STOPPED) continue;
                var row = new SpatialSample { location=edgeLocation, element=element.ToString(), handle=pair.Key, state=state.ToString(),
                    player=owner.transform.position, camera=camera.transform.position, listener=camera.LocalAudioListener.transform.position,
                    listeners=StudioListener.ListenerCount, userMaster=options.MasterVolume, userEffects=options.EffectsVolume };
                row.attributesResult=instance.get3DAttributes(out var attributes).ToString(); row.source=Position(attributes.position);
                if (RuntimeManager.StudioSystem.getListenerAttributes(0,out var listener) == FMOD.RESULT.OK) row.fmodListener=Position(listener.position);
                row.geometricDistance=Vector3.Distance(row.source,row.fmodListener);
                row.oldCameraDistance=Vector3.Distance(row.source,new Vector3(row.camera.x,row.camera.y,row.player.z-10));
                row.distanceResult=instance.getParameterByName("Distance",out row.distance,out row.finalDistance).ToString();
                row.phaseResult=instance.getParameterByName("Phase",out row.phase,out row.finalPhase).ToString();
                instance.getVolume(out row.eventVolume,out row.eventFinalVolume); // Not a measured output gain.
                instance.getTimelinePosition(out row.timelineMs);
                if (instance.getDescription(out var description)==FMOD.RESULT.OK)
                { description.getID(out var guid); row.guid=guid.ToString(); description.getPath(out row.path); }
                var result=instance.getChannelGroup(out var group); row.groupResult=result.ToString();
                if (result==FMOD.RESULT.OK)
                {
                    ReadGroup(group,row.dsp,0);
                    if (group.getDSP(FMOD.CHANNELCONTROL_DSP_INDEX.HEAD,out var meter)==FMOD.RESULT.OK)
                    {
                        meter.setMeteringEnabled(false,true); // Diagnostic event only; ends with this instance.
                        var read=meter.getMeteringInfo(IntPtr.Zero,out var output); row.meterResult=read.ToString();
                        if (read==FMOD.RESULT.OK) for(int i=0;i<output.numchannels;i++)
                        { row.peak=Mathf.Max(row.peak,output.peaklevel[i]); row.rms=Mathf.Max(row.rms,output.rmslevel[i]); }
                    }
                }
                Write("spatial-sample",JsonUtility.ToJson(row));
            }
        }

        private static void ReadGroup(FMOD.ChannelGroup group,List<string> values,int depth)
        {
            if (depth>8 || values.Count>=128) return;
            if (group.getNumDSPs(out int count)==FMOD.RESULT.OK)
                for(int i=0;i<count && values.Count<128;i++) if(group.getDSP(i,out var dsp)==FMOD.RESULT.OK) ReadDsp(dsp,values,$"g{depth}/{i}");
            if (group.getNumChannels(out count)==FMOD.RESULT.OK)
                for(int i=0;i<count && values.Count<128;i++) if(group.getChannel(i,out var channel)==FMOD.RESULT.OK && channel.getNumDSPs(out int n)==FMOD.RESULT.OK)
                    for(int j=0;j<n && values.Count<128;j++) if(channel.getDSP(j,out var dsp)==FMOD.RESULT.OK) ReadDsp(dsp,values,$"g{depth}/c{i}/{j}");
            if (group.getNumGroups(out count)==FMOD.RESULT.OK)
                for(int i=0;i<count && values.Count<128;i++) if(group.getGroup(i,out var child)==FMOD.RESULT.OK) ReadGroup(child,values,depth+1);
        }
        private static void ReadDsp(FMOD.DSP dsp,List<string> values,string path)
        {
            if(dsp.getType(out var type)!=FMOD.RESULT.OK) return;
            dsp.getBypass(out bool bypass);
            var text=new StringBuilder($"{path}:{type};bypass={bypass}");
            if(dsp.getNumParameters(out int count)==FMOD.RESULT.OK)
                for(int i=0;i<count;i++) if(dsp.getParameterInfo(i,out var info)==FMOD.RESULT.OK)
                {
                    string name=Encoding.UTF8.GetString(info.name).TrimEnd('\0');
                    if(info.type==FMOD.DSP_PARAMETER_TYPE.FLOAT && dsp.getParameterFloat(i,out float number)==FMOD.RESULT.OK) text.Append($";{name}={number:R}");
                    else if(info.type==FMOD.DSP_PARAMETER_TYPE.INT && dsp.getParameterInt(i,out int integer)==FMOD.RESULT.OK) text.Append($";{name}={integer}");
                }
            values.Add(text.ToString());
        }
    }
}
