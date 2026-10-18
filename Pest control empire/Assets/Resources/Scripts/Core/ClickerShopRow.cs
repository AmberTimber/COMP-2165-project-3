using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace COMP2165.Clicker
{
    public class ClickerShopRow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public RawImage icon;
        public Text title, description, actionText;
        public Button action;
        [HideInInspector] public ClickerTooltip tooltip;
        [HideInInspector] public string tooltipText;

        public bool UseTitleOnlyLayout(int fontSize)
        {
            bool changed = false;
            bool oldLayout = description != null && description.gameObject.activeSelf;
            if (oldLayout)
            {
                description.gameObject.SetActive(false);
                changed = true;
                var titleRect = title == null ? null : title.rectTransform;
                if (titleRect != null)
                {
                    titleRect.anchoredPosition = new Vector2(90f, -12f);
                    titleRect.sizeDelta = new Vector2(335f, 68f);
                }
            }
            if (title != null && title.fontSize < fontSize)
            {
                title.fontSize = fontSize;
                changed = true;
            }
            return changed;
        }

        public void UseCompactLayout(int fontSize)
        {
            if (description != null) description.gameObject.SetActive(false);
            var rect = transform as RectTransform;
            if (rect != null) rect.sizeDelta = new Vector2(299f, 88f);
            var layout = GetComponent<LayoutElement>();
            if (layout != null) layout.minHeight = layout.preferredHeight = 88f;
            SetRect(icon.rectTransform, 7f, 18f, 52f, 52f);
            SetRect(title.rectTransform, 65f, 8f, 116f, 72f);
            title.fontSize = fontSize;
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 13;
            title.resizeTextMaxSize = fontSize;
            SetRect(action.GetComponent<RectTransform>(), 188f, 14f, 102f, 60f);
            SetRect(actionText.rectTransform, 4f, 3f, 94f, 54f);
            actionText.fontSize = 15;
            actionText.resizeTextForBestFit = true;
            actionText.resizeTextMinSize = 11;
            actionText.resizeTextMaxSize = 15;
        }

        static void SetRect(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (tooltip != null) tooltip.Show(tooltipText, eventData.position);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (tooltip != null) tooltip.Hide();
        }

        void OnDisable()
        {
            if (tooltip != null) tooltip.Hide();
        }
    }
}
