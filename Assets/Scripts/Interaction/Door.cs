using UnityEngine;

namespace BasicRPG.Interaction
{
    /// <summary>
    /// A hinged door. Interact toggles open/closed; the leaf rotates about its hinge (Y axis)
    /// smoothly in Update. Unlocked for now — key/lock can be added later.
    /// </summary>
    public class Door : MonoBehaviour, IInteractable
    {
        [SerializeField] private Transform leaf;     // the pivoting panel
        [SerializeField] private float openAngle = 90f;
        [SerializeField] private float speed = 120f; // deg/s
        [SerializeField] private string label = "Door";

        private bool isOpen;

        public string GetPrompt() => $"{(isOpen ? "Close" : "Open")} {label}";

        public void Interact(GameObject interactor) => isOpen = !isOpen;

        void Update()
        {
            if (leaf == null) return;
            Quaternion target = isOpen
                ? Quaternion.Euler(0f, openAngle, 0f)
                : Quaternion.identity;
            leaf.localRotation = Quaternion.RotateTowards(
                leaf.localRotation, target, speed * Time.deltaTime);
        }
    }
}