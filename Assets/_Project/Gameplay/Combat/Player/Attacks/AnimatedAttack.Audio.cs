using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
    public partial class AnimatedAttack
    {
        [Header("Source staged audio (optional)")]
        [SerializeField, HideInInspector] private int audioAdaptationVersion;
        [SerializeField] private EventReference stagedSound;
        [SerializeField] private string completionSoundParameter = "";
        private EventInstance stagedSoundInstance;
        private PARAMETER_ID completionSoundParameterId;
        private bool hasCompletionSoundParameter;
        private float stagedSoundAge;
        private uint soundGeneration;

        private void StartStagedSound()
        {
            if (stagedSound.IsNull || stagedSoundInstance.isValid()) return;
            if (!OptionalAudio.TryGetEventDescription(stagedSound, out var description)) return;
            hasCompletionSoundParameter = false;
            if (!string.IsNullOrEmpty(completionSoundParameter))
            {
                if (description.getParameterDescriptionByName(completionSoundParameter, out var parameter) != FMOD.RESULT.OK)
                {
                    Debug.LogWarning($"[Audio] Missing staged parameter {completionSoundParameter} on {name}.", this);
                    return;
                }
                completionSoundParameterId = parameter.id;
                hasCompletionSoundParameter = true;
            }
            stagedSoundInstance = OptionalAudio.CreateInstance(stagedSound);
            if (!stagedSoundInstance.isValid()) return;
            soundGeneration++;
            OptionalAudio.AttachInstanceToGameObject(stagedSoundInstance, gameObject);
            if (hasCompletionSoundParameter) stagedSoundInstance.setParameterByID(completionSoundParameterId, 0);
            // Set the existing event's timeline before starting: no fresh intro followed by a seek.
            if (stagedSoundAge > 0) stagedSoundInstance.setTimelinePosition(Mathf.RoundToInt(stagedSoundAge * 1000));
            var result = stagedSoundInstance.start();
            AttackAudioAudit.Record(this, soundGeneration, "start", stagedSoundInstance, result, stagedSoundAge);
        }

        private void CompleteStagedSound()
        {
            if (!stagedSoundInstance.isValid()) return;
            // Original binding is on the end of the exit animation, not the end of the damage window.
            if (hasCompletionSoundParameter)
            {
                var result = stagedSoundInstance.setParameterByID(completionSoundParameterId, 1);
                AttackAudioAudit.Record(this, soundGeneration, "completion-parameter", stagedSoundInstance, result, 1);
            }
            StopStagedSound(false);
        }

        private void StopStagedSound(bool immediate)
        {
            if (!stagedSoundInstance.isValid()) return;
            var result = stagedSoundInstance.stop(immediate ? FMOD.Studio.STOP_MODE.IMMEDIATE : FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            AttackAudioAudit.Record(this, soundGeneration, immediate ? "cancel" : "stop", stagedSoundInstance, result);
            result = stagedSoundInstance.release();
            AttackAudioAudit.Record(this, soundGeneration, "release", stagedSoundInstance, result);
            stagedSoundInstance.clearHandle();
            hasCompletionSoundParameter = false;
        }
    }
}
