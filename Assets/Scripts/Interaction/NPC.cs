using UnityEngine;

namespace BasicRPG.Interaction
{
    /// <summary>Linear dialogue NPC. Interact opens DialogueManager with the NPC's name + lines.</summary>
    public class NPC : MonoBehaviour, IInteractable
    {
        [SerializeField] private string npcName = "NPC";
        [TextArea] [SerializeField] private string[] lines = { "Hello, traveler." };

        public string GetPrompt() => $"Talk to {npcName}";

        public void Interact(GameObject interactor)
        {
            if (lines == null || lines.Length == 0) return;
            DialogueManager.StartDialogue(npcName, lines);
        }
    }
}