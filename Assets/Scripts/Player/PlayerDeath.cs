using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using BasicRPG.Stats;
using BasicRPG.Interaction;

namespace BasicRPG.Player
{
    /// <summary>
    /// Player death + respawn. The player's <see cref="Health"/> fires <see cref="Health.OnDeath"/>
    /// at 0 HP but nothing else acted on it — this handles it: lock input, show a "You died —
    /// respawning…" overlay, then after a delay move the player back to the spawn point, revive
    /// health, top up stamina, and unlock. The overlay is built in code (ugui, no prefab/TMP).
    /// Uses <see cref="WaitForSecondsRealtime"/> so respawn works even if time is frozen.
    /// </summary>
    public class PlayerDeath : MonoBehaviour
    {
        [SerializeField] private Health health;
        [SerializeField] private Stamina stamina;
        [Tooltip("Optional explicit spawn point. If null, the player's position at Start is used.")]
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private float respawnDelay = 3f;

        private Vector3 startPos;
        private bool weLocked;
        private bool respawning;
        private Image overlay;
        private Text overlayText;
        private Canvas overlayCanvas;

        void Start()
        {
            startPos = spawnPoint != null ? spawnPoint.position : transform.position;
            if (health != null) health.OnDeath += HandleDeath;
            BuildOverlay();
        }

        void BuildOverlay()
        {
            GameObject canvasObj = new GameObject("DeathOverlayCanvas");
            overlayCanvas = canvasObj.AddComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.sortingOrder = 300; // above the metal wheel (200)
            canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            GameObject bg = new GameObject("BG");
            bg.transform.SetParent(canvasObj.transform, false);
            RectTransform bgRT = bg.AddComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = bgRT.offsetMax = Vector2.zero;
            overlay = bg.AddComponent<Image>();
            overlay.color = new Color(0f, 0f, 0f, 0f);
            overlay.raycastTarget = false;

            GameObject txtObj = new GameObject("Text");
            txtObj.transform.SetParent(canvasObj.transform, false);
            RectTransform txtRT = txtObj.AddComponent<RectTransform>();
            txtRT.anchorMin = new Vector2(0.5f, 0.5f); txtRT.anchorMax = new Vector2(0.5f, 0.5f);
            txtRT.pivot = new Vector2(0.5f, 0.5f);
            txtRT.sizeDelta = new Vector2(800f, 80f);
            txtRT.anchoredPosition = Vector2.zero;
            overlayText = txtObj.AddComponent<Text>();
            overlayText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            overlayText.fontSize = 36;
            overlayText.alignment = TextAnchor.MiddleCenter;
            overlayText.color = new Color(1f, 0.85f, 0.8f, 0f);
            overlayText.raycastTarget = false;
            overlayText.text = "You died — respawning…";

            overlayCanvas.gameObject.SetActive(false);
        }

        void HandleDeath()
        {
            if (respawning) return;
            respawning = true;
            if (!InteractionLock.IsLocked) { InteractionLock.IsLocked = true; weLocked = true; }
            overlayCanvas.gameObject.SetActive(true);
            StopAllCoroutines();
            StartCoroutine(RespawnRoutine());
        }

        IEnumerator RespawnRoutine()
        {
            // Fade the overlay in.
            float t = 0f;
            while (t < 0.4f)
            {
                t += Time.unscaledDeltaTime;
                float a = Mathf.Clamp01(t / 0.4f);
                overlay.color = new Color(0f, 0f, 0f, 0.7f * a);
                overlayText.color = new Color(1f, 0.85f, 0.8f, a);
                yield return null;
            }

            yield return new WaitForSecondsRealtime(Mathf.Max(0f, respawnDelay - 0.4f));

            // Respawn: move, revive, top up stamina.
            Vector3 pos = spawnPoint != null ? spawnPoint.position : startPos;
            CharacterController cc = GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.enabled = false; // teleport reliably (Move can't jump the controller)
                transform.position = pos;
                cc.enabled = true;
            }
            else
            {
                transform.position = pos;
            }

            if (health != null) health.Revive();
            if (stamina != null) stamina.Restore(stamina.Max);

            // Fade the overlay out, then release the lock.
            t = 0f;
            while (t < 0.4f)
            {
                t += Time.unscaledDeltaTime;
                float a = 1f - Mathf.Clamp01(t / 0.4f);
                overlay.color = new Color(0f, 0f, 0f, 0.7f * a);
                overlayText.color = new Color(1f, 0.85f, 0.8f, a);
                yield return null;
            }
            overlayCanvas.gameObject.SetActive(false);

            if (weLocked) { InteractionLock.IsLocked = false; weLocked = false; }
            respawning = false;
        }

        void OnDestroy()
        {
            if (health != null) health.OnDeath -= HandleDeath;
            if (weLocked) InteractionLock.IsLocked = false;
        }
    }
}