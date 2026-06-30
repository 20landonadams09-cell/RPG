using UnityEngine;

namespace BasicRPG.Interaction
{
    /// <summary>Anything the player can interact with via proximity + E.</summary>
    public interface IInteractable
    {
        /// <summary>Short verb phrase shown in the prompt, e.g. "Talk to Sazed", "Open Chest".</summary>
        string GetPrompt();

        /// <summary>Called when the player presses the interact key while this is the current target.</summary>
        void Interact(GameObject interactor);
    }
}