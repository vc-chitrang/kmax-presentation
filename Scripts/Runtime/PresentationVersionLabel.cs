using TMPro;
using UnityEngine;

namespace ViitorCloud.KmaxPresentation {
    /// <summary>
    /// Small "v1.0.0 (1)" label at the bottom-left of the presentation UI canvas. The version and version code come from
    /// Resources/PresentationBuildInfo, which the editor writes from the active Game Info profile before every build and Play;
    /// without that file the player version (Application.version) is shown alone.
    /// </summary>
    public sealed class PresentationVersionLabel : MonoBehaviour {
        public const string BuildInfoResource = "PresentationBuildInfo";
        private const float Margin = 16.0f;

        public static PresentationVersionLabel Create(Canvas canvas, TMP_Text fontSource) {
            GameObject labelGo = new GameObject("VersionLabel", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(PresentationVersionLabel));
            labelGo.transform.SetParent(canvas.transform, false);
            RectTransform rect = labelGo.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.sizeDelta = new Vector2(400.0f, 24.0f);
            rect.anchoredPosition = new Vector2(Margin, Margin * 0.5f);

            TextMeshProUGUI text = labelGo.GetComponent<TextMeshProUGUI>();
            if (fontSource != null) {
                text.font = fontSource.font;
            }
            text.fontSize = 16.0f;
            text.color = new Color(1.0f, 1.0f, 1.0f, 0.65f);
            text.alignment = TextAlignmentOptions.BottomLeft;
            text.raycastTarget = false;
            text.text = BuildVersionText();
            return labelGo.GetComponent<PresentationVersionLabel>();
        }

        private static string BuildVersionText() {
            TextAsset info = Resources.Load<TextAsset>(BuildInfoResource);
            if (info == null) {
                Debug.LogWarning("[Presentation] Resources/" + BuildInfoResource + " is missing; showing the player version only.");
                return "v" + Application.version;
            }
            string[] parts = info.text.Trim().Split('|');
            string version = parts.Length > 0 && parts[0].Length > 0 ? parts[0] : Application.version;
            string code = parts.Length > 1 ? parts[1] : string.Empty;
            if (code.Length == 0) {
                return "v" + version;
            }
            return "v" + version + " (" + code + ")";
        }
    }
}
