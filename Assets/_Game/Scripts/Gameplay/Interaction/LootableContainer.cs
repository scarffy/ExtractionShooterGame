using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Player;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Interaction
{
    /// <summary>
    /// Makes an <see cref="ItemContainer"/> searchable: interacting asks the server to open it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ItemContainer))]
    public sealed class LootableContainer : MonoBehaviour, IInteractable
    {
        [SerializeField] private string _promptVerb = "Search";

        private ItemContainer _container;

        private void Awake()
        {
            _container = GetComponent<ItemContainer>();
        }

        public string GetPrompt(PlayerCharacter player) => $"{_promptVerb} {_container.DisplayName}";

        public bool CanInteract(PlayerCharacter player) =>
            player != null && player.Health.IsAlive && player.Inventory != null && _container.IsSpawned;

        public void Interact(PlayerCharacter player) => player.Inventory.RequestOpen(_container);
    }
}
