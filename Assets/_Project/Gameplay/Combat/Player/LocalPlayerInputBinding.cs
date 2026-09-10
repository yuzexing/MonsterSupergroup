using System;
using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.CameraFX;
using AstralShift.Managers;
using FMODUnity;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace AstralShift.HellMaiden.Player
{
    /// <summary>Owner-only input/camera binding. It never creates or resets gameplay state.</summary>
    public sealed class LocalPlayerInputBinding : IDisposable
    {
        private PlayerMovement player;
        private PlayerController_HMD controller;
        private ControllerManager controllerManager;
        private GameplayCameraRig cameraRig;
        private StudioListener audioListener;

        public PlayerMovement BoundPlayer => player;

        public void Bind(PlayerMovement value)
        {
            if (GameplayRuntimeEnvironment.IsDedicatedServer) return;
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (player != value)
            {
                Dispose();
                player = value;
                player.SetLocalOwnerBound(true);
                // Listen on the local avatar's gameplay plane, not the camera's negative Z.
                audioListener = player.GetComponent<StudioListener>();
                if (audioListener == null) audioListener = player.gameObject.AddComponent<StudioListener>();
                audioListener.enabled = true;
            }
            Refresh();
        }

        public void Refresh()
        {
            if (player == null) { Dispose(); return; }
            if (GameplayRuntimeEnvironment.IsDedicatedServer) return;
            if (controller == null)
            {
                ControllerManager manager = ControllerManager.Instance;
                if (manager != null && manager.Stack != null && manager.AvailableControllers != null)
                {
                    foreach (var available in manager.AvailableControllers)
                    {
                        if (!(available is PlayerController_HMD candidate)) continue;
                        candidate.Bind(player);
                        if (manager.OverrideGameController(candidate))
                        {
                            controller = candidate;
                            controllerManager = manager;
                        }
                        break;
                    }
                }
            }
            var availableRig = UnityEngine.Object.FindFirstObjectByType<GameplayCameraRig>();
            if (availableRig != null && !availableRig.isActiveAndEnabled) availableRig = null;
            if (cameraRig != availableRig)
            {
                if (cameraRig != null) cameraRig.ReleaseOwner(player);
                cameraRig = availableRig;
            }
            // Refresh also repairs a camera enabled after its Owner, or replaced during scene loading.
            if (cameraRig != null) cameraRig.BindOwner(player);
            player.SetInputCamera(cameraRig != null ? cameraRig.GameCamera : Camera.main);
        }

        public void PlayCameraShake(int presetIndex)
        {
            if (cameraRig != null) cameraRig.PlayShake(player, presetIndex);
        }

        public void Dispose()
        {
            if (controller != null && controller.BoundPlayer == player)
            {
                controllerManager?.ReleaseGameController(controller);
                controller.Unbind(player);
            }
            if (cameraRig != null) cameraRig.ReleaseOwner(player);
            if (audioListener != null) audioListener.enabled = false;
            if (player != null)
            {
                player.SetInputCamera(null);
                player.SetLocalOwnerBound(false);
            }
            player = null;
            controller = null;
            controllerManager = null;
            cameraRig = null;
            audioListener = null;
        }
    }
}
