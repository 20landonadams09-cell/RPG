using UnityEngine;
using UnityEngine.UI;

namespace BasicRPG.UI
{
    /// <summary>
    /// A filled Image + optional Text label driven by a normalized (0..1) value.
    /// Used for the health and stamina HUD bars. No TextMeshPro dependency.
    /// </summary>
    public class StatBar : MonoBehaviour
    {
        [SerializeField] private Image fillImage;
        [SerializeField] private Text label;
        [SerializeField] private string labelFormat = "{0:P0}";

        public void SetFill(float normalized)
        {
            normalized = Mathf.Clamp01(normalized);
            // Drive the fill's RectTransform.anchorMax.x (left-pivot) rather than Image.fillAmount —
            // a Filled image with no sprite renders as a full quad, so fillAmount is invisible. The
            // anchorMax.x technique shrinks the rect itself, which works with a sprite-less Image.
            if (fillImage != null)
            {
                RectTransform rt = fillImage.rectTransform;
                Vector2 a = rt.anchorMax; a.x = normalized; rt.anchorMax = a;
            }
            if (label != null) label.text = string.Format(labelFormat, normalized);
        }

        public void SetFillImage(Image image) => fillImage = image;
        public void SetLabel(Text text) => label = text;
    }
}