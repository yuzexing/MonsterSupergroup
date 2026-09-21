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
        private static readonly int[] Actions = { 14, 4, 51, 5 };
        private static readonly System.Reflection.FieldInfo State = typeof(NetworkPlayerUltimate)
            .GetField("state", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        private PlayerMovement owner;
        private NetworkPlayerUltimate ultimate;
        private uint lastRevision, lastGluttonyRevision;
        private NetworkPlayerGluttony gluttony;

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
                owner = next; lastRevision = lastGluttonyRevision = 0;
                gluttony = owner != null ? owner.GetComponent<NetworkPlayerGluttony>() : null;
                ultimate = owner != null ? owner.GetComponent<NetworkPlayerUltimate>() : null;
                if (owner != null) { owner.OnDashStart += DashStarted; owner.OnDashEnd += DashEnded; }
                Debug.Log($"[AbilityKeyboard] bound={(owner != null ? owner.GetComponent<NetworkIdentity>().netId : 0)}");
            }
            if (owner == null || !global::Rewired.ReInput.isReady) return;
            var input = global::Rewired.ReInput.players.GetPlayer(0);
            foreach (int action in Actions)
                if (input.GetButtonDown(action))
                    Debug.Log($"[AbilityKeyboard] action={action} player={NetworkClient.localPlayer.netId} focus={Application.isFocused} selecting={owner.IsUpgradeSelectionLocked} charge={ultimate.HasCharge}");
            if (gluttony != null && gluttony.State.Revision != lastGluttonyRevision)
            {
                var s = gluttony.State; lastGluttonyRevision = s.Revision;
                Debug.Log($"[GluttonyKeyboard] player={NetworkClient.localPlayer.netId} revision={s.Revision} cast={s.CastId} marked={s.Marked} collected={s.Collected} passive={s.PassiveKills}");
            }
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
