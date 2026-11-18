using System;
using ExtractionShooter.Gameplay.Player;
using FishNet.Object;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Interaction
{
    /// <summary>
    /// Owner only: finds the <see cref="IInteractable"/> under the crosshair and uses it on Interact.
    /// Purely a client convenience; every resulting action is validated by the server.
    /// Also carries the owner's requests to server-owned world objects that have no inventory route (doors).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCharacter))]
    public sealed class PlayerInteractor : NetworkBehaviour
    {
        private const int HitBufferSize = 8;

        [SerializeField, Min(0.5f)] private float _range = 2.5f;
        [SerializeField] private LayerMask _interactionMask = ~0;

        private readonly RaycastHit[] _hitBuffer = new RaycastHit[HitBufferSize];

        private PlayerCharacter _character;
        private IInteractable _target;

        /// <summary>Owner side. Raised when the interactable under the crosshair changes (may be null).</summary>
        public event Action<IInteractable> TargetChanged;

        public IInteractable Target => _target;

        private void Awake()
        {
            _character = GetComponent<PlayerCharacter>();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            SetTarget(null);
        }

        private void Update()
        {
            if (!IsOwner) return;

            IInteractable found = _character.Health.IsAlive ? FindTarget() : null;
            SetTarget(found);

            if (_target != null && _character.Input.InteractPressed)
                _target.Interact(_character);
        }

        /// <summary>Owner: asks the server to open a door, using up one use of a carried key if it is locked.</summary>
        public void RequestOpenDoor(LockedDoor door)
        {
            if (IsOwner && door != null) ServerOpenDoor(door);
        }

        [ServerRpc]
        private void ServerOpenDoor(LockedDoor door)
        {
            // The door checks reach, the player being alive and the key itself.
            if (door != null && door.IsSpawned) door.ServerTryOpen(_character);
        }

        private IInteractable FindTarget()
        {
            Vector3 origin = _character.Look.EyePosition;
            int count = Physics.RaycastNonAlloc(origin, _character.Look.AimDirection, _hitBuffer, _range,
                _interactionMask, QueryTriggerInteraction.Collide);

            IInteractable closest = null;
            float closestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hitBuffer[i];
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= closestDistance) continue;

                closestDistance = hit.distance;
                IInteractable interactable = hit.collider.GetComponentInParent<IInteractable>();
                closest = interactable != null && interactable.CanInteract(_character) ? interactable : null;
            }
            return closest;
        }

        private void SetTarget(IInteractable target)
        {
            if (ReferenceEquals(target, _target)) return;
            _target = target;
            TargetChanged?.Invoke(_target);
        }
    }
}
