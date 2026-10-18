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
    }

    [Serializable] public class CurrencyData { public string id, name; public float startingAmount, clickAmount; }
    [Serializable] public class BuildingData { public string id, name, description, tooltip, image, costCurrency, yieldCurrency, unlockCurrency; public float cost, perSecond, unlockAt; }
    [Serializable] public class UpgradeData { public string id, name, description, tooltip, targetType, targetId, operation, image, costCurrency, unlockCurrency; public float cost, amount, unlockAt; }
    [Serializable] public class EventData { public string title, description, option1, option2, image, sound, currencyId; public int correctOption; }
    [Serializable] public class BuildingSave { public string id; public int count; }
    [Serializable] public class CurrencySave { public string id; public float amount, lifetime; }
    [Serializable] public class ClickerSave
    {
        public float money, lifetime;
        public List<CurrencySave> currencies = new List<CurrencySave>();
        public List<BuildingSave> buildings = new List<BuildingSave>();
        public List<string> upgrades = new List<string>();
    }
}
