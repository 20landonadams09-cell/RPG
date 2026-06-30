#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using BasicRPG.Player;
using BasicRPG.Stats;
using BasicRPG.UI;
using BasicRPG.Interaction;
using BasicRPG.Items;
using BasicRPG.Combat;
using BasicRPG.Allomancy;
using UniversalRendererData = UnityEngine.Rendering.Universal.UniversalRendererData;
using UniversalRenderPipelineAsset = UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;

/// <summary>
/// One-click starter scene + URP setup. Menu: RPG → Build Starter Scene.
/// Creates the URP render-pipeline assets (so nothing renders pink), builds a small
/// play scene (ground + obstacles + player capsule + third-person camera + HUD), saves
/// it, and adds it to Build Settings at index 0. Idempotent: safe to re-run.
/// </summary>
public static class RPGSceneBuilder
{
    const string MENU = "RPG/Build Starter Scene";

    const string SETTINGS_DIR = "Assets/Settings";
    const string URP_ASSET_PATH = "Assets/Settings/URP.asset";
    const string URP_RENDERER_PATH = "Assets/Settings/URP_Renderer.asset";
    const string SCENE_PATH = "Assets/Scenes/Starter.unity";
    const string TIN_TEST_PATH = "Assets/Scenes/TinTest.unity";
    const string PEWTER_TEST_PATH = "Assets/Scenes/PewterTest.unity";
    const string IRONSTEEL_TEST_PATH = "Assets/Scenes/IronSteelTest.unity";
    const string ZINCBRASS_TEST_PATH = "Assets/Scenes/ZincBrassTest.unity";
    const string COPPERBRONZE_TEST_PATH = "Assets/Scenes/CopperBronzeTest.unity";

    const string URP_LIT_SHADER = "Universal Render Pipeline/Lit";

