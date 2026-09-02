using System;

namespace MonsterSupergroup.GAS
{
    [Serializable]
    public abstract class EquipmentModifierParameters
    {
        public virtual bool TryGetNumericParameter(int index, out float value)
        {
            value = 0f;
            return false;
        }
    }

    [Serializable]
    public abstract class PerkModifierParameters
    {
        public virtual bool TryGetNumericParameter(int index, out float value)
        {
            value = 0f;
            return false;
        }
    }
}
