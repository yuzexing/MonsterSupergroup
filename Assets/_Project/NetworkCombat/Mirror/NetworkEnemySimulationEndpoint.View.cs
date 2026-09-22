using AstralShift.HellMaiden.CameraFX;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkEnemySimulationEndpoint
    {
        private GameplayCameraRig viewRig;
        private double nextViewReport;
        private uint viewSequence;
        private PlayerViewReport serverView;

        private void ReportLocalView()
        {
            if (Time.unscaledTimeAsDouble < nextViewReport || BootGameplayNetworkManager.CombatHasEnded) return;
            nextViewReport = Time.unscaledTimeAsDouble + .1;
            if (viewRig == null) viewRig = FindFirstObjectByType<GameplayCameraRig>();
            if (viewRig == null || viewRig.BoundPlayer == null || viewRig.BoundPlayer.GetComponentInParent<NetworkIdentity>() != netIdentity ||
                viewRig.GameCamera == null || !viewRig.GameCamera.isActiveAndEnabled) return;
            Bounds bounds = GameplayCameraGeometry.ViewBounds(viewRig.GameCamera);
            CmdReportView(new PlayerViewReport {
                Round = NetworkEnemySimulationWorld.CurrentRound, Sequence = viewSequence = NextSequence(viewSequence),
                SampledAt = NetworkTime.predictedTime, Center = bounds.center, Size = bounds.size
            });
        }

        [Command(channel = Channels.Unreliable)]
        private void CmdReportView(PlayerViewReport report, NetworkConnectionToClient sender = null)
        {
            if (sender == null || sender != connectionToClient || sender.identity != netIdentity || !sender.isReady || !sender.isAuthenticated ||
                !report.IsValid(NetworkEnemySimulationWorld.CurrentRound, NetworkTime.time) ||
                (serverView.Round == report.Round && !EnemySimulationSequence.IsNewer(report.Sequence, serverView.Sequence))) return;
            serverView = report;
        }

        internal bool TryGetServerView(out Bounds bounds)
        {
            bounds = serverView.Bounds;
            return isServer && serverView.IsFresh(NetworkEnemySimulationWorld.CurrentRound, NetworkTime.time);
        }
    }
}
