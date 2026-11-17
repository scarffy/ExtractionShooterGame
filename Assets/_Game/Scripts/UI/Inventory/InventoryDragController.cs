using System;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Inventory;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Inventory
{
    /// <summary>
    /// Drag and drop between inventory views. A press on an item becomes a drag once the pointer moves a few pixels;
    /// a short press stays a click.
    /// <list type="bullet">
    /// <item>Over a grid: a footprint preview shows where the item would land (its top-left follows the pointer, keeping
    /// the cell it was grabbed by under the pointer), green when it fits or would merge into the same item, red otherwise.
    /// R turns the item (non-square items). Releasing drops it there.</item>
    /// <item>Over a plain slot (equipment): drops onto that slot. Over a panel but off its slots or cells: places
    /// automatically (not back into the container it came from).</item>
    /// </list>
    /// Plain C# helper owned by the inventory screens, which must call <see cref="Tick"/> every frame (rotation key).
    /// The server validates every move.
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
        private Vector2 _pointerPosition;
        private bool _dragging;
        private int _dropFrame = -1;

        // The dragged item and how it is held.
        private ItemStack _stack;
        private bool _rotated;
        private Vector2Int _grabCell; // cell of the item under the pointer, relative to its top-left, as currently rotated

        // Where a release would drop, recomputed on every move.
        private SlotGridView _hoverView;
        private int _hoverSlot = -1;
        private bool _hoverValid;

        /// <summary>(source container, source slot, target container, target slot or -1, rotated at the target).</summary>
        public event Action<ItemContainer, int, ItemContainer, int, bool> Dropped;

        /// <summary>Optional: true if the held item can be fitted onto the item under the pointer (an attachment on a weapon).</summary>
        public Func<ItemStack, ItemStack, bool> CanFit { get; set; }

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

        /// <summary>True while an item is being dragged.</summary>
        public bool IsDragging => _dragging;

        /// <summary>Call every frame from the owning screen: turns the dragged item when R is pressed.</summary>
        public void Tick()
        {
            if (!_dragging) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.rKey.wasPressedThisFrame) return;

            SlotInventory layout = _hoverView != null ? _hoverView.Layout : null;
            ItemSize size = layout != null && layout.IsGrid ? layout.FootprintOf(_stack.WithRotation(false)) : ItemSize.One;
            if (size.IsSquare && layout != null && layout.IsGrid) return;

            _rotated = !_rotated;
            _grabCell = new Vector2Int(_grabCell.y, _grabCell.x); // keep holding the same part of the item
            UpdateHover(_pointerPosition);
        }

        /// <summary>Called by a view when the left button goes down on an item.</summary>
        public void Press(SlotGridView view, int slot, Vector2 position, int pointerId)
        {
            Cancel();
            _sourceView = view;
            _sourceSlot = slot;
            _pressPosition = position;
            _pointerPosition = position;
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
            ClearHover();

            if (source != null) source.SetDragSource(-1);
            if (wasDragging && _root.HasPointerCapture(pointerId)) _root.ReleasePointer(pointerId);
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (_sourceView == null || evt.pointerId != _pointerId) return;

            var position = new Vector2(evt.position.x, evt.position.y);
            _pointerPosition = position;
            if (!_dragging)
            {
                if (Vector2.Distance(position, _pressPosition) < StartDistance) return;
                BeginDrag();
            }

            _ghost.style.left = position.x + 12f;
            _ghost.style.top = position.y + 12f;
            UpdateHover(position);
        }

        private void BeginDrag()
        {
            _dragging = true;
            _stack = _sourceView.GetStack(_sourceSlot);
            _rotated = _stack.Rotated;
            _grabCell = GrabCell(_sourceView, _sourceSlot, _pressPosition);

            _root.CapturePointer(_pointerId);
            _sourceView.SetDragSource(_sourceSlot);
            _ghost.text = _sourceView.ItemName(_sourceSlot);
            _ghost.EnableInClassList(HiddenClass, false);
            _ghost.BringToFront();
        }

        /// <summary>Which cell of the item the press landed on (0,0 outside grids).</summary>
        private static Vector2Int GrabCell(SlotGridView view, int slot, Vector2 pressPosition)
        {
            SlotInventory layout = view.Layout;
            if (!view.IsGrid || layout == null || !layout.IsGrid || !view.TryCellAt(pressPosition, out int x, out int y))
                return Vector2Int.zero;

            int width = layout.GridWidth;
            ItemSize size = layout.FootprintOf(layout[slot]);
            return new Vector2Int(Mathf.Clamp(x - slot % width, 0, size.Width - 1), Mathf.Clamp(y - slot / width, 0, size.Height - 1));
        }

        /// <summary>Works out the drop under the pointer and updates the footprint preview.</summary>
        private void UpdateHover(Vector2 position)
        {
            VisualElement picked = _root.panel != null ? _root.panel.Pick(position) : null;
            SlotGridView.TryResolve(picked, out SlotGridView view, out int slot);

            if (_hoverView != null && _hoverView != view) _hoverView.HidePlacement();
            _hoverView = view;
            _hoverSlot = -1;
            _hoverValid = false;
            if (view == null || view.Container == null) return;

            if (!view.IsGrid)
            {
                _hoverSlot = slot;
                return;
            }

            SlotInventory layout = view.Layout;
            if (layout == null || !layout.IsGrid || !view.TryCellAt(position, out int x, out int y))
            {
                // On the panel but off the cells: place automatically, unless that is where it came from.
                view.HidePlacement();
                _hoverValid = view.Container != _sourceView.Container;
                return;
            }

            ItemStack held = _stack.WithRotation(_rotated);
            ItemSize footprint = layout.FootprintOf(held);
            bool sameContainer = view.Container == _sourceView.Container;

            // Over the same plain item somewhere else: the server merges onto whichever of its cells it is dropped on.
            int pointerCell = y * layout.GridWidth + x;
            int under = layout.AnchorAt(pointerCell);
            if (under >= 0 && !(sameContainer && under == _sourceSlot) &&
                (layout[under].CanStackWith(_stack) || (CanFit != null && CanFit(_stack, layout[under]))))
            {
                _hoverSlot = pointerCell;
                _hoverValid = true;
                view.ShowPlacement(under, layout.FootprintOf(layout[under]), valid: true);
                return;
            }

            int left = Mathf.Clamp(x - _grabCell.x, 0, Mathf.Max(0, layout.GridWidth - footprint.Width));
            int top = Mathf.Clamp(y - _grabCell.y, 0, Mathf.Max(0, layout.GridHeight - footprint.Height));
            int anchor = top * layout.GridWidth + left;

            _hoverSlot = anchor;
            _hoverValid = layout.CanPlaceAt(anchor, held, sameContainer ? _sourceSlot : -1);
            view.ShowPlacement(anchor, footprint, _hoverValid);
        }

        private void ClearHover()
        {
            if (_hoverView != null) _hoverView.HidePlacement();
            _hoverView = null;
            _hoverSlot = -1;
            _hoverValid = false;
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (_sourceView == null || evt.pointerId != _pointerId) return;

            if (!_dragging)
            {
                Cancel(); // a plain click: ClickEvent handles it
                return;
            }

            var position = new Vector2(evt.position.x, evt.position.y);
            UpdateHover(position);

            SlotGridView source = _sourceView;
            int sourceSlot = _sourceSlot;
            SlotGridView target = _hoverView;
            int targetSlot = _hoverSlot;
            bool valid = _hoverValid;
            bool rotated = _rotated;
            Cancel();

            _dropFrame = Time.frameCount;
            evt.StopPropagation();

            if (target == null || source.Container == null || target.Container == null) return;

            if (target.IsGrid)
            {
                if (!valid) return;
                if (targetSlot < 0)
                {
                    Dropped?.Invoke(source.Container, sourceSlot, target.Container, -1, false);
                    return;
                }
                bool unchanged = target == source && targetSlot == sourceSlot && rotated == source.GetStack(sourceSlot).Rotated;
                if (unchanged) return;
                Dropped?.Invoke(source.Container, sourceSlot, target.Container, targetSlot, rotated);
                return;
            }

            // Plain slots: onto a slot, or anywhere on the panel to place automatically.
            if (target == source && (targetSlot == sourceSlot || targetSlot < 0)) return;
            Dropped?.Invoke(source.Container, sourceSlot, target.Container, targetSlot, false);
        }
    }
}
