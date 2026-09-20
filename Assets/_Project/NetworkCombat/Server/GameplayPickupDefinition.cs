using System;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public enum PickupEffect : byte { None, Experience, RestoreHealth }

    [CreateAssetMenu(menuName = "Network Combat/Pickup Definition")]
    public sealed class GameplayPickupDefinition : ScriptableObject
    {
        public uint id;
        public PickupEffect effect;
        public float value;
        public NetworkExperienceGem prefab;
        [Min(0)] public int worldLimit;
        [Min(0)] public int idleCapacity = 100;
        public float backDuration = .3f, jumpDuration = .5f, backDistance = 1, jumpHeight = 1;
        public AnimationCurve backCurve = AnimationCurve.Linear(0, 0, 1, 1);
        public AnimationCurve jumpCurve = AnimationCurve.Linear(0, 0, 1, 1);

        public PickupDefinitionSnapshot Capture()
        {
            if (id == 0 || effect == PickupEffect.None || prefab == null || idleCapacity < 0 || worldLimit < 0 ||
                !ExperienceParameters.Finite(value) || value < 0 ||
                !ExperienceParameters.Finite(backDuration) || !ExperienceParameters.Finite(jumpDuration) ||
                !ExperienceParameters.Finite(backDistance) || !ExperienceParameters.Finite(jumpHeight) ||
                backDuration <= 0 || jumpDuration <= 0 || backDistance < 0 || jumpHeight < 0 ||
                backCurve == null || jumpCurve == null || backCurve.length == 0 || jumpCurve.length == 0)
                throw new ArgumentException("Invalid pickup definition: " + name);
            return new PickupDefinitionSnapshot(this);
        }
    }

    public sealed class PickupDefinitionSnapshot
    {
        public readonly uint Id;
        public readonly PickupEffect Effect;
        public readonly float Value, BackDuration, JumpDuration, BackDistance, JumpHeight;
        public readonly int WorldLimit, IdleCapacity;
        public readonly NetworkExperienceGem Prefab;
        public readonly AnimationCurve BackCurve, JumpCurve;
        public float FlightDuration => BackDuration + JumpDuration;
        public PickupDefinitionSnapshot(GameplayPickupDefinition source)
        {
            Id = source.id; Effect = source.effect; Value = source.value; Prefab = source.prefab;
            WorldLimit = source.worldLimit; IdleCapacity = source.idleCapacity;
            BackDuration = source.backDuration; JumpDuration = source.jumpDuration;
            BackDistance = source.backDistance; JumpHeight = source.jumpHeight;
            BackCurve = new AnimationCurve(source.backCurve.keys);
            JumpCurve = new AnimationCurve(source.jumpCurve.keys);
        }
    }
}
