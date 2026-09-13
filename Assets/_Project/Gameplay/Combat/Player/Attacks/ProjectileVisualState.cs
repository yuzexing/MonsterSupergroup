using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
    /// <summary>Restores authored appearance between pooled uses, without resetting progression transforms.</summary>
    [DisallowMultipleComponent]
    public sealed class ProjectileVisualState : MonoBehaviour
    {
        private ParticleSystem[] particles;
        private ParticleSystem.MinMaxGradient[] colors;
        private SpriteRenderer[] sprites;
        private Color[] spriteColors;
        private GameObject[] children;
        private bool[] active;
        private readonly List<Material> materials = new List<Material>();
        private readonly List<Material> defaults = new List<Material>();

        private void Initialize()
        {
            if (particles != null) return;
            particles = GetComponentsInChildren<ParticleSystem>(true);
            colors = new ParticleSystem.MinMaxGradient[particles.Length];
            for (int i = 0; i < particles.Length; i++) colors[i] = particles[i].main.startColor;
            sprites = GetComponentsInChildren<SpriteRenderer>(true);
            spriteColors = new Color[sprites.Length];
            for (int i = 0; i < sprites.Length; i++) spriteColors[i] = sprites[i].color;
            Transform[] transforms = GetComponentsInChildren<Transform>(true);
            children = new GameObject[transforms.Length - 1];
            active = new bool[children.Length];
            for (int i = 1; i < transforms.Length; i++)
            { children[i - 1] = transforms[i].gameObject; active[i - 1] = children[i - 1].activeSelf; }
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                Material[] copies = renderer.sharedMaterials;
                for (int i = 0; i < copies.Length; i++)
                {
                    if (copies[i] == null) continue;
                    defaults.Add(copies[i]);
                    copies[i] = new Material(copies[i]) { name = copies[i].name + " (Projectile)", hideFlags = HideFlags.DontSave };
                    materials.Add(copies[i]);
                }
                renderer.sharedMaterials = copies;
            }
        }

        public void ResetForSpawn()
        {
            Initialize();
            ClearParticles();
            for (int i = 0; i < materials.Count; i++) materials[i].CopyPropertiesFromMaterial(defaults[i]);
            for (int i = 0; i < children.Length; i++) children[i].SetActive(active[i]);
            for (int i = 0; i < sprites.Length; i++) sprites[i].color = spriteColors[i];
            for (int i = 0; i < particles.Length; i++)
            { var main = particles[i].main; main.startColor = colors[i]; }
        }

        public void SeekParticles(float elapsed)
        {
            Initialize();
            foreach (ParticleSystem particle in particles)
            {
                if (!particle.gameObject.activeInHierarchy) continue;
                particle.Simulate(elapsed, false, true, false);
                particle.Play(false);
            }
        }

        public void ClearParticles()
        {
            if (particles == null) return;
            foreach (ParticleSystem particle in particles)
                if (particle != null) particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void OnDisable() => ClearParticles();
        private void OnDestroy()
        {
            foreach (Material material in materials) if (material != null) Destroy(material);
        }
    }
}
