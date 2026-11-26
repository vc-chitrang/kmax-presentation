using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ViitorCloud.KmaxPresentation {
    /// <summary>
    /// Editor tuning aid: Previous / Next buttons at the bottom centre and an "index / total" counter above them, so every model can be
    /// checked in order, one by one. Built at runtime under the presentation UI canvas (world space, 1920 x 1080 reference), so it needs
    /// no scene edit. Left and Right arrow keys do the same. Turn it on with PresentationDirector.debugBrowse; a player build never shows
    /// it (the director switches browsing off outside the Editor).
    /// </summary>
    public sealed class PresentationDebugBrowser : MonoBehaviour {
        private const float ButtonWidth = 150.0f;
        private const float ButtonHeight = 42.0f;
        private const float Margin = 32.0f;
        private const float ButtonGap = 12.0f;
        private const float CounterGap = 12.0f;

        private PresentationDirector director;
        private TMP_Text counterText;

        public void Build(PresentationDirector owner, Canvas canvas, TMP_Text fontSource, Camera eventCamera) {
            director = owner;
            RectTransform root = canvas.GetComponent<RectTransform>();
            EnableClicks(canvas, eventCamera);

            // Bottom centre, side by side, with the counter just above them (owner: check each model's size by hand).
            Vector2 bottomCentre = new Vector2(0.5f, 0.0f);
            float offset = ButtonWidth * 0.5f + ButtonGap * 0.5f;
            CreateButton(root, "DebugPrevious", "Previous", bottomCentre, new Vector2(-offset, Margin), fontSource, OnPrevious);
            CreateButton(root, "DebugNext", "Next", bottomCentre, new Vector2(offset, Margin), fontSource, OnNext);
            counterText = CreateLabel(root, "DebugCounter", bottomCentre, new Vector2(0.0f, Margin + ButtonHeight + CounterGap), fontSource);
            Refresh();
        }

        public void Refresh() {
            if (counterText == null || director == null) {
                return;
            }
            PresentationItem item = director.GetItem(director.BrowseIndex);
            string title = item != null ? item.title : string.Empty;
            counterText.text = (director.BrowseIndex + 1) + " / " + director.ItemCount + "   " + title;
        }

        private void Update() {
            if (Input.GetKeyDown(KeyCode.RightArrow)) {
                OnNext();
            } else if (Input.GetKeyDown(KeyCode.LeftArrow)) {
                OnPrevious();
            }
        }

        // The presentation scene ships without an EventSystem (it is hands-off), so the debug buttons bring their own and give the
        // world-space canvas the camera that draws it; without both, no click reaches a button.
        private static void EnableClicks(Canvas canvas, Camera eventCamera) {
            if (UnityEngine.EventSystems.EventSystem.current == null) {
                GameObject eventSystemGo = new GameObject("DebugEventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
                Debug.Log("[Presentation Browse] Added " + eventSystemGo.name + " so the debug buttons can be clicked.");
            }
            // The scene canvas keeps its GraphicRaycaster switched off (the Kmax pen uses KmaxUIRaycaster); the mouse needs it on.
            GraphicRaycaster mouseRaycaster = canvas.GetComponent<GraphicRaycaster>();
            if (mouseRaycaster == null) {
                mouseRaycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();
            }
            mouseRaycaster.enabled = true;
            if (canvas.worldCamera == null && eventCamera != null) {
                canvas.worldCamera = eventCamera;
            }
            if (canvas.worldCamera == null) {
                Debug.LogWarning("[Presentation Browse] The UI canvas has no event camera; use the arrow keys or the inspector buttons.");
            }
        }

        private void OnNext() {
            director.BrowseOffset(1);
            Refresh();
        }

        private void OnPrevious() {
            director.BrowseOffset(-1);
            Refresh();
        }

        private static void CreateButton(RectTransform parent, string objectName, string label, Vector2 anchor, Vector2 position, TMP_Text fontSource, UnityEngine.Events.UnityAction onClick) {
            GameObject buttonGo = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(parent, false);
            RectTransform rect = buttonGo.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
            rect.anchoredPosition = position;

            Image image = buttonGo.GetComponent<Image>();
            image.color = new Color(0.10f, 0.16f, 0.27f, 0.90f);

            Button button = buttonGo.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.30f, 0.50f, 0.80f, 1.0f);
            colors.pressedColor = new Color(0.50f, 0.70f, 1.0f, 1.0f);
            button.colors = colors;
            button.onClick.AddListener(onClick);

            TMP_Text text = CreateLabel(rect, objectName + "Label", new Vector2(0.5f, 0.5f), Vector2.zero, fontSource);
            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            text.text = label;
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 17.0f;
            text.raycastTarget = false;
        }

        private static TMP_Text CreateLabel(RectTransform parent, string objectName, Vector2 anchor, Vector2 position, TMP_Text fontSource) {
            GameObject labelGo = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(parent, false);
            RectTransform rect = labelGo.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = new Vector2(900.0f, 46.0f);
            rect.anchoredPosition = position;

            TextMeshProUGUI text = labelGo.GetComponent<TextMeshProUGUI>();
            if (fontSource != null) {
                text.font = fontSource.font;
            }
            text.fontSize = 26.0f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }
    }
}
