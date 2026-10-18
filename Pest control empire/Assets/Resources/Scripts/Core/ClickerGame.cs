using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Networking;
using UnityEngine.UI;
using uClicker;

namespace COMP2165.Clicker
{
    public class ClickerGame : MonoBehaviour
    {
        readonly Dictionary<string, string> settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> visualPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Vector2Int> spriteTiles = new Dictionary<string, Vector2Int>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> audioPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Texture2D> images = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Texture2D> fallbackIcons = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, AudioClip> sounds = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        readonly List<CurrencyData> currencyDefinitions = new List<CurrencyData>();
        readonly Dictionary<string, Currency> currencyAssets = new Dictionary<string, Currency>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Clickable> clickAssets = new Dictionary<string, Clickable>(StringComparer.OrdinalIgnoreCase);
        readonly List<BuildingData> buildings = new List<BuildingData>();
        readonly List<UpgradeData> upgrades = new List<UpgradeData>();
        readonly List<EventData> events = new List<EventData>();
        readonly List<EventData> eventQueue = new List<EventData>();
        readonly List<float> eventBoostEnds = new List<float>();
        readonly Dictionary<string, Building> buildingAssets = new Dictionary<string, Building>();
        readonly Dictionary<string, Upgrade> upgradeAssets = new Dictionary<string, Upgrade>();
        readonly Dictionary<string, ClickerShopRow> buildingRows = new Dictionary<string, ClickerShopRow>();
        readonly Dictionary<string, ClickerShopRow> upgradeRows = new Dictionary<string, ClickerShopRow>();

        ClickerManager manager;
        ClickerCanvasView ui;
        ClickerTooltip shopTooltip;
        Currency currency;
        Clickable clickable;
        AudioSource music;
        Texture2D fallbackCookie, fallbackGold;
        string spriteSheetPath;
        int spriteTileSize = 48;
        bool ready, eventOpen, goldVisible, sellingBuildings;
        int buildingBatch = 1;
        string error, toast;
        float toastUntil, nextTick, nextEvent, nextGold, nextSave, goldEnd;
        float goldBonus = 7, bonusDuration = 20, eventInterval = 35, goldInterval = 50, wrongPenalty = 0.1f, sellRefund = 0.5f, shopReveal = 0.5f;

        void Start()
        {
            ConfigureInputModule();
            ui = FindFirstObjectByType<ClickerCanvasView>();
            if (ui == null) { Debug.LogError("The Clicker scene needs its editable Canvas. Use COMP2165 > Create Editable Canvas."); return; }
            BindCanvas();
            StartCoroutine(LoadGame());
        }

        static void ConfigureInputModule()
        {
            var eventSystem = FindFirstObjectByType<EventSystem>();
            if (eventSystem == null) return;
            var modern = eventSystem.GetComponent<InputSystemUIInputModule>();
            var legacy = eventSystem.GetComponent<StandaloneInputModule>();
#if ENABLE_INPUT_SYSTEM
            if (legacy != null) legacy.enabled = false;
            if (modern == null) modern = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            if (modern.actionsAsset == null) modern.AssignDefaultActions();
            modern.enabled = true;
#else
            if (modern != null) modern.enabled = false;
            if (legacy == null) legacy = eventSystem.gameObject.AddComponent<StandaloneInputModule>();
            legacy.enabled = true;
#endif
        }

        IEnumerator LoadGame()
        {
            string[] tables = { "Settings", "Currencies", "Buildings", "Upgrades", "Events", "Visuals", "Audio", "Palette" };
            var loaded = new Dictionary<string, List<Dictionary<string, string>>>();
            foreach (string table in tables)
            {
                string url = StreamingUrl("Data/" + table + ".csv");
                using (var request = UnityWebRequest.Get(url))
                {
                    yield return request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        error = "Could not load " + table + ".csv: " + request.error;
                        yield break;
                    }
                    try { loaded[table] = Csv.Read(request.downloadHandler.text); }
                    catch (Exception ex) { error = table + ".csv: " + ex.Message; yield break; }
                }
            }

            foreach (var row in loaded["Settings"]) settings[Csv.Get(row, "key")] = Csv.Get(row, "value");
            goldBonus = Setting("gold_multiplier", 7);
            bonusDuration = Setting("bonus_duration_seconds", 20);
            eventInterval = Setting("event_interval_seconds", 35);
            goldInterval = Setting("gold_interval_seconds", 50);
            wrongPenalty = Setting("wrong_penalty_percent", 10) / 100f;
            sellRefund = Setting("sell_refund_percent", 50) / 100f;
            shopReveal = Setting("shop_reveal_percent", 50) / 100f;
            if (goldBonus < 1 || bonusDuration <= 0 || eventInterval <= 0 || goldInterval <= 0 || wrongPenalty < 0 || wrongPenalty > 1 || sellRefund < 0 || sellRefund > 1 || shopReveal < 0 || shopReveal > 1)
            { error = "Check multiplier, duration, interval and penalty values in Settings.csv."; yield break; }

