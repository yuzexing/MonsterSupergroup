using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MonsterSupergroup.Gameplay.Combat
{
    /// <summary>Rendering-only counterpart of the planar shaders. No transform,
    /// collider, particle simulation, animation or gameplay state is changed.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(31000)]
    public sealed class GameplayPlanarEffect : MonoBehaviour
    {
        private static readonly Unity.Profiling.ProfilerMarker BoundsMarker = new("Limbo.PlanarBounds");
        private static readonly HashSet<GameplayPlanarEffect> Active = new();
        private Renderer[] renderers = Array.Empty<Renderer>();
        private ParticleSystem[] particles = Array.Empty<ParticleSystem>();
        public static Vector3 Project(Vector3 world) => new(world.x, world.y, 0);
        public static Bounds Project(Bounds bounds) => new(Project(bounds.center), new Vector3(bounds.size.x, bounds.size.y, .1f));
        public static bool UsesPlanarMaterial(Renderer renderer)
        {
            foreach (var material in renderer.sharedMaterials)
                if (material != null && material.HasProperty("_GameplayPlanar")) return true;
            return false;
        }
        public static void Attach(GameObject root)
        {
            if (root.TryGetComponent<GameplayPlanarEffect>(out _)) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (UsesPlanarMaterial(r)) { root.AddComponent<GameplayPlanarEffect>(); return; }
        }
        private void OnEnable()
        {
            var all = GetComponentsInChildren<Renderer>(true);
            renderers = Array.FindAll(all, r => UsesPlanarMaterial(r) && r.GetComponentInParent<GameplayPlanarEffect>() == this);
            particles = Array.ConvertAll(renderers, r => r.GetComponent<ParticleSystem>());
            if (Active.Count == 0)
            {
                RenderPipelineManager.beginCameraRendering += BeginCamera;
                RenderPipelineManager.endCameraRendering += EndCamera;
            }
            Active.Add(this);
        }
        private static void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            foreach (var effect in Active) effect.RefreshBounds();
        }
        private static void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            foreach (var effect in Active) effect.ResetBounds();
        }
        public void RefreshBounds()
        {
            using var sample = BoundsMarker.Auto();
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                // Override only for camera culling/drawing. Keeping a world override
                // across particle simulation prevents Unity from refreshing native bounds.
                r.ResetBounds();
                // A cleared/pooled particle system has no renderable vertices. Unity's
                // native MinMaxAABB may be invalid until it emits again; don't query it.
                if (particles[i] != null && particles[i].particleCount == 0) continue;
                r.bounds = Project(r.bounds);
            }
        }
        private void OnDisable()
        {
            Active.Remove(this);
            if (Active.Count == 0)
            {
                RenderPipelineManager.beginCameraRendering -= BeginCamera;
                RenderPipelineManager.endCameraRendering -= EndCamera;
            }
            ResetBounds();
        }
        private void ResetBounds()
        {
            foreach (var r in renderers) if (r != null) r.ResetBounds();
        }
    }
}
