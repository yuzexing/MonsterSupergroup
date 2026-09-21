using System;
using System.Collections.Generic;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat.Content
{
    [CreateAssetMenu(menuName = "MonsterSupergroup/Enemies/Appearance")]
    public sealed class EnemyAppearanceDefinition : ScriptableObject
    {
        [Serializable]
        public struct TextureMapping { public Texture2D original; public Texture2D baked; }
        [SerializeField] private Texture2D lut;
        [SerializeField] private TextureMapping[] textureMappings = Array.Empty<TextureMapping>();
        [SerializeField, HideInInspector] private string contentHash;
        public Texture2D Lut => lut;
        public string ContentHash => contentHash;
        public IReadOnlyList<TextureMapping> TextureMappings => Array.AsReadOnly(textureMappings ?? Array.Empty<TextureMapping>());
        public bool UsesPalette => lut != null;
        public void Validate()
        {
            var originals = new HashSet<Texture2D>();
            var mappings = textureMappings ?? Array.Empty<TextureMapping>();
            if (lut == null && mappings.Length != 0) throw new ArgumentException("Original-color appearance cannot contain palette mappings: " + name);
            if (lut != null && mappings.Length == 0) throw new ArgumentException("Baked palette mappings are missing: " + name);
            foreach (var mapping in mappings)
                if (mapping.original == null || mapping.baked == null || !originals.Add(mapping.original) ||
                    mapping.original.width != mapping.baked.width || mapping.original.height != mapping.baked.height)
                    throw new ArgumentException("Missing, duplicate or differently-sized appearance texture mapping: " + name);
        }
        public Texture2D FindBaked(Texture2D original)
        {
            foreach (var mapping in textureMappings ?? Array.Empty<TextureMapping>())
                if (mapping.original == original) return mapping.baked;
            return null;
        }
#if UNITY_EDITOR
        public void SetAuthoringValues(Texture2D colorLut, TextureMapping[] mappings)
        { lut = colorLut; textureMappings = (TextureMapping[])(mappings ?? Array.Empty<TextureMapping>()).Clone(); }
        public void SetContentHash(string value) => contentHash = value;
#endif
    }
}
