using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace COMP2165.Clicker
{
    public static class ClickerPalette
    {
        public static void Apply(ClickerCanvasView view, List<Dictionary<string, string>> rows)
        {
            if (view == null || rows == null) return;
            Color? primary = null, title = null, secondary = null, buttonText = null, notificationText = null, tooltipText = null;
            foreach (var row in rows)
            {
                string key = Csv.Get(row, "key").Trim().ToLowerInvariant();
                string hex = Csv.Get(row, "hex");
                Color color;
                if (!ColorUtility.TryParseHtmlString(hex, out color))
                {
                    Debug.LogWarning("Palette.csv has an invalid hex colour for " + key + ": " + hex);
                    continue;
                }
                switch (key)
                {
                    case "background": SetNamed(view, "Background Color", color); break;
                    case "background_dim": SetNamed(view, "Background Dim", color); break;
                    case "header": SetNamed(view, "Header - move and resize", color); break;
                    case "cookie_panel": SetNamed(view, "Cookie Panel - move and resize", color); break;
                    case "shop_panel": SetNamed(view, "Shop Panel - move and resize", color); break;
                    case "event_overlay": SetNamed(view, "Event Overlay - edit modal here", color); break;
                    case "event_card": SetNamed(view, "Event Card", color); break;
                    case "notification": SetNamed(view, "Notification - always in front", color); break;
                    case "tooltip_panel": SetNamed(view, "Shop Tooltip", color); break;
                    case "loading_panel": SetNamed(view, "Loading Message", color); break;
                    case "shop_row":
                        foreach (var shopRow in view.GetComponentsInChildren<ClickerShopRow>(true))
                        {
                            var image = shopRow.GetComponent<Image>();
                            if (image != null) image.color = color;
                        }
                        break;
                    case "button":
                        foreach (var button in view.GetComponentsInChildren<Button>(true))
                            if (button.targetGraphic is Image image) image.color = color;
                        break;
                    case "text_primary": primary = color; break;
                    case "text_title": title = color; break;
                    case "text_secondary": secondary = color; break;
                    case "text_button": buttonText = color; break;
                    case "text_notification": notificationText = color; break;
                    case "text_tooltip": tooltipText = color; break;
                    default: Debug.LogWarning("Unknown Palette.csv key: " + key); break;
                }
            }

            if (primary.HasValue)
                foreach (var text in view.GetComponentsInChildren<Text>(true)) text.color = primary.Value;
            if (title.HasValue)
            {
                view.gameTitle.color = title.Value;
                view.money.color = title.Value;
                view.eventTitle.color = title.Value;
            }
            if (secondary.HasValue)
            {
                view.production.color = secondary.Value;
                view.gameDescription.color = secondary.Value;
                view.batchStatus.color = secondary.Value;
                view.eventDescription.color = secondary.Value;
                view.eventQueueText.color = secondary.Value;
                foreach (var shopRow in view.GetComponentsInChildren<ClickerShopRow>(true))
                    if (shopRow.description != null) shopRow.description.color = secondary.Value;
            }
            if (buttonText.HasValue)
                foreach (var button in view.GetComponentsInChildren<Button>(true))
                {
                    var caption = button.GetComponentInChildren<Text>(true);
                    if (caption != null) caption.color = buttonText.Value;
                }
            if (notificationText.HasValue) view.toastText.color = notificationText.Value;
            if (tooltipText.HasValue)
                foreach (var tooltip in view.GetComponentsInChildren<ClickerTooltip>(true))
                {
                    var caption = tooltip.GetComponentInChildren<Text>(true);
                    if (caption != null) caption.color = tooltipText.Value;
                }
        }

        static void SetNamed(ClickerCanvasView view, string name, Color color)
        {
            foreach (var image in view.GetComponentsInChildren<Image>(true))
                if (image.gameObject.name == name) { image.color = color; return; }
            Debug.LogWarning("Canvas panel missing for Palette.csv: " + name);
        }
    }
}
