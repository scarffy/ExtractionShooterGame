using System.Collections;
using FishNet.Object;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Inventory
{
    /// <summary>
    /// Server only: despawns this networked object after a fixed time (corpses, dropped items).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkLifetime : NetworkBehaviour
    {
        [Tooltip("Seconds before the object is despawned. 0 keeps it forever.")]
        [SerializeField, Min(0f)] private float _lifetime = 300f;

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (_lifetime > 0f) StartCoroutine(DespawnAfterLifetime());
        }

        private IEnumerator DespawnAfterLifetime()
        {
            yield return new WaitForSeconds(_lifetime);
            if (IsSpawned) Despawn();
        }
    }
}