            foreach (var row in loaded["Currencies"])
            {
                var item = new CurrencyData { id = Csv.Get(row, "id"), name = Csv.Get(row, "name"), startingAmount = Csv.Number(row, "starting_amount"), clickAmount = Csv.Number(row, "click_amount") };
                if (string.IsNullOrWhiteSpace(item.id) || string.IsNullOrWhiteSpace(item.name) || item.startingAmount < 0 || item.clickAmount < 0 || currencyDefinitions.Exists(existing => existing.id.Equals(item.id, StringComparison.OrdinalIgnoreCase)))
                { error = "Currencies.csv needs unique IDs, names, and non-negative amounts."; yield break; }
                currencyDefinitions.Add(item);
            }
            if (currencyDefinitions.Count == 0) { error = "Currencies.csv needs at least one currency."; yield break; }
            string primaryId = currencyDefinitions[0].id;

            foreach (var row in loaded["Buildings"])
            {
                var item = new BuildingData { id = Csv.Get(row, "id"), name = Csv.Get(row, "name"), description = Csv.Get(row, "description"), tooltip = Csv.Get(row, "tooltip"), image = Csv.Get(row, "image"), cost = Csv.Number(row, "base_cost"), perSecond = Csv.Number(row, "per_second"), unlockAt = Csv.Number(row, "unlock_at"), costCurrency = Csv.Get(row, "cost_currency", primaryId), yieldCurrency = Csv.Get(row, "yield_currency", primaryId) };
                item.unlockCurrency = Csv.Get(row, "unlock_currency", item.costCurrency);
                if (string.IsNullOrEmpty(item.id) || item.cost < 0 || item.perSecond < 0) { error = "Invalid Buildings.csv row."; yield break; }
                buildings.Add(item);
            }
            foreach (var row in loaded["Upgrades"])
            {
                var item = new UpgradeData { id = Csv.Get(row, "id"), name = Csv.Get(row, "name"), description = Csv.Get(row, "description"), tooltip = Csv.Get(row, "tooltip"), image = Csv.Get(row, "image"), targetType = Csv.Get(row, "target_type"), targetId = Csv.Get(row, "target_id"), operation = Csv.Get(row, "operation"), cost = Csv.Number(row, "cost"), amount = Csv.Number(row, "amount", 1), unlockAt = Csv.Number(row, "unlock_at"), costCurrency = Csv.Get(row, "cost_currency", primaryId) };
                item.unlockCurrency = Csv.Get(row, "unlock_currency", item.costCurrency);
                if (string.IsNullOrEmpty(item.id) || item.cost < 0 || item.amount <= 0) { error = "Invalid Upgrades.csv row."; yield break; }
                upgrades.Add(item);
            }
            foreach (var row in loaded["Events"])
            {
                int correctOption;
                if (!int.TryParse(Csv.Get(row, "Correct Option"), NumberStyles.Integer, CultureInfo.InvariantCulture, out correctOption) || correctOption < 0 || correctOption > 2)
                { error = "Every event needs Correct Option 0 (random), 1, or 2."; yield break; }
                var item = new EventData { title = Csv.Get(row, "Event Title"), description = Csv.Get(row, "Event Description"), option1 = Csv.Get(row, "Event Option 1"), option2 = Csv.Get(row, "Event Option 2"), image = Csv.Get(row, "Image"), sound = Csv.Get(row, "Sound"), currencyId = Csv.Get(row, "Currency", primaryId), correctOption = correctOption };
                events.Add(item);
            }
            foreach (var row in loaded["Visuals"])
            {
                string key = Csv.Get(row, "key");
                string path = Csv.Get(row, "path");
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (key.Equals("sprite_sheet", StringComparison.OrdinalIgnoreCase))
                {
                    spriteSheetPath = path;
                    int size;
                    if (int.TryParse(Csv.Get(row, "tile_size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out size) && size > 0) spriteTileSize = size;
                }
                else if (!string.IsNullOrWhiteSpace(path)) visualPaths[key] = path;
                else
                {
                    int column, spriteRow;
                    if (int.TryParse(Csv.Get(row, "sprite_column"), NumberStyles.Integer, CultureInfo.InvariantCulture, out column) &&
                        int.TryParse(Csv.Get(row, "sprite_row"), NumberStyles.Integer, CultureInfo.InvariantCulture, out spriteRow))
                        spriteTiles[key] = new Vector2Int(column, spriteRow);
                }
            }
            foreach (var row in loaded["Audio"]) audioPaths[Csv.Get(row, "key")] = Csv.Get(row, "path");
            SetupEconomy();
            if (!string.IsNullOrEmpty(error)) yield break;
            LoadProgress();
            music = gameObject.AddComponent<AudioSource>();
            music.loop = true;
            music.volume = Mathf.Clamp01(Setting("music_volume", 0.45f));
            if (spriteTiles.Count > 0 && !string.IsNullOrWhiteSpace(spriteSheetPath))
            {
                yield return LoadImage("sprite_sheet", spriteSheetPath);
                Texture2D sheet = Image("sprite_sheet");
                if (sheet != null) foreach (var pair in spriteTiles) SliceImage(pair.Key, sheet, pair.Value);
            }
            foreach (var pair in visualPaths) yield return LoadImage(pair.Key, pair.Value);
            foreach (var pair in audioPaths) if (!string.IsNullOrEmpty(pair.Value)) yield return LoadAudio(pair.Key, pair.Value);
            AudioClip clip;
            if (sounds.TryGetValue("music", out clip)) { music.clip = clip; music.Play(); }
            nextTick = Time.time + 1;
            nextEvent = Time.time + eventInterval;
            nextGold = Time.time + goldInterval;
            nextSave = Time.time + 15;
            shopTooltip = ClickerTooltip.Ensure(ui);
            ClickerPalette.Apply(ui, loaded["Palette"]);
            ready = true;
            BuildShopRows();
            RefreshCanvas();
        }

        float Setting(string key, float fallback)
        {
            string text;
            float value;
            return settings.TryGetValue(key, out text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        string Label(string key, string fallback)
        {
            string value;
            return settings.TryGetValue(key, out value) && !string.IsNullOrEmpty(value) ? value : fallback;
        }

        void Register(ClickerComponent component)
        {
            component.GUIDContainer = new GUIDContainer(Guid.NewGuid());
            ClickerComponent.RuntimeLookup[component.GUIDContainer.Guid] = component;
        }

        void SetupEconomy()
        {
            var uCurrencies = new List<Currency>();
            var uClickables = new List<Clickable>();
            foreach (var row in currencyDefinitions)
            {
                var item = ScriptableObject.CreateInstance<Currency>();
                item.name = row.name; Register(item);
                currencyAssets.Add(row.id, item); uCurrencies.Add(item);
                var click = ScriptableObject.CreateInstance<Clickable>();
                click.name = row.name + " clickable"; click.Currency = item;
                click.Amount = row.clickAmount; Register(click);
                clickAssets.Add(row.id, click); uClickables.Add(click);
            }
            currency = uCurrencies[0];
            clickable = uClickables[0];
            var uBuildings = new List<Building>();
            foreach (var row in buildings)
            {
                if (buildingAssets.ContainsKey(row.id)) { error = "Duplicate building ID: " + row.id; return; }
                if (!currencyAssets.ContainsKey(row.costCurrency) || !currencyAssets.ContainsKey(row.yieldCurrency) || !currencyAssets.ContainsKey(row.unlockCurrency))
                { error = "Unknown building currency: " + row.id; return; }
                var item = ScriptableObject.CreateInstance<Building>();
                item.name = row.name; item.Cost = new CurrencyTuple { Currency = currencyAssets[row.costCurrency], Amount = row.cost };
                item.YieldAmount = new CurrencyTuple { Currency = currencyAssets[row.yieldCurrency], Amount = row.perSecond };
                item.RequirementGroups = new RequirementGroup[0]; Register(item);
                buildingAssets.Add(row.id, item); uBuildings.Add(item);
            }
            var uUpgrades = new List<Upgrade>();
            foreach (var row in upgrades)
            {
                if (upgradeAssets.ContainsKey(row.id)) { error = "Duplicate upgrade ID: " + row.id; return; }
                if (!currencyAssets.ContainsKey(row.costCurrency) || !currencyAssets.ContainsKey(row.unlockCurrency))
                { error = "Unknown upgrade currency: " + row.id; return; }
                var item = ScriptableObject.CreateInstance<Upgrade>();
                item.name = row.name; item.Cost = new CurrencyTuple { Currency = currencyAssets[row.costCurrency], Amount = row.cost };
                item.RequirementGroups = new RequirementGroup[0];
                var perk = new UpgradePerk { Amount = row.amount, Operation = row.operation.Equals("add", StringComparison.OrdinalIgnoreCase) ? Operation.Add : Operation.Multiply };
                string targetId = string.IsNullOrEmpty(row.targetId) ? currencyDefinitions[0].id : row.targetId;
                if (row.targetType.Equals("click", StringComparison.OrdinalIgnoreCase) && clickAssets.ContainsKey(targetId)) { perk.Type = UpgradeType.Clickable; perk.TargetClickable = clickAssets[targetId]; }
                else if (row.targetType.Equals("all", StringComparison.OrdinalIgnoreCase) && currencyAssets.ContainsKey(targetId)) { perk.Type = UpgradeType.Currency; perk.TargetCurrency = currencyAssets[targetId]; }
                else if (row.targetType.Equals("building", StringComparison.OrdinalIgnoreCase) && buildingAssets.ContainsKey(row.targetId)) { perk.Type = UpgradeType.Building; perk.TargetBuilding = buildingAssets[row.targetId]; }
                else { error = "Unknown upgrade target: " + row.id; return; }
                item.UpgradePerk = new[] { perk }; Register(item);
                upgradeAssets.Add(row.id, item); uUpgrades.Add(item);
            }
            foreach (var row in events) if (!currencyAssets.ContainsKey(row.currencyId)) { error = "Unknown event currency: " + row.currencyId; return; }
            manager = ScriptableObject.CreateInstance<ClickerManager>();
            manager.Config = new ManagerConfig { Currencies = uCurrencies.ToArray(), Clickables = uClickables.ToArray(), AvailableBuildings = uBuildings.ToArray(), AvailableUpgrades = uUpgrades.ToArray(), BuildingCostIncrease = Setting("building_cost_growth_percent", 15) / 100f };
            manager.OnEnable();
            foreach (var row in currencyDefinitions)
            {
                var asset = currencyAssets[row.id];
                manager.State.CurrencyCurrentTotals[asset] = row.startingAmount;
                manager.State.CurrencyHistoricalTotals[asset] = row.startingAmount;
            }
        }

        float Balance(Currency asset) { float value; return manager.State.CurrencyCurrentTotals.TryGetValue(asset, out value) ? value : 0; }
        float Historical(Currency asset) { float value; return manager.State.CurrencyHistoricalTotals.TryGetValue(asset, out value) ? value : 0; }
        float Money { get { return Balance(currency); } }
        float Lifetime { get { return Historical(currency); } }
        float Multiplier
        {
            get
            {
                double value = Time.time < goldEnd ? goldBonus : 1;
                for (int i = 0; i < eventBoostEnds.Count; i++) if (Time.time < eventBoostEnds[i]) value *= goldBonus * 10;
                return (float)Math.Min(value, 1e12);
            }
        }

        void Update()
        {
            if (!ready)
            {
                if (ui != null && ui.loadingText != null) ui.loadingText.text = error ?? "Loading game data...";
                return;
            }
            for (int i = eventBoostEnds.Count - 1; i >= 0; i--) if (Time.time >= eventBoostEnds[i]) eventBoostEnds.RemoveAt(i);
            if (Time.time >= nextTick)
            {
                nextTick = Time.time + 1;
                var originals = new List<float>();
                foreach (var row in buildings)
                {
                    var asset = buildingAssets[row.id]; originals.Add(asset.YieldAmount.Amount);
                    var value = asset.YieldAmount; value.Amount *= Multiplier; asset.YieldAmount = value;
                }
                manager.Tick();
                for (int i = 0; i < buildings.Count; i++)
                { var asset = buildingAssets[buildings[i].id]; var value = asset.YieldAmount; value.Amount = originals[i]; asset.YieldAmount = value; }
            }
            if (Time.time >= nextEvent && events.Count > 0)
            { var next = events[UnityEngine.Random.Range(0, events.Count)]; eventQueue.Add(QueueEvent(next)); nextEvent = Time.time + eventInterval; PlaySound(string.IsNullOrEmpty(next.sound) ? "event" : next.sound); }
            if (Time.time >= nextGold && !goldVisible)
            { goldVisible = true; nextGold = Time.time + goldInterval; }
            if (Time.time >= nextSave) { SaveProgress(); nextSave = Time.time + 15; }
            RefreshCanvas();
        }

        void ClickCookie()
        {
            EnsureMusic();
            foreach (var row in currencyDefinitions)
            {
                if (row.clickAmount <= 0) continue;
                var source = clickAssets[row.id];
                source.Amount = row.clickAmount * Multiplier;
                manager.Click(source);
                source.Amount = row.clickAmount;
            }
            PlaySound("click");
        }

        int BuildingCount(BuildingData row)
        {
            int count;
            manager.State.EarnedBuildings.TryGetValue(buildingAssets[row.id], out count);
            return count;
        }

        float BuildingBatchValue(BuildingData row, int count, bool selling)
        {
            if (selling && count < buildingBatch) return float.PositiveInfinity;
            int start = selling ? count - buildingBatch : count;
            double total = 0;
            double growth = 1 + manager.Config.BuildingCostIncrease;
            for (int i = 0; i < buildingBatch; i++) total += (int)row.cost * Math.Pow(growth, start + i);
            if (selling) total *= sellRefund;
            return (float)Math.Min(total, float.MaxValue);
        }

        void TransactBuilding(BuildingData row)
        {
            var asset = buildingAssets[row.id];
            var costCurrency = asset.Cost.Currency;
            int count = BuildingCount(row);
            if (sellingBuildings)
            {
                if (count < buildingBatch) return;
                float refund = BuildingBatchValue(row, count, true);
                manager.State.EarnedBuildings[asset] = count - buildingBatch;
                manager.State.CurrencyCurrentTotals[costCurrency] = Balance(costCurrency) + refund;
                ShowToast("Sold " + buildingBatch + " " + row.name + " for " + Format(refund) + " " + costCurrency.name + ".");
            }
            else
            {
                float cost = BuildingBatchValue(row, count, false);
                if (Balance(costCurrency) < cost) return;
                for (int i = 0; i < buildingBatch; i++) manager.BuyBuilding(asset);
                ShowToast("Bought " + buildingBatch + " " + row.name + " for " + Format(cost) + " " + costCurrency.name + ".");
            }
            PlaySound("buy");
            SaveProgress();
        }

        EventData QueueEvent(EventData source)
        {
            if (source.correctOption != 0) return source;
            return new EventData
            {
                title = source.title,
                description = source.description,
                option1 = source.option1,
                option2 = source.option2,
                image = source.image,
                sound = source.sound,
                currencyId = source.currencyId,
                correctOption = UnityEngine.Random.Range(1, 3)
            };
        }

        void ResolveEvent(int choice)
        {
            if (eventQueue.Count == 0) return;
            EventData current = eventQueue[eventQueue.Count - 1];
            if (choice == current.correctOption)
            {
                eventBoostEnds.Add(Time.time + bonusDuration);
                ShowToast("Good call! " + Format(goldBonus * 10) + "x for " + Format(bonusDuration) + " seconds.");
                PlaySound("success");
            }
            else
            {
                var eventCurrency = currencyAssets[current.currencyId];
                float balance = Balance(eventCurrency);
                float loss = Mathf.Min(balance, balance * wrongPenalty);
                manager.State.CurrencyCurrentTotals[eventCurrency] = balance - loss;
                ShowToast("Wrong call. Lost " + Format(loss) + " " + eventCurrency.name + ".");
                PlaySound("failure");
            }
            eventQueue.RemoveAt(eventQueue.Count - 1);
            if (eventQueue.Count == 0) eventOpen = false;
            SaveProgress();
        }

        void ShowToast(string message) { toast = message; toastUntil = Time.time + 4; }
        void EnsureMusic() { if (music != null && music.clip != null && !music.isPlaying) music.Play(); }
        static string Format(float value)
        {
            if (value >= 1e12f) return (value / 1e12f).ToString("0.##", CultureInfo.InvariantCulture) + "T";
            if (value >= 1e9f) return (value / 1e9f).ToString("0.##", CultureInfo.InvariantCulture) + "B";
            if (value >= 1e6f) return (value / 1e6f).ToString("0.##", CultureInfo.InvariantCulture) + "M";
            if (value >= 1000) return (value / 1000).ToString("0.##", CultureInfo.InvariantCulture) + "K";
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }

        void SaveProgress()
        {
            if (manager == null) return;
            var save = new ClickerSave { money = Money, lifetime = Lifetime };
            foreach (var row in currencyDefinitions)
            {
                var asset = currencyAssets[row.id];
                save.currencies.Add(new CurrencySave { id = row.id, amount = Balance(asset), lifetime = Historical(asset) });
            }
            foreach (var row in buildings)
            { int count; manager.State.EarnedBuildings.TryGetValue(buildingAssets[row.id], out count); save.buildings.Add(new BuildingSave { id = row.id, count = count }); }
            foreach (var row in upgrades) if (manager.State.EarnedUpgrades.Contains(upgradeAssets[row.id])) save.upgrades.Add(row.id);
            PlayerPrefs.SetString("COMP2165_" + Label("game_id", "clicker"), JsonUtility.ToJson(save));
            PlayerPrefs.Save();
        }

        void LoadProgress()
        {
            string json = PlayerPrefs.GetString("COMP2165_" + Label("game_id", "clicker"), "");
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                ClickerSave save = JsonUtility.FromJson<ClickerSave>(json);
                if (save.currencies != null && save.currencies.Count > 0)
                {
                    foreach (var item in save.currencies)
                    {
                        Currency asset;
                        if (!currencyAssets.TryGetValue(item.id, out asset)) continue;
                        manager.State.CurrencyCurrentTotals[asset] = Mathf.Max(0, item.amount);
                        manager.State.CurrencyHistoricalTotals[asset] = Mathf.Max(0, item.lifetime);
                    }
                }
                else
                {
                    manager.State.CurrencyCurrentTotals[currency] = Mathf.Max(0, save.money);
                    manager.State.CurrencyHistoricalTotals[currency] = Mathf.Max(0, save.lifetime);
                }
                foreach (var item in save.buildings) if (buildingAssets.ContainsKey(item.id)) manager.State.EarnedBuildings[buildingAssets[item.id]] = Mathf.Max(0, item.count);
                foreach (string id in save.upgrades) if (upgradeAssets.ContainsKey(id)) manager.State.EarnedUpgrades.Add(upgradeAssets[id]);
            }
            catch (Exception ex) { Debug.LogWarning("Could not load saved progress: " + ex.Message); }
        }

        void OnApplicationPause(bool paused) { if (paused && ready) SaveProgress(); }
        void OnApplicationQuit() { if (ready) SaveProgress(); }

        static string StreamingUrl(string relative)
        {
            string root = Application.streamingAssetsPath.TrimEnd('/', '\\');
            if (root.Contains("://") || root.StartsWith("jar:", StringComparison.OrdinalIgnoreCase)) return root.Replace('\\', '/') + "/" + relative;
            return new Uri(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))).AbsoluteUri;
        }

        string MediaUrl(string path)
        {
            if (path.Contains("..") || path.Contains(":") || path.StartsWith("/") || path.StartsWith("\\")) return null;
            string[] parts = path.Replace('\\', '/').Split('/');
            if (Application.streamingAssetsPath.Contains("://")) for (int i = 0; i < parts.Length; i++) parts[i] = Uri.EscapeDataString(parts[i]);
            return StreamingUrl("Media/" + string.Join("/", parts));
        }

        IEnumerator LoadImage(string key, string path)
        {
            string url = MediaUrl(path);
            if (url == null) { Debug.LogWarning("Invalid image path: " + path); yield break; }
            using (var request = UnityWebRequestTexture.GetTexture(url, false))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    var texture = DownloadHandlerTexture.GetContent(request);
                    texture.filterMode = FilterMode.Point;
                    texture.wrapMode = TextureWrapMode.Clamp;
                    images[key] = texture;
                }
                else Debug.LogWarning("Image not loaded: " + path + " (" + request.error + ")");
            }
        }

