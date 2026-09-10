using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FMODUnity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class AudioMigrationPlayModeTests
    {
        [TestCase("1235e4b8-dcb5-43e8-8bb7-41a363bff4b8")]
        [TestCase("b865f76c-9c2a-4679-9709-a288f22c9619")]
        [TestCase("840d4d3b-6223-4aab-a508-f0bcd8e4de60")]
        [TestCase("0ddbe74c-0c1e-4afc-a293-557438dbd8e0")]
        [TestCase("d34df8fa-1ca2-43a3-b073-e782b614760a")]
        [TestCase("00f986d5-ee9b-42ea-87c4-cd2e653e5099")]
        [TestCase("5a78060c-ce45-44ff-af63-a975bac85129")]
        [TestCase("0c0db1f5-703c-45a4-b90c-1b1050ef5b6f")]
        [TestCase("5a48cc5f-7052-431c-a1e4-0beb9095c74e")]
        [TestCase("57a4d957-64b2-417b-b722-ff987e78629a")]
        [TestCase("2df3bdbe-8ccb-4f00-b5b1-6fdb3ac7bddf")]
        [TestCase("8f40b0c2-bc01-4cb3-adf2-a95cb6c4f4d0")]
        [TestCase("a32506b7-fbfd-4e62-ad46-7ce4a0bee1a3")]
        [TestCase("1cdd3260-fa38-4f3e-8440-a13293578e2e")]
        [TestCase("284fd736-4c4d-434a-aee6-e7e659c8af71")]
        [TestCase("32e577a5-fc21-44ce-9600-85ed8e145ecf")]
        public void MigratedCombatEvent_CreatesPlayableInstance(string guid)
        {
            var instance = OptionalAudio.CreateInstance(FMOD.GUID.Parse(guid));
            try
            {
                Assert.That(instance.isValid(), Is.True, guid);
                Assert.That(instance.set3DAttributes(RuntimeUtils.To3DAttributes(Vector3.zero)), Is.EqualTo(FMOD.RESULT.OK));
                Assert.That(instance.start(), Is.EqualTo(FMOD.RESULT.OK));
            }
            finally
            {
                instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                instance.release();
            }
        }

        [Test]
        public void EmptyReferences_SkipPlaybackAndAttachment()
        {
            var reference = default(EventReference);
            Assert.That(OptionalAudio.CreateInstance(reference).isValid(), Is.False);
            Assert.That(OptionalAudio.CreateInstance((string)null).isValid(), Is.False);
            OptionalAudio.PlayOneShot(reference);
            OptionalAudio.PlayOneShot("");
            OptionalAudio.PlayOneShotAttached(reference, null);
            OptionalAudio.AttachInstanceToGameObject(default, (GameObject)null);
            Assert.That(OptionalAudio.TryGetEventDescription(reference, out _), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MissingGuidAndPath_SkipRepeatedPlaybackWithOneWarningEach()
        {
            var reference = new EventReference { Guid = FMOD.GUID.Parse(Guid.NewGuid().ToString()) };
            string path = "event:/missing-audio-test/" + Guid.NewGuid();
            int warnings = 0;
            Application.LogCallback observe = (message, trace, type) =>
            {
                if (message.StartsWith("[Audio] Optional FMOD event is unavailable")) warnings++;
            };
            Application.logMessageReceived += observe;
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    Assert.That(OptionalAudio.CreateInstance(reference).isValid(), Is.False);
                    Assert.That(OptionalAudio.CreateInstance(path).isValid(), Is.False);
                    OptionalAudio.PlayOneShot(reference);
                    OptionalAudio.PlayOneShot(path);
                }
                Assert.That(warnings, Is.EqualTo(2));
            }
            finally { Application.logMessageReceived -= observe; }
        }

        [UnityTest]
        public IEnumerator MissingEmitterAndParameterEvent_DoNotInterruptActivation()
        {
            var target = new GameObject("Missing optional audio fixture");
            target.SetActive(false);
            try
            {
                var emitter = target.AddComponent<StudioEventEmitter>();
                emitter.EventReference = new EventReference { Guid = FMOD.GUID.Parse(Guid.NewGuid().ToString()) };
                emitter.Preload = true;
                emitter.PlayEvent = EmitterGameEvent.ObjectStart;
                var trigger = target.AddComponent<StudioParameterTrigger>();
                trigger.Emitters = new[] { null, new EmitterRef { Target = emitter, Params = new[] { new ParamRef { Name = "unused", Value = 1 } } } };
                target.SetActive(true);
                yield return null;
                trigger.TriggerParameters();
                emitter.Play();
                Assert.That(emitter.EventInstance.isValid(), Is.False);
                Assert.That(target.activeInHierarchy, Is.True);
            }
            finally { Object.DestroyImmediate(target); }
        }

        [Test]
        public void ReloadedBank_RetriesPreviouslyMissingEvent()
        {
            var reference = new EventReference { Guid = FMOD.GUID.Parse("5a48cc5f-7052-431c-a1e4-0beb9095c74e") };
            Assert.That(OptionalAudio.TryGetEventDescription(reference, out _), Is.True);
            RuntimeManager.UnloadBank("plr");
            RuntimeManager.StudioSystem.flushCommands();
            try
            {
                Assert.That(OptionalAudio.CreateInstance(reference).isValid(), Is.False);
            }
            finally
            {
                RuntimeManager.LoadBank("plr");
                RuntimeManager.StudioSystem.flushCommands();
            }
            Assert.That(OptionalAudio.TryGetEventDescription(reference, out _), Is.True);
        }

        [UnityTest]
        public IEnumerator MigratedHurtSound_ProducesNonSilentMixerOutput()
        {
            var listener = new GameObject("Audio output test listener");
            listener.AddComponent<StudioListener>();
#if UNITY_EDITOR
            bool wasEditorMuted = UnityEditor.EditorUtility.audioMasterMute;
            UnityEditor.EditorUtility.audioMasterMute = false;
#endif
            Assert.That(RuntimeManager.CoreSystem.getMasterChannelGroup(out var master), Is.EqualTo(FMOD.RESULT.OK));
            Assert.That(master.getDSP(FMOD.CHANNELCONTROL_DSP_INDEX.HEAD, out var meter), Is.EqualTo(FMOD.RESULT.OK));
            Assert.That(meter.setMeteringEnabled(false, true), Is.EqualTo(FMOD.RESULT.OK));
            var instance = OptionalAudio.CreateInstance(FMOD.GUID.Parse("57a4d957-64b2-417b-b722-ff987e78629a"));
            try
            {
                Assert.That(instance.start(), Is.EqualTo(FMOD.RESULT.OK));
                float peak = 0f;
                float deadline = Time.realtimeSinceStartup + 2f;
                while (Time.realtimeSinceStartup < deadline && peak <= 0.00001f)
                {
                    yield return null;
                    Assert.That(meter.getMeteringInfo(IntPtr.Zero, out var output), Is.EqualTo(FMOD.RESULT.OK));
                    for (int i = 0; i < output.numchannels; i++) peak = Mathf.Max(peak, output.rmslevel[i]);
                }
                Assert.That(peak, Is.GreaterThan(0.00001f), "A valid event must contain audible sample data.");
            }
            finally
            {
                meter.setMeteringEnabled(false, false);
                instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                instance.release();
                Object.DestroyImmediate(listener);
#if UNITY_EDITOR
                UnityEditor.EditorUtility.audioMasterMute = wasEditorMuted;
#endif
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void MissingBank_DoesNotBlockOtherBanksOrSampleLoading(bool missingFirst)
        {
            var reference = new EventReference { Guid = FMOD.GUID.Parse("5a48cc5f-7052-431c-a1e4-0beb9095c74e") };
            Assert.That(OptionalAudio.TryGetEventDescription(reference, out _), Is.True);
            var settings = Settings.Instance;
            var previousType = settings.BankLoadType;
            var previousBanks = settings.BanksToLoad;
            bool previousSamples = settings.AutomaticSampleLoading;
            RuntimeManager.UnloadBank("plr");
            RuntimeManager.StudioSystem.flushCommands();
            try
            {
                string missing = "missing-audio-bank-" + Guid.NewGuid();
                settings.BankLoadType = BankLoadType.Specified;
                settings.BanksToLoad = missingFirst ? new List<string> { missing, "plr" } : new List<string> { "plr", missing };
                settings.AutomaticSampleLoading = true;
                typeof(RuntimeManager).GetMethod("LoadBanks", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(Resources.FindObjectsOfTypeAll<RuntimeManager>()[0], new object[] { settings });
                Assert.That(OptionalAudio.TryGetEventDescription(reference, out var description), Is.True);
                Assert.That(description.getSampleLoadingState(out var state), Is.EqualTo(FMOD.RESULT.OK));
                Assert.That(state, Is.EqualTo(FMOD.Studio.LOADING_STATE.LOADED));
            }
            finally
            {
                settings.BankLoadType = previousType;
                settings.BanksToLoad = previousBanks;
                settings.AutomaticSampleLoading = previousSamples;
                if (!RuntimeManager.HasBankLoaded("plr")) RuntimeManager.LoadBank("plr");
            }
        }

        [Test]
        public void MissingUnityAudioClip_CompletesInteraction()
        {
            var target = new GameObject("Missing Unity audio clip fixture");
            target.SetActive(false);
            try
            {
                var interaction = target.AddComponent<AstralShift.QTI.Interactions.Audio.AudioPlayOneShotInteraction>();
                interaction.mode = AstralShift.QTI.Interactions.Audio.AudioPlayOneShotInteraction.AudioPlayOneShotInteractionMode.Position2D;
                target.SetActive(true);
                int completions = 0;
                interaction.Interact(null, () => completions++);
                Assert.That(completions, Is.EqualTo(1));
                LogAssert.NoUnexpectedReceived();
            }
            finally { Object.DestroyImmediate(target); }
        }
    }
}
