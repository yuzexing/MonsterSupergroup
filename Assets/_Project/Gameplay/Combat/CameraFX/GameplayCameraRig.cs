using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Player;
using Com.LuisPedroFonseca.ProCamera2D;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.Options;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace AstralShift.HellMaiden.CameraFX
{
    /// <summary>Local presentation owned by LocalPlayerInputBinding, never a network player registry.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ProCamera2D), typeof(ProCamera2DShake), typeof(ProCamera2DNumericBoundaries))]
    [DefaultExecutionOrder(100)] // The plugin must create its shake parent before we capture it.
    public sealed class GameplayCameraRig : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer boundaryGround;
        [SerializeField] private bool shakeEnabled = true;
        [SerializeField] private bool nordicFixedView;
        [SerializeField] private ShakePreset playerHitPreset;

        private ProCamera2D rig;
        private ProCamera2DShake shake;
        private Transform shakeContainer;
        private PlayerMovement owner;
        private CombatantBehaviour ownerCombatant;
        private bool referenceFraming;
        private Vector2 referenceFrameCenter, referenceFrameOrigin;
        private float referenceFrameWidth, referenceFrameProgress;
        private readonly Dictionary<uint, Vector2> referenceRemoteOrigins = new Dictionary<uint, Vector2>();

        public void SetReferenceTrapFraming(Vector2 center, float halfWidth, float progress)
        {
            if (!referenceFraming) { referenceFrameOrigin = transform.position; referenceRemoteOrigins.Clear(); }
            referenceFraming = true; referenceFrameCenter = center;
            referenceFrameWidth = halfWidth; referenceFrameProgress = Mathf.Clamp01(progress);
        }

        public void ClearReferenceTrapFraming() { referenceFraming = false; referenceRemoteOrigins.Clear(); ConstrainView(); }

        public Bounds ReferenceFollowView(uint participant, Vector2 position, Bounds map)
        {
            var view = GameplayCameraGeometry.ViewBounds(GameCamera);
            view.center = position;
            if (referenceFraming)
            {
                if (!referenceRemoteOrigins.TryGetValue(participant, out var origin))
                {
                    float height = 20 * Mathf.Tan(40 * Mathf.Deg2Rad);
                    origin = GameplayCameraGeometry.ClampView(new Bounds(position, new Vector3(height * GameCamera.aspect, height, 0)), map).center;
                    referenceRemoteOrigins.Add(participant, origin);
                }
                view.center = Vector2.Lerp(origin, referenceFrameCenter, referenceFrameProgress);
            }
            return GameplayCameraGeometry.ClampView(view, map);
        }

        public PlayerMovement BoundPlayer => owner;
        public Camera GameCamera => rig.GameCamera;
        public event Action<int> ShakePlayed;

        private void Awake()
        {
            rig = GetComponent<ProCamera2D>();
            shake = GetComponent<ProCamera2DShake>();
            shakeContainer = transform.parent;
            rig.RemoveAllCameraTargets();
            rig.enabled = false;
            ApplyGroundBoundaries();
        }

        private void ApplyGroundBoundaries()
        {
            var boundaries = GetComponent<ProCamera2DNumericBoundaries>();
            if (boundaryGround == null)
            {
                boundaries.UseNumericBoundaries = false;
                Debug.LogError("Gameplay camera requires the approved Ground boundary reference.", this);
                return;
            }
            // Ground is the map range explicitly approved for Gameplay. Renderer bounds include
            // sprite dimensions, pivot and hierarchy transforms, unlike localScale alone.
            Bounds bounds = boundaryGround.bounds;
            boundaries.LeftBoundary = bounds.min.x;
            boundaries.RightBoundary = bounds.max.x;
            boundaries.BottomBoundary = bounds.min.y;
            boundaries.TopBoundary = bounds.max.y;
            boundaries.UseLeftBoundary = boundaries.UseRightBoundary = true;
            boundaries.UseBottomBoundary = boundaries.UseTopBoundary = true;
            boundaries.UseNumericBoundaries = true;
            if (nordicFixedView)
            {
                boundaries.UseSoftBoundaries = false;
                rig.RemoveSizeOverrider(boundaries);
                ConstrainView();
            }
        }

        public void BindOwner(PlayerMovement player)
        {
            if (!isActiveAndEnabled || GameplayRuntimeEnvironment.IsDedicatedServer ||
                player == null || !player.IsLocalOwnerBound) return;
            if (owner == player && rig.CameraTargets.Count == 1 &&
                rig.CameraTargets[0].TargetTransform == player.transform) return;
            ReleaseOwner(owner);
            owner = player;
            ownerCombatant = player.GetComponent<CombatantBehaviour>();
            if (ownerCombatant != null) ownerCombatant.DamageReceived += OnOwnerDamage;
            rig.AddCameraTarget(player.transform);
            rig.enabled = true;
            rig.Reset();
        }

        public void PlayShake(PlayerMovement player, int presetIndex)
        {
            if (!isActiveAndEnabled || !shakeEnabled || !GameOptionsService.ScreenShakeEnabled || player == null || player != owner ||
                !player.IsLocalOwnerBound || !shake.isActiveAndEnabled ||
                presetIndex < 0 || presetIndex >= shake.ShakePresets.Count) return;
            shake.Shake(presetIndex);
            ShakePlayed?.Invoke(presetIndex);
        }

        private void OnOwnerDamage(DamageInfo damage, bool isStatusDamage)
        {
            // Actual Owner damage only. Canonical reconciliation emits HealthChanged, not this event.
            if (damage.Value > 0 && playerHitPreset != null)
                PlayShake(owner, shake.ShakePresets.IndexOf(playerHitPreset));
        }

        public void ReleaseOwner(PlayerMovement expectedOwner)
        {
            // Reference equality also handles an Owner already destroyed by Mirror.
            if (!ReferenceEquals(owner, expectedOwner) || rig == null) return;
            if (ownerCombatant != null) ownerCombatant.DamageReceived -= OnOwnerDamage;
            ownerCombatant = null;
            referenceFraming = false;
            owner = null;
            rig.RemoveAllCameraTargets(); // Plugin RemoveCameraTarget dereferences destroyed transforms.
            rig.enabled = false;
            if (shake != null)
            {
                shake.StopConstantShaking(0f);
                shake.StopShaking();
            }
            if (shakeContainer != null) shakeContainer.localPosition = Vector3.zero;
            rig.Reset(centerOnTargets: false);
        }

        private void Update()
        {
            ConstrainView();
            if (owner == null || !owner.IsLocalOwnerBound || !owner.isActiveAndEnabled)
            {
                if (rig.enabled) ReleaseOwner(owner);
            }
        }

        private void LateUpdate() => ConstrainView();
        private void ConstrainView()
        {
            if (!nordicFixedView || rig == null || boundaryGround == null) return;
            if (shakeContainer != null)
            {
                Vector3 p = shakeContainer.localPosition; p.z = 0;
                shakeContainer.localPosition = p; shakeContainer.localRotation = Quaternion.identity;
            }
            var camera = GetComponent<Camera>();
            float distance = 10;
            if (referenceFraming)
            {
                var center = Vector2.Lerp(referenceFrameOrigin, referenceFrameCenter, referenceFrameProgress);
                transform.position = new Vector3(center.x, center.y, transform.position.z);
                float fit = referenceFrameWidth / (Mathf.Max(.1f, camera.aspect) * Mathf.Tan(40 * Mathf.Deg2Rad));
                distance = Mathf.Lerp(10, Mathf.Max(10, fit), referenceFrameProgress);
            }
            GameplayCameraGeometry.ConstrainNordic(camera, rig, boundaryGround.bounds, distance);
        }

        private void OnEnable() => GameOptionsService.Changed += ApplyShakePreference;
        private void ApplyShakePreference()
        {
            if (GameOptionsService.ScreenShakeEnabled || shake == null) return;
            shake.StopConstantShaking(0f); shake.StopShaking();
            if (shakeContainer != null) shakeContainer.localPosition = Vector3.zero;
        }
        private void OnDisable()
        {
            GameOptionsService.Changed -= ApplyShakePreference;
            ReleaseOwner(owner);
        }
    }
}
