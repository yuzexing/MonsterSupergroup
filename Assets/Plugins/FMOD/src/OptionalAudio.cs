using System;
using System.Collections.Generic;
using UnityEngine;

namespace FMODUnity
{
    /// <summary>Optional game presentation. Missing events return invalid handles, never abort gameplay.</summary>
    public static class OptionalAudio
    {
        private static readonly HashSet<FMOD.GUID> unavailable = new HashSet<FMOD.GUID>();
        private static readonly HashSet<string> unavailablePaths = new HashSet<string>();
        private static readonly HashSet<string> reported = new HashSet<string>();
        private static readonly bool dedicatedServer = Array.Exists(Environment.GetCommandLineArgs(), argument =>
            string.Equals(argument, "--dedicated-server", StringComparison.OrdinalIgnoreCase));

        public static bool Enabled
        {
            get
            {
#if UNITY_SERVER
                return false;
#else
                return !dedicatedServer;
#endif
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            unavailable.Clear();
            unavailablePaths.Clear();
            reported.Clear();
        }

        // Retry previously unavailable events when banks are loaded or unloaded.
        internal static void OnBanksChanged()
        {
            unavailable.Clear();
            unavailablePaths.Clear();
        }

        public static bool TryGetEventDescription(EventReference reference, out FMOD.Studio.EventDescription description)
        {
            return TryGetEventDescription(reference.Guid, out description);
        }

        private static bool TryGetEventDescription(FMOD.GUID guid, out FMOD.Studio.EventDescription description)
        {
            description = default;
            if (!Enabled || guid.IsNull || unavailable.Contains(guid)) return false;
            try
            {
                description = RuntimeManager.GetEventDescription(guid);
                return description.isValid();
            }
            catch (EventNotFoundException)
            {
                unavailable.Add(guid);
                ReportMissing(guid.ToString());
                return false;
            }
        }

        public static FMOD.Studio.EventInstance CreateInstance(EventReference reference) => CreateInstance(reference.Guid);

        public static FMOD.Studio.EventInstance CreateInstance(FMOD.GUID guid)
        {
            if (!TryGetEventDescription(guid, out _)) return default;
            return RuntimeManager.CreateInstance(guid);
        }

        public static FMOD.Studio.EventInstance CreateInstance(string path)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(path) || unavailablePaths.Contains(path)) return default;
            try
            {
                return CreateInstance(RuntimeManager.PathToGUID(path));
            }
            catch (EventNotFoundException)
            {
                unavailablePaths.Add(path);
                ReportMissing(path);
                return default;
            }
        }

        public static void PlayOneShot(EventReference reference, Vector3 position = default) => PlayOneShot(reference.Guid, position);

        public static void PlayOneShot(FMOD.GUID guid, Vector3 position = default)
        {
            PlayOneShot(CreateInstance(guid), position);
        }

        public static void PlayOneShot(string path, Vector3 position = default)
        {
            PlayOneShot(CreateInstance(path), position);
        }

        private static void PlayOneShot(FMOD.Studio.EventInstance instance, Vector3 position)
        {
            if (!instance.isValid()) return;
            instance.set3DAttributes(RuntimeUtils.To3DAttributes(position));
            instance.start();
            instance.release();
        }

        public static void PlayOneShotAttached(EventReference reference, GameObject target)
        {
            if (target == null || !TryGetEventDescription(reference, out _)) return;
            RuntimeManager.PlayOneShotAttached(reference.Guid, target);
        }

        public static void AttachInstanceToGameObject(FMOD.Studio.EventInstance instance, GameObject target)
        {
            if (instance.isValid() && target != null) RuntimeManager.AttachInstanceToGameObject(instance, target);
        }

        public static void AttachInstanceToGameObject(FMOD.Studio.EventInstance instance, Transform target)
        {
            if (instance.isValid() && target != null) RuntimeManager.AttachInstanceToGameObject(instance, target);
        }

        private static void ReportMissing(string key)
        {
            if (reported.Add(key)) Debug.LogWarning($"[Audio] Optional FMOD event is unavailable; continuing without sound: {key}");
        }
    }
}
