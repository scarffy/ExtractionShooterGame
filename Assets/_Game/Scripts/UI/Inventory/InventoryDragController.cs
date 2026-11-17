using System;
using ExtractionShooter.Gameplay.Inventory;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Inventory
{
    /// <summary>
    /// Drag and drop between slot grids. A press on a filled slot becomes a drag once the pointer moves
    /// a few pixels; releasing over a slot reports (source, target slot), over a grid or panel reports
    /// target slot -1 (place automatically). A short press stays a click. Plain C# helper owned by
    /// <see cref="InventoryScreen"/>; the server validates every move.
    /// </summary>
    internal sealed class InventoryDragController
    {
        private const string GhostClass = "drag-ghost";
        private const string HiddenClass = "hidden";
        private const float StartDistance = 6f;

        private readonly VisualElement _root;
        private readonly Label _ghost;

        private SlotGridView _sourceView;
        private int _sourceSlot = -1;
        private int _pointerId = -1;
        private Vector2 _pressPosition;
        private bool _dragging;
        private int _dropFrame = -1;

        /// <summary>(source container, source slot, target container, target slot or -1).</summary>
        public event Action<ItemContainer, int, ItemContainer, int> Dropped;

        public InventoryDragController(VisualElement root)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));

            _ghost = new Label { name = "drag-ghost", pickingMode = PickingMode.Ignore };
            _ghost.AddToClassList(GhostClass);
            _ghost.AddToClassList(HiddenClass);
            _root.Add(_ghost);

            _root.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _root.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _root.RegisterCallback<PointerCaptureOutEvent>(_ => Cancel());
        }

        /// <summary>
        /// True for the click that the release of a drag generates in the same frame; that click must
        /// not also quick-move the item.
        /// </summary>
        public bool IsClickFromDrop => _dropFrame == Time.frameCount;

        /// <summary>Called by a grid when the left button goes down on a filled slot.</summary>
        public void Press(SlotGridView view, int slot, Vector2 position, int pointerId)
        {
            Cancel();
            _sourceView = view;
            _sourceSlot = slot;
            _pressPosition = position;
            _pointerId = pointerId;
        }

        /// <summary>Stops any press or drag without moving anything.</summary>
        public void Cancel()
        {
            // Reset state before releasing: releasing raises PointerCaptureOutEvent, which calls Cancel again.
            bool wasDragging = _dragging;
            int pointerId = _pointerId;
            SlotGridView source = _sourceView;

            _dragging = false;
            _sourceView = null;
            _sourceSlot = -1;
            _pointerId = -1;
            _ghost.EnableInClassList(HiddenClass, true);

            if (source != null) source.SetDragSource(-1);
            if (wasDragging && _root.HasPointerCapture(pointerId)) _root.ReleasePointer(pointerId);
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (_sourceView == null || evt.pointerId != _pointerId) return;

            var position = new Vector2(evt.position.x, evt.position.y);
            if (!_dragging)
            {
                if (Vector2.Distance(position, _pressPosition) < StartDistance) return;

                _dragging = true;
                _root.CapturePointer(_pointerId);
                _sourceView.SetDragSource(_sourceSlot);
                _ghost.text = _sourceView.ItemName(_sourceSlot);
                _ghost.EnableInClassList(HiddenClass, false);
                _ghost.BringToFront();
            }

            _ghost.style.left = position.x + 12f;
            _ghost.style.top = position.y + 12f;
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (_sourceView == null || evt.pointerId != _pointerId) return;

            if (!_dragging)
            {
                Cancel(); // a plain click: ClickEvent handles it
                return;
            }

            SlotGridView source = _sourceView;
            int sourceSlot = _sourceSlot;
            var position = new Vector2(evt.position.x, evt.position.y);
            Cancel();

            _dropFrame = Time.frameCount;
            evt.StopPropagation();

            VisualElement picked = _root.panel != null ? _root.panel.Pick(position) : null;
            if (!SlotGridView.TryResolve(picked, out SlotGridView target, out int targetSlot)) return;
            if (source.Container == null || target.Container == null) return;
            if (target == source && (targetSlot == sourceSlot || targetSlot < 0)) return;

            Dropped?.Invoke(source.Container, sourceSlot, target.Container, targetSlot);
        }
    }
}
