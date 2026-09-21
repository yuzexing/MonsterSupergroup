using System;
using System.Collections.Generic;
using MonsterSupergroup.Gameplay.Combat.Content;
using UnityEngine;

namespace AstralShift.Rendering
{
    // Different appearances may share a LUT but must never share different baked outputs.
    public static class EnemyAppearanceCache
    {
        internal sealed class Entry
        {
            public int Users;
            public readonly Dictionary<Texture2D, Texture2D> Textures = new Dictionary<Texture2D, Texture2D>();
            public readonly Dictionary<Sprite, Sprite> Sprites = new Dictionary<Sprite, Sprite>();
            public readonly Dictionary<Sprite, Sprite> Originals = new Dictionary<Sprite, Sprite>();
            public bool Palette;
            public string Name;
            public void Clear()
            {
                foreach (var sprite in Sprites.Values)
                    if (sprite != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(sprite); else UnityEngine.Object.DestroyImmediate(sprite); }
                Sprites.Clear(); Originals.Clear(); Textures.Clear();
            }
        }
        public sealed class Lease : IDisposable
        {
            private EnemyAppearanceDefinition definition;
            private Entry entry;
            internal Lease(EnemyAppearanceDefinition owner, Entry value) { definition = owner; entry = value; }
            public Sprite Original(Sprite sprite) => entry != null && sprite != null && entry.Originals.TryGetValue(sprite, out var original) ? original : sprite;
            public Sprite Map(Sprite source)
            {
                if (entry == null) throw new ObjectDisposedException(nameof(Lease));
                if (source == null || !entry.Palette) return source;
                source = Original(source);
                if (entry.Sprites.TryGetValue(source, out var result) && result != null) return result;
                if (!entry.Textures.TryGetValue(source.texture, out var baked))
                    throw new InvalidOperationException("Missing baked texture in appearance '" + entry.Name + "': " + source.texture.name);
                result = PaletteSwapSpriteManager.SliceModifiedTexture(source, baked);
                entry.Sprites.Add(source, result); entry.Originals.Add(result, source); return result;
            }
            public void Dispose()
            {
                if (entry == null) return;
                if (--entry.Users == 0)
                {
                    entry.Clear();
                    if (Entries.TryGetValue(definition, out var current) && ReferenceEquals(current, entry)) Entries.Remove(definition);
                }
                entry = null; definition = null;
            }
        }
        private static readonly Dictionary<EnemyAppearanceDefinition, Entry> Entries = new Dictionary<EnemyAppearanceDefinition, Entry>();
        public static Lease Acquire(EnemyAppearanceDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (!Entries.TryGetValue(definition, out var entry))
            {
                definition.Validate(); entry = new Entry { Palette = definition.UsesPalette, Name = definition.name };
                foreach (var mapping in definition.TextureMappings) entry.Textures.Add(mapping.original, mapping.baked);
                Entries.Add(definition, entry);
            }
            entry.Users++; return new Lease(definition, entry);
        }
        public static void ClearAll()
        {
            foreach (var entry in Entries.Values) entry.Clear();
            Entries.Clear();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ClearAll();
    }
}
