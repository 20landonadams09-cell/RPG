using UnityEngine;
using BasicRPG.Items;

namespace BasicRPG.Interaction
{
    /// <summary>
    /// A world item you pick up via the interaction prompt. On interact, adds to the
    /// interactor's Inventory and destroys itself.
    /// </summary>
    public class ItemPickup : MonoBehaviour, IInteractable
    {
        [SerializeField] private ItemSO item;
        [SerializeField] private int count = 1;

        /// <summary>Set the item/count at runtime (used when enemies drop loot).</summary>
        public void Init(ItemSO item, int count) { this.item = item; this.count = count; }

        public string GetPrompt() => item != null ? $"Pick up {item.displayName}" : "Pick up";

        public void Interact(GameObject interactor)
        {
            if (item == null || interactor == null) return;
            var inv = interactor.GetComponent<Inventory>();
            if (inv == null) return;

            if (inv.Add(item, count))
            {
                NotificationUI.Show(count > 1 ? $"Picked up {count} x {item.displayName}" : $"Picked up {item.displayName}");
                Destroy(gameObject);
            }
            else
            {
                NotificationUI.Show("Inventory full");
            }
        }
    }
}