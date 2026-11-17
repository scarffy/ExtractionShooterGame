using ExtractionShooter.Gameplay.Player;

namespace ExtractionShooter.Gameplay.Interaction
{
    /// <summary>
    /// Something the local player can use by looking at it and pressing Interact.
    /// Called on the owning client; implementations send their own validated server request.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Short verb phrase for the HUD prompt, e.g. "Search Supply Crate".</summary>
        string GetPrompt(PlayerCharacter player);

        bool CanInteract(PlayerCharacter player);

        void Interact(PlayerCharacter player);
    }
}