        void SliceImage(string key, Texture2D sheet, Vector2Int tile)
        {
            int x = tile.x * spriteTileSize;
            int y = sheet.height - (tile.y + 1) * spriteTileSize;
            if (x < 0 || y < 0 || x + spriteTileSize > sheet.width || y + spriteTileSize > sheet.height)
            {
                Debug.LogWarning("Sprite tile outside sheet: " + key);
                return;
            }
            try
            {
                var result = new Texture2D(spriteTileSize, spriteTileSize, TextureFormat.RGBA32, false);
                result.SetPixels(sheet.GetPixels(x, y, spriteTileSize, spriteTileSize));
                result.Apply(false, true);
                result.filterMode = FilterMode.Point;
                result.wrapMode = TextureWrapMode.Clamp;
                images[key] = result;
            }
            catch (UnityException exception) { Debug.LogWarning("Sprite tile not loaded: " + key + " (" + exception.Message + ")"); }
        }

        IEnumerator LoadAudio(string key, string path)
        {
            string url = MediaUrl(path);
            if (url == null) { Debug.LogWarning("Invalid audio path: " + path); yield break; }
            AudioType type = path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? AudioType.WAV : path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ? AudioType.OGGVORBIS : AudioType.MPEG;
            using (var request = UnityWebRequestMultimedia.GetAudioClip(url, type))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success) sounds[key] = DownloadHandlerAudioClip.GetContent(request);
                else Debug.LogWarning("Audio not loaded: " + path + " (" + request.error + ")");
            }
        }

        void PlaySound(string key) { AudioClip clip; if (music != null && sounds.TryGetValue(key, out clip)) music.PlayOneShot(clip, Mathf.Clamp01(Setting("sound_volume", 0.7f))); }

        Texture2D Image(string key) { Texture2D result; return images.TryGetValue(key, out result) ? result : null; }
        bool ShopVisible(float price, Currency costCurrency, bool owned, bool startingItem = false) { return owned || startingItem || Historical(costCurrency) >= price * shopReveal; }

        Texture2D Icon(string key, string motif)
        {
            Texture2D existing;
            if (fallbackIcons.TryGetValue(key, out existing)) return existing;
            const int size = 72;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color accent = Color.HSVToRGB(((uint)key.GetHashCode() % 360) / 360f, 0.58f, 0.85f);
            Color dark = new Color(0.10f, 0.14f, 0.20f);
            Color light = new Color(0.97f, 0.95f, 0.88f);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float px = (x - 35.5f) / 35.5f, py = (y - 35.5f) / 35.5f;
                bool mark = false;
                switch (motif)
                {
                    case "cursor":
                        mark = Triangle(px, py, -0.50f, -0.62f, -0.50f, 0.65f, 0.48f, -0.18f) || Box(px, py, -0.02f, -0.72f, 0.20f, -0.12f);
                        break;
                    case "grandma":
                        mark = Circle(px, py, 0, 0.19f, 0.34f) || Circle(px, py, 0, -0.80f, 0.65f) || (Circle(px, py, 0, 0.26f, 0.48f) && !Circle(px, py, 0, 0.19f, 0.35f));
                        break;
                    case "farm":
                        mark = Box(px, py, -0.06f, -0.68f, 0.06f, 0.45f) || Circle(px, py, -0.28f, 0.25f, 0.28f) || Circle(px, py, 0.28f, 0.44f, 0.25f);
                        break;
                    case "factory":
                        mark = Box(px, py, -0.65f, -0.66f, 0.65f, 0.18f) || Box(px, py, -0.40f, 0.05f, -0.17f, 0.65f) || Box(px, py, 0.25f, 0.05f, 0.48f, 0.55f);
                        break;
                    case "bank":
                        mark = Triangle(px, py, -0.75f, 0.23f, 0.75f, 0.23f, 0, 0.70f) || Box(px, py, -0.72f, -0.67f, 0.72f, -0.54f) || (py > -0.51f && py < 0.18f && (Mathf.Abs(px) < 0.11f || Mathf.Abs(px - 0.43f) < 0.11f || Mathf.Abs(px + 0.43f) < 0.11f));
                        break;
                    case "click":
                        mark = Triangle(px, py, -0.19f, -0.75f, 0.49f, 0.14f, 0.02f, 0.14f) || Triangle(px, py, -0.49f, -0.10f, 0.20f, 0.75f, -0.02f, -0.10f);
                        break;
                    case "all":
                        float angle = Mathf.Atan2(py, px);
                        float radius = Mathf.Sqrt(px * px + py * py);
                        mark = radius < 0.35f || radius < 0.48f + 0.15f * Mathf.Cos(angle * 8);
                        break;
                    default:
                        mark = Box(px, py, -0.56f, -0.65f, 0.56f, -0.38f) || Box(px, py, -0.12f, -0.38f, 0.12f, 0.55f) || Triangle(px, py, -0.42f, 0.22f, 0.42f, 0.22f, 0, 0.72f);
                        break;
                }
                bool roundedCorner = Mathf.Abs(px) > 0.81f && Mathf.Abs(py) > 0.81f && new Vector2(Mathf.Abs(px) - 0.81f, Mathf.Abs(py) - 0.81f).sqrMagnitude > 0.03f;
                Color baseColor = Color.Lerp(dark, accent, 0.60f + 0.14f * py);
                texture.SetPixel(x, y, roundedCorner ? Color.clear : mark ? light : baseColor);
            }
            texture.Apply();
            fallbackIcons[key] = texture;
            return texture;
        }

        static bool Box(float x, float y, float left, float bottom, float right, float top) { return x >= left && x <= right && y >= bottom && y <= top; }
        static bool Circle(float x, float y, float cx, float cy, float r) { float dx = x - cx, dy = y - cy; return dx * dx + dy * dy <= r * r; }
        static bool Triangle(float x, float y, float ax, float ay, float bx, float by, float cx, float cy)
        {
            float a = (x - ax) * (by - ay) - (y - ay) * (bx - ax);
            float b = (x - bx) * (cy - by) - (y - by) * (cx - bx);
            float c = (x - cx) * (ay - cy) - (y - cy) * (ax - cx);
            return (a >= 0 && b >= 0 && c >= 0) || (a <= 0 && b <= 0 && c <= 0);
        }

        static Texture2D CookieTexture(bool gold)
        {
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var chips = new[] { new Vector2(-0.43f, 0.35f), new Vector2(0.25f, 0.48f), new Vector2(0.55f, 0.02f), new Vector2(-0.08f, 0.06f), new Vector2(-0.48f, -0.33f), new Vector2(0.16f, -0.42f), new Vector2(0.43f, -0.47f) };
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float px = (x - size / 2f) / (size / 2f), py = (y - size / 2f) / (size / 2f);
                float radius = Mathf.Sqrt(px * px + py * py);
                float edge = 0.87f + 0.025f * Mathf.Sin(x * 0.17f) * Mathf.Sin(y * 0.21f);
                if (radius > edge) { texture.SetPixel(x, y, Color.clear); continue; }
                bool chip = false;
                foreach (var point in chips) if ((new Vector2(px, py) - point).sqrMagnitude < 0.012f) { chip = true; break; }
                Color baseColor = gold ? new Color(1f, 0.77f, 0.18f) : new Color(0.78f, 0.47f, 0.22f);
                if (chip) baseColor = gold ? new Color(1f, 0.96f, 0.62f) : new Color(0.25f, 0.13f, 0.10f);
                float shade = Mathf.Clamp01(1.12f - radius * 0.35f + py * 0.10f);
                texture.SetPixel(x, y, baseColor * shade);
            }
            texture.Apply(); return texture;
        }

        void BindCanvas()
        {
            fallbackCookie = CookieTexture(false);
            fallbackGold = CookieTexture(true);
            ui.cookieButton.onClick.AddListener(() => { if (ready) ClickCookie(); });
            ui.goldenButton.onClick.AddListener(() =>
            {
                if (!ready || !goldVisible) return;
                EnsureMusic();
                goldVisible = false; goldEnd = Time.time + bonusDuration;
                ShowToast("Golden bonus: " + Format(goldBonus) + "x!"); PlaySound("gold");
            });
            ui.stackButton.onClick.AddListener(() => { if (ready && eventQueue.Count > 0) eventOpen = true; });
            ui.closeEvent.onClick.AddListener(() => eventOpen = false);
            ui.optionOne.onClick.AddListener(() => { if (ready) ResolveEvent(1); });
            ui.optionTwo.onClick.AddListener(() => { if (ready) ResolveEvent(2); });
            ui.buyMode.onClick.AddListener(() => sellingBuildings = false);
            ui.sellMode.onClick.AddListener(() => sellingBuildings = true);
            ui.batchOne.onClick.AddListener(() => buildingBatch = 1);
            ui.batchTen.onClick.AddListener(() => buildingBatch = 10);
            ui.batchHundred.onClick.AddListener(() => buildingBatch = 100);
            ui.eventOverlay.SetActive(false);
            ui.toastPanel.SetActive(false);
            ui.goldenButton.gameObject.SetActive(false);
            ui.stackButton.gameObject.SetActive(false);
            ui.buildingTemplate.gameObject.SetActive(false);
            ui.upgradeTemplate.gameObject.SetActive(false);
            ui.loadingPanel.SetActive(true);
        }

        void BuildShopRows()
        {
            foreach (var data in buildings)
            {
                var row = Instantiate(ui.buildingTemplate, ui.buildingContent);
                row.name = "Building - " + data.name;
                if (ui.layoutVersion >= 3) row.UseCompactLayout(18);
                else row.UseTitleOnlyLayout(24);
                row.tooltip = shopTooltip;
                row.tooltipText = string.IsNullOrWhiteSpace(data.tooltip) ? string.Empty : data.name + "\n" + data.tooltip;
                row.action.onClick.AddListener(() => { if (ready) TransactBuilding(data); });
                buildingRows.Add(data.id, row);
            }
            foreach (var data in upgrades)
            {
                var row = Instantiate(ui.upgradeTemplate, ui.upgradeContent);
                row.name = "Upgrade - " + data.name;
                if (ui.layoutVersion >= 3) row.UseCompactLayout(17);
                else row.UseTitleOnlyLayout(22);
                row.tooltip = shopTooltip;
                row.tooltipText = string.IsNullOrWhiteSpace(data.tooltip) ? string.Empty : data.name + "\n" + data.tooltip;
                row.action.onClick.AddListener(() =>
                {
                    if (!ready) return;
                    manager.BuyUpgrade(upgradeAssets[data.id]); PlaySound("buy"); SaveProgress();
                });
                upgradeRows.Add(data.id, row);
            }
            ui.loadingPanel.SetActive(false);
        }

        void RefreshCanvas()
        {
            if (ui == null || !ready) return;
            ui.gameTitle.text = Label("game_title", "Cookie Works");
            ui.gameDescription.text = Label("game_description", "Build your production empire.");
            var balances = new StringBuilder();
            var production = new StringBuilder();
            foreach (var definition in currencyDefinitions)
            {
                var asset = currencyAssets[definition.id];
                if (balances.Length > 0) { balances.Append('\n'); production.Append('\n'); }
                balances.Append(Format(Balance(asset))).Append(' ').Append(asset.name);
                production.Append(Format(PerSecond(asset))).Append(' ').Append(asset.name).Append("/s");
            }
            ui.money.text = balances.ToString();
            ui.production.text = production.Append("  •  ").Append(Format(Multiplier)).Append("x active").ToString();
            ui.eventCount.text = "EVENTS x " + eventQueue.Count;
            ui.stackButtonText.text = "OPEN STACK";
            ui.stackButton.gameObject.SetActive(eventQueue.Count > 0);
            ui.background.texture = Image("background");
            ui.background.enabled = ui.background.texture != null;
            ui.cookie.texture = Image("clickable") ?? fallbackCookie;
            ui.goldenCookie.texture = Image("golden_cookie") ?? fallbackGold;
            ui.goldenButton.gameObject.SetActive(goldVisible);

            int owned = 0;
            foreach (var data in upgrades) if (manager.State.EarnedUpgrades.Contains(upgradeAssets[data.id])) owned++;
            ui.upgradesTabText.text = "UPGRADES  " + owned + "/" + upgrades.Count;
            ui.buildingControls.SetActive(true);
            ui.buildingList.SetActive(true);
            ui.upgradeList.SetActive(true);
            ui.buyMode.interactable = sellingBuildings;
            ui.sellMode.interactable = !sellingBuildings;
            ui.batchOne.interactable = buildingBatch != 1;
            ui.batchTen.interactable = buildingBatch != 10;
            ui.batchHundred.interactable = buildingBatch != 100;
            ui.batchStatus.text = (sellingBuildings ? "Selling " : "Buying ") + buildingBatch + " at a time";

            foreach (var data in buildings)
            {
                var row = buildingRows[data.id];
                int count = BuildingCount(data);
                var asset = buildingAssets[data.id];
                var costCurrency = asset.Cost.Currency;
                bool visible = ShopVisible(manager.BuildingCost(asset).Amount, costCurrency, count > 0, data == buildings[0]);
                row.gameObject.SetActive(visible);
                if (!visible) continue;
                var unlockCurrency = currencyAssets[data.unlockCurrency];
                bool unlocked = Historical(unlockCurrency) >= data.unlockAt;
                float value = BuildingBatchValue(data, count, sellingBuildings);
                row.icon.texture = Image(data.image) ?? Icon(data.id, data.id);
                row.title.text = data.name + "  ×" + count;
                row.tooltipText = string.IsNullOrWhiteSpace(data.tooltip) ? string.Empty : data.name + "\n" + data.tooltip + (unlocked ? "" : "\nUnlock at " + Format(data.unlockAt) + " lifetime " + unlockCurrency.name);
                row.action.interactable = unlocked && (sellingBuildings ? count >= buildingBatch : Balance(costCurrency) >= value);
                row.actionText.text = !unlocked ? "LOCKED" : sellingBuildings && count < buildingBatch ? "NEED " + buildingBatch : (sellingBuildings ? "SELL\n" : "BUY\n") + Format(value) + " " + costCurrency.name;
            }
            foreach (var data in upgrades)
            {
                var row = upgradeRows[data.id];
                bool purchased = manager.State.EarnedUpgrades.Contains(upgradeAssets[data.id]);
                var costCurrency = upgradeAssets[data.id].Cost.Currency;
                bool visible = ShopVisible(data.cost, costCurrency, purchased);
                row.gameObject.SetActive(visible);
                if (!visible) continue;
                var unlockCurrency = currencyAssets[data.unlockCurrency];
                bool unlocked = Historical(unlockCurrency) >= data.unlockAt;
                row.icon.texture = Image(data.image) ?? Icon(data.id, data.targetType);
                row.title.text = data.name;
                row.tooltipText = string.IsNullOrWhiteSpace(data.tooltip) ? string.Empty : data.name + "\n" + data.tooltip + (unlocked ? "" : "\nUnlock at " + Format(data.unlockAt) + " lifetime " + unlockCurrency.name);
                row.action.interactable = !purchased && unlocked && Balance(costCurrency) >= data.cost;
                row.actionText.text = purchased ? "OWNED" : unlocked ? "BUY\n" + Format(data.cost) + " " + costCurrency.name : "LOCKED";
            }

            bool showEvent = eventOpen && eventQueue.Count > 0;
            if (showEvent && shopTooltip != null) shopTooltip.Hide();
            ui.eventOverlay.SetActive(showEvent);
            if (showEvent)
            {
                var current = eventQueue[eventQueue.Count - 1];
                ui.eventTitle.text = current.title;
                ui.eventDescription.text = current.description;
                ui.eventImage.texture = Image(current.image) ?? Icon("event", "all");
                ui.optionOneText.text = current.option1;
                ui.optionTwoText.text = current.option2;
                ui.eventQueueText.text = eventQueue.Count + " card(s) waiting. Close to return to the game.";
            }
            bool showToast = !string.IsNullOrEmpty(toast) && Time.time < toastUntil;
            ui.toastPanel.SetActive(showToast);
            if (showToast) ui.toastText.text = toast;
        }

        float PerSecond(Currency asset)
        {
            float total = 0;
            foreach (var owned in manager.State.EarnedBuildings)
            {
                var building = owned.Key;
                if (building.YieldAmount.Currency != asset) continue;
                total += building.YieldAmount.Amount * owned.Value * Multiplier;
                foreach (var upgrade in manager.State.EarnedUpgrades)
                {
                    foreach (var perk in upgrade.UpgradePerk)
                    {
                        if (perk.TargetBuilding != building) continue;
                        total = perk.Operation == Operation.Add ? total + perk.Amount : total * perk.Amount;
                    }
                }
            }
            foreach (var upgrade in manager.State.EarnedUpgrades)
            {
                foreach (var perk in upgrade.UpgradePerk)
                {
                    if (perk.TargetCurrency != asset) continue;
                    total = perk.Operation == Operation.Add ? total + perk.Amount : total * perk.Amount;
                }
            }
            return total;
        }

    }
}
