namespace AstralShift.BehaviourGraph.Flow
{
	public interface IBehaviourGraphWeighted
	{
		float GetTotalWeight();

		float GetCurrentWeight();

		void ApplyPityChance();

		void RestartWeight();
	}
}
