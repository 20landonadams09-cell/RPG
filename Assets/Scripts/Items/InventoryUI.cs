using UnityEngine;
using UnityEngine.UI;
using BasicRPG.Interaction;

namespace BasicRPG.Items
{
    /// <summary>
    /// Toggle inventory panel (press I). Builds its own slot cells at runtime so the scene
    /// builder only has to wire container references. While open: locks movement, pauses camera
    /// orbit, and unlocks the cursor so slots are clickable. Click a bag slot to use/equip;
    /// click an equipment slot to unequip.
    /// </summary>
    public class InventoryUI : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }

        [SerializeField] private Inventory inventory;
        [SerializeField] private GameObject panel;
        [SerializeField] private Transform bagParent;          // has a GridLayoutGroup
        [SerializeField] private Transform weaponSlotParent;
        [SerializeField] private Transform armorSlotParent;
        [SerializeField] private int bagSlotCount = 12;

        private class SlotView
        {
            public Image bg;
            public Text nameText;
            public Text countText;
        }

        private SlotView[] bag;
        private SlotView weapon;
        private SlotView armor;
        private Font font;

        static readonly Color ColMetal = new Color(0.55f, 0.55f, 0.6f, 1f);
        static readonly Color ColConsumable = new Color(0.20f, 0.75f, 0.40f, 1f);
        static readonly Color ColEquipment = new Color(0.30f, 0.55f, 0.85f, 1f);
        static readonly Color ColMisc = new Color(0.7f, 0.7f, 0.7f, 1f);
        static readonly Color ColEmpty = new Color(0.15f, 0.15f, 0.15f, 0.6f);

        void Awake()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            bag = new SlotView[bagSlotCount];
            for (int i = 0; i < bagSlotCount; i++)
            {
                int captured = i;
                bag[i] = CreateSlot(bagParent, () => inventory.UseOrEquip(captured));
            }
            weapon = CreateSlot(weaponSlotParent, () => inventory.Unequip(EquipSlot.Weapon));
            armor = CreateSlot(armorSlotParent, () => inventory.Unequip(EquipSlot.Armor));

            if (inventory != null) inventory.OnChanged += Refresh;
            if (panel != null) panel.SetActive(false);
        }

        void Update()
        {
            // Don't let the player pop the inventory open while the guided tutorial is running
            // (the tutorial freezes the world but leaves allomancy input flowing; inventory would
            // pull them out of the step). The tutorial releases this on finish/destroy.
            if (Input.GetKeyDown(KeyCode.I) && !InteractionLock.TutorialActive) Toggle();
        }

        void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        void Open()
        {
            IsOpen = true;
            InteractionLock.IsLocked = true;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            if (panel != null) panel.SetActive(true);
            Refresh();
        }

        void Close()
        {
            IsOpen = false;
            InteractionLock.IsLocked = false;
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
            if (panel != null) panel.SetActive(false);
        }

        void Refresh()
        {
            if (inventory == null) return;

            for (int i = 0; i < bag.Length; i++)
            {
                var stack = i < inventory.Stacks.Count ? inventory.Stacks[i] : null;
                if (stack != null && stack.item != null)
                {
                    bag[i].bg.color = CategoryColor(stack.item.category);
                    bag[i].nameText.text = stack.item.displayName;
                    bag[i].countText.text = stack.count > 1 ? stack.count.ToString() : "";
                }
                else
                {
                    bag[i].bg.color = ColEmpty;
                    bag[i].nameText.text = "";
                    bag[i].countText.text = "";
                }
            }

            FillEquip(weapon, inventory.EquippedWeapon, "Weapon");
            FillEquip(armor, inventory.EquippedArmor, "Armor");
        }

        void FillEquip(SlotView view, ItemSO item, string label)
        {
            view.nameText.text = item != null ? $"{label}\n{item.displayName}" : $"{label}\nEmpty";
            view.bg.color = item != null ? CategoryColor(item.category) : ColEmpty;
            view.countText.text = "";
        }

        Color CategoryColor(ItemCategory c)
        {
            switch (c)
            {
                case ItemCategory.Metal: return ColMetal;
                case ItemCategory.Consumable: return ColConsumable;
                case ItemCategory.Equipment: return ColEquipment;
                default: return ColMisc;
            }
        }

        SlotView CreateSlot(Transform parent, UnityEngine.Events.UnityAction onClick)
        {
            GameObject obj = new GameObject("Slot");
            obj.transform.SetParent(parent, false);
            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            SlotView view = new SlotView();
            view.bg = obj.AddComponent<Image>();
            view.bg.color = ColEmpty;

            Button button = obj.AddComponent<Button>();
            button.onClick.AddListener(onClick);

            // Name label (top of cell)
            GameObject nameObj = new GameObject("Name");
            nameObj.transform.SetParent(obj.transform, false);
            RectTransform nameRT = nameObj.AddComponent<RectTransform>();
            nameRT.anchorMin = new Vector2(0f, 0.5f);
            nameRT.anchorMax = new Vector2(1f, 1f);
            nameRT.offsetMin = new Vector2(2f, 0f);
            nameRT.offsetMax = new Vector2(-2f, -1f);
            view.nameText = nameObj.AddComponent<Text>();
            view.nameText.font = font;
            view.nameText.fontSize = 11;
            view.nameText.alignment = TextAnchor.UpperCenter;
            view.nameText.color = Color.white;
            view.nameText.raycastTarget = false;

            // Count label (bottom-right)
            GameObject countObj = new GameObject("Count");
            countObj.transform.SetParent(obj.transform, false);
            RectTransform countRT = countObj.AddComponent<RectTransform>();
            countRT.anchorMin = new Vector2(1f, 0f);
            countRT.anchorMax = new Vector2(1f, 0f);
            countRT.pivot = new Vector2(1f, 0f);
            countRT.sizeDelta = new Vector2(28f, 16f);
            countRT.anchoredPosition = new Vector2(-2f, 1f);
            view.countText = countObj.AddComponent<Text>();
            view.countText.font = font;
            view.countText.fontSize = 12;
            view.countText.alignment = TextAnchor.LowerRight;
            view.countText.color = Color.white;
            view.countText.raycastTarget = false;

            return view;
        }
    }
}