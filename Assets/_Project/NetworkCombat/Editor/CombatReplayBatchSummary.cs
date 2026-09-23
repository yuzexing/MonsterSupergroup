using System;
using System.Collections.Generic;
using MonsterSupergroup.NetworkCombat.Diagnostics;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    [Serializable] public sealed class CombatReplayBatchSummary
    {
        public int total, passed, diverged, unreliable, exitCode;
        public string outcome;

        public static CombatReplayBatchSummary Classify(IEnumerable<ReplayReport> reports)
        {
            var summary = new CombatReplayBatchSummary();
            foreach (var report in reports)
            {
                summary.total++;
                if (report == null || !report.reliable || report.executed <= 0) summary.unreliable++;
                else if (report.passed) summary.passed++;
                else summary.diverged++;
            }
            summary.exitCode = summary.total == 0 || summary.unreliable > 0 ? 3 : summary.diverged > 0 ? 2 : 0;
            summary.outcome = summary.total == 0 ? "NoFixtures" : summary.exitCode == 3 ? "Unverifiable" : summary.exitCode == 2 ? "Diverged" : "Matched";
            return summary;
        }
    }
}
