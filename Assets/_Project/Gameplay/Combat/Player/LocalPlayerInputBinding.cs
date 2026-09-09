using System;
using AstralShift.HellMaiden.Controllers;
using AstralShift.Managers;
using Com.LuisPedroFonseca.ProCamera2D;
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
        private ProCamera2D cameraRig;

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
            }
            Refresh();
        }

        public void Refresh()
        {
            if (player == null || GameplayRuntimeEnvironment.IsDedicatedServer) return;
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
            // The current Gameplay scene uses a plain Camera. ProCamera's singleton
            // getter throws when its optional rig is absent.
            var availableRig = UnityEngine.Object.FindFirstObjectByType<ProCamera2D>();
            if (cameraRig != availableRig)
            {
                if (cameraRig != null) cameraRig.RemoveCameraTarget(player.transform);
                cameraRig = availableRig;
                if (cameraRig != null) cameraRig.AddCameraTarget(player.transform);
            }
            player.SetInputCamera(cameraRig != null ? cameraRig.GameCamera : Camera.main);
        }

        public void Dispose()
        {
            if (controller != null && controller.BoundPlayer == player)
            {
                controllerManager?.ReleaseGameController(controller);
                controller.Unbind(player);
            }
            if (player != null)
            {
                if (cameraRig != null) cameraRig.RemoveCameraTarget(player.transform);
                player.SetInputCamera(null);
                player.SetLocalOwnerBound(false);
            }
            player = null;
            controller = null;
            controllerManager = null;
            cameraRig = null;
        }
    }
}