    [MenuItem(MENU)]
    public static void Build()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[RPGBuilder] Cannot build while in Play mode. Exit Play mode first, then run RPG → Build Starter Scene.");
            return;
        }

        Log("Starting build...");

        if (!Directory.Exists(SETTINGS_DIR)) AssetDatabase.CreateFolder("Assets", "Settings");
        if (!Directory.Exists("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");

        SetupURP();
        EnsureRightStickAxes();
        BuildScene();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Log("Build complete. Press Play. Controls: WASD move, Shift sprint, Space jump, mouse look, E interact, I inventory, K damage, H heal, LMB attack, RMB block, C dodge, Tab metal wheel, B burn metal, X drink metal, 1-8 select metal, F steelpush, Q ironpull. GAMEPAD (DualSense): left stick move, right stick look, Cross jump, Circle dodge, Square interact, Triangle burn, L1 block, R1 attack, L2 pull, R2 push, Share wheel, Options flare, L3 sprint, R3 inventory, Dpad Up drink, Dpad Left save, Dpad Right load.");
    }

    // ---------------------------------------------------------------- URP ----

    static void SetupURP()
    {
        // Renderer data
        UniversalRendererData renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(URP_RENDERER_PATH);
        if (renderer == null)
        {
            renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            renderer.name = "URP_Renderer";
            renderer.opaqueLayerMask = ~0;
            renderer.transparentLayerMask = ~0;
            AssetDatabase.CreateAsset(renderer, URP_RENDERER_PATH);
            Log($"Created renderer data: {URP_RENDERER_PATH}");
        }
        else
        {
            Log($"Renderer data already exists: {URP_RENDERER_PATH}");
        }

        // Pipeline asset
        UniversalRenderPipelineAsset pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(URP_ASSET_PATH);
        if (pipeline == null)
        {
            pipeline = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
            pipeline.name = "URP";
            AssetDatabase.CreateAsset(pipeline, URP_ASSET_PATH);
            Log($"Created pipeline asset: {URP_ASSET_PATH}");
        }
        else
        {
            Log($"Pipeline asset already exists: {URP_ASSET_PATH}");
        }

        // Wire the renderer list into the pipeline via serialized fields (stable across URP 12+).
        SerializedObject so = new SerializedObject(pipeline);
        SerializedProperty list = so.FindProperty("m_RendererDataList");
        if (list == null)
        {
            LogWarning("Could not find m_RendererDataList on pipeline asset — URP renderer will need manual assignment.");
        }
        else
        {
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            SerializedProperty defaultIdx = so.FindProperty("m_DefaultRendererIndex");
            if (defaultIdx != null) defaultIdx.intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Assign globally so URP is active immediately (otherwise everything is pink).
        GraphicsSettings.defaultRenderPipeline = pipeline;
        EditorUtility.SetDirty(pipeline);
        Log("URP assigned as the default render pipeline.");
    }

    // ---------------------------------------------------------- Input axes ----
    // The classic Input Manager has no default right-stick axis (Mouse X/Y are mouse-only), so
    // the DualSense right stick can't drive the camera out of the box. Add two Joystick Axis
    // entries — "RightStickX" (physical axis 2) and "RightStickY" (physical axis 3, inverted so
    // up on the stick looks up) — if they aren't already present. This is project-wide
    // (ProjectSettings/InputManager.asset), so it runs once; idempotent. If the right stick is
    // dead or on the wrong axes on your controller/backend, tweak these in
    // Project Settings → Input Manager (axis index is the "Axis" dropdown per entry).
    static void EnsureRightStickAxes()
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset");
        if (assets == null || assets.Length == 0)
        {
            LogWarning("InputManager.asset not found — right-stick camera axes not added (gamepad look will be inert).");
            return;
        }
        SerializedObject so = new SerializedObject(assets[0]);
        SerializedProperty axes = so.FindProperty("m_Axes");
        if (axes == null)
        {
            LogWarning("m_Axes not found on InputManager — right-stick camera axes not added.");
            return;
        }
        bool addedX = EnsureJoyAxis(axes, "RightStickX", axisIndex: 2, invert: false);
        bool addedY = EnsureJoyAxis(axes, "RightStickY", axisIndex: 3, invert: true);
        so.ApplyModifiedProperties();
        if (addedX || addedY)
        {
            EditorUtility.SetDirty(assets[0]);
            Log("Added right-stick Input Manager axes (RightStickX=axis 2, RightStickY=axis 3 inverted). If the right stick is dead/misaligned, adjust in Project Settings → Input Manager.");
        }
    }

    // Append a Joystick Axis entry named `name` (type=2) if none with that name exists. Returns
    // true if a new entry was inserted. Field names match the InputManager.asset YAML keys.
    static bool EnsureJoyAxis(SerializedProperty axes, string name, int axisIndex, bool invert)
    {
        for (int i = 0; i < axes.arraySize; i++)
        {
            SerializedProperty n = axes.GetArrayElementAtIndex(i).FindPropertyRelative("m_Name");
            if (n != null && n.stringValue == name) return false; // already present
        }
        int idx = axes.arraySize;
        axes.InsertArrayElementAtIndex(idx);
        SerializedProperty a = axes.GetArrayElementAtIndex(idx);
        a.FindPropertyRelative("m_Name").stringValue = name;
        a.FindPropertyRelative("descriptiveName").stringValue = "";
        a.FindPropertyRelative("descriptiveNegativeName").stringValue = "";
        a.FindPropertyRelative("negativeButton").stringValue = "";
        a.FindPropertyRelative("positiveButton").stringValue = "";
        a.FindPropertyRelative("altNegativeButton").stringValue = "";
        a.FindPropertyRelative("altPositiveButton").stringValue = "";
        a.FindPropertyRelative("gravity").floatValue = 0f;
        a.FindPropertyRelative("dead").floatValue = 0.19f;
        a.FindPropertyRelative("sensitivity").floatValue = 1f;
        a.FindPropertyRelative("snap").boolValue = false;
        a.FindPropertyRelative("invert").boolValue = invert;
        a.FindPropertyRelative("type").intValue = 2;   // 2 = Joystick Axis
        a.FindPropertyRelative("axis").intValue = axisIndex;
        a.FindPropertyRelative("joyNum").intValue = 0; // 0 = all joysticks
        return true;
    }

    // --------------------------------------------------------------- Scene ----

    static void BuildScene()
    {
        // New scene from scratch (no default objects).
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BuildPlayerCore(out GameObject player, out _, out Transform hudCanvas,
                        out _, out _, out _);
        BuildPlayerSystems(player, hudCanvas, out Inventory inventory,
                           out ItemSO iron, out ItemSO steel, out ItemSO pewter);
        BuildAllomancy(player, hudCanvas, inventory, iron, steel, pewter);
        BuildStarterContent(player, hudCanvas, inventory, iron, steel, pewter);

        // Save
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, SCENE_PATH);
        Log($"Scene saved: {SCENE_PATH}");

        AddToBuildSettings(SCENE_PATH);
    }

    // ── Shared core: ground + player + camera (post-proc on) + light + ambient + HUD + stats.
    // Used by every scene builder so all scenes share identical player/camera/HUD scaffolding.
    static void BuildPlayerCore(out GameObject player, out Camera cam, out Transform hudCanvas,
                                out Health health, out Stamina stamina, out Light dirLight)
    {
        Material litMat = CreateLitMaterial("GroundMat", new Color(0.25f, 0.45f, 0.25f, 1f));
        Material playerMat = CreateLitMaterial("PlayerMat", new Color(0.30f, 0.55f, 0.85f, 1f));

        // Ground
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(5f, 1f, 5f); // plane default 10u -> 50x50
        ground.GetComponent<Renderer>().sharedMaterial = litMat;

        // Player
        player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        player.name = "Player";
        player.tag = "Player";
        player.transform.position = new Vector3(0f, 1f, 0f);
        player.GetComponent<Renderer>().sharedMaterial = playerMat;
        // CreatePrimitive adds a CapsuleCollider; CharacterController is its own collider, so drop the extra.
        var extraCol = player.GetComponent<CapsuleCollider>();
        if (extraCol != null) Object.DestroyImmediate(extraCol);

        // NOTE: use Unity's `== null` (not `??`); GetComponent returns a "fake-null"
        // object when missing, and `??` only checks the C# reference so it would skip AddComponent.
        CharacterController cc = player.GetComponent<CharacterController>();
        if (cc == null) cc = player.AddComponent<CharacterController>();
        cc.height = 2f;
        cc.radius = 0.5f;
        cc.center = new Vector3(0f, 0f, 0f);
        cc.skinWidth = 0.02f;
        cc.minMoveDistance = 0.001f;
        cc.stepOffset = 0.4f;

        PlayerController controller = player.AddComponent<PlayerController>();
        health = player.AddComponent<Health>();
        stamina = player.AddComponent<Stamina>();
        PlayerStats stats = player.AddComponent<PlayerStats>();

        // Main camera
        GameObject camObj = new GameObject("Main Camera");
        camObj.tag = "MainCamera";
        cam = camObj.AddComponent<Camera>();
        camObj.AddComponent<AudioListener>();
        cam.transform.position = new Vector3(0f, 3f, -6f);
        ThirdPersonCamera tpc = camObj.AddComponent<ThirdPersonCamera>();
        tpc.SetTarget(player.transform);

        // URP post-processing must be enabled on the camera or Volumes (Tin) won't render.
        var camData = cam.GetUniversalAdditionalCameraData();
        camData.renderPostProcessing = true;

        SetField(controller, "cameraTransform", camObj.transform);

        // Replace the placeholder capsule visual with Erbium's humanoid model + Animator
        // (locomotion only). Gracefully no-ops if the Erbium assets aren't imported yet.
        BuildPlayerModel(player, controller);

        // Directional light
        dirLight = new GameObject("Directional Light").AddComponent<Light>();
        dirLight.type = LightType.Directional;
        dirLight.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        dirLight.color = new Color(1f, 0.96f, 0.85f, 1f);
        dirLight.intensity = 1.1f;

        // Mild ambient so shadows aren't pure black
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.45f, 0.5f, 0.55f, 1f);

        // HUD
        StatBar healthBar, staminaBar;
        BuildHUD(out healthBar, out staminaBar, out hudCanvas);

        // Wire stats
        SetField(stats, "health", health);
        SetField(stats, "stamina", stamina);
        SetField(stats, "healthBar", healthBar);
        SetField(stats, "staminaBar", staminaBar);
        SetField(controller, "stamina", stamina);

        // Death + respawn (revives at the start position when HP hits 0).
        PlayerDeath death = player.AddComponent<PlayerDeath>();
        SetField(death, "health", health);
        SetField(death, "stamina", stamina);

        Log("Player core (ground + player + camera w/ post-proc + light + HUD + stats + death/respawn) created.");
    }

    // ── Shared systems: metal items + inventory + combat + inventory UI + notification toast.
    // Returns the three basic-metal ItemSOs so Allomancy can wire drinkable metals.
    static void BuildPlayerSystems(GameObject player, Transform hudCanvas, out Inventory inventory,
                                   out ItemSO iron, out ItemSO steel, out ItemSO pewter)
    {
        // Basic metals (drinkable in every scene). Consumables/equipment are scene-specific.
        iron = CreateItem("Iron", "iron", "Iron", ItemCategory.Metal, maxStack: 99);
        steel = CreateItem("Steel", "steel", "Steel", ItemCategory.Metal, maxStack: 99);
        pewter = CreateItem("Pewter", "pewter", "Pewter", ItemCategory.Metal, maxStack: 99);

        // Inventory on the player
        inventory = player.AddComponent<Inventory>();
        SetField(inventory, "health", player.GetComponent<Health>());
        SetField(inventory, "stamina", player.GetComponent<Stamina>());

        // Combat on the player (wired to health/inventory/stamina/controller).
        PlayerCombat combat = player.AddComponent<PlayerCombat>();
        SetField(combat, "health", player.GetComponent<Health>());
        SetField(combat, "inventory", inventory);
        SetField(combat, "stamina", player.GetComponent<Stamina>());
        SetField(combat, "controller", player.GetComponent<CharacterController>());

        // Inventory UI panel (closed by default)
        GameObject invPanel;
        Transform bagParent, weaponSlotParent, armorSlotParent;
        BuildInventoryPanel(hudCanvas, out invPanel, out bagParent, out weaponSlotParent, out armorSlotParent);
        InventoryUI invUI = new GameObject("InventoryUI").AddComponent<InventoryUI>();
        SetField(invUI, "inventory", inventory);
        SetField(invUI, "panel", invPanel);
        SetField(invUI, "bagParent", bagParent);
        SetField(invUI, "weaponSlotParent", weaponSlotParent);
        SetField(invUI, "armorSlotParent", armorSlotParent);

        // Notification toast (top-center) — enemies/Pewter call NotificationUI.Show (null-safe).
        Text notifText = MakeText(hudCanvas, "Notification",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -40f), new Vector2(600f, 30f), 18, TextAnchor.MiddleCenter);
        NotificationUI notif = new GameObject("NotificationUI").AddComponent<NotificationUI>();
        SetField(notif, "text", notifText);

        Log("Player systems (inventory + combat + inventory UI + toast) created.");
    }

    // --------------------------------------------------------------- HUD ----

    static void BuildHUD(out StatBar healthBar, out StatBar staminaBar, out Transform hudCanvas)
    {
        GameObject canvasObj = new GameObject("HUD");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasObj.AddComponent<GraphicRaycaster>();
        hudCanvas = canvasObj.transform;

        // EventSystem (required for input on the canvas; harmless here)
        var esObj = new GameObject("EventSystem");
        esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
        esObj.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

        healthBar = MakeBar(canvas.transform, "HealthBar", new Vector2(20, -20), new Color(0.85f, 0.20f, 0.20f, 1f), "Health {0:P0}");
        staminaBar = MakeBar(canvas.transform, "StaminaBar", new Vector2(20, -60), new Color(0.20f, 0.75f, 0.40f, 1f), "Stamina {0:P0}");

        Log("HUD canvas + bars created.");
    }

    static StatBar MakeBar(Transform parent, string name, Vector2 anchorOffset, Color fillColor, string format)
    {
        // Root anchored top-left
        GameObject root = new GameObject(name);
        root.transform.SetParent(parent, false);
        RectTransform rootRT = root.AddComponent<RectTransform>();
        rootRT.anchorMin = new Vector2(0f, 1f);
        rootRT.anchorMax = new Vector2(0f, 1f);
        rootRT.pivot = new Vector2(0f, 1f);
        rootRT.sizeDelta = new Vector2(220f, 32f);
        rootRT.anchoredPosition = anchorOffset;

        Image bg = root.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.55f);

        // Fill child
        GameObject fillObj = new GameObject("Fill");
        fillObj.transform.SetParent(root.transform, false);
        RectTransform fillRT = fillObj.AddComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.pivot = new Vector2(0f, 0.5f);
        fillRT.offsetMin = new Vector2(4f, 4f);
        fillRT.offsetMax = new Vector2(-4f, -4f);
        Image fill = fillObj.AddComponent<Image>();
        fill.color = fillColor;
        // Simple solid rect — StatBar shrinks it via RectTransform.anchorMax.x (not Image.fillAmount,
        // which needs a sprite to render correctly).

        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(root.transform, false);
        RectTransform labelRT = labelObj.AddComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;
        Text label = labelObj.AddComponent<Text>();
        label.text = format;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 14;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.raycastTarget = false;

        StatBar bar = root.AddComponent<StatBar>();
        SetField(bar, "fillImage", fill);
        SetField(bar, "label", label);
        SetField(bar, "labelFormat", format);
        return bar;
    }

    // ------------------------------------------------------ Interaction ----

    // ── Starter-only content: obstacles + NPC + chests + door + consumable/equipment
    // pickups + 3 enemies + dialogue + interaction prompt. (Test scenes use their own content.)
    static void BuildStarterContent(GameObject player, Transform hudCanvas, Inventory inventory,
                                    ItemSO iron, ItemSO steel, ItemSO pewter)
    {
        Material obstacleMat = CreateLitMaterial("ObstacleMat", new Color(0.55f, 0.45f, 0.30f, 1f));
        Material npcMat = CreateLitMaterial("NPCMat", new Color(0.9f, 0.8f, 0.2f, 1f));
        Material chestBodyMat = CreateLitMaterial("ChestBodyMat", new Color(0.45f, 0.30f, 0.18f, 1f));
        Material chestLidMat = CreateLitMaterial("ChestLidMat", new Color(0.55f, 0.40f, 0.25f, 1f));
        Material cacheMat = CreateLitMaterial("CacheMat", new Color(0.55f, 0.55f, 0.6f, 1f));
        Material cacheLidMat = CreateLitMaterial("CacheLidMat", new Color(0.35f, 0.35f, 0.4f, 1f));

        // Obstacles
        MakeCube("Obstacle_Cube", new Vector3(6f, 0.5f, 3f), new Vector3(2f, 1f, 2f), obstacleMat);
        MakeCube("Obstacle_Cube2", new Vector3(-5f, 1f, -4f), new Vector3(1.5f, 2f, 1.5f), obstacleMat);
        MakeCube("Obstacle_Wall", new Vector3(0f, 0.75f, 12f), new Vector3(20f, 1.5f, 0.5f), obstacleMat);

        CreateNPC("NPC_Sazed", new Vector3(4f, 1f, -3f), npcMat, "Sazed", new string[] {
            "Ah, a traveler. The mists are thick tonight.",
            "If you seek metal, search the caches scattered about the field.",
            "Tread carefully — the Lord Ruler's men are never far.",
            "May the Survivor watch over you."
        });

        CreateChest("Chest", new Vector3(-3f, 0f, 4f), chestBodyMat, chestLidMat, "Chest", "Found 3 iron ingots");
        CreateChest("MetalCache", new Vector3(2f, 0f, 7f), cacheMat, cacheLidMat, "Metal Cache", "Found a pouch of iron filings");

        // ---- Consumables + equipment (metal pickups use the shared iron/steel ItemSOs) ----
        ItemSO healthPotion = CreateItem("HealthPotion", "health_potion", "Health Potion", ItemCategory.Consumable, maxStack: 20, healthRestore: 30f);
        ItemSO staminaTonic = CreateItem("StaminaTonic", "stamina_tonic", "Stamina Tonic", ItemCategory.Consumable, maxStack: 20, staminaRestore: 40f);
        ItemSO ironSword = CreateItem("IronSword", "iron_sword", "Iron Sword", ItemCategory.Equipment, stackable: false, maxStack: 1, equipSlot: EquipSlot.Weapon, power: 5);
        ItemSO leatherArmor = CreateItem("LeatherArmor", "leather_armor", "Leather Armor", ItemCategory.Equipment, stackable: false, maxStack: 1, equipSlot: EquipSlot.Armor, power: 3);

        Material pickupMetalMat = CreateLitMaterial("PickupMetalMat", new Color(0.6f, 0.6f, 0.65f, 1f));
        Material pickupConsumableMat = CreateLitMaterial("PickupConsumableMat", new Color(0.25f, 0.8f, 0.4f, 1f));
        Material pickupEquipMat = CreateLitMaterial("PickupEquipMat", new Color(0.3f, 0.55f, 0.85f, 1f));

        CreatePickup("Pickup_IronA", new Vector3(3f, 0.25f, 3f), pickupMetalMat, iron, 2);
        CreatePickup("Pickup_IronB", new Vector3(-2f, 0.25f, 6f), pickupMetalMat, iron, 1);
        CreatePickup("Pickup_Steel", new Vector3(6f, 0.25f, -2f), pickupMetalMat, steel, 1);
        CreatePickup("Pickup_HealthPotion", new Vector3(-4f, 0.25f, 0f), pickupConsumableMat, healthPotion, 1);
        CreatePickup("Pickup_StaminaTonic", new Vector3(5f, 0.25f, 6f), pickupConsumableMat, staminaTonic, 1);
        CreatePickup("Pickup_IronSword", new Vector3(-6f, 0.25f, 3f), pickupEquipMat, ironSword, 1);
        CreatePickup("Pickup_LeatherArmor", new Vector3(7f, 0.25f, 5f), pickupEquipMat, leatherArmor, 1);

        CreateDoor("Door", new Vector3(0f, 0f, 14f));
        Log("Starter content: NPC + chests + door + pickups created.");

        // ---- Enemies (drop metal loot) ----
        Material enemyMat = CreateLitMaterial("EnemyMat", new Color(0.8f, 0.2f, 0.2f, 1f));
        CreateEnemy("Enemy_A", new Vector3(9f, 1f, -1f),
            new Vector3[] { new Vector3(9f, 0f, -1f), new Vector3(9f, 0f, -7f), new Vector3(13f, 0f, -7f) },
            enemyMat, iron, 2);
        CreateEnemy("Enemy_B", new Vector3(-9f, 1f, 8f),
            new Vector3[] { new Vector3(-9f, 0f, 8f), new Vector3(-9f, 0f, 2f), new Vector3(-13f, 0f, 5f) },
            enemyMat, steel, 1);
        CreateEnemy("Enemy_C", new Vector3(11f, 1f, 11f),
            new Vector3[] { new Vector3(11f, 0f, 11f), new Vector3(7f, 0f, 13f), new Vector3(11f, 0f, 15f) },
            enemyMat, pewter, 1);
        Log("Enemies (patrol + chase + attack, world health bars, loot on death) created.");

        // Prompt (bottom-center)
        Text promptText = MakeText(hudCanvas, "Prompt",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 60f), new Vector2(360f, 36f), 18, TextAnchor.MiddleCenter);
        GameObject promptRoot = promptText.gameObject;

        // Dialogue panel (lower-center)
        GameObject panelGO;
        Text dlgName, dlgLine, dlgHint;
        BuildDialoguePanel(hudCanvas, out panelGO, out dlgName, out dlgLine, out dlgHint);

        // Dialogue manager (scene object)
        DialogueManager dlgMgr = new GameObject("DialogueManager").AddComponent<DialogueManager>();
        SetField(dlgMgr, "panel", panelGO);
        SetField(dlgMgr, "nameText", dlgName);
        SetField(dlgMgr, "lineText", dlgLine);
        SetField(dlgMgr, "hintText", dlgHint);

        // Player interaction driver (NPC/chest/door/pickup prompts)
        PlayerInteraction pInteract = player.AddComponent<PlayerInteraction>();
        SetField(pInteract, "promptRoot", promptRoot);
        SetField(pInteract, "promptText", promptText);

        Log("Starter content: dialogue + interaction prompt wired.");
    }

    // ----------------------------------------------------- Test scenes ----

    [MenuItem("RPG/Build Tin Test Scene")]
    public static void BuildTinTest()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[RPGBuilder] Cannot build while in Play mode. Exit Play mode first, then run RPG → Build Tin Test Scene.");
            return;
        }
        Log("Starting Tin test scene build...");
        if (!Directory.Exists(SETTINGS_DIR)) AssetDatabase.CreateFolder("Assets", "Settings");
        if (!Directory.Exists("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");

        SetupURP();
        EnsureRightStickAxes();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BuildPlayerCore(out GameObject player, out _, out Transform hudCanvas,
                        out _, out _, out Light dirLight);
        BuildPlayerSystems(player, hudCanvas, out Inventory inventory,
                           out ItemSO iron, out ItemSO steel, out ItemSO pewter);
        BuildAllomancy(player, hudCanvas, inventory, iron, steel, pewter);
        BuildTinTestContent(player, hudCanvas, dirLight);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, TIN_TEST_PATH);
        Log($"Scene saved: {TIN_TEST_PATH}");
        AddToBuildSettings(TIN_TEST_PATH);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Log("Tin test scene complete. Controls: WASD move, mouse look, Tab wheel, B burn. Tab → Tin → B to see in the dark; approach the glowing light to feel overload.");
    }

    [MenuItem("RPG/Build Pewter Test Scene")]
    public static void BuildPewterTest()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[RPGBuilder] Cannot build while in Play mode. Exit Play mode first, then run RPG → Build Pewter Test Scene.");
            return;
        }
        Log("Starting Pewter test scene build...");
        if (!Directory.Exists(SETTINGS_DIR)) AssetDatabase.CreateFolder("Assets", "Settings");
        if (!Directory.Exists("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");

        SetupURP();
        EnsureRightStickAxes();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BuildPlayerCore(out GameObject player, out _, out Transform hudCanvas,
                        out _, out _, out _);
        BuildPlayerSystems(player, hudCanvas, out Inventory inventory,
                           out ItemSO iron, out ItemSO steel, out ItemSO pewter);
        BuildAllomancy(player, hudCanvas, inventory, iron, steel, pewter);
        BuildPewterTestContent(player, hudCanvas);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, PEWTER_TEST_PATH);
        Log($"Scene saved: {PEWTER_TEST_PATH}");
        AddToBuildSettings(PEWTER_TEST_PATH);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Log("Pewter test scene complete. Controls: WASD/Space/mouse, Tab wheel, B burn, LMB attack. Tab → Pewter → B: ~2.5x melee, jump the gap, shrug off hits (half held at bay — they hit when you STOP pewter), burn past ~30s then stop = drag crash, hold R to flare.");
    }

    /// <summary>Tin test scene: pitch-black-ish arena with alcoves (enemies behind walls),
    /// bright enemy materials (postExposure can reveal them), patrolling enemies for
    /// scent/vibration, and a bright-light hazard carrying a SensorySource for overload.</summary>
    static void BuildTinTestContent(GameObject player, Transform hudCanvas, Light dirLight)
    {
        // Very low light — Tin's postExposure night vision amplifies what little signal exists.
        RenderSettings.ambientLight = new Color(0.04f, 0.04f, 0.05f, 1f);
        if (dirLight != null) dirLight.intensity = 0.08f;

        Material wallMat = CreateLitMaterial("TinWallMat", new Color(0.10f, 0.10f, 0.12f, 1f));

        // Alcove walls — enemies can be behind these (scent/vibration pierce geometry).
        MakeCube("Wall_N",  new Vector3(0f, 1.5f,  10f), new Vector3(22f, 3f, 0.5f), wallMat);
        MakeCube("Wall_S",  new Vector3(0f, 1.5f, -10f), new Vector3(22f, 3f, 0.5f), wallMat);
        MakeCube("Wall_E",  new Vector3( 10f, 1.5f, 0f), new Vector3(0.5f, 3f, 22f), wallMat);
        MakeCube("Wall_W",  new Vector3(-10f, 1.5f, 0f), new Vector3(0.5f, 3f, 22f), wallMat);
        // Internal partitions forming alcoves.
        MakeCube("Wall_P1", new Vector3(-3f, 1.5f,  2f), new Vector3(0.5f, 3f, 8f), wallMat);
        MakeCube("Wall_P2", new Vector3( 3f, 1.5f, -2f), new Vector3(0.5f, 3f, 8f), wallMat);

        // Bright enemy material so Tin's night vision has something to reveal.
        Material enemyMat = CreateLitMaterial("TinEnemyMat", new Color(0.9f, 0.3f, 0.3f, 1f));
        CreateEnemy("TinEnemy_A", new Vector3(6f, 1f, 6f),
            new Vector3[] { new Vector3(6f, 0f, 6f), new Vector3(6f, 0f, -6f), new Vector3(-6f, 0f, -6f) },
            enemyMat, null, 0);
        CreateEnemy("TinEnemy_B", new Vector3(-6f, 1f, -6f),
            new Vector3[] { new Vector3(-6f, 0f, -6f), new Vector3(-6f, 0f, 6f), new Vector3(6f, 0f, 6f) },
            enemyMat, null, 0);
        CreateEnemy("TinEnemy_C", new Vector3(0f, 1f, 8f),
            new Vector3[] { new Vector3(0f, 0f, 8f), new Vector3(4f, 0f, 4f), new Vector3(-4f, 0f, 4f) },
            enemyMat, null, 0);
        Log("Tin test: pitch-dark arena + alcoves + 3 patrolling enemies created.");

        // Bright-light hazard: glowing cube + point light + SensorySource (overload trigger).
        // Placed a clear walk north of spawn (player spawns at origin) so the "walk to the light"
        // overload step is an actual walk, not instant.
        Material glowMat = CreateLitMaterial("TinGlowMat", new Color(1f, 0.95f, 0.6f, 1f));
        GameObject hazard = MakeCube("BrightHazard", new Vector3(0f, 0.5f, 7f), new Vector3(1f, 1f, 1f), glowMat);
        Light point = new GameObject("HazardLight").AddComponent<Light>();
        point.type = LightType.Point;
        point.range = 10f;
        point.intensity = 3f;
        point.color = new Color(1f, 0.95f, 0.6f, 1f);
        point.transform.position = new Vector3(0f, 1.2f, 7f);
        SensorySource src = hazard.AddComponent<SensorySource>();
        src.type = SensorySource.SourceType.BrightLight;
        src.intensity = 1f;
        src.radius = 8f;
        src.falloff = 1f;

        // Step-by-step tutorial (freezes the world while teaching, advances on each action).
        BuildTutorial("Tin — Enhanced Senses", new TutorialOverlay.TutorialStep[] {
            S("Tin — enhanced senses. The arena is nearly pitch black. Press TAB (or Share) to open the metal wheel.", TutorialOverlay.TutorialStepType.OpenWheel, true),
            S("Click Tin (slot 4) or press 4 to make it the active metal.", TutorialOverlay.TutorialStepType.SelectMetal, true, metal: MetalType.Tin),
            S("Close the wheel (Esc or click) if it's open, then press B to burn Tin — the world brightens (night vision).", TutorialOverlay.TutorialStepType.StartBurning, true),
            S("Now WALK to the glowing light to feel sensory overload (the cost of Tin). The arena is live — move!", TutorialOverlay.TutorialStepType.FeelOverload, false),
            FinishStep("Overload whites out the screen, muffles audio and slows you — step away to recover. Hold R to flare (sharper but faster overload + drain). Tutorial complete — press Enter (or Dpad↓) when you're done exploring."),
        });
        Log("Tin test: bright-light hazard + tutorial created.");
    }

    /// <summary>Pewter test scene: a stationary high-HP damage dummy, a platform gap clearable
    /// only with Pewter's jump multiplier, and attacking enemies for damage reduction / drag crash.</summary>
    static void BuildPewterTestContent(GameObject player, Transform hudCanvas)
    {
        Material platformMat = CreateLitMaterial("PewterPlatformMat", new Color(0.35f, 0.35f, 0.40f, 1f));
        Material dummyMat = CreateLitMaterial("PewterDummyMat", new Color(0.85f, 0.6f, 0.2f, 1f));
        Material enemyMat = CreateLitMaterial("PewterEnemyMat", new Color(0.8f, 0.2f, 0.2f, 1f));

        // Two platforms with a ~4.5u gap: normal jump (~3.5u) falls short, Pewter jump (~6.4u) clears.
        MakeCube("PlatformA", new Vector3(-3.25f, 0.75f, 0f), new Vector3(2f, 1.5f, 4f), platformMat);
        MakeCube("PlatformB", new Vector3( 3.25f, 0.75f, 0f), new Vector3(2f, 1.5f, 4f), platformMat);
        // A higher ledge past platform B to climb.
        MakeCube("HighLedge", new Vector3(7.5f, 1.5f, 0f), new Vector3(2f, 3f, 4f), platformMat);

        // Start the player on platform A.
        player.transform.position = new Vector3(-3.25f, 2.6f, 0f);

        // Stationary high-HP damage dummy (no waypoints, tiny detect range → stands still).
        Enemy dummy = CreateEnemy("DamageDummy", new Vector3(-3.25f, 1f, -1.5f),
            new Vector3[0], dummyMat, null, 0);
        SetField(dummy, "detectRange", 0.01f);
        Health dummyHp = dummy.gameObject.GetComponent<Health>();
        if (dummyHp != null) SetField(dummyHp, "maxHealth", 500);

        // Two attacking enemies for damage-reduction + drag-crash testing (no loot).
        CreateEnemy("PewterEnemy_A", new Vector3(8f, 1f, 4f),
            new Vector3[] { new Vector3(8f, 0f, 4f), new Vector3(8f, 0f, -4f), new Vector3(12f, 0f, 0f) },
            enemyMat, null, 0);
        CreateEnemy("PewterEnemy_B", new Vector3(-8f, 1f, -4f),
            new Vector3[] { new Vector3(-8f, 0f, -4f), new Vector3(-8f, 0f, 4f), new Vector3(-12f, 0f, 0f) },
            enemyMat, null, 0);
        Log("Pewter test: platforms + gap + damage dummy + 2 enemies created.");

        // Step-by-step tutorial (freezes the world while teaching, advances on each action).
        BuildTutorial("Pewter — Physical Enhancement", new TutorialOverlay.TutorialStep[] {
            S("Pewter — physical enhancement. Press TAB (or Share) to open the metal wheel.", TutorialOverlay.TutorialStepType.OpenWheel, true),
            S("Click Pewter (slot 3) or press 3 to make it the active metal.", TutorialOverlay.TutorialStepType.SelectMetal, true, metal: MetalType.Pewter),
            S("Close the wheel (Esc or click) if it's open, then press B to burn Pewter — you're stronger, faster, tougher.", TutorialOverlay.TutorialStepType.StartBurning, true),
            S("HOW TO DEAL DAMAGE: Left Mouse (or R1 on gamepad) is your melee attack — face an enemy and click. Your hits land in a small arc in front of you. While burning Pewter, every hit lands ~2.5x harder. Try it on the orange dummy (it has lots of HP, so don't expect it to drop fast). Press any key / button to start the live arena.", TutorialOverlay.TutorialStepType.AnyKey, true),
            FinishStep("The arena is live. Smash the dummy (LMB / R1), Space to jump the gap (Pewter clears it; a normal jump falls short). Take hits while burning — half of each wound is HELD at bay, so your health barely drops. But the instant you stop pewter (B again, or it runs dry) all those held wounds hit at once — so don't burn long then drop it. Burn past ~30s then stop = DRAG CRASH (damage + exhaustion + slow). Hold R to flare (burn harder, drains faster). Press Enter (or Dpad↓) when you're done."),
        });
        Log("Pewter test: tutorial created.");
    }

    [MenuItem("RPG/Build Iron/Steel Test Scene")]
    public static void BuildIronSteelTest()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[RPGBuilder] Cannot build while in Play mode. Exit Play mode first, then run RPG → Build Iron/Steel Test Scene.");
            return;
        }
        Log("Starting Iron/Steel test scene build...");
        if (!Directory.Exists(SETTINGS_DIR)) AssetDatabase.CreateFolder("Assets", "Settings");
        if (!Directory.Exists("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");

        SetupURP();
        EnsureRightStickAxes();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BuildPlayerCore(out GameObject player, out _, out Transform hudCanvas,
                        out _, out _, out _);
        BuildPlayerSystems(player, hudCanvas, out Inventory inventory,
                           out ItemSO iron, out ItemSO steel, out ItemSO pewter);
        BuildAllomancy(player, hudCanvas, inventory, iron, steel, pewter);
        BuildIronSteelTestContent(player, hudCanvas);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, IRONSTEEL_TEST_PATH);
        Log($"Scene saved: {IRONSTEEL_TEST_PATH}");
        AddToBuildSettings(IRONSTEEL_TEST_PATH);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Log("Iron/Steel test scene complete. Tab → Steel → B → hold F to push off a wall/anchor; Tab → Iron → B → hold Q to pull toward one. Shove enemies (they're anchors too).");
    }

    /// <summary>Iron/Steel test scene: scattered metal anchor cubes (push off / pull toward),
    /// a wall anchor behind spawn to Steelpush off, a raised anchor to Ironpull up to, and
    /// wandering enemies to shove.</summary>
    static void BuildIronSteelTestContent(GameObject player, Transform hudCanvas)
    {
        // Metallic anchor material — full metallic + high smoothness so pushable metal reads as
        // metal (reflective) vs the matte non-metal walls. Enemies here are armored (also anchors)
        // so they're metallic too — it's clear you can shove them.
        Material anchorMat = CreateMetalMaterial("AnchorMat", new Color(0.78f, 0.80f, 0.84f, 1f));
        Material wallMat = CreateLitMaterial("ISWallMat", new Color(0.40f, 0.42f, 0.46f, 1f));
        Material enemyMat = CreateMetalMaterial("ISEnemyMat", new Color(0.82f, 0.30f, 0.26f, 1f));

        // A wall right behind spawn — aim back + Steelpush (F) to launch forward off it.
        MakeAnchor("Anchor_Wall", new Vector3(0f, 2f, -6f), new Vector3(8f, 4f, 0.5f), anchorMat);

        // Ground-level anchor cubes scattered around to push off / pull toward.
        MakeAnchor("Anchor_A", new Vector3(6f, 0.5f, 4f), new Vector3(1f, 1f, 1f), anchorMat);
        MakeAnchor("Anchor_B", new Vector3(-6f, 0.5f, 5f), new Vector3(1f, 1f, 1f), anchorMat);
        MakeAnchor("Anchor_C", new Vector3(0f, 0.5f, 10f), new Vector3(1.2f, 1.2f, 1.2f), anchorMat);

        // A raised anchor on a pillar — Ironpull (Q) to yank up onto it. (Pillar is NOT metal.)
        MakeCube("Pillar", new Vector3(10f, 1f, -4f), new Vector3(2f, 2f, 2f), wallMat);
        MakeAnchor("Anchor_High", new Vector3(10f, 2.6f, -4f), new Vector3(1f, 1f, 1f), anchorMat);

        // A far platform with an anchor — pull yourself across the gap to reach it. (Platform is NOT metal.)
        MakeCube("FarPlatform", new Vector3(16f, 0.5f, 0f), new Vector3(4f, 1f, 4f), wallMat);
        MakeAnchor("Anchor_Far", new Vector3(16f, 1.2f, 0f), new Vector3(1f, 1f, 1f), anchorMat);

        // Wandering enemies (already carry MetalAnchor via CreateEnemy → Steelpush shoves them).
        CreateEnemy("ISEnemy_A", new Vector3(3f, 1f, 3f),
            new Vector3[] { new Vector3(3f, 0f, 3f), new Vector3(-3f, 0f, 3f), new Vector3(0f, 0f, 7f) },
            enemyMat, null, 0);
        CreateEnemy("ISEnemy_B", new Vector3(-4f, 1f, -2f),
            new Vector3[] { new Vector3(-4f, 0f, -2f), new Vector3(4f, 0f, -2f) },
            enemyMat, null, 0);
        Log("Iron/Steel test: anchors + wall + raised/far anchors + 2 enemies created.");

        // Step-by-step tutorial (freezes the world while teaching, advances on each action).
        BuildTutorial("Iron & Steel — Push / Pull on Metal", new TutorialOverlay.TutorialStep[] {
            S("Iron & Steel — push/pull on METAL. The shiny metallic cubes (and the armored enemies) are anchors; a sight line shows your target. Press TAB (or Share) to open the metal wheel.", TutorialOverlay.TutorialStepType.OpenWheel, true),
            S("Click Steel (slot 2) or press 2 — let's push first.", TutorialOverlay.TutorialStepType.SelectMetal, true, metal: MetalType.Steel),
            S("Close the wheel (Esc or click) if it's open, then press B to burn Steel.", TutorialOverlay.TutorialStepType.StartBurning, true),
            S("Aim at a metal cube (a blue line marks your target) and HOLD F to Steelpush — launch off it. The arena is live now.", TutorialOverlay.TutorialStepType.PushOrPull, false),
            S("Nice. Now let's pull. Press TAB (or Share) to switch metals.", TutorialOverlay.TutorialStepType.OpenWheel, true),
            S("Click Iron (slot 1) or press 1.", TutorialOverlay.TutorialStepType.SelectMetal, true, metal: MetalType.Iron),
            S("Press B to burn Iron.", TutorialOverlay.TutorialStepType.StartBurning, true),
            S("Aim at an anchor (a gold line marks your target) and HOLD Q to Ironpull — yank toward it.", TutorialOverlay.TutorialStepType.PushOrPull, false),
            FinishStep("Heavier anchors reach farther; shoving an enemy recoils you too (mass-split); hold R to flare. Tutorial complete — press Enter (or Dpad↓) when you're done exploring."),
        });
        Log("Iron/Steel test: tutorial created.");
    }

    /// <summary>A cube that is also a metal anchor (Iron/Steel can push/pull against it).</summary>
    static GameObject MakeAnchor(string name, Vector3 pos, Vector3 scale, Material mat)
    {
        GameObject cube = MakeCube(name, pos, scale, mat);
        cube.AddComponent<MetalAnchor>();
        return cube;
    }

    [MenuItem("RPG/Build Zinc/Brass Test Scene")]
    public static void BuildZincBrassTest()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[RPGBuilder] Cannot build while in Play mode. Exit Play mode first, then run RPG → Build Zinc/Brass Test Scene.");
            return;
        }
        Log("Starting Zinc/Brass test scene build...");
        if (!Directory.Exists(SETTINGS_DIR)) AssetDatabase.CreateFolder("Assets", "Settings");
        if (!Directory.Exists("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");

        SetupURP();
        EnsureRightStickAxes();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BuildPlayerCore(out GameObject player, out _, out Transform hudCanvas,
                        out _, out _, out _);
        BuildPlayerSystems(player, hudCanvas, out Inventory inventory,
                           out ItemSO iron, out ItemSO steel, out ItemSO pewter);
        BuildAllomancy(player, hudCanvas, inventory, iron, steel, pewter);
        BuildZincBrassTestContent(player, hudCanvas);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ZINCBRASS_TEST_PATH);
        Log($"Scene saved: {ZINCBRASS_TEST_PATH}");
        AddToBuildSettings(ZINCBRASS_TEST_PATH);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Log("Zinc/Brass test scene complete. Enemies stand off (out of normal detect range). Tab → Brass → B to walk past them calmly; Tab → Zinc → B to make them swarm.");
    }

    /// <summary>Zinc/Brass test scene: enemies placed just beyond normal detect range so the
    /// emotion aura is the difference — Brass lets you stroll past, Zinc makes them swarm.</summary>
    static void BuildZincBrassTestContent(GameObject player, Transform hudCanvas)
    {
        Material enemyMat = CreateLitMaterial("ZBEnemyMat", new Color(0.8f, 0.2f, 0.2f, 1f));
        Material markerMat = CreateLitMaterial("ZBMarkerMat", new Color(0.3f, 0.3f, 0.32f, 1f));

        // Ring the player with enemies at ~12u (default detectRange is 8, so they ignore you
        // until Zinc widens it to ~16). Each paces a short local patrol.
        CreateEnemy("ZBEnemy_N",  new Vector3(0f, 1f,  12f),
            new Vector3[] { new Vector3(-2f, 0f, 12f), new Vector3(2f, 0f, 12f) }, enemyMat, null, 0);
        CreateEnemy("ZBEnemy_S",  new Vector3(0f, 1f, -12f),
            new Vector3[] { new Vector3(2f, 0f, -12f), new Vector3(-2f, 0f, -12f) }, enemyMat, null, 0);
        CreateEnemy("ZBEnemy_E",  new Vector3( 12f, 1f, 0f),
            new Vector3[] { new Vector3(12f, 0f, -2f), new Vector3(12f, 0f, 2f) }, enemyMat, null, 0);
        CreateEnemy("ZBEnemy_W",  new Vector3(-12f, 1f, 0f),
            new Vector3[] { new Vector3(-12f, 0f, 2f), new Vector3(-12f, 0f, -2f) }, enemyMat, null, 0);
        Log("Zinc/Brass test: 4 enemies ringed just outside normal detect range.");

        // Low markers so the player can see the ring without obstruction.
        MakeCube("Marker_N", new Vector3(0f, 0.25f,  10f), new Vector3(8f, 0.5f, 0.3f), markerMat);
        MakeCube("Marker_S", new Vector3(0f, 0.25f, -10f), new Vector3(8f, 0.5f, 0.3f), markerMat);
        MakeCube("Marker_E", new Vector3( 10f, 0.25f, 0f), new Vector3(0.3f, 0.5f, 8f), markerMat);
        MakeCube("Marker_W", new Vector3(-10f, 0.25f, 0f), new Vector3(0.3f, 0.5f, 8f), markerMat);

        // Step-by-step tutorial (freezes the world while teaching, advances on each action).
        BuildTutorial("Zinc & Brass — Riot / Soothe Emotions", new TutorialOverlay.TutorialStep[] {
            S("Zinc & Brass — riot/soothe emotions. Four enemies ring you, just OUTSIDE normal detect range (they ignore you for now). Press TAB (or Share) to open the metal wheel.", TutorialOverlay.TutorialStepType.OpenWheel, true),
            S("Click Brass (slot 8) or press 8 — let's soothe first.", TutorialOverlay.TutorialStepType.SelectMetal, true, metal: MetalType.Brass),
            S("Close the wheel if it's open, then press B to burn Brass — nearby enemies turn calm.", TutorialOverlay.TutorialStepType.StartBurning, true),
            S("While burning Brass, nearby enemies turn calm and sluggish — they won't chase. Press any key to switch to Zinc.", TutorialOverlay.TutorialStepType.AnyKey, false),
            S("Now press TAB (or Share) to open the wheel and switch metals.", TutorialOverlay.TutorialStepType.OpenWheel, true),
            S("Click Zinc (slot 7) or press 7.", TutorialOverlay.TutorialStepType.SelectMetal, true, metal: MetalType.Zinc),
            S("Press B to burn Zinc — nearby enemies turn hyper-aggressive and swarm!", TutorialOverlay.TutorialStepType.StartBurning, true),
            FinishStep("Stop burning (B) to let them reset. Hold R to flare (wider aura). Tutorial complete — press Enter (or Dpad↓) when you're done."),
        });
        Log("Zinc/Brass test: tutorial created.");
    }

    [MenuItem("RPG/Build Copper/Bronze Test Scene")]
    public static void BuildCopperBronzeTest()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[RPGBuilder] Cannot build while in Play mode. Exit Play mode first, then run RPG → Build Copper/Bronze Test Scene.");
            return;
        }
        Log("Starting Copper/Bronze test scene build...");
        if (!Directory.Exists(SETTINGS_DIR)) AssetDatabase.CreateFolder("Assets", "Settings");
        if (!Directory.Exists("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");

        SetupURP();
        EnsureRightStickAxes();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BuildPlayerCore(out GameObject player, out _, out Transform hudCanvas,
                        out _, out _, out _);
        BuildPlayerSystems(player, hudCanvas, out Inventory inventory,
                           out ItemSO iron, out ItemSO steel, out ItemSO pewter);
        BuildAllomancy(player, hudCanvas, inventory, iron, steel, pewter);
        BuildCopperBronzeTestContent(player, hudCanvas);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, COPPERBRONZE_TEST_PATH);
        Log($"Scene saved: {COPPERBRONZE_TEST_PATH}");
        AddToBuildSettings(COPPERBRONZE_TEST_PATH);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Log("Copper/Bronze test scene complete. Pewter-thug enemies buff themselves. Tab → Bronze → B to hear their pulses; Tab → Copper → B and approach to suppress them (they slow + weaken).");
    }

    /// <summary>Copper/Bronze test scene: Pewter-thug enemies (EnemyAllomancer) that buff
    /// themselves. Bronze senses their pulses; Copper's cloud suppresses them on approach.</summary>
    static void BuildCopperBronzeTestContent(GameObject player, Transform hudCanvas)
    {
        Material thugMat = CreateLitMaterial("ThugMat", new Color(0.7f, 0.25f, 0.15f, 1f));   // Pewter-thugs (darker red)
        Material normalMat = CreateLitMaterial("CBNormalMat", new Color(0.8f, 0.2f, 0.2f, 1f));

        // Two Pewter-thug allomancer enemies — faster + hit harder while burning. Placed BEYOND the
        // coppercloud radius (14) from spawn, so the player must actually walk up to suppress one
        // (the tutorial's suppress step checks for a real suppression event, not just burning Copper).
        CreateEnemy("Thug_A", new Vector3(18f, 1f, 10f),
            new Vector3[] { new Vector3(18f, 0f, 10f), new Vector3(22f, 0f, 10f), new Vector3(20f, 0f, 14f) },
            thugMat, null, 0, allomancer: true);
        CreateEnemy("Thug_B", new Vector3(-18f, 1f, -10f),
            new Vector3[] { new Vector3(-18f, 0f, -10f), new Vector3(-22f, 0f, -10f), new Vector3(-20f, 0f, -14f) },
            thugMat, null, 0, allomancer: true);
        Log("Copper/Bronze test: 2 Pewter-thug (allomancer) enemies created.");

        // One normal enemy for contrast (no buff, no pulse).
        CreateEnemy("Grunt_C", new Vector3(0f, 1f, 10f),
            new Vector3[] { new Vector3(0f, 0f, 10f), new Vector3(3f, 0f, 12f), new Vector3(-3f, 0f, 12f) },
            normalMat, null, 0);

        // Step-by-step tutorial (freezes the world while teaching, advances on each action).
        BuildTutorial("Copper & Bronze — Hide / Sense Allomancers", new TutorialOverlay.TutorialStep[] {
            S("Copper & Bronze — hide/sense allomancers. The two dark-red Thugs are Pewter-burning enemies (faster + hit harder). The plain red Grunt is normal. Press TAB (or Share) to open the metal wheel.", TutorialOverlay.TutorialStepType.OpenWheel, true),
            S("Click Bronze (slot 6) or press 6 — let's sense first.", TutorialOverlay.TutorialStepType.SelectMetal, true, metal: MetalType.Bronze),
            S("Close the wheel if it's open, then press B to burn Bronze — hear the Thugs' allomantic pulses (directional pings + toast).", TutorialOverlay.TutorialStepType.StartBurning, true),
            S("Toasts report how many allomancers are near. Press any key to continue to Copper.", TutorialOverlay.TutorialStepType.AnyKey, false),
            S("Now press TAB (or Share) to open the wheel and switch metals.", TutorialOverlay.TutorialStepType.OpenWheel, true),
            S("Click Copper (slot 5) or press 5.", TutorialOverlay.TutorialStepType.SelectMetal, true, metal: MetalType.Copper),
            S("Press B to burn Copper — emit a coppercloud that SUPPRESSES nearby Thugs.", TutorialOverlay.TutorialStepType.StartBurning, true),
            S("The arena is live. APPROACH a Thug to suppress it — it turns grey-blue, slows and hits weaker.", TutorialOverlay.TutorialStepType.SuppressThug, false),
            FinishStep("Step away (out of the cloud) and it resumes burning + re-buffs. Hold R to flare (wider cloud/pulse). Tutorial complete — press Enter (or Dpad↓) when you're done."),
        });
        Log("Copper/Bronze test: tutorial created.");
    }

    static void BuildAllomancy(GameObject player, Transform hudCanvas, Inventory inventory, ItemSO iron, ItemSO steel, ItemSO pewter)
    {
        // ---- Compact HUD panel (bottom-left): active metal name + reserve bar + % ----
        GameObject panel = new GameObject("AllomancyHUD");
        panel.transform.SetParent(hudCanvas, false);
        RectTransform prt = panel.AddComponent<RectTransform>();
        prt.anchorMin = new Vector2(0f, 0f);
        prt.anchorMax = new Vector2(0f, 0f);
        prt.pivot = new Vector2(0f, 0f);
        prt.sizeDelta = new Vector2(320f, 60f);
        prt.anchoredPosition = new Vector2(16f, 16f);
        Image pbg = panel.AddComponent<Image>();
        pbg.color = new Color(0f, 0f, 0f, 0.45f);

        // Name (top-left) + percent (top-right)
        Text nameText = MakeText(panel.transform, "MetalName",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(12f, -6f), new Vector2(210f, 22f), 16, TextAnchor.UpperLeft);
        nameText.color = new Color(1f, 0.9f, 0.5f);
        Text pctText = MakeText(panel.transform, "MetalPct",
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-12f, -6f), new Vector2(90f, 22f), 16, TextAnchor.UpperRight);
        pctText.color = Color.white;

        // Reserve bar (below the name row): bg + fill (anchorMax.x driven by the HUD)
        GameObject barBg = new GameObject("ReserveBarBg");
        barBg.transform.SetParent(panel.transform, false);
        RectTransform barBgRT = barBg.AddComponent<RectTransform>();
        barBgRT.anchorMin = new Vector2(0f, 1f);
        barBgRT.anchorMax = new Vector2(1f, 1f);
        barBgRT.pivot = new Vector2(0.5f, 1f);
        barBgRT.sizeDelta = new Vector2(-24f, 16f);
        barBgRT.anchoredPosition = new Vector2(0f, -30f);
        Image barBgImg = barBg.AddComponent<Image>();
        barBgImg.color = new Color(0f, 0f, 0f, 0.6f);

        GameObject fill = new GameObject("ReserveFill");
        fill.transform.SetParent(barBg.transform, false);
        RectTransform fillRT = fill.AddComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.pivot = new Vector2(0f, 0.5f);
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;
        Image fillImg = fill.AddComponent<Image>();
        fillImg.color = Color.white;

        AllomancyHUD hud = panel.AddComponent<AllomancyHUD>();
        SetField(hud, "reserveFill", fillRT);
        SetField(hud, "nameText", nameText);
        SetField(hud, "pctText", pctText);

        // ---- Allomancer on the player ----
        Allomancer allomancer = player.AddComponent<Allomancer>();
        SetField(allomancer, "inventory", inventory);
        SetField(allomancer, "hud", hud);

        // metalItems indexed by (int)MetalType: Iron=0, Steel=1, Pewter=2 (others null → not drinkable yet)
        ItemSO[] metalItems = new ItemSO[Metals.Count];
        metalItems[(int)MetalType.Iron] = iron;
        metalItems[(int)MetalType.Steel] = steel;
        metalItems[(int)MetalType.Pewter] = pewter;
        SetField(allomancer, "metalItems", metalItems);

        // ---- Radial metal wheel (Tab) — builds its own canvas/mist/slots in Awake ----
        MetalWheel wheel = player.AddComponent<MetalWheel>();
        SetField(wheel, "allomancer", allomancer);

        // ---- Pewter effect + red vignette overlay ----
        Image pewterVignette = MakeFullscreenOverlay(hudCanvas, "PewterVignette", new Color(0.8f, 0.2f, 0.2f, 0f));
        Pewter pewterEffect = player.AddComponent<Pewter>();
        SetField(pewterEffect, "allomancer", allomancer);
        SetField(pewterEffect, "mover", player.GetComponent<PlayerController>());
        SetField(pewterEffect, "combat", player.GetComponent<PlayerCombat>());
        SetField(pewterEffect, "stamina", player.GetComponent<Stamina>());
        SetField(pewterEffect, "health", player.GetComponent<Health>());
        SetField(pewterEffect, "vignette", pewterVignette);

        // ---- Tin effect (enhanced senses) + camera shake helper ----
        Camera cam = Camera.main;
        CameraShake shake = cam != null ? cam.GetComponent<CameraShake>() : null;
        if (cam != null && shake == null) shake = cam.gameObject.AddComponent<CameraShake>();
        Tin tin = player.AddComponent<Tin>();
        SetField(tin, "allomancer", allomancer);
        SetField(tin, "playerCamera", cam);
        SetField(tin, "shake", shake);

        // ---- Iron/Steel effect (Steelpush F / Ironpull Q against metal anchors) ----
        IronSteel ironSteel = player.AddComponent<IronSteel>();
        SetField(ironSteel, "allomancer", allomancer);
        SetField(ironSteel, "mover", player.GetComponent<PlayerController>());
        SetField(ironSteel, "playerCamera", cam);

        // ---- Zinc/Brass effect (Riot/Soothe enemy-emotion aura; passive while burning) ----
        ZincBrass zincBrass = player.AddComponent<ZincBrass>();
        SetField(zincBrass, "allomancer", allomancer);

        // ---- Copper (coppercloud: suppresses enemy allomancers) + Bronze (sense their pulses) ----
        Copper copper = player.AddComponent<Copper>();
        SetField(copper, "allomancer", allomancer);
        Bronze bronze = player.AddComponent<Bronze>();
        SetField(bronze, "allomancer", allomancer);

        // Starter fuel so drinking is testable immediately.
        inventory.Add(pewter, 3);
        inventory.Add(iron, 2);

        // ---- Save / load (F5 / F9) — persists player state to persistentDataPath/save.json ----
        SaveSystem save = player.AddComponent<SaveSystem>();
        SetField(save, "health", player.GetComponent<Health>());
        SetField(save, "stamina", player.GetComponent<Stamina>());
        SetField(save, "inventory", inventory);
        SetField(save, "allomancer", allomancer);
        SetField(save, "controller", player.GetComponent<CharacterController>());

        Log("Allomancy (reserves, burn, drink, HUD, metal wheel, pewter, tin, iron/steel, zinc/brass, copper/bronze) + save system created.");
    }

    /// <summary>A fullscreen ScreenSpaceOverlay Image (raycastTarget=false) for vignettes/overlays.</summary>
    static Image MakeFullscreenOverlay(Transform hudCanvas, string name, Color color)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(hudCanvas, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        Image img = obj.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    static NPC CreateNPC(string name, Vector3 pos, Material mat, string npcName, string[] lines)
    {
        GameObject npc = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        npc.name = name;
        npc.transform.position = pos;
        npc.GetComponent<Renderer>().sharedMaterial = mat;
        // Keep the CapsuleCollider — proximity detection needs a collider.
        NPC npcComp = npc.AddComponent<NPC>();
        SetField(npcComp, "npcName", npcName);
        SetField(npcComp, "lines", lines);
        return npcComp;
    }

    static Chest CreateChest(string name, Vector3 pos, Material bodyMat, Material lidMat, string label, string loot)
    {
        GameObject chest = new GameObject(name);
        chest.transform.position = pos;

        // Body
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(chest.transform, false);
        body.transform.localPosition = new Vector3(0f, 0.4f, 0f);
        body.transform.localScale = new Vector3(1.2f, 0.8f, 0.8f);
        body.GetComponent<Renderer>().sharedMaterial = bodyMat;

        // Lid pivot at the back-top edge so the lid hinges open (Chest rotates this).
        GameObject lidPivot = new GameObject("Lid");
        lidPivot.transform.SetParent(chest.transform, false);
        lidPivot.transform.localPosition = new Vector3(0f, 0.8f, -0.4f);

        GameObject lidMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
        lidMesh.name = "LidMesh";
        lidMesh.transform.SetParent(lidPivot.transform, false);
        lidMesh.transform.localPosition = new Vector3(0f, 0.08f, 0.4f);
        lidMesh.transform.localScale = new Vector3(1.2f, 0.16f, 0.8f);
        lidMesh.GetComponent<Renderer>().sharedMaterial = lidMat;

        Chest chestComp = chest.AddComponent<Chest>();
        SetField(chestComp, "label", label);
        SetField(chestComp, "lootMessage", loot);
        SetField(chestComp, "lid", lidPivot.transform);
        return chestComp;
    }

    static void BuildDialoguePanel(Transform parent, out GameObject panelGO, out Text nameText, out Text lineText, out Text hintText)
    {
        panelGO = new GameObject("DialoguePanel");
        panelGO.transform.SetParent(parent, false);
        RectTransform rt = panelGO.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 120f);
        rt.sizeDelta = new Vector2(640f, 180f);
        Image bg = panelGO.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.72f);

        nameText = MakeText(panelGO.transform, "Name",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(20f, -12f), new Vector2(600f, 30f), 22, TextAnchor.UpperLeft);
        nameText.color = new Color(1f, 0.9f, 0.5f);

        lineText = MakeText(panelGO.transform, "Line",
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(20f, -10f), new Vector2(600f, 80f), 22, TextAnchor.UpperLeft);
        lineText.color = Color.white;

        hintText = MakeText(panelGO.transform, "Hint",
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(20f, 12f), new Vector2(600f, 24f), 14, TextAnchor.LowerLeft);
        hintText.color = new Color(0.8f, 0.8f, 0.8f);
        hintText.text = "Press E, Space, or click to continue";

        panelGO.SetActive(false); // hidden until a dialogue opens
    }

    static Text MakeText(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pivot, Vector2 anchoredPos, Vector2 size, int fontSize, TextAnchor alignment)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;
        Text t = obj.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = fontSize;
        t.alignment = alignment;
        t.color = Color.white;
        t.raycastTarget = false;
        return t;
    }

    // --------------------------------------------------- Items & door ----

    static ItemSO CreateItem(string assetName, string id, string displayName, ItemCategory category,
        bool stackable = true, int maxStack = 99, float healthRestore = 0f, float staminaRestore = 0f,
        EquipSlot equipSlot = EquipSlot.None, int power = 0)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Settings/Items"))
            AssetDatabase.CreateFolder("Assets/Settings", "Items");
        string path = $"Assets/Settings/Items/{assetName}.asset";

        // Idempotent: reuse an existing asset from a prior build (definitions are static).
        ItemSO so = AssetDatabase.LoadAssetAtPath<ItemSO>(path);
        if (so != null) return so;

        so = ScriptableObject.CreateInstance<ItemSO>();
        so.id = id;
        so.displayName = displayName;
        so.category = category;
        so.stackable = stackable;
        so.maxStack = maxStack;
        so.healthRestore = healthRestore;
        so.staminaRestore = staminaRestore;
        so.equipSlot = equipSlot;
        so.power = power;
        AssetDatabase.CreateAsset(so, path);
        return so;
    }

    static void CreatePickup(string name, Vector3 pos, Material mat, ItemSO item, int count)
    {
        GameObject pickup = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pickup.name = name;
        pickup.transform.position = pos;
        pickup.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
        pickup.GetComponent<Renderer>().sharedMaterial = mat;
        // BoxCollider from CreatePrimitive stays — proximity detection needs a collider.
        ItemPickup pickupComp = pickup.AddComponent<ItemPickup>();
        SetField(pickupComp, "item", item);
        SetField(pickupComp, "count", count);
    }

    static void CreateDoor(string name, Vector3 pos)
    {
        Material frameMat = CreateLitMaterial("DoorFrameMat", new Color(0.4f, 0.35f, 0.3f, 1f));
        Material leafMat = CreateLitMaterial("DoorLeafMat", new Color(0.5f, 0.4f, 0.25f, 1f));

        GameObject door = new GameObject(name);
        door.transform.position = pos;

        // Frame: two posts + a header, leaving a 2-wide x 3-tall opening.
        MakeChildCube(door.transform, "PostL", new Vector3(-1.5f, 1.5f, 0f), new Vector3(1f, 3f, 0.3f), frameMat);
        MakeChildCube(door.transform, "PostR", new Vector3(1.5f, 1.5f, 0f), new Vector3(1f, 3f, 0.3f), frameMat);
        MakeChildCube(door.transform, "Header", new Vector3(0f, 3.25f, 0f), new Vector3(4f, 0.5f, 0.3f), frameMat);

        // Leaf pivot at the left edge of the opening (Door rotates this).
        GameObject leafPivot = new GameObject("Leaf");
        leafPivot.transform.SetParent(door.transform, false);
        leafPivot.transform.localPosition = new Vector3(-1f, 0f, 0f);

        GameObject leafMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
        leafMesh.name = "LeafMesh";
        leafMesh.transform.SetParent(leafPivot.transform, false);
        leafMesh.transform.localPosition = new Vector3(1f, 1.5f, 0f);
        leafMesh.transform.localScale = new Vector3(2f, 3f, 0.15f);
        leafMesh.GetComponent<Renderer>().sharedMaterial = leafMat;

        Door doorComp = door.AddComponent<Door>();
        SetField(doorComp, "leaf", leafPivot.transform);
        SetField(doorComp, "label", "Door");
    }

    static Enemy CreateEnemy(string name, Vector3 pos, Vector3[] waypointPositions, Material mat, ItemSO loot, int lootCount, bool allomancer = false)
    {
        GameObject enemy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        enemy.name = name;
        enemy.transform.position = pos;
        enemy.GetComponent<Renderer>().sharedMaterial = mat;
        // CharacterController is its own collider — drop the primitive's CapsuleCollider.
        var extraCol = enemy.GetComponent<CapsuleCollider>();
        if (extraCol != null) Object.DestroyImmediate(extraCol);

        CharacterController cc = enemy.AddComponent<CharacterController>();
        cc.height = 2f;
        cc.radius = 0.5f;
        cc.center = new Vector3(0f, 0f, 0f);
        cc.skinWidth = 0.02f;
        cc.minMoveDistance = 0.001f;
        cc.stepOffset = 0.4f;

        Health hp = enemy.AddComponent<Health>();
        SetField(hp, "maxHealth", 50);

        Enemy enemyComp = enemy.AddComponent<Enemy>();
        SetField(enemyComp, "health", hp);
        SetField(enemyComp, "controller", cc);
        SetField(enemyComp, "rend", enemy.GetComponent<Renderer>());
        SetField(enemyComp, "lootItem", loot);
        SetField(enemyComp, "lootCount", lootCount);

        // Enemies are metal-armored → valid Iron/Steel anchors (Steelpush shoves them).
        enemy.AddComponent<MetalAnchor>();

        // Optional: an allomancer enemy (Pewter-thug) — faster/harder while burning, suppressible
        // by a Coppercloud, detectable by Bronze.
        if (allomancer)
        {
            EnemyAllomancer ea = enemy.AddComponent<EnemyAllomancer>();
            SetField(ea, "enemy", enemyComp);
        }

        // Static waypoint markers (root-level, so they don't move with the enemy).
        Transform[] wps = new Transform[waypointPositions.Length];
        for (int i = 0; i < waypointPositions.Length; i++)
        {
            GameObject wp = new GameObject($"{name}_WP{i}");
            wp.transform.position = waypointPositions[i];
            wps[i] = wp.transform;
        }
        SetField(enemyComp, "patrolWaypoints", wps);

        MakeEnemyHealthBar(enemy.transform, hp);

        // Replace the blob capsule with a flat-tinted Erbium humanoid (a hostile "variant" of the
        // player) + a locomotion Animator driven by Enemy. No-ops if the Erbium assets are absent.
        BuildEnemyModel(enemy, enemyComp, mat != null ? mat.color : new Color(0.8f, 0.25f, 0.25f, 1f));
        return enemyComp;
    }

    static void MakeEnemyHealthBar(Transform enemy, Health health)
    {
        GameObject canvasObj = new GameObject("HealthBarCanvas");
        canvasObj.transform.SetParent(enemy, false);
        canvasObj.transform.localPosition = new Vector3(0f, 2.3f, 0f);
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform canvasRT = canvasObj.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(1f, 0.15f);
        canvasRT.localScale = Vector3.one;

        // Background
        GameObject bg = new GameObject("BG");
        bg.transform.SetParent(canvasObj.transform, false);
        RectTransform bgRT = bg.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.6f);

        // Fill (left-anchored; EnemyHealthBar drives anchorMax.x to shrink it)
        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(canvasObj.transform, false);
        RectTransform fillRT = fill.AddComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.pivot = new Vector2(0f, 0.5f);
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;
        Image fillImg = fill.AddComponent<Image>();
        fillImg.color = new Color(0.85f, 0.2f, 0.2f, 1f);

        EnemyHealthBar bar = canvasObj.AddComponent<EnemyHealthBar>();
        SetField(bar, "health", health);
        SetField(bar, "fill", fillRT);
        SetField(bar, "bar", canvasObj.transform);
    }

    static void BuildInventoryPanel(Transform parent, out GameObject panelGO, out Transform bagParent,
        out Transform weaponSlotParent, out Transform armorSlotParent)
    {
        panelGO = new GameObject("InventoryPanel");
        panelGO.transform.SetParent(parent, false);
        RectTransform rt = panelGO.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(560f, 420f);
        Image bg = panelGO.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.82f);

        Text title = MakeText(panelGO.transform, "Title",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -16f), new Vector2(560f, 30f), 22, TextAnchor.MiddleCenter);
        title.text = "Inventory   (press I to close)";
        title.color = new Color(1f, 0.9f, 0.5f);

        MakeEquipSlot(panelGO.transform, "WeaponSlot", new Vector2(0.3f, 0.82f), out weaponSlotParent);
        MakeEquipSlot(panelGO.transform, "ArmorSlot", new Vector2(0.7f, 0.82f), out armorSlotParent);

        // Bag grid fills the lower portion of the panel.
        GameObject bagGO = new GameObject("Bag");
        bagGO.transform.SetParent(panelGO.transform, false);
        RectTransform bagRT = bagGO.AddComponent<RectTransform>();
        bagRT.anchorMin = new Vector2(0f, 0f);
        bagRT.anchorMax = new Vector2(1f, 1f);
        bagRT.offsetMin = new Vector2(20f, 20f);
        bagRT.offsetMax = new Vector2(-20f, -160f);
        GridLayoutGroup grid = bagGO.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(80f, 80f);
        grid.spacing = new Vector2(8f, 8f);
        grid.padding = new RectOffset(8, 8, 8, 8);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 6;
        grid.childAlignment = TextAnchor.UpperCenter;
        bagParent = bagGO.transform;

        panelGO.SetActive(false); // hidden until opened with I
    }

    static void MakeEquipSlot(Transform parent, string name, Vector2 anchor, out Transform slotParent)
    {
        GameObject container = new GameObject(name);
        container.transform.SetParent(parent, false);
        RectTransform rt = container.AddComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(110f, 90f);
        rt.anchoredPosition = Vector2.zero;
        slotParent = container.transform;
    }

    static GameObject MakeChildCube(Transform parent, string name, Vector3 localPos, Vector3 localScale, Material mat)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = localPos;
        cube.transform.localScale = localScale;
        cube.GetComponent<Renderer>().sharedMaterial = mat;
        return cube;
    }

    // ----------------------------------------------------------- Helpers ----

    /// <summary>Adds a guided, step-by-step tutorial overlay (frozen while teaching, advances only
    /// when the player completes each step). The step list is wired via the serialized `steps`
    /// field so it persists into the scene.</summary>
    static void BuildTutorial(string title, TutorialOverlay.TutorialStep[] steps)
    {
        TutorialOverlay tut = new GameObject("Tutorial").AddComponent<TutorialOverlay>();
        SetField(tut, "title", title);
        SetField(tut, "steps", steps);
        // Dpad-Down skips the whole tutorial on a gamepad (Backspace on keyboard).
        SetField(tut, "padSkip", KeyCode.JoystickButton13);
    }

    /// <summary>Concise step constructor for the tutorial scripts above.</summary>
    static TutorialOverlay.TutorialStep S(string text, TutorialOverlay.TutorialStepType type, bool freeze,
        KeyCode key = KeyCode.None, MetalType metal = MetalType.Pewter, float wait = 0f,
        KeyCode padKey = KeyCode.None)
        => new TutorialOverlay.TutorialStep { text = text, type = type, freeze = freeze,
            key = key, metal = metal, waitSeconds = wait, padKey = padKey };

    /// <summary>Final "you're free to explore" step: completes on Enter (keyboard) or Dpad-Down
    /// (gamepad) — NOT on movement keys, so the player can experiment in the live arena without
    /// the first WASD/stick nudge instantly ending the tutorial.</summary>
    static TutorialOverlay.TutorialStep FinishStep(string text)
        => S(text, TutorialOverlay.TutorialStepType.PressKey, false,
            KeyCode.Return, padKey: KeyCode.JoystickButton13);

    static GameObject MakeCube(string name, Vector3 pos, Vector3 scale, Material mat)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.position = pos;
        cube.transform.localScale = scale;
        cube.GetComponent<Renderer>().sharedMaterial = mat;
        return cube;
    }

    static Material CreateLitMaterial(string name, Color color)
    {
        Shader lit = Shader.Find(URP_LIT_SHADER);
        Material mat = new Material(lit != null ? lit : Shader.Find("Standard") ?? Shader.Find("Unlit/Color"));
        mat.name = name;
        mat.color = color;
        AssetDatabase.CreateAsset(mat, $"Assets/Settings/{name}.mat");
        return mat;
    }

    /// <summary>A lit material that reads as METAL: full metallic + high smoothness on URP/Lit, so
    /// pushable metal anchors are visually distinct from matte scenery (they reflect the
    /// environment). Used for Iron/Steel anchor cubes (and the armored enemies you can shove).</summary>
    static Material CreateMetalMaterial(string name, Color color)
    {
        Material mat = CreateLitMaterial(name, color);
        // URP/Lit exposes metallic (0–1) and smoothness (0–1) as "_Metallic" / "_Smoothness".
        mat.SetFloat("_Metallic", 1f);
        mat.SetFloat("_Smoothness", 0.85f);
        return mat;
    }

    static void AddToBuildSettings(string scenePath)
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

        // Remove any stale entries pointing at this path
        scenes.RemoveAll(s => s.path == scenePath);

        SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
        EditorBuildSettingsScene entry = new EditorBuildSettingsScene(scenePath, true);
        scenes.Insert(0, entry); // index 0
        EditorBuildSettings.scenes = scenes.ToArray();
        Log($"Added to Build Settings at index 0: {scenePath}");
    }

    static void SetField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        if (field != null) field.SetValue(target, value);
        else LogWarning($"Could not set field '{fieldName}' on {target.GetType().Name} (serialized field name mismatch).");
    }

    // ── Erbium humanoid model + locomotion Animator ──────────────────────────────
    // Imports the Erbium humanoid model (Assets/Art/Player/model.fbx, Humanoid-rigged with an
    // Avatar) as a child of the code-built player, wires its Animator to Erbium's
    // Character.controller (Assets/Animations/Humanoid/Character.controller), disables the
    // placeholder capsule mesh, aligns the model's feet to the floor via renderer bounds, and
    // hands the Animator to PlayerController so it can drive horInput/verInput/inputMagnitude/
    // groundVelocity/isFalling each frame. No prefabs are used (assets only); CharacterController
    // stays the movement authority and ApplyRootMotion is off so the Animator only visualizes.
    static void BuildPlayerModel(GameObject player, PlayerController controller)
    {
        const string modelPath = "Assets/Art/Player/model.fbx";
        const string controllerPath = "Assets/Animations/Humanoid/Character.controller";

        GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (modelPrefab == null)
        {
            LogWarning($"Erbium model not found at {modelPath} (assets not imported yet?). Player keeps the capsule visual.");
            return;
        }
        RuntimeAnimatorController rc = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);

        // The Avatar is a sub-asset generated by the FBX's Humanoid rig import.
        Avatar avatar = null;
        foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            if (sub is Avatar av) { avatar = av; break; }

        // Hide the placeholder capsule mesh — the humanoid model is the visual now.
        var capsuleRenderer = player.GetComponent<Renderer>();
        if (capsuleRenderer != null) capsuleRenderer.enabled = false;

        GameObject model = Object.Instantiate(modelPrefab, player.transform, false);
        model.name = "Model";

        Animator animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
        if (rc != null) animator.runtimeAnimatorController = rc;
        if (avatar != null) animator.avatar = avatar;
        animator.applyRootMotion = false;

        // Align the model's feet to the floor (player root at y=1 over a y=0 ground plane). Uses
        // the renderer bounds so it's correct regardless of where the rig's origin sits.
        Renderer[] rends = model.GetComponentsInChildren<Renderer>();
        Bounds b = new Bounds(); bool init = false;
        foreach (var r in rends)
        {
            if (!r.enabled) continue;
            if (!init) { b = r.bounds; init = true; }
            else b.Encapsulate(r.bounds);
        }
        if (init) model.transform.position += Vector3.up * (0f - b.min.y);

        SetField(controller, "anim", animator);
        Log($"Erbium humanoid model + Character.controller wired (avatar={(avatar != null ? "ok" : "MISSING")}, controller={(rc != null ? "ok" : "MISSING")}). If the model walks backward, set Model.localRotation = Quaternion.Euler(0,180,0) and rebuild.");
    }

    // ── Enemy humanoid model (Erbium, flat-tinted so it reads as a hostile "variant" of the
    // player — a humanoid silhouette, not a capsule blob) + a locomotion Animator wired into
    // Enemy so it walks/idles/falls instead of sliding. No-ops if the Erbium assets are absent
    // (the enemy keeps the capsule). Mirrors BuildPlayerModel but tints the model flat and aligns
    // the feet to the CharacterController's base (spawn-height-agnostic). ─────────────────────
    static void BuildEnemyModel(GameObject enemy, Enemy enemyComp, Color tint)
    {
        const string modelPath = "Assets/Art/Player/model.fbx";
        const string controllerPath = "Assets/Animations/Humanoid/Character.controller";

        GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (modelPrefab == null) return; // no Erbium asset → keep the capsule (still playable)

        RuntimeAnimatorController rc = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
        Avatar avatar = null;
        foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            if (sub is Avatar av) { avatar = av; break; }

        // Hide the placeholder capsule mesh — the humanoid is the visual now. Enemy still holds a
        // ref to this (disabled) renderer for its base tint + fallback telegraph.
        var capsuleRenderer = enemy.GetComponent<Renderer>();
        if (capsuleRenderer != null) capsuleRenderer.enabled = false;

        CharacterController cc = enemy.GetComponent<CharacterController>();

        GameObject model = Object.Instantiate(modelPrefab, enemy.transform, false);
        model.name = "Model";

        // Flat URP/Lit in the enemy color. Collected so Enemy's attack telegraph can flash them.
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        var tintRenderers = new System.Collections.Generic.List<Renderer>();
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            if (lit != null)
            {
                Material mat = new Material(lit);
                mat.color = tint;
                r.sharedMaterial = mat;
            }
            tintRenderers.Add(r);
        }

        Animator animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
        if (rc != null) animator.runtimeAnimatorController = rc;
        if (avatar != null) animator.avatar = avatar;
        animator.applyRootMotion = false;

        // Align the model's feet to the CharacterController's base (correct for any spawn height).
        Renderer[] rends = model.GetComponentsInChildren<Renderer>();
        Bounds b = new Bounds(); bool init = false;
        foreach (var r in rends)
        {
            if (!r.enabled) continue;
            if (!init) { b = r.bounds; init = true; }
            else b.Encapsulate(r.bounds);
        }
        if (init && cc != null)
            model.transform.position += Vector3.up * ((enemy.transform.position.y - cc.height * 0.5f) - b.min.y);

        SetField(enemyComp, "anim", animator);
        SetField(enemyComp, "tintRenderers", tintRenderers.ToArray());
    }

    static void Log(string msg) => Debug.Log($"[RPGBuilder] {msg}");
    static void LogWarning(string msg) => Debug.LogWarning($"[RPGBuilder] {msg}");
}
#endif