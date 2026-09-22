using System;
using System.Linq;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Tests
{
    /// <summary>Opt-in keyboard evidence only: never starts a session, grants resources or invokes an ability.</summary>
    public sealed class RewiredAbilityInputObserver : MonoBehaviour
    {
        private static readonly int[] Actions = { 14, 4, 51, 5, 63, 64, 65, 66, 67, 68 };
        private static readonly System.Reflection.FieldInfo State = typeof(NetworkPlayerUltimate)
            .GetField("state", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        private PlayerMovement owner;
        private NetworkPlayerUltimate ultimate;
        private uint lastRevision, lastGluttonyRevision, lastSelectionRevision;
        private NetworkPlayerGluttony gluttony;
        private NetworkPlayerPrototypeAbilities prototypes;
        private readonly bool[] actionHeld = new bool[Actions.Length];
        private readonly bool[] holdReported = new bool[Actions.Length];
        private readonly double[] heldSince = new double[Actions.Length];
        private readonly int[] pressedEdges = new int[Actions.Length];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Environment.GetCommandLineArgs().Contains("--observe-ability-input")) return;
            DontDestroyOnLoad(new GameObject("Ability keyboard evidence").AddComponent<RewiredAbilityInputObserver>().gameObject);
        }

        private void Update()
        {
            var next = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<PlayerMovement>() : null;
            if (next != owner)
            {
                if (owner != null) { owner.OnDashStart -= DashStarted; owner.OnDashEnd -= DashEnded; }
                owner = next; lastRevision = lastGluttonyRevision = lastSelectionRevision = 0;
                Array.Clear(actionHeld, 0, actionHeld.Length);
                Array.Clear(holdReported, 0, holdReported.Length);
                Array.Clear(pressedEdges, 0, pressedEdges.Length);
                gluttony = owner != null ? owner.GetComponent<NetworkPlayerGluttony>() : null;
                ultimate = owner != null ? owner.GetComponent<NetworkPlayerUltimate>() : null;
                prototypes = owner != null ? owner.GetComponent<NetworkPlayerPrototypeAbilities>() : null;
                if (owner != null) { owner.OnDashStart += DashStarted; owner.OnDashEnd += DashEnded; }
                Debug.Log($"[AbilityKeyboard] bound={(owner != null ? owner.GetComponent<NetworkIdentity>().netId : 0)}");
            }
            if (owner == null || !global::Rewired.ReInput.isReady) return;
            var input = global::Rewired.ReInput.players.GetPlayer(0);
            double now = Time.realtimeSinceStartupAsDouble;
            for (int i = 0; i < Actions.Length; i++)
            {
                int action = Actions[i];
                bool held = input.GetButton(action);
                if (held && !actionHeld[i])
                {
                    heldSince[i] = now; pressedEdges[i] = 0; holdReported[i] = false;
                }
                if (input.GetButtonDown(action))
                {
                    pressedEdges[i]++;
                    Debug.Log($"[AbilityKeyboard] action={action} event=pressed player={NetworkClient.localPlayer.netId} frame={Time.frameCount} focus={Application.isFocused} selecting={owner.IsUpgradeSelectionLocked} charge={ultimate != null && ultimate.HasCharge}");
                }
                if (held && !holdReported[i] && now - heldSince[i] >= .5)
                {
                    holdReported[i] = true;
                    Debug.Log($"[AbilityKeyboard] action={action} event=held player={NetworkClient.localPlayer.netId} heldFor={now - heldSince[i]:F3} pressedEdges={pressedEdges[i]}");
                }
                if (!held && actionHeld[i])
                    Debug.Log($"[AbilityKeyboard] action={action} event=released player={NetworkClient.localPlayer.netId} heldFor={now - heldSince[i]:F3} pressedEdges={pressedEdges[i]}");
                actionHeld[i] = held;
            }
            if (prototypes != null && prototypes.SelectionRevision != lastSelectionRevision)
            {
                lastSelectionRevision = prototypes.SelectionRevision;
                Debug.Log($"[PrototypeKeyboard] player={NetworkClient.localPlayer.netId} ability={prototypes.SelectedAbility} revision={lastSelectionRevision} enabled={prototypes.PrototypeEnabled} frame={Time.frameCount}");
            }
            if (gluttony != null && gluttony.State.Revision != lastGluttonyRevision)
            {
                var s = gluttony.State; lastGluttonyRevision = s.Revision;
                Debug.Log($"[GluttonyKeyboard] player={NetworkClient.localPlayer.netId} revision={s.Revision} cast={s.CastId} marked={s.Marked} collected={s.Collected} totalCollected={s.TotalCollected} passive={s.PassiveKills} passiveReadyAt={s.PassiveReadyAt:F3} activeReadyAt={s.ActiveReadyAt:F3}");
            }
            if (ultimate == null) return;
            var current = (NetworkUltimateState)State.GetValue(ultimate);
            if (current.Revision != lastRevision)
            {
                lastRevision = current.Revision;
                Debug.Log($"[AbilityKeyboard] player={NetworkClient.localPlayer.netId} revision={lastRevision} held={current.Snapshot.HasCharge} activeUntil={current.Snapshot.ActiveUntil:F3}");
            }
        }

        private void DashStarted() => Debug.Log($"[AbilityKeyboard] dash-start player={NetworkClient.localPlayer.netId} position={owner.transform.position}");
        private void DashEnded() => Debug.Log($"[AbilityKeyboard] dash-end player={NetworkClient.localPlayer.netId} position={owner.transform.position}");
        private void OnDestroy()
        {
            if (owner != null) { owner.OnDashStart -= DashStarted; owner.OnDashEnd -= DashEnded; }
        }
    }
}
