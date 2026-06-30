using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace BasicRPG.Interaction
{
    /// <summary>Transient on-screen toast for loot/notifications. Singleton.</summary>
    public class NotificationUI : MonoBehaviour
    {
        public static NotificationUI Instance { get; private set; }

        [SerializeField] private Text text;
        [SerializeField] private float duration = 2.5f;

        private Coroutine hideRoutine;

        void Awake()
        {
            Instance = this;
            if (text != null) text.gameObject.SetActive(false);
        }

        public static void Show(string msg)
        {
            if (Instance == null) return;
            Instance.ShowInternal(msg);
        }

        void ShowInternal(string msg)
        {
            if (text != null)
            {
                text.text = msg;
                text.gameObject.SetActive(true);
            }
            if (hideRoutine != null) StopCoroutine(hideRoutine);
            hideRoutine = StartCoroutine(HideAfter(duration));
        }

        IEnumerator HideAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (text != null) text.gameObject.SetActive(false);
            hideRoutine = null;
        }
    }
}