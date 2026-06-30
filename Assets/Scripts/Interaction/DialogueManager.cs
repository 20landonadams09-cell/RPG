using UnityEngine;
using UnityEngine.UI;

namespace BasicRPG.Interaction
{
    /// <summary>
    /// Singleton driving a linear dialogue panel. Opens via StartDialogue, advances on
    /// E/Space/left-click, closes at the end. Locks player movement while open. JustClosed
    /// (cleared in LateUpdate) stops the closing key from instantly re-triggering interaction
    /// regardless of MonoBehaviour Update order between this and PlayerInteraction.
    /// </summary>
    public class DialogueManager : MonoBehaviour
    {
        public static DialogueManager Instance { get; private set; }
        public static bool IsOpen { get; private set; }
        public static bool JustClosed { get; private set; }

        [SerializeField] private GameObject panel;
        [SerializeField] private Text nameText;
        [SerializeField] private Text lineText;
        [SerializeField] private Text hintText;

        private string[] lines;
        private int index;

        void Awake()
        {
            Instance = this;
            Hide();
        }

        public static void StartDialogue(string speakerName, string[] dialogueLines)
        {
            if (Instance == null || dialogueLines == null || dialogueLines.Length == 0) return;
            Instance.Begin(speakerName, dialogueLines);
        }

        void Begin(string speakerName, string[] dialogueLines)
        {
            lines = dialogueLines;
            index = 0;
            if (nameText != null) nameText.text = speakerName;
            Show();
            IsOpen = true;
            InteractionLock.IsLocked = true;
            Refresh();
        }

        void Update()
        {
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0))
                Advance();
        }

        void Advance()
        {
            index++;
            if (index >= lines.Length)
            {
                Close();
                return;
            }
            Refresh();
        }

        void Refresh()
        {
            if (lineText != null) lineText.text = lines[index];
        }

        void Close()
        {
            Hide();
            IsOpen = false;
            JustClosed = true;
            InteractionLock.IsLocked = false;
        }

        void LateUpdate()
        {
            JustClosed = false;
        }

        void Show() { if (panel != null) panel.SetActive(true); }
        void Hide() { if (panel != null) panel.SetActive(false); }
    }
}