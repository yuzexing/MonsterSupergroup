using System;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.Gameplay.Combat
{
    public sealed partial class PlayerBuildRuntime
    {
        // NetworkCombat subscribes only in diagnostic investigation mode. Gameplay has no reverse
        // assembly dependency; a failed observer can never replace a gameplay result or exception.
        public event Action<string, ulong> BuildMutationStarted;
        public event Action<string, ulong, Exception> BuildMutationObserved;
        private int observedBuildMutationDepth;
        private ulong nextObservedBuildMutation, observedBuildMutation;
        private bool BeginObservedBuildMutation(string operation)
        {
            if (BuildMutationObserved == null) return false;
            observedBuildMutationDepth++;
            if (observedBuildMutationDepth == 1)
            {
                observedBuildMutation = ++nextObservedBuildMutation;
                try { BuildMutationStarted?.Invoke(operation, observedBuildMutation); }
                catch (Exception error) { ReportBuildObserverFailure(operation, error); }
            }
            return true;
        }

        private void EndObservedBuildMutation(bool observed, string operation, Exception failure)
        {
            if (!observed || --observedBuildMutationDepth != 0) return;
            try { BuildMutationObserved?.Invoke(operation, observedBuildMutation, failure); }
            catch (Exception error) { ReportBuildObserverFailure(operation, error); }
        }
        private static void ReportBuildObserverFailure(string operation, Exception error) =>
            CombatEvidence.ReportCaptureFailure(new DiagnosticRecord { stage = "investigation.state",
                role = "PlayerBuild", operation = operation }, error);
    }
}
