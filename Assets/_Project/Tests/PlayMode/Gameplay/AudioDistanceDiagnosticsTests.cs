#if UNITY_EDITOR
using System.Collections;
using FMODUnity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class AudioDistanceDiagnosticsTests
    {
        [UnityTest]
        public IEnumerator BreathSameInstance_DistanceDrivesLowpass_AndReturnsAtOriginalDistance()
        {
            Assert.That(StudioListener.ListenerCount, Is.Zero, "Diagnostic requires an isolated listener.");
            var listener = new GameObject("Isolated fixed-depth listener");
            listener.transform.position = new Vector3(0,0,-10);
            listener.AddComponent<StudioListener>();
            var instance = OptionalAudio.CreateInstance(FMOD.GUID.Parse("b865f76c-9c2a-4679-9709-a288f22c9619"));
            var cutoff = new float[3];
            try
            {
                Assert.That(instance.isValid(), Is.True);
                instance.set3DAttributes(RuntimeUtils.To3DAttributes(Vector3.zero));
                Assert.That(instance.setParameterByName("Phase",0), Is.EqualTo(FMOD.RESULT.OK));
                Assert.That(instance.start(), Is.EqualTo(FMOD.RESULT.OK));
                float[] distances = {10,18,10};
                for(int sample=0;sample<distances.Length;sample++)
                {
                    float distance=distances[sample];
                    var source=new Vector3(Mathf.Sqrt(distance*distance-100),0,0);
                    Assert.That(instance.set3DAttributes(RuntimeUtils.To3DAttributes(source)), Is.EqualTo(FMOD.RESULT.OK));
                    // Let FMOD consume 3D updates and its parameter automation; never set Distance.
                    yield return new WaitForSecondsRealtime(.25f);
                    RuntimeManager.StudioSystem.flushCommands();
                    Assert.That(instance.getParameterByName("Distance",out _,out float actual), Is.EqualTo(FMOD.RESULT.OK));
                    Assert.That(actual, Is.EqualTo(distance).Within(.01f));
                    Assert.That(instance.getParameterByName("Phase",out _,out float phase), Is.EqualTo(FMOD.RESULT.OK));
                    Assert.That(phase, Is.Zero, "Keep the same playback stage and instance.");
                    Assert.That(instance.getChannelGroup(out var group), Is.EqualTo(FMOD.RESULT.OK));
                    Assert.That(group.getNumDSPs(out int count), Is.EqualTo(FMOD.RESULT.OK));
                    bool found=false;
                    for(int i=0;i<count;i++)
                    {
                        group.getDSP(i,out var dsp); dsp.getType(out var type);
                        if(type!=FMOD.DSP_TYPE.MULTIBAND_EQ) continue;
                        dsp.getParameterInt((int)FMOD.DSP_MULTIBAND_EQ.A_FILTER,out int filter);
                        if(filter!=(int)FMOD.DSP_MULTIBAND_EQ_FILTER_TYPE.LOWPASS_12DB) continue;
                        Assert.That(dsp.getParameterFloat((int)FMOD.DSP_MULTIBAND_EQ.A_FREQUENCY,out cutoff[sample]),Is.EqualTo(FMOD.RESULT.OK));
                        found=true; break;
                    }
                    Assert.That(found,Is.True,"Read the real bank's event low-pass, not a synthetic DSP.");
                    Debug.Log($"AUDIO-DISTANCE same-instance={instance.handle} distance={actual:R} phase={phase:R} lowpassHz={cutoff[sample]:R}");
                }
                Assert.That(cutoff[1],Is.LessThan(cutoff[0]),"Moving only the world source must lower the cutoff.");
                Assert.That(cutoff[2],Is.EqualTo(cutoff[0]).Within(1),"Returning to the original distance must restore the same cutoff.");
            }
            finally
            {
                instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE); instance.release();
                Object.DestroyImmediate(listener);
            }
        }
    }
}
#endif
