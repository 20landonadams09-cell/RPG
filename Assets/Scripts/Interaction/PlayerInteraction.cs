using UnityEngine;
using UnityEngine.UI;
using BasicRPG.Items;

namespace BasicRPG.Interaction
{
    /// <summary>
    /// Proximity interaction driver on the player. Each frame finds the nearest
    /// IInteractable in range, shows a prompt, and fires Interact() on the interact key.
    /// </summary>
    public class PlayerInteraction : MonoBehaviour
    {
        [SerializeField] private float range = 2.6f;
        [SerializeField] private KeyCode interactKey = KeyCode.E;
        [SerializeField] private GameObject promptRoot;
        [SerializeField] private Text promptText;

        private readonly Collider[] hits = new Collider[16];
        private IInteractable current;

        void Update()
        {
            // While a dialogue, the inventory, or the tutorial is active, that UI owns input —
            // no proximity prompts and no E to interact (the tutorial freezes the world but must
            // not let the player trigger interactables mid-step).
            if (DialogueManager.IsOpen || InventoryUI.IsOpen || InteractionLock.TutorialActive)
            {
                if (promptRoot != null) promptRoot.SetActive(false);
                return;
            }

            current = FindNearest();

            if (current != null)
            {
                if (promptRoot != null) promptRoot.SetActive(true);
                if (promptText != null) promptText.text = $"Press E — {current.GetPrompt()}";
            }
            else if (promptRoot != null)
            {
                promptRoot.SetActive(false);
            }

            // JustClosed guards against the E that ended a dialogue reopening it this frame.
            if (Input.GetKeyDown(interactKey) && current != null && !DialogueManager.JustClosed)
            {
                current.Interact(gameObject);
            }
        }

        IInteractable FindNearest()
        {
            IInteractable best = null;
            float bestSqr = float.MaxValue;
            Vector3 pos = transform.position;

            int count = Physics.OverlapSphereNonAlloc(pos, range, hits);
            for (int i = 0; i < count; i++)
            {
                Collider col = hits[i];
                if (col == null) continue;
                // Non-generic form is the safest way to query an interface across Unity versions.
                var interactable = col.GetComponentInParent(typeof(IInteractable)) as IInteractable;
                if (interactable == null) continue;
                // Dedup: a chest has a body + lid collider both pointing at the same Chest.
                if (best != null && ReferenceEquals(best, interactable)) continue;
                float sqr = (col.transform.position - pos).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = interactable;
                }
            }
            return best;
        }
    }
}