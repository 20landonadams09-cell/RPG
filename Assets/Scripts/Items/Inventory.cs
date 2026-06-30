using System;
using System.Collections.Generic;
using UnityEngine;
using BasicRPG.Stats;
using BasicRPG.Interaction;

namespace BasicRPG.Items
{
    /// <summary>
    /// Player inventory: a bag of item stacks plus Weapon/Armor equipment slots.
    /// Consumables apply effects via Health/Stamina; equipment moves into its slot (swapping).
    /// Fires OnChanged whenever contents change so the UI can refresh.
    /// </summary>
    public class Inventory : MonoBehaviour
    {
        [SerializeField] private Health health;
        [SerializeField] private Stamina stamina;
        [SerializeField] private int bagCapacity = 12;

        [SerializeField] private List<ItemStack> stacks = new List<ItemStack>();
        [SerializeField] private ItemSO equippedWeapon;
        [SerializeField] private ItemSO equippedArmor;

        public event Action OnChanged;
        public IReadOnlyList<ItemStack> Stacks => stacks;
        public ItemSO EquippedWeapon => equippedWeapon;
        public ItemSO EquippedArmor => equippedArmor;
        public int BagCapacity => bagCapacity;

        /// <summary>Add as much as fits. Returns true if all `count` were added.</summary>
        public bool Add(ItemSO item, int count)
        {
            if (item == null || count <= 0) return false;
            int remaining = count;

            if (item.stackable)
            {
                // Fill existing stacks first.
                for (int i = 0; i < stacks.Count && remaining > 0; i++)
                {
                    if (stacks[i].item != item) continue;
                    int room = item.maxStack - stacks[i].count;
                    if (room <= 0) continue;
                    int add = Mathf.Min(room, remaining);
                    stacks[i].count += add;
                    remaining -= add;
                }
                // Then new stacks.
                while (remaining > 0)
                {
                    if (stacks.Count >= bagCapacity) { Raise(); return false; }
                    int add = Mathf.Min(item.maxStack, remaining);
                    stacks.Add(new ItemStack(item, add));
                    remaining -= add;
                }
            }
            else
            {
                // Non-stackable (equipment): one stack per unit.
                while (remaining > 0)
                {
                    if (stacks.Count >= bagCapacity) { Raise(); return false; }
                    stacks.Add(new ItemStack(item, 1));
                    remaining--;
                }
            }

            Raise();
            return remaining == 0;
        }

        public int CountOf(ItemSO item)
        {
            int total = 0;
            for (int i = 0; i < stacks.Count; i++)
                if (stacks[i].item == item) total += stacks[i].count;
            return total;
        }

        /// <summary>Remove up to `count` of an item from the bag (reverse of Add). Returns true if the full count was consumed.</summary>
        public bool Consume(ItemSO item, int count)
        {
            if (item == null || count <= 0) return false;
            if (CountOf(item) < count) return false;

            int remaining = count;
            for (int i = stacks.Count - 1; i >= 0 && remaining > 0; i--)
            {
                if (stacks[i].item != item) continue;
                int take = Mathf.Min(remaining, stacks[i].count);
                stacks[i].count -= take;
                remaining -= take;
                if (stacks[i].count <= 0) stacks.RemoveAt(i);
            }
            Raise();
            return remaining == 0;
        }

        /// <summary>Bag-slot click: use a consumable, or equip equipment. No-op for metals/misc.</summary>
        public void UseOrEquip(int bagIndex)
        {
            if (bagIndex < 0 || bagIndex >= stacks.Count) return;
            ItemSO item = stacks[bagIndex].item;
            if (item == null) return;

            switch (item.category)
            {
                case ItemCategory.Consumable: UseConsumable(bagIndex); break;
                case ItemCategory.Equipment: Equip(bagIndex); break;
                // Metal / Misc: nothing to do yet.
            }
        }

        void UseConsumable(int bagIndex)
        {
            ItemStack stack = stacks[bagIndex];
            ItemSO item = stack.item;
            if (health != null && item.healthRestore > 0f) health.Heal(Mathf.RoundToInt(item.healthRestore));
            if (stamina != null && item.staminaRestore > 0f) stamina.Restore(item.staminaRestore);
            NotificationUI.Show($"Used {item.displayName}");
            RemoveOne(bagIndex);
            Raise();
        }

        /// <summary>Equip the equipment item at the given bag index into its slot (swapping).</summary>
        void Equip(int bagIndex)
        {
            ItemStack stack = stacks[bagIndex];
            ItemSO item = stack.item;
            if (item.equipSlot == EquipSlot.None) return;

            ItemSO previous = (item.equipSlot == EquipSlot.Weapon) ? equippedWeapon : equippedArmor;

            // Remove the item from the bag (equipment is non-stackable — the whole stack goes).
            stacks.RemoveAt(bagIndex);

            // Return whatever was previously equipped (always fits: we just freed a slot).
            if (previous != null) stacks.Add(new ItemStack(previous, 1));

            if (item.equipSlot == EquipSlot.Weapon) equippedWeapon = item;
            else equippedArmor = item;

            NotificationUI.Show($"Equipped {item.displayName}");
            Raise();
        }

        public void Unequip(EquipSlot slot)
        {
            ItemSO current = slot == EquipSlot.Weapon ? equippedWeapon : equippedArmor;
            if (current == null) return;

            if (stacks.Count >= bagCapacity)
            {
                NotificationUI.Show("Bag full — can't unequip");
                return;
            }

            stacks.Add(new ItemStack(current, 1));
            if (slot == EquipSlot.Weapon) equippedWeapon = null;
            else equippedArmor = null;

            NotificationUI.Show($"Unequipped {current.displayName}");
            Raise();
        }

        void RemoveOne(int bagIndex)
        {
            stacks[bagIndex].count--;
            if (stacks[bagIndex].count <= 0) stacks.RemoveAt(bagIndex);
        }

        void Raise() => OnChanged?.Invoke();

        // ── Save / load ───────────────────────────────────────────────────────────────
        // Items are saved by stable `id` (ItemSO.GetById resolves them on load). Equipment
        // ids are null when the slot is empty.

        public System.Collections.Generic.IEnumerable<(string id, int count)> SaveStacks()
        {
            for (int i = 0; i < stacks.Count; i++)
                if (stacks[i] != null && stacks[i].item != null)
                    yield return (stacks[i].item.id, stacks[i].count);
        }

        public string SaveEquippedWeapon() => equippedWeapon != null ? equippedWeapon.id : null;
        public string SaveEquippedArmor() => equippedArmor != null ? equippedArmor.id : null;

        /// <summary>Clear the bag + equipment and rebuild from saved ids. Items whose id can't
        /// be resolved (asset not loaded) are dropped silently.</summary>
        public void LoadSaveData(System.Collections.Generic.IEnumerable<(string id, int count)> bag,
                                 string weaponId, string armorId)
        {
            stacks.Clear();
            equippedWeapon = null;
            equippedArmor = null;

            if (bag != null)
            {
                foreach (var entry in bag)
                {
                    ItemSO item = ItemSO.GetById(entry.id);
                    if (item == null || entry.count <= 0) continue;
                    Add(item, entry.count); // Add respects stackability / capacity
                }
            }

            equippedWeapon = ItemSO.GetById(weaponId);
            equippedArmor = ItemSO.GetById(armorId);
            Raise();
        }
    }
}