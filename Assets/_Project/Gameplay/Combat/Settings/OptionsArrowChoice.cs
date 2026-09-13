using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MonsterSupergroup.Gameplay.Options
{
    /// <summary>One keyboard focus target with looping previous/next choices, also usable with the mouse.</summary>
    public sealed class OptionsArrowChoice : Selectable, IPointerClickHandler
    {
        private string[] options = Array.Empty<string>();
        private int selected;
        private Text label;
        private Color labelColor;
        private Button previous, next;
        private Action<int> changed;
        public IReadOnlyList<string> Options => options;
        public int Value { get => selected; set => SetValue(value, true); }

        public void Initialize(Text caption, Button previousButton, Button nextButton, string[] values, int value, Action<int> onChange)
        {
            label = caption; labelColor = caption.color; previous = previousButton; next = nextButton; changed = onChange;
            previous.onClick.AddListener(() => Step(-1));
            next.onClick.AddListener(() => Step(1));
            SetOptions(values, value);
        }

        public void SetOptions(IEnumerable<string> values, int value)
        {
            options = values.ToArray();
            SetValue(value, false);
        }

        private void SetValue(int value, bool notify)
        {
            int clamped = Mathf.Clamp(value, 0, Math.Max(0, options.Length - 1));
            bool different = clamped != selected;
            selected = clamped; Refresh();
            if (notify && different) changed?.Invoke(selected);
        }

        public void Step(int direction)
        {
            if (!IsActive() || !IsInteractable() || options.Length < 2) return;
            Value = (selected + Math.Sign(direction) + options.Length) % options.Length;
            Select();
        }

        public override void OnMove(AxisEventData eventData)
        {
            if (eventData.moveDir == MoveDirection.Left || eventData.moveDir == MoveDirection.Right)
            {
                Step(eventData.moveDir == MoveDirection.Left ? -1 : 1);
                eventData.Use();
            }
            else base.OnMove(eventData);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && IsInteractable()) Select();
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            Refresh();
        }

        private void Refresh()
        {
            if (label == null) return;
            label.text = options.Length == 0 ? "" : options[selected];
            bool enabled = IsInteractable();
            label.color = new Color(labelColor.r, labelColor.g, labelColor.b, labelColor.a * (enabled ? 1 : .45f));
            previous.interactable = next.interactable = enabled && options.Length > 1;
        }
    }
}
