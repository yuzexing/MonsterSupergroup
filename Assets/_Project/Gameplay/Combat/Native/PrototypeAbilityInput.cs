namespace MonsterSupergroup.Gameplay.Combat
{
    /// <summary>Stable IDs shared by owner input and authoritative ability selection.</summary>
    public enum PrototypeAbilityId : byte
    {
        Gluttony = 1,
        Music = 2,
        Allure = 3
    }

    public enum PrototypeAbilityAction : byte
    {
        Primary,
        Secondary,
        Decoy,
        Rhythm
    }
}
