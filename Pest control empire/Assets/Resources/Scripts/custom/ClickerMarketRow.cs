using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace COMP2165.Clicker
{
    // One row of the market (a StoreItems sheet entry). Same shape as ClickerShopRow:
    // icon + title + buy button, with a tooltip on hover. ClickerGame fills it in.
    public class ClickerMarketRow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public RawImage icon;
        public Text title, description, actionText;
        public Button action;
        [HideInInspector] public ClickerTooltip tooltip;
        [HideInInspector] public string tooltipText;

        void Awake() { AutoBind(); }
        void Reset() { AutoBind(); }

        // Fills any field left empty in the Inspector from the row's children:
        // first RawImage = icon, first Button = action, the Text inside that button = actionText,
        // then the first remaining Text = title and the next = description. Safe to call more than once.
        public void AutoBind()
        {
            if (icon == null) icon = GetComponentInChildren<RawImage>(true);
            if (action == null) action = GetComponentInChildren<Button>(true);
            if (actionText == null && action != null) actionText = action.GetComponentInChildren<Text>(true);
            foreach (var text in GetComponentsInChildren<Text>(true))
            {
                if (text == actionText) continue;
                if (title == null) title = text;
                else if (description == null && text != title) description = text;
            }
        }

        public bool IsReady { get { return title != null && actionText != null && action != null; } }

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
            if (icon != null) SetRect(icon.rectTransform, 7f, 18f, 52f, 52f);
            if (title != null)
            {
                SetRect(title.rectTransform, 65f, 8f, 116f, 72f);
                title.fontSize = fontSize;
                title.resizeTextForBestFit = true;
                title.resizeTextMinSize = 13;
                title.resizeTextMaxSize = fontSize;
            }
            if (action != null) SetRect(action.GetComponent<RectTransform>(), 188f, 14f, 102f, 60f);
            if (actionText != null)
            {
                SetRect(actionText.rectTransform, 4f, 3f, 94f, 54f);
                actionText.fontSize = 15;
                actionText.resizeTextForBestFit = true;
                actionText.resizeTextMinSize = 11;
                actionText.resizeTextMaxSize = 15;
            }
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