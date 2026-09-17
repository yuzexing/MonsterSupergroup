using System;
using FMOD.Studio;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
    // Passive edge observation for tests and explicit listening fixtures. No per-frame production logging.
    public static class AttackAudioAudit
    {
        public struct Entry
        {
            public int Instance;
            public uint Generation;
            public string ObjectName, Operation, Event;
            public long Handle;
            public float Value;
            public FMOD.RESULT Result;
        }
        public static event Action<Entry> Changed;
        public static void Record(Component owner, uint generation, string operation, EventInstance sound, FMOD.RESULT result, float value = 0)
        {
            if (Changed == null) return;
            string path = "";
            if (sound.getDescription(out var description) == FMOD.RESULT.OK) description.getPath(out path);
            Changed(new Entry { Instance = owner.GetInstanceID(), Generation = generation, ObjectName = owner.name,
                Operation = operation, Event = path, Handle = sound.handle.ToInt64(), Value = value, Result = result });
        }
    }
}
