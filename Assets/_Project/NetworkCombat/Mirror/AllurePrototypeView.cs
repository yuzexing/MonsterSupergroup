using System.Collections.Generic;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Fixed, non-interactive decoy and brief target feedback on every observing client.</summary>
    [DisallowMultipleComponent]
    public sealed class AllurePrototypeView : MonoBehaviour
    {
        [SerializeField] private Material prototypeMaterial;
        private NetworkPlayerAllure skill;
        private NetworkPlayerPrototypeAbilities abilities;
        private GameObject root;
        private Material fallbackMaterial;
        private LineRenderer decoyRing, decoyBody, decoyHead;
        private GUIStyle labelStyle;
        private readonly List<Flash> flashes = new List<Flash>();
        private sealed class Flash
        {
            public LineRenderer Line;
            public Vector2 Position;
            public Color Color;
            public float StartedAt;
        }
        private void Awake()
        { skill = GetComponent<NetworkPlayerAllure>(); abilities = GetComponent<NetworkPlayerPrototypeAbilities>(); }
        public bool HasVisibleDecoy => decoyBody != null && decoyBody.enabled;
        public void ShowCast(AllureAction action, Vector2[] positions)
        {
            if (!isActiveAndEnabled || skill == null || !skill.isClient || positions == null) return;
            EnsureVisuals();
            Color color = action == AllureAction.Throw ? new Color(1, .35f, .75f) :
                action == AllureAction.Take ? new Color(.3f, 1, .8f) : new Color(.8f, .5f, 1);
            foreach (Vector2 position in positions)
                flashes.Add(new Flash { Line = MakeLine("Allure affected enemy", .06f, true),
                    Position = position, Color = color, StartedAt = Time.unscaledTime });
        }
        private void EnsureVisuals()
        {
            if (root != null) return;
            root = new GameObject("Allure prototype visuals") { hideFlags = HideFlags.DontSave };
            if (prototypeMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader != null) fallbackMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            }
            decoyRing = MakeLine("Decoy ground ring", .045f, true);
            decoyBody = MakeLine("Decoy body", .08f, false);
            decoyHead = MakeLine("Decoy head", .075f, true);
            SetDecoyVisible(false);
        }
        private LineRenderer MakeLine(string label, float width, bool loop)
        {
            var go = new GameObject(label); go.transform.SetParent(root.transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = prototypeMaterial != null ? prototypeMaterial : fallbackMaterial;
            line.useWorldSpace = true; line.loop = loop; line.widthMultiplier = width;
            line.sortingLayerID = SortingLayer.NameToID("Foreground"); line.sortingOrder = 31998;
            return line;
        }
        private void LateUpdate()
        {
            if (skill == null || !skill.isClient || abilities == null || !abilities.PrototypeEnabled)
            { if (root != null) Clear(); return; }
            bool active = skill.DecoyRemaining > 0;
            if (active)
            {
                EnsureVisuals(); SetDecoyVisible(true);
                Vector3 center = skill.State.DecoyPosition;
                Color color = skill.isOwned ? new Color(.4f, 1, .9f, .95f) : new Color(1, .45f, .9f, .9f);
                SetCircle(decoyRing, center, .48f + .035f * Mathf.Sin(Time.unscaledTime * 6), color);
                SetCircle(decoyHead, center + Vector3.up * .95f, .18f, color);
                decoyBody.startColor = decoyBody.endColor = color;
                decoyBody.positionCount = 8;
                decoyBody.SetPositions(new[] { center + new Vector3(-.3f, 0), center + new Vector3(0, .42f),
                    center + new Vector3(.3f, 0), center + new Vector3(0, .42f), center + new Vector3(0, .77f),
                    center + new Vector3(-.35f, .56f), center + new Vector3(0, .77f), center + new Vector3(.35f, .56f) });
            }
            else SetDecoyVisible(false);
            for (int i = flashes.Count - 1; i >= 0; i--)
            {
                var flash = flashes[i]; float age = Time.unscaledTime - flash.StartedAt;
                if (age >= .65f || flash.Line == null)
                { if (flash.Line != null) Destroy(flash.Line.gameObject); flashes.RemoveAt(i); continue; }
                Color color = flash.Color; color.a = 1 - age / .65f;
                SetCircle(flash.Line, flash.Position, .35f + age * .8f, color);
            }
        }
        private void SetDecoyVisible(bool visible)
        {
            if (decoyBody != null) decoyBody.enabled = visible;
            if (decoyHead != null) decoyHead.enabled = visible;
            if (decoyRing != null) decoyRing.enabled = visible;
        }
        private static void SetCircle(LineRenderer line, Vector3 center, float radius, Color color)
        {
            const int points = 32;
            line.positionCount = points; line.startColor = line.endColor = color;
            for (int i = 0; i < points; i++)
            { float angle = i * Mathf.PI * 2 / points; line.SetPosition(i, center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * radius); }
        }
        private void OnGUI()
        {
            if (skill == null || !skill.isClient || abilities == null || !abilities.PrototypeEnabled) return;
            if (labelStyle == null) labelStyle = new GUIStyle(GUI.skin.label)
            { alignment = TextAnchor.MiddleCenter, fontSize = 14, fontStyle = FontStyle.Bold };
            if (skill.isOwned && Time.unscaledTime < skill.NotificationUntil)
            {
                var notice = new Rect(Screen.width * .5f - 210, 65, 420, 34);
                GUI.Box(notice, GUIContent.none); GUI.Label(notice, skill.LastNotification, labelStyle);
            }
            if (skill.DecoyRemaining <= 0 || Camera.main == null) return;
            Vector3 point = Camera.main.WorldToScreenPoint((Vector3)skill.State.DecoyPosition + Vector3.up * 1.35f);
            if (point.z <= 0 || point.x < 0 || point.x > Screen.width || point.y < 0 || point.y > Screen.height) return;
            GUI.Label(new Rect(point.x - 90, Screen.height - point.y - 20, 180, 24),
                $"{(skill.isOwned ? "Your decoy" : "Teammate decoy")} {skill.DecoyRemaining:0.0}s", labelStyle);
        }
        public void Clear()
        {
            flashes.Clear(); if (root != null) Destroy(root); root = null;
            decoyRing = decoyBody = decoyHead = null;
            if (fallbackMaterial != null) Destroy(fallbackMaterial); fallbackMaterial = null; labelStyle = null;
        }
        private void OnDisable() => Clear();
        private void OnDestroy() => Clear();
    }
}
