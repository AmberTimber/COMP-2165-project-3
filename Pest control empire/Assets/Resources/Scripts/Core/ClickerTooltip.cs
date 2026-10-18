using UnityEngine;
using UnityEngine.UI;

namespace COMP2165.Clicker
{
    public class ClickerTooltip : MonoBehaviour
    {
        const float Width = 360f;
        RectTransform panel;
        RectTransform canvasRect;
        Canvas canvas;
        Text caption;
        Transform notification;

        public static ClickerTooltip Ensure(ClickerCanvasView view)
        {
            var existing = view.GetComponentInChildren<ClickerTooltip>(true);
            if (existing != null) { existing.Initialize(view); return existing; }

            var go = new GameObject("Shop Tooltip", typeof(RectTransform), typeof(Image), typeof(ClickerTooltip));
            go.layer = LayerMask.NameToLayer("UI");
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(view.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(Width, 100f);
            var background = go.GetComponent<Image>();
            background.color = new Color32(76, 48, 27, 250);
            background.raycastTarget = false;

            var textObject = new GameObject("Tooltip Text", typeof(RectTransform), typeof(Text));
            textObject.layer = LayerMask.NameToLayer("UI");
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.SetParent(rect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(14f, 12f);
            textRect.offsetMax = new Vector2(-14f, -12f);
            var text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 17;
            text.color = new Color32(255, 248, 221, 255);
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.supportRichText = false;

            var tooltip = go.GetComponent<ClickerTooltip>();
            tooltip.Initialize(view);
            go.SetActive(false);
            return tooltip;
        }

        void Initialize(ClickerCanvasView view)
        {
            panel = (RectTransform)transform;
            canvasRect = (RectTransform)view.transform;
            canvas = view.GetComponent<Canvas>();
            caption = GetComponentInChildren<Text>(true);
            notification = view.toastPanel == null ? null : view.toastPanel.transform;
        }

        public void Show(string content, Vector2 screenPosition)
        {
            if (string.IsNullOrWhiteSpace(content)) { Hide(); return; }
            caption.text = content;
            float height = Mathf.Clamp(caption.preferredHeight + 28f, 72f, 220f);
            float width = panel.sizeDelta.x;
            panel.sizeDelta = new Vector2(width, height);
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, canvas.worldCamera, out local);
            float x = Mathf.Clamp(local.x + 18f, canvasRect.rect.xMin + 8f, canvasRect.rect.xMax - width - 8f);
            float y = Mathf.Clamp(local.y - 18f, canvasRect.rect.yMin + height + 8f, canvasRect.rect.yMax - 8f);
            panel.anchoredPosition = new Vector2(x, y);
            if (notification != null) transform.SetSiblingIndex(notification.GetSiblingIndex());
            else transform.SetAsLastSibling();
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }
    }
}
