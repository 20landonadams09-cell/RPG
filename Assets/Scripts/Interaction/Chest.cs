using UnityEngine;

namespace BasicRPG.Interaction
{
    /// <summary>
    /// Openable container — used for both chests and metal caches (different label/loot).
    /// Interact toggles open; the lid transform rotates open/closed smoothly in Update.
    /// The first open fires a loot toast (no inventory yet — that's a later system).
    /// </summary>
    public class Chest : MonoBehaviour, IInteractable
    {
        [SerializeField] private string label = "Chest";
        [SerializeField] private string lootMessage = "Found 3 iron ingots";
        [SerializeField] private Transform lid;        // optional child/pivot to rotate
        [SerializeField] private float openAngle = 95f;
        [SerializeField] private float openSpeed = 120f; // deg/s

        private bool isOpen;
        private bool looted;

        public string GetPrompt() => $"{(isOpen ? "Close" : "Open")} {label}";

        public void Interact(GameObject interactor)
        {
            isOpen = !isOpen;
            if (isOpen && !looted)
            {
                looted = true;
                NotificationUI.Show(lootMessage);
            }
        }

        void Update()
        {
            if (lid == null) return;
            Quaternion target = isOpen
                ? Quaternion.Euler(-openAngle, 0f, 0f)
                : Quaternion.identity;
            lid.localRotation = Quaternion.RotateTowards(
                lid.localRotation, target, openSpeed * Time.deltaTime);
        }
    }
}