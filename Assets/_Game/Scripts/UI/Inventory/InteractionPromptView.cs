using ExtractionShooter.Gameplay.Interaction;
using ExtractionShooter.Gameplay.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Inventory
{
    /// <summary>
    /// Shows "[E] Search Supply Crate" while the local player looks at something interactable.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class InteractionPromptView : MonoBehaviour
    {
        private const string HiddenClass = "hidden";

        [SerializeField] private string _keyHint = "[E]";
        [SerializeField] private InventoryScreen _inventoryScreen;

        private Label _prompt;
        private PlayerCharacter _player;
        private IInteractable _target;

        private void OnEnable()
        {
            _prompt = GetComponent<UIDocument>().rootVisualElement.Q<Label>("interaction-prompt");
            if (_prompt == null)
            {
                Debug.LogError($"[{nameof(InteractionPromptView)}] UXML has no Label named 'interaction-prompt'.", this);
                enabled = false;
                return;
            }

            PlayerCharacter.LocalPlayerChanged += Bind;
            Bind(PlayerCharacter.Local);
        }

        private void OnDisable()
        {
            PlayerCharacter.LocalPlayerChanged -= Bind;
            Bind(null);
        }

        private void LateUpdate()
        {
            bool visible = _target != null && _player != null && (_inventoryScreen == null || !_inventoryScreen.IsOpen);
            if (visible) _prompt.text = $"{_keyHint} {_target.GetPrompt(_player)}";
            _prompt.EnableInClassList(HiddenClass, !visible);
        }

        private void Bind(PlayerCharacter player)
        {
            if (_player != null && _player.Interactor != null) _player.Interactor.TargetChanged -= OnTargetChanged;

            _player = player;
            _target = null;

            if (_player != null && _player.Interactor != null)
            {
                _player.Interactor.TargetChanged += OnTargetChanged;
                _target = _player.Interactor.Target;
            }
        }

        private void OnTargetChanged(IInteractable target) => _target = target;
    }
}
