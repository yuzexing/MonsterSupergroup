namespace AstralShift.HellMaiden.Player.Attacks
{
	public interface ILeveleable
	{
		void LevelUp();

		string GetDescription();

		string GetName();

		int GetLevel();

		void ResetAttack();

		LeveleableType GetLeveleableType();

		LeveleableAttribute[] GetAttributes();
	}
}
