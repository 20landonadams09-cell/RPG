using System.Collections.Generic;
using UnityEngine;
using BasicRPG.Stats;
using BasicRPG.Items;
using BasicRPG.Allomancy;
using BasicRPG.Interaction;

namespace BasicRPG.Player
{
    /// <summary>
    /// Save / load the player's state to a JSON file in <see cref="Application.persistentDataPath"/>.
    /// Persists position + rotation, health, stamina, inventory bag + equipment, and allomantic
    /// reserves + active metal + burn toggle. Items are stored by stable id and resolved back via
    /// <see cref="ItemSO.GetById"/> on load (ItemSOs are assets, not scene objects). F5 = save,
    /// F9 = load. No keybinds while a dialogue/inventory/wheel lock is held.
    ///
    /// Note: this saves player *state*, not the *world* (consumed pickups stay consumed only if
    /// rebuilt from the same scene — re-running the builder re-creates them). It's a checkpoint
    /// of how the player stands, not a full world save. That matches the README's "no save system
    /// yet — just the basics" gap, now filled for the player.
    /// </summary>
    public class SaveSystem : MonoBehaviour
    {
        [SerializeField] private Health health;
        [SerializeField] private Stamina stamina;
        [SerializeField] private Inventory inventory;
        [SerializeField] private Allomancer allomancer;
        [SerializeField] private CharacterController controller;

        const string FILE = "save.json";

        string Path => System.IO.Path.Combine(Application.persistentDataPath, FILE);

        void Update()
        {
            if (InteractionLock.IsLocked || InteractionLock.TutorialActive) return;
            if (Keybinds.SaveDown()) Save();
            if (Keybinds.LoadDown()) Load();
        }

        // ── Save ───────────────────────────────────────────────────────────────────────

        public void Save()
        {
            SaveData data = new SaveData
            {
                posX = transform.position.x,
                posY = transform.position.y,
                posZ = transform.position.z,
                rotY = transform.eulerAngles.y,
                health = health != null ? health.CurrentHealth : 0,
                stamina = stamina != null ? stamina.Current : 0f,
                equippedWeapon = inventory != null ? inventory.SaveEquippedWeapon() : null,
                equippedArmor = inventory != null ? inventory.SaveEquippedArmor() : null,
                reserves = allomancer != null ? allomancer.SaveReserves() : null,
                activeMetal = allomancer != null ? (int)allomancer.ActiveMetal : 0,
                burningSet = allomancer != null ? allomancer.SaveBurningSet() : null,
                burnPaused = allomancer != null && allomancer.IsBurningPaused,
            };

            if (inventory != null)
            {
                List<BagEntry> bag = new List<BagEntry>();
                foreach (var (id, count) in inventory.SaveStacks())
                    bag.Add(new BagEntry { id = id, count = count });
                data.bag = bag.ToArray();
            }

            try
            {
                System.IO.File.WriteAllText(Path, JsonUtility.ToJson(data, true));
                NotificationUI.Show("Game saved");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SaveSystem] Save failed: {e.Message}");
                NotificationUI.Show("Save failed");
            }
        }

        // ── Load ───────────────────────────────────────────────────────────────────────

        public void Load()
        {
            if (!System.IO.File.Exists(Path))
            {
                NotificationUI.Show("No save file");
                return;
            }

            SaveData data;
            try { data = JsonUtility.FromJson<SaveData>(System.IO.File.ReadAllText(Path)); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SaveSystem] Load failed: {e.Message}");
                NotificationUI.Show("Load failed");
                return;
            }

            if (data == null) { NotificationUI.Show("Load failed"); return; }

            // Position — teleport via the controller (disable it so Move can't fight the snap).
            Vector3 pos = new Vector3(data.posX, data.posY, data.posZ);
            if (controller != null)
            {
                controller.enabled = false;
                transform.position = pos;
                controller.enabled = true;
            }
            else
            {
                transform.position = pos;
            }
            transform.rotation = Quaternion.Euler(0f, data.rotY, 0f);

            if (health != null) health.LoadState(data.health);
            if (stamina != null) stamina.LoadState(data.stamina);

            if (inventory != null)
            {
                List<(string id, int count)> bag = new List<(string, int)>();
                if (data.bag != null)
                    foreach (BagEntry e in data.bag)
                        if (e != null) bag.Add((e.id, e.count));
                inventory.LoadSaveData(bag, data.equippedWeapon, data.equippedArmor);
            }

            if (allomancer != null && data.reserves != null)
                allomancer.LoadReserves(data.reserves, (MetalType)data.activeMetal, data.burningSet, data.burnPaused);

            NotificationUI.Show("Game loaded");
        }

        // ── Serializable state ─────────────────────────────────────────────────────────

        [System.Serializable]
        public class SaveData
        {
            public float posX, posY, posZ, rotY;
            public int health;
            public float stamina;
            public string equippedWeapon;
            public string equippedArmor;
            public BagEntry[] bag;
            public float[] reserves;
            public int activeMetal;
            public bool[] burningSet;   // multi-burn burn-set membership (per (int)MetalType)
            public bool burnPaused;     // B-key global pause state
        }

        [System.Serializable]
        public class BagEntry
        {
            public string id;
            public int count;
        }
    }
}