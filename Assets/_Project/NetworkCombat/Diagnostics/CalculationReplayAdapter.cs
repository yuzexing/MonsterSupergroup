using System;
using MonsterSupergroup.GAS;
using Newtonsoft.Json.Linq;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    /// <summary>Replays business calculation inputs; saved outcomes are assertions, never execution instructions.</summary>
    public sealed class CalculationReplayAdapter : IReplayAdapter
    {
        public string Domain { get; }
        private OutputStatisticsState outputState;
        public CalculationReplayAdapter(string domain) { Domain = domain; }
        public void RestoreReplayState(JToken checkpoint)
        {
            if (Domain == "output_stats") outputState = EvidenceJson.Convert<OutputStatisticsState>(checkpoint);
            else if (EvidenceJson.Convert<CalculationReplayState>(checkpoint)?.version != 1)
                throw new InvalidOperationException("UnsupportedCalculationCheckpoint");
        }
        public JToken CaptureReplayState() => CombatReplayAdapter.Token(Domain == "output_stats" ? (object)outputState : new CalculationReplayState());
        public JToken Execute(string operation, JArray arguments, JToken boundary)
        {
            if (arguments == null || arguments.Count != 1) throw new InvalidOperationException("MissingCalculationInput");
            if (Domain == "damage" && operation == "Calculate")
                return CombatReplayAdapter.Token(DamageCalculation.Replay(EvidenceJson.Convert<DamageCalculationInput>(arguments[0])));
            if (Domain == "weapon_stats" && operation == "Rebuild")
                return CombatReplayAdapter.Token(EvidenceJson.Convert<AttackStatsEvidenceInput>(arguments[0]).Rebuild());
            if (Domain == "output_stats" && operation == "ApplyDamage")
            {
                outputState = OutputStatistics.ApplyDamage(outputState, EvidenceJson.Convert<OutputStatisticInput>(arguments[0]));
                return CombatReplayAdapter.Token(outputState);
            }
            throw new InvalidOperationException("UnsupportedCalculationOperation: " + Domain + "." + operation);
        }
    }
}
