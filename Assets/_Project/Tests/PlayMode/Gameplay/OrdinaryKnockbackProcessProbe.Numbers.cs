using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DamageNumbersPro;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed partial class OrdinaryKnockbackProcessProbe
    {
        private bool validateDamageNumbers;
        private readonly HashSet<ulong> numberIds = new HashSet<ulong>();
        private readonly HashSet<ulong> sessionNumberIds = new HashSet<ulong>();
        private int capturedNumberFrames;
        private readonly List<ulong> acceptedNumberIds = new List<ulong>();

        private void RecordAcceptedNumbers(CanonicalWorldBatch batch)
        {
            if (batch.EnemyHitPresentations == null) return;
            foreach (var hit in batch.EnemyHitPresentations)
            {
                Require(hit.HasPosition, "Server did not capture damage position before broadcasting.");
                acceptedNumberIds.Add(hit.DamageEventId);
            }
        }

        private void RecordNumber(EnemyHitPresentation hit)
        {
            if (!validateDamageNumbers) return;
            Require(sessionNumberIds.Add(hit.DamageEventId), "Damage number was submitted twice after prediction/echo or handoff.");
            Require(numberIds.Add(hit.DamageEventId), "Repeated number within one phase.");
            Require(hit.Damage > 0 && hit.HasPosition, "Damage number lost its resolved value or position.");
            var popups = FindObjectsByType<DamageNumber>(FindObjectsSortMode.None);
            Require(popups.Any(n => n.number == hit.Damage), "DNP did not receive the exact resolved damage.");
            Debug.Log($"[EnemyDamageNumbersProcess] number role={role} event={hit.DamageEventId} target={hit.TargetEntityId} version={hit.TargetStateVersion} damage={hit.Damage} type={hit.PresentationDamageType} critical={hit.IsCritical} source={hit.SourcePlayerId}:{hit.DamageSourceId}");
            if (capture && capturedNumberFrames < 3)
                StartCoroutine(Guard(CaptureNumberFrame(++capturedNumberFrames)));
        }

        private IEnumerator CaptureNumberFrame(int index)
        {
            // Render the actual game camera even when the OS does not repaint a hidden player window.
            yield return new WaitForSeconds(.08f);
            var camera = Camera.main;
            Require(camera != null, "Missing gameplay camera for visual acceptance.");
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var target = RenderTexture.GetTemporary(1920, 1080, 24, RenderTextureFormat.ARGB32);
            try
            {
                camera.targetTexture = target;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
                var pixels = texture.GetPixels32();
                Require(pixels.Count(p => p.r > 20 || p.g > 20 || p.b > 20) > pixels.Length / 100,
                    "Damage number screenshot is blank.");
                File.WriteAllBytes(Path.Combine(artifacts, $"numbers-{role}-{index}.png"), texture.EncodeToPNG());
                Destroy(texture);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
