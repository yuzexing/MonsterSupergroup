using System.Collections.Generic;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class MusicPrototypeView
    {
        private sealed class EffectVisual
        {
            public LineRenderer Line;
            public Vector3 Center;
            public Color Color;
            public float Born, Duration, Radius;
            public bool Expands;
        }
        private readonly List<EffectVisual> effects = new List<EffectVisual>();

        public void ShowEffect(MusicEffect effect, Vector2[] positions, int affected)
        {
            if (!isActiveAndEnabled || skill == null || !skill.isClient) return;
            EnsureVisuals();
            if (skill.isOwned)
            {
                feedback = EffectName(effect) + " | " + affected;
                feedbackUntil = Time.unscaledTime + .45f;
            }
            if (effect == MusicEffect.Push || effect == MusicEffect.Speed)
            {
                float radius = effect == MusicEffect.Push ? skill.Parameters.PushRadius : 1.2f;
                AddRing(transform.position, radius, effect == MusicEffect.Push ? new Color(.3f, .9f, 1) : new Color(.3f, 1, .4f), .4f);
                return;
            }
            bool finale = effect == MusicEffect.Finale;
            if (finale) AddRing(transform.position, skill.Parameters.FinaleRadius, new Color(1, .85f, .2f), .55f);
            if (positions == null) return;
            foreach (Vector2 target in positions)
            {
                var line = MakeLine(finale ? "Music finale lightning" : "Music lightning", finale ? .09f : .065f, false);
                line.positionCount = 7;
                Color color = finale ? new Color(1, .85f, .2f) : new Color(.65f, .75f, 1);
                line.startColor = line.endColor = color;
                Vector3 from = transform.position + Vector3.up * .7f;
                Vector3 to = (Vector3)target + Vector3.up * .4f;
                Vector3 normal = new Vector3(-(to - from).y, (to - from).x, 0).normalized;
                for (int i = 0; i < 7; i++)
                {
                    float offset = i == 0 || i == 6 ? 0 : (i % 2 == 0 ? -.2f : .2f);
                    line.SetPosition(i, Vector3.Lerp(from, to, i / 6f) + normal * offset);
                }
                effects.Add(new EffectVisual { Line = line, Color = color, Born = Time.unscaledTime, Duration = .25f });
            }
        }

        private static string EffectName(MusicEffect effect) => effect == MusicEffect.Push ? "PUSH" :
            effect == MusicEffect.Lightning ? "LIGHTNING" : effect == MusicEffect.Speed ? "SPEED" : "PERFECT FINALE";

        private void AddRing(Vector3 center, float radius, Color color, float duration)
        {
            var line = MakeLine("Music effect wave", .055f, true);
            effects.Add(new EffectVisual { Line = line, Center = center, Radius = radius, Color = color,
                Born = Time.unscaledTime, Duration = duration, Expands = true });
        }

        private void UpdateEffects()
        {
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                var effect = effects[i];
                float age = (Time.unscaledTime - effect.Born) / effect.Duration;
                if (effect.Line == null || age >= 1)
                {
                    if (effect.Line != null) Destroy(effect.Line.gameObject);
                    effects.RemoveAt(i);
                    continue;
                }
                Color color = effect.Color;
                color.a = 1 - age;
                if (effect.Expands) SetCircle(effect.Line, effect.Center, Mathf.Lerp(.4f, effect.Radius, age), color);
                else effect.Line.startColor = effect.Line.endColor = color;
            }
        }
    }
}
