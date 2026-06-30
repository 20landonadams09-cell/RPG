using System;

namespace BasicRPG.Items
{
    /// <summary>A stack of one item type in the inventory.</summary>
    [Serializable]
    public class ItemStack
    {
        public ItemSO item;
        public int count;

        public ItemStack() { }
        public ItemStack(ItemSO item, int count) { this.item = item; this.count = count; }
    }
}