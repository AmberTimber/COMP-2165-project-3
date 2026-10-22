using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace COMP2165.Clicker
{
    public static class Csv
    {
        public static List<Dictionary<string, string>> Read(string source)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < source.Length && source[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = !quoted;
                }
                else if (c == ',' && !quoted) { row.Add(field.ToString()); field.Length = 0; }
                else if ((c == '\n' || c == '\r') && !quoted)
                {
                    if (c == '\r' && i + 1 < source.Length && source[i + 1] == '\n') i++;
                    row.Add(field.ToString()); field.Length = 0;
                    if (row.Count > 1 || row[0].Length > 0) rows.Add(row);
                    row = new List<string>();
                }
                else field.Append(c);
            }
            if (quoted) throw new FormatException("Unclosed CSV quote");
            if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
            var result = new List<Dictionary<string, string>>();
            if (rows.Count == 0) return result;
            var headers = rows[0];
            if (headers.Count > 0) headers[0] = headers[0].TrimStart('\uFEFF');
            for (int i = 1; i < rows.Count; i++)
            {
                var item = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int j = 0; j < headers.Count; j++) item[headers[j].Trim()] = j < rows[i].Count ? rows[i][j].Trim() : "";
                result.Add(item);
            }
            return result;
        }

        public static string Get(Dictionary<string, string> row, string key, string fallback = "")
        {
            string value;
            return row.TryGetValue(key, out value) && !string.IsNullOrEmpty(value) ? value : fallback;
        }

        public static float Number(Dictionary<string, string> row, string key, float fallback = 0)
        {
            float value;
            return float.TryParse(Get(row, key), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        // Reads "5" as 5 points, or "10%" as 10 percent (percent = true). Blank, negative or unreadable cells give 0.
        public static float Amount(Dictionary<string, string> row, string key, out bool percent)
        {
            string text = Get(row, key);
            percent = text.EndsWith("%");
            if (percent) text = text.Substring(0, text.Length - 1).Trim();
            float value;
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0f ? value : 0f;
        }

        // Reads a share and returns it as 0..1. Accepts a fraction (0.5), a percent number (50) or Excel's percent text ("50%").
        // Plain numbers above 1 count as percents; exactly 1 means 100%. Blank or unreadable cells give the fallback.
        public static float Fraction(Dictionary<string, string> row, string key, float fallback = 0)
        {
            string text = Get(row, key);
            if (text.Length == 0) return fallback;
            bool percent = text.EndsWith("%");
            if (percent) text = text.Substring(0, text.Length - 1).Trim();
            float value;
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return fallback;
            if (percent || value > 1f) value /= 100f;
            return Math.Max(0f, Math.Min(1f, value));
        }
    }

    [Serializable] public class CurrencyData { public string id, name; public float startingAmount, clickAmount; }
    [Serializable]
    public class BuildingData
    {
        public string id, name, description, tooltip, image, costCurrency, yieldCurrency, unlockCurrency;
        public float cost, perSecond, unlockAt;

        // Consumption. Both come from the Buildings sheet. The header spelling "conesume_currency" is kept to match it.
        /// <summary>Currency id this building burns every second (e.g. "sugar"). Ignored when consumeable is 0.</summary>
        public string conesume_currency;
        /// <summary>Amount of conesume_currency burned per owned building, per second. 0 = consumes nothing.</summary>
        public float consumeable;
        /// <summary>Share (0 to 1) of its normal output a building still makes when it can't pay for any of its input.
        /// Sheet column "productionW/Oconsumption". 0 = it stops completely; with partial input the output sits in between.</summary>
        public float productionWithoutConsumption;
    }
    // One row of the StoreItems sheet: pay inputAmount of inputCurrency, receive outputAmount of outputCurrency (per purchase).
    [Serializable]
    public class StoreItemData
    {
        public string id, name, description, tooltip, image, inputCurrency, outputCurrency;
        public float inputAmount, outputAmount;
    }
    [Serializable] public class UpgradeData { public string id, name, description, tooltip, targetType, targetId, operation, image, costCurrency, unlockCurrency; public float cost, amount, unlockAt; }
    [Serializable]
    public class EventData
    {
        public string title, description, option1, option2, image, sound, currencyId;
        public int correctOption;
        // Optional building changes (Events sheet). A correct answer gives successCount of successBuilding;
        // a wrong answer, or running out of time, takes failureCount of failureBuilding.
        public string successBuilding, failureBuilding;
        public int successCount, failureCount;
        // Reputation taken every time the event fails (wrong answer or timeout): a number of points, or a percent of the
        // current reputation when the sheet cell ends in %. 0 = none.
        public float reputationLoss;
        public bool reputationLossIsPercent;
    }
    [Serializable] public class BuildingSave { public string id; public int count; }
    [Serializable] public class CurrencySave { public string id; public float amount, lifetime; }
    [Serializable]
    public class ClickerSave
    {
        public float money, lifetime;
        public List<CurrencySave> currencies = new List<CurrencySave>();
        public List<BuildingSave> buildings = new List<BuildingSave>();
        public List<string> upgrades = new List<string>();
    }
}