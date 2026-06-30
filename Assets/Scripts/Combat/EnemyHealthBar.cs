using UnityEngine;
using UnityEngine.UI;
using BasicRPG.Stats;

namespace BasicRPG.Combat
{
    /// <summary>
    /// Billboarded world-space health bar above an enemy. Reads the enemy's Health and
    /// shrinks the fill by driving its anchorMax.x (robust in world-space canvases, where
    /// Image.Type.Filled can render the full quad). Rotates the bar to face the camera.
    /// </summary>
    public class EnemyHealthBar : MonoBehaviour
    {
        [SerializeField] private Health health;
        [SerializeField] private RectTransform fill;
        [SerializeField] private Transform bar;

        void Update()
        {
            if (health == null || fill == null) return;
            Vector2 a = fill.anchorMax;
            a.x = health.Normalized;
            fill.anchorMax = a;

            Camera cam = Camera.main;
            if (cam != null && bar != null) bar.rotation = cam.transform.rotation;
        }
    }
}