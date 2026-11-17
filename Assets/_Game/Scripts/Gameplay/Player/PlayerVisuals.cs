using ExtractionShooter.Gameplay.Combat;
using FishNet.Object;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Player
{
    /// <summary>
    /// Client-side presentation of the player body: hidden for the owner
    /// (first person) and for everyone while dead.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerVisuals : NetworkBehaviour
    {
        [Tooltip("Renderers that make up the third-person body.")]
        [SerializeField] private Renderer[] _bodyRenderers;

        private Health _health;

        private void Awake()
        {
            _health = GetComponent<Health>();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            _health.Changed += OnHealthChanged;
            Refresh();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            _health.Changed -= OnHealthChanged;
        }

        private void OnHealthChanged(int previous, int current) => Refresh();

        private void Refresh()
        {
            bool visible = !IsOwner && _health.IsAlive;
            foreach (Renderer bodyRenderer in _bodyRenderers)
            {
                if (bodyRenderer != null) bodyRenderer.enabled = visible;
            }
        }
    }
}
