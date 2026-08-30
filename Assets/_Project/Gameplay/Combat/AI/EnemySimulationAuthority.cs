using System;
using UnityEngine;

namespace AstralShift.HellMaiden.AI
{
	public enum EnemySimulationRole : byte
	{
		Standalone = 0,
		Frozen = 1,
		ClientOwner = 2,
		Replica = 3,
		ServerFallback = 4,
		ServerAuthoritative = 5
	}

	public enum EnemySimulationMode : byte
	{
		NormalClient = 0,
		EliteClient = 1,
		BossServer = 2
	}

	/// <summary>
	/// Replicated attack-facing state. This is deliberately smaller than the
	/// EnemyController FSM: observers do not need pathfinding, stuck or internal
	/// attack-script state in order to reconstruct an attack presentation.
	/// </summary>
	public enum EnemyAttackPresentationPhase : byte
	{
		Inactive = 0,
		Warning = 1,
		Active = 2,
		Recovery = 3,
		Cancelled = 4
	}

	/// <summary>
	/// Transport-neutral gameplay authority for one Enemy. Mirror decides the role,
	/// while EnemyAIManager only consumes these capabilities.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class EnemySimulationAuthority : MonoBehaviour
	{
		[SerializeField] private bool networkManaged;
		[SerializeField] private EnemySimulationMode simulationMode =
			EnemySimulationMode.NormalClient;
		[SerializeField] private bool combatDecisionSimulationEnabled;

		private EnemySimulationRole _role = EnemySimulationRole.Standalone;
		private bool _hasPendingDiscontinuity;

		public EnemySimulationRole Role => _role;

		public EnemySimulationMode SimulationMode => simulationMode;

		public uint SimulationOwnerPlayerId { get; private set; }

		public uint AggroTargetPlayerId { get; private set; }

		public uint AssignmentEpoch { get; private set; }

		public bool RunsNavigation =>
			_role == EnemySimulationRole.Standalone ||
			_role == EnemySimulationRole.ClientOwner ||
			_role == EnemySimulationRole.ServerFallback ||
			_role == EnemySimulationRole.ServerAuthoritative;

		public bool RunsCombatDecisions =>
			RunsNavigation && (!networkManaged || combatDecisionSimulationEnabled);

		public bool RunsRubberBand =>
			_role == EnemySimulationRole.Standalone ||
			_role == EnemySimulationRole.ClientOwner ||
			_role == EnemySimulationRole.ServerAuthoritative;

		public bool ConsumesSnapshots => _role == EnemySimulationRole.Replica;

		public event Action<EnemySimulationRole, EnemySimulationRole> RoleChanged;

		public void ConfigureNetworkManaged(bool enableCombatDecisions = false)
		{
			networkManaged = true;
			combatDecisionSimulationEnabled = enableCombatDecisions;
			ApplyRole(EnemySimulationRole.Frozen, 0u, 0u, 0u);
		}

		public void ConfigureNetworkManaged(
			EnemySimulationMode mode,
			bool enableCombatDecisions = false)
		{
			simulationMode = mode;
			ConfigureNetworkManaged(enableCombatDecisions);
		}

		public void SetCombatDecisionSimulationEnabled(bool enabled)
		{
			combatDecisionSimulationEnabled = enabled;
		}

		public void ApplyRole(
			EnemySimulationRole role,
			uint simulationOwnerPlayerId,
			uint aggroTargetPlayerId,
			uint assignmentEpoch)
		{
			EnemySimulationRole previous = _role;
			_role = role;
			SimulationOwnerPlayerId = simulationOwnerPlayerId;
			AggroTargetPlayerId = aggroTargetPlayerId;
			AssignmentEpoch = assignmentEpoch;
			if (previous != role)
			{
				RoleChanged?.Invoke(previous, role);
			}
		}

		public void MarkDiscontinuity()
		{
			if (RunsNavigation)
			{
				_hasPendingDiscontinuity = true;
			}
		}

		public bool ConsumeDiscontinuity()
		{
			bool result = _hasPendingDiscontinuity;
			_hasPendingDiscontinuity = false;
			return result;
		}
	}
}
