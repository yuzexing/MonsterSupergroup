using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat.Diagnostics;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkModifierSelection
    {
        private Dictionary<string, ulong> investigationRevisions;
        private bool investigationSubscribed;
        private string investigationBirth;
        private IDiagnosticSink investigationSink;

        private void AttachInvestigation()
        {
            if (!CombatInvestigationEvidence.Enabled || investigationSubscribed || build == null) return;
            build.BuildMutationStarted += ObserveBuildMutationStarted;
            build.BuildMutationObserved += ObserveBuildMutation;
            investigationSubscribed = true;
        }
        private void DetachInvestigation()
        {
            if (investigationSubscribed && build != null)
            { build.BuildMutationStarted -= ObserveBuildMutationStarted; build.BuildMutationObserved -= ObserveBuildMutation; }
            investigationSubscribed = false;
        }
        private string InvestigationPerspective => isServer ? "Server" : isOwned ? "Owner" : "Replica";
        private object InvestigationIdentity()
        {
            var participant = GetComponent<NetworkRunParticipant>();
            EnsureInvestigationScope(participant);
            return new { participantId = participant != null ? participant.ParticipantId.ToString() : null,
                avatar = netId, connectionEpoch = GetComponent<MirrorNetworkCombatBridge>()?.ConnectionEpoch ?? 0,
                run = participant?.RunId, birth = investigationBirth };
        }
        private void EnsureInvestigationScope(NetworkRunParticipant participant)
        {
            string birth = participant != null ? participant.InvestigationBirth : investigationBirth ?? Guid.NewGuid().ToString("N");
            if (birth != investigationBirth || !ReferenceEquals(investigationSink, CombatEvidence.Sink))
            {
                investigationRevisions?.Clear();
                investigationBirth = birth;
                investigationSink = CombatEvidence.Sink;
            }
        }
        private void TraceState(string domain, string operation, string outcome, Func<object> read,
            string perspective = null, bool continuous = false, string error = null, ulong eventId = 0, ulong operationId = 0)
        {
            if (!CombatInvestigationEvidence.Enabled) return;
            EnsureInvestigationScope(GetComponent<NetworkRunParticipant>());
            perspective ??= InvestigationPerspective;
            string key = domain + "/" + perspective;
            investigationRevisions ??= new Dictionary<string, ulong>();
            investigationRevisions.TryGetValue(key, out ulong previous);
            ulong revision = previous + 1; investigationRevisions[key] = revision;
            CombatInvestigationEvidence.Capture("state", outcome, () => new {
                schemaVersion = 1, entity = netId, domain, stateRevision = revision.ToString(),
                buildRevision = perspective == "Server" ? buildRevision : perspective == "Owner" ? (uint?)ownerAttackBuildRevision : null,
                perspective, baseline = previous == 0, continuous, operationId = operationId == 0 ? null : operationId.ToString(),
                identity = InvestigationIdentity(), state = read(),
                cause = new { operation, outcome, error, eventId = eventId == 0 ? null : eventId.ToString() }
            }, reason: operation, source: netId, target: netId, perspective: perspective);
        }
        private void ObserveBuildMutationStarted(string operation, ulong operationId)
        {
            if (!CombatInvestigationEvidence.Enabled) return;
            string perspective = InvestigationPerspective;
            CombatInvestigationEvidence.Capture("build.begin", "Started", () => new {
                entity = netId, domain = "build", operationId = operationId.ToString(), operation,
                perspective, identity = InvestigationIdentity()
            }, source: netId, target: netId, perspective: perspective);
        }
        private void ObserveBuildMutation(string operation, ulong operationId, Exception failure) => TraceBuild(operation,
            failure == null ? "Completed" : "Failed", error: failure?.GetType().Name, operationId: operationId);
        private void TraceBuild(string operation, string outcome = "Observed", string perspective = null, string error = null, ulong operationId = 0)
        {
            if (!CombatInvestigationEvidence.Enabled) return;
            TraceState("build", operation, outcome, () => new { active = build.IsBuildActive, definition = build.CaptureState() },
                perspective, continuous: investigationSubscribed, error: error, operationId: operationId);
        }
        private void TraceProgression(string operation, string outcome = "Committed", string error = null)
        {
            if (!CombatInvestigationEvidence.Enabled) return;
            // CaptureProgression also includes selection recovery fields. They do not have a
            // complete mutation-boundary contract, so this full snapshot is observed state.
            TraceState("progression", operation, outcome, () => new { authoritative = true,
                experiencePerLevel, snapshot = CaptureProgression() }, "Server", continuous: false, error: error);
        }
        private void TraceSelection(string operation, string outcome = "Observed", ulong eventId = 0, string error = null,
            string perspective = null)
        {
            if (!CombatInvestigationEvidence.Enabled) return;
            perspective ??= InvestigationPerspective;
            TraceState("selection", operation, outcome, () => perspective == "Server" ? (object)new {
                selecting, ownerCanSelect, deferred = OffersDeferred, pendingEventId = PendingEventId.ToString(),
                stage = stage.ToString(), offeredLevel = OfferedLevel, selectedEquipmentId,
                offers = CaptureOffers(serverOffers), retainedOffers = (PlayerUpgradeOfferSnapshot[])restoredOffers.Clone(),
                originalOffers = (PlayerUpgradeOfferSnapshot[])originalOffers.Clone(), pendingRewards = rewards.ToArray()
            } : new { selecting, ownerReady, pendingEventId = localEventId.ToString(),
                presentationAvailable = presentation != null,
                stage = presentation != null ? presentation.Stage.ToString() : null,
                offeredLevel = presentation != null ? (int?)presentation.EarnedLevel : null,
                options = CapturePresentedOptions(),
                appliedBuildRevision = ownerAttackBuildRevision }, perspective, false, error, eventId);
        }
        private UpgradeOptionMessage[] CapturePresentedOptions()
        {
            if (presentation == null) return null;
            var offers = presentation.Offers;
            var options = new UpgradeOptionMessage[offers.Count];
            for (int i = 0; i < options.Length; i++)
            {
                ModifierOffer offer = offers[i];
                options[i] = new UpgradeOptionMessage { OptionId = offer.OfferId, Kind = offer.Kind,
                    ContentId = offer.ContentId, Rarity = offer.Rarity, PerkLevel = offer.PerkLevel,
                    LevelIndex = offer.LevelIndex, SlotIndex = offer.TargetSlotIndex };
            }
            return options;
        }
        private void TraceSelectionRequest(string action, ulong eventId, int index, ulong optionId = 0)
        {
            if (!CombatInvestigationEvidence.Enabled) return;
            CombatInvestigationEvidence.Capture("selection.request", "Requested", () => new {
                identity = InvestigationIdentity(), action, eventId = eventId.ToString(), index,
                optionId = optionId.ToString(), buildRevision = ownerAttackBuildRevision
            }, source: netId, target: netId, perspective: "Owner");
        }
        public override void OnStartClient()
        {
            if (CombatInvestigationEvidence.Enabled) GetComponent<NetworkRunParticipant>()?.BeginInvestigationSpawn();
            base.OnStartClient(); AttachInvestigation();
            if (CombatInvestigationEvidence.Enabled) TraceProgressionApplied("spawn-baseline", null, null);
        }
        private void OnLevelApplied(int previous, int current)
        { if (CombatInvestigationEvidence.Enabled) TraceProgressionApplied("level", previous, current); }
        private void OnExperienceApplied(float previous, float current)
        { if (CombatInvestigationEvidence.Enabled) TraceProgressionApplied("experience", previous, current); }
        private void OnExperienceThresholdApplied(int previous, int current)
        { if (CombatInvestigationEvidence.Enabled) TraceProgressionApplied("experiencePerLevel", previous, current); }
        private void TraceProgressionApplied(string field, object previous, object current)
        {
            TraceState("progression", "SyncVarApplied", "Applied", () => new {
                level, experience, experiencePerLevel, changedField = field, previous, current,
                atomicSnapshot = false, synchronization = "independent SyncVar application; no shared progression revision"
            }, isOwned ? "Owner" : "Replica", false);
        }
    }
}
