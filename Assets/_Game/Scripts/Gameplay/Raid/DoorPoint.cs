using ExtractionShooter.Gameplay.Interaction;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Raid
{
    /// <summary>
    /// Where a <see cref="LockedDoor"/> goes and how it fits: placed at the bottom centre of a doorway, local x across the
    /// opening and local z through the wall. Read by the server's door spawner before it spawns the door.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DoorPoint : MonoBehaviour
    {
        [Tooltip("Lock id a key must match (the key item's lock id). Empty: a plain door anyone can open.")]
        [SerializeField] private string _lockId = string.Empty;

        [Tooltip("Name in the interaction prompt.")]
        [SerializeField] private string _displayName = "Door";

        [Tooltip("Doorway width (local x), height, and panel thickness (local z), in metres.")]
        [SerializeField] private Vector3 _size = new Vector3(1.4f, 2.38f, 0.08f);

        [Tooltip("Slides sideways into the wall (heavy vault doors) instead of swinging.")]
        [SerializeField] private bool _slides;

        [Tooltip("Swings toward local +z (slides toward +x) instead of -z (-x).")]
        [SerializeField] private bool _reverse;

        public LockedDoorSettings Settings => new LockedDoorSettings
        {
            LockId = _lockId,
            DisplayName = _displayName,
            Size = _size,
            Slides = _slides,
            Reverse = _reverse,
        };

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = string.IsNullOrEmpty(_lockId) ? new Color(0.4f, 0.85f, 0.4f) : new Color(0.95f, 0.3f, 0.25f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0f, _size.y * 0.5f, 0f), _size);
        }
#endif
    }
}
