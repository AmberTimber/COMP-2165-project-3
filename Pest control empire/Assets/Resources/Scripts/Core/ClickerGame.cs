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
using TMPro;

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
        readonly List<QueuedEvent> eventQueue = new List<QueuedEvent>(); // newest last; the last one is the one on screen
        readonly Dictionary<string, Building> buildingAssets = new Dictionary<string, Building>();
        readonly Dictionary<string, Upgrade> upgradeAssets = new Dictionary<string, Upgrade>();
        readonly Dictionary<string, ClickerShopRow> buildingRows = new Dictionary<string, ClickerShopRow>();
        readonly Dictionary<string, ClickerShopRow> upgradeRows = new Dictionary<string, ClickerShopRow>();
        // consumption: which currency each consuming building burns, and how well fed each building was last tick (0..1)
        readonly Dictionary<string, Currency> consumeAssets = new Dictionary<string, Currency>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<Building, float> satisfaction = new Dictionary<Building, float>();
        // how much of its normal output each building made last tick (see RunProductionTick)
        readonly Dictionary<Building, float> outputFactor = new Dictionary<Building, float>();
        // market (StoreItems sheet): currency exchanges
        readonly List<StoreItemData> storeItems = new List<StoreItemData>();
        readonly Dictionary<string, ClickerMarketRow> marketRows = new Dictionary<string, ClickerMarketRow>(StringComparer.OrdinalIgnoreCase);
        int marketBatch = 1;

        ClickerManager manager;
        ClickerCanvasView ui;
        ClickerTooltip shopTooltip;
        Currency currency;
        Clickable clickable;
        AudioSource music;
        Texture2D fallbackCookie, fallbackGold;
        string spriteSheetPath;
        int spriteTileSize = 48;
        bool ready, goldVisible, sellingBuildings;
        int buildingBatch = 1;
        string error, toast;
        float toastUntil, nextTick, nextEvent, nextGold, nextSave, goldEnd;
        float goldBonus = 7, bonusDuration = 20, eventInterval = 35, goldInterval = 50, wrongPenalty = 0.1f, sellRefund = 0.5f, shopReveal = 0.5f;
        // Settings.csv: reputation bonus per point (as a fraction), its ceiling and floor, and how long a failed event's debuff lasts
        float reputationPerPoint = 0.01f, reputationMaxBonus = 1f, reputationMaxPenalty = 0.99f, eventDebuffSeconds = 20f;
        float reputationMax = 100f, reputationMin = -100f; // Settings.csv reputation_max / reputation_min: reputation can't go beyond these
        // Timed changes to all income: failed-event debuffs, and the buffs and penalties of three-option events
        class TimedEffect { public float end, factor; }
        readonly List<TimedEffect> effects = new List<TimedEffect>();
        bool savingBlocked; // set by ResetProgress so nothing writes the old save back
        // Offline income (Settings.csv offline_income_percent / offline_max_hours): the share of its normal rate the empire keeps
        // earning and consuming while the game isn't running, and the longest absence that counts (0 = no limit)
        float offlinePercent = 50f, offlineMaxHours = 24f;
        [Tooltip("Absences shorter than this many seconds earn nothing (quick restarts).")]
        public float offlineMinimumSeconds = 60f;
        [Tooltip("Currency id (Currencies.csv) reported as 'used' on the welcome-back panel. If it isn't found, the non-main currency the buildings burned most is shown instead.")]
        public string pesticideCurrencyId = "pesticides";
        // custom
        public float overallBonus = 1;
        [Tooltip("Seconds an event stays on screen before it counts as failed. 0 = no time limit.")]
        public float eventTimeLimit = 20f;
        // Settings.csv event_multiplier: a correct event answer pays this percent of the most the buildings could make
        // over bonus_duration_seconds (see EventReward)
        float eventRewardPercent = 100;
        // The golden cookie is called the ladybug on screen only; the code, sheets and sounds still say golden.
        const string GoldenDisplayName = "Ladybug";
        // In the Events sheet's Success Building / Failure Building columns, this word means "one building the player owns, picked at random".
        const string RandomBuilding = "random";
        // The currency (by id) whose balance drives the reputation bonus and is reduced when events fail.
        const string ReputationId = "reputation";
        // Highlight colour for active modifiers; taken from the palette's text_title colour after ClickerPalette.Apply
        Color modifierAccent = new Color(1f, 0.92f, 0.2f);

        void Start()
        {
            ConfigureInputModule();
            ui = FindFirstObjectByType<ClickerCanvasView>();
            if (ui == null) { Debug.LogError("The Clicker scene needs its editable Canvas. Use COMP2165 > Create Editable Canvas."); return; }
            BindCanvas();
            StartCoroutine(LoadGame());
            // custom
            overallBonus = PlayerPrefs.GetFloat("overallBonus", overallBonus);
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
            string[] tables = { "Settings", "Currencies", "Buildings", "Upgrades", "Events", "Visuals", "Audio", "Palette", "StoreItems" };
            var loaded = new Dictionary<string, List<Dictionary<string, string>>>();
            foreach (string table in tables)
            {
                string url = StreamingUrl("Data/" + table + ".csv");
                using (var request = UnityWebRequest.Get(url))
                {
                    yield return request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        if (table == "Palette" || table == "StoreItems") { loaded[table] = new List<Dictionary<string, string>>(); continue; } // optional table
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
            eventRewardPercent = Setting("event_multiplier", 100);
            reputationPerPoint = Setting("reputation_bonus_percent", 1) / 100f;
            reputationMaxBonus = Setting("reputation_bonus_max_percent", 100) / 100f;
            reputationMaxPenalty = Setting("reputation_penalty_max_percent", 99) / 100f;
            eventDebuffSeconds = Setting("event_debuff_interval_seconds", 20);
            offlinePercent = Setting("offline_income_percent", 50);
            offlineMaxHours = Setting("offline_max_hours", 24);
            reputationMax = Setting("reputation_max", 100);
            reputationMin = Setting("reputation_min", -100);
            sellRefund = Setting("sell_refund_percent", 50) / 100f;
            shopReveal = Setting("shop_reveal_percent", 50) / 100f;
            if (goldBonus < 1 || eventRewardPercent < 0 || reputationPerPoint < 0 || reputationMaxBonus < 0 || reputationMaxPenalty < 0 || reputationMaxPenalty > 1 || eventDebuffSeconds < 0 || offlinePercent < 0 || offlineMaxHours < 0 || reputationMin > reputationMax || bonusDuration <= 0 || eventInterval <= 0 || goldInterval <= 0 || wrongPenalty < 0 || wrongPenalty > 1 || sellRefund < 0 || sellRefund > 1 || shopReveal < 0 || shopReveal > 1)
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
                var item = new BuildingData
                {
                    id = Csv.Get(row, "id"),
                    name = Csv.Get(row, "name"),
                    description = Csv.Get(row, "description"),
                    tooltip = Csv.Get(row, "tooltip"),
                    image = Csv.Get(row, "image"),
                    cost = Csv.Number(row, "base_cost"),
                    perSecond = Csv.Number(row, "per_second"),
                    unlockAt = Csv.Number(row, "unlock_at"),
                    costCurrency = Csv.Get(row, "cost_currency", primaryId),
                    yieldCurrency = Csv.Get(row, "yield_currency", primaryId),
                    // consumption: currency id burned by this building, and how much per building per second.
                    // Header is spelled "conesume_currency" in the sheet; "consume_currency" is accepted too.
                    conesume_currency = Csv.Get(row, "conesume_currency", Csv.Get(row, "consume_currency")),
                    consumeable = Csv.Number(row, "consumeable"),
                    // share of normal output still made when none of the consumed currency can be paid for
                    productionWithoutConsumption = Csv.Fraction(row, "productionW/Oconsumption")
                };
                item.unlockCurrency = Csv.Get(row, "unlock_currency", item.costCurrency);
                if (string.IsNullOrEmpty(item.id) || item.cost < 0 || item.perSecond < 0 || item.consumeable < 0 || float.IsInfinity(item.cost) || float.IsNaN(item.cost)) { error = "Invalid Buildings.csv row (is the cost a number below 3 x 10^38?): " + item.id; yield break; }
                if (item.consumeable > 0 && string.IsNullOrEmpty(item.conesume_currency)) { error = "Buildings.csv: '" + item.id + "' has consumeable > 0 but no conesume_currency."; yield break; }
                buildings.Add(item);
            }
            foreach (var row in loaded["Upgrades"])
            {
                var item = new UpgradeData { id = Csv.Get(row, "id"), name = Csv.Get(row, "name"), description = Csv.Get(row, "description"), tooltip = Csv.Get(row, "tooltip"), image = Csv.Get(row, "image"), targetType = Csv.Get(row, "target_type"), targetId = Csv.Get(row, "target_id"), operation = Csv.Get(row, "operation"), cost = Csv.Number(row, "cost"), amount = Csv.Number(row, "amount", 1), unlockAt = Csv.Number(row, "unlock_at"), costCurrency = Csv.Get(row, "cost_currency", primaryId) };
                item.unlockCurrency = Csv.Get(row, "unlock_currency", item.costCurrency);
                if (string.IsNullOrEmpty(item.id) || item.cost < 0 || item.amount <= 0 || float.IsInfinity(item.cost) || float.IsNaN(item.cost)) { error = "Invalid Upgrades.csv row (is the cost a number below 3 x 10^38?): " + item.id; yield break; }
                upgrades.Add(item);
            }
            foreach (var row in loaded["Events"])
            {
                int correctOption = 0;
                string correctText = Csv.Get(row, "Correct Option");
                bool choiceRow = !string.IsNullOrEmpty(Csv.Get(row, "Event Option 3")); // three-option events don't use Correct Option
                if (!(choiceRow && correctText.Length == 0)
                    && (!int.TryParse(correctText, NumberStyles.Integer, CultureInfo.InvariantCulture, out correctOption) || correctOption < 0 || correctOption > 2))
                { error = "Every event needs Correct Option 0 (random), 1, or 2."; yield break; }
                bool lossIsPercent;
                float repLoss = Csv.Amount(row, "Reputation Loss", out lossIsPercent);
                var item = new EventData
                {
                    title = Csv.Get(row, "Event Title"),
                    description = Csv.Get(row, "Event Description"),
                    option1 = Csv.Get(row, "Event Option 1"),
                    option2 = Csv.Get(row, "Event Option 2"),
                    image = Csv.Get(row, "Image"),
                    sound = Csv.Get(row, "Sound"),
                    currencyId = Csv.Get(row, "Currency", primaryId),
                    correctOption = correctOption,
                    // optional: a correct answer gives buildings, a wrong answer or a timeout takes them away (blank count = 1)
                    successBuilding = Csv.Get(row, "Success Building"),
                    successCount = Mathf.RoundToInt(Csv.Number(row, "Success Building Count", 1)),
                    failureBuilding = Csv.Get(row, "Failure Building"),
                    failureCount = Mathf.RoundToInt(Csv.Number(row, "Failure Building Count", 1)),
                    // reputation lost on every failure: points, or a percent of current reputation when the cell ends in %
                    reputationLoss = repLoss,
                    reputationLossIsPercent = lossIsPercent,
                    reputationGain = Mathf.Max(0f, Csv.Number(row, "Reputation gain")), // points for a correct classic answer
                    option3 = Csv.Get(row, "Event Option 3")
                };
                if (item.IsChoice)
                {
                    item.chance = Csv.Fraction(row, "Option 1 Chance %", 0.5f);
                    item.sacrificeId = Csv.Get(row, "Option 2 Cost");
                    item.sacrificeMin = Mathf.Max(0f, Csv.Number(row, "Option 2 Cost Min", 1));
                    item.sacrificeMax = Mathf.Max(item.sacrificeMin, Csv.Number(row, "Option 2 Cost Max", item.sacrificeMin));
                    item.win = ReadOutcome(row, "Win", 1, true);
                    item.lose = ReadOutcome(row, "Lose", -1, false);
                    item.sacrifice = ReadOutcome(row, "Sacrifice", 1, false);
                    item.wrong = ReadOutcome(row, "Wrong", -1, false);
                    if (string.IsNullOrEmpty(item.sacrificeId))
                    { error = "Events.csv: the three-option event '" + item.title + "' needs an Option 2 Cost (a building or currency id)."; yield break; }
                }
                if (item.successCount < 0 || item.failureCount < 0) { error = "Events.csv: building counts can't be negative (event '" + item.title + "')."; yield break; }
                events.Add(item);
            }
            foreach (var row in loaded["StoreItems"])
            {
                var item = new StoreItemData
                {
                    id = Csv.Get(row, "id"),
                    name = Csv.Get(row, "name"),
                    description = Csv.Get(row, "description"),
                    tooltip = Csv.Get(row, "tooltip"),
                    image = Csv.Get(row, "image"),
                    inputCurrency = Csv.Get(row, "InputCurrency", primaryId),
                    outputCurrency = Csv.Get(row, "OutputCurrency"),
                    inputAmount = Csv.Number(row, "inputAmount"),
                    outputAmount = Csv.Number(row, "OutputAmount")
                };
                if (string.IsNullOrEmpty(item.id) && string.IsNullOrEmpty(item.name)) continue; // blank row left in the sheet
                if (string.IsNullOrEmpty(item.id) || string.IsNullOrEmpty(item.outputCurrency) || item.inputAmount <= 0 || item.outputAmount <= 0)
                { error = "Invalid StoreItems.csv row '" + item.id + "': it needs an id, an OutputCurrency, and inputAmount/OutputAmount above 0."; yield break; }
                storeItems.Add(item);
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
            if (loaded["Palette"].Count > 0) ClickerPalette.Apply(ui, loaded["Palette"]);
            if (ui.money != null) modifierAccent = ui.money.color; // money uses the text_title palette colour
            ClampReputation();
            ApplyOfflineProgress(); // pay out what the empire made while the game wasn't running
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
                if (row.consumeable > 0)
                {
                    Currency consumed;
                    if (!currencyAssets.TryGetValue(row.conesume_currency, out consumed))
                    { error = "Unknown consume currency '" + row.conesume_currency + "' for building: " + row.id; return; }
                    consumeAssets[row.id] = consumed;
                }
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
            var storeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in storeItems)
            {
                if (!storeIds.Add(row.id)) { error = "Duplicate store item ID: " + row.id; return; }
                if (!currencyAssets.ContainsKey(row.inputCurrency) || !currencyAssets.ContainsKey(row.outputCurrency))
                { error = "Unknown currency in StoreItems.csv row: " + row.id; return; }
                if (row.inputCurrency.Equals(row.outputCurrency, StringComparison.OrdinalIgnoreCase))
                { error = "Store item " + row.id + " has the same input and output currency."; return; }
            }
            foreach (var row in events) if (!currencyAssets.ContainsKey(row.currencyId)) { error = "Unknown event currency: " + row.currencyId; return; }
            foreach (var row in events)
                if ((row.reputationLoss > 0 || row.reputationGain > 0) && !currencyAssets.ContainsKey(ReputationId))
                { error = "Events.csv uses reputation, but Currencies.csv has no '" + ReputationId + "' currency."; return; }
            foreach (var row in events)
            {
                if (!string.IsNullOrEmpty(row.successBuilding))
                {
                    string known = BuildingIdFor(row.successBuilding);
                    if (known == null) { error = "Events.csv: unknown Success Building '" + row.successBuilding + "' in event '" + row.title + "'."; return; }
                    row.successBuilding = known;
                }
                if (!string.IsNullOrEmpty(row.failureBuilding))
                {
                    string known = BuildingIdFor(row.failureBuilding);
                    if (known == null) { error = "Events.csv: unknown Failure Building '" + row.failureBuilding + "' in event '" + row.title + "'."; return; }
                    row.failureBuilding = known;
                }
            }
            foreach (var row in events)
            {
                if (!row.IsChoice) continue;
                // the sacrificed resource is a building or, failing that, a currency
                string sacrificed = BuildingIdFor(row.sacrificeId);
                if (sacrificed != null && sacrificed != RandomBuilding) { row.sacrificeId = sacrificed; row.sacrificeIsBuilding = true; }
                else
                {
                    string currencyKey = null;
                    foreach (var key in currencyAssets.Keys) if (string.Equals(key, row.sacrificeId, StringComparison.OrdinalIgnoreCase)) currencyKey = key;
                    if (currencyKey == null) { error = "Events.csv: unknown Option 2 Cost '" + row.sacrificeId + "' in event '" + row.title + "' (use a building or currency id)."; return; }
                    row.sacrificeId = currencyKey;
                    row.sacrificeIsBuilding = false;
                }
                foreach (var outcome in new[] { row.win, row.lose, row.sacrifice, row.wrong })
                {
                    if (string.IsNullOrEmpty(outcome.building)) continue;
                    string known = BuildingIdFor(outcome.building);
                    if (known == null) { error = "Events.csv: unknown building '" + outcome.building + "' in event '" + row.title + "'."; return; }
                    outcome.building = known;
                }
            }
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

        // Lets other scripts (e.g. PestHit) grant a currency by its Currencies.csv id. Returns false if the game
        // isn't ready yet or the id is unknown. Gains also count towards the currency's lifetime total.
        public bool AddCurrency(string id, float amount)
        {
            Currency asset;
            if (!ready || amount == 0 || string.IsNullOrEmpty(id) || !currencyAssets.TryGetValue(id, out asset)) return false;
            GrantCurrency(asset, amount);
            return true;
        }

        void GrantCurrency(Currency asset, float amount)
        {
            manager.State.CurrencyCurrentTotals[asset] = AllowedBalance(asset, Balance(asset) + amount);
            if (amount > 0) manager.State.CurrencyHistoricalTotals[asset] = Historical(asset) + amount;
            ClampReputation();
        }

        // Every currency stays at zero or above, except reputation, which may go negative.
        float AllowedBalance(Currency asset, float value)
        {
            Currency reputation;
            return currencyAssets.TryGetValue(ReputationId, out reputation) && reputation == asset ? value : Mathf.Max(0f, value);
        }

        // Reputation never goes above reputation_max or below reputation_min (Settings.csv).
        void ClampReputation()
        {
            Currency asset;
            if (!currencyAssets.TryGetValue(ReputationId, out asset)) return;
            float value = Balance(asset);
            float clamped = Mathf.Clamp(value, reputationMin, reputationMax);
            if (value != clamped) manager.State.CurrencyCurrentTotals[asset] = clamped;
        }

        // How hard one click hits a pest (used by PestHit): the click multiplier from every click upgrade owned, never below 1.
        // Multiply perks multiply it and Add perks add to the click amount, the same way they change what a click earns.
        public int ClickDamage
        {
            get
            {
                if (!ready) return 1;
                float start = currencyDefinitions[0].clickAmount > 0 ? currencyDefinitions[0].clickAmount : 1f;
                float value = start;
                foreach (var upgrade in manager.State.EarnedUpgrades)
                    foreach (var perk in upgrade.UpgradePerk)
                    {
                        if (perk.Type != UpgradeType.Clickable) continue;
                        value = perk.Operation == Operation.Add ? value + perk.Amount : value * perk.Amount;
                    }
                return Mathf.Max(1, Mathf.RoundToInt(value / start));
            }
        }
        // Golden cookie bonus while it lasts, otherwise 1.
        double GoldenFactor { get { return Time.time < goldEnd ? goldBonus : 1; } }
        float Multiplier { get { return (float)Math.Min(GoldenFactor, 1e12); } }

        // Reputation bonus as a share of base income: reputation_bonus_percent (1 = 1%) for every point above 0 and the same
        // loss for every point below 0, never above reputation_bonus_max_percent (100) or below -reputation_penalty_max_percent (99).
        // Reputation is the currency with the id "reputation". Example: 57 points -> +0.57, 250 -> +1.00, -500 -> -0.99.
        float ReputationBonus
        {
            get
            {
                Currency asset;
                if (!currencyAssets.TryGetValue(ReputationId, out asset)) return 0f;
                return Mathf.Clamp(Balance(asset) * reputationPerPoint, -reputationMaxPenalty, reputationMaxBonus);
            }
        }

        // Timed effects multiply all income while they last. Every failed classic event starts a debuff of wrong_penalty_percent
        // for event_debuff_interval_seconds, and three-option events add their own buffs and penalties. They stack by multiplying
        // (two 25% debuffs leave 0.75 x 0.75 = 56% of income; a +100% buff doubles it).
        float EffectFactor
        {
            get
            {
                float factor = 1f;
                for (int i = 0; i < effects.Count; i++)
                    if (Time.time < effects[i].end) factor *= effects[i].factor;
                return factor;
            }
        }

        // What every unit of base income (buildings and clicks) is multiplied by. The ladybug and pests-killed bonuses multiply
        // each other, the reputation bonus is kept separate and added on top, and timed event effects then scale the whole result.
        // Example: ladybug x2, pests +70%, reputation +57% -> 2 x 1.7 + 0.57 = 3.97, or 2.98 while one 25% debuff is active.
        float IncomeMultiplier { get { return (Multiplier * overallBonus + ReputationBonus) * EffectFactor; } }

        void Update()
        {
            if (!ready)
            {
                if (ui != null && ui.loadingText != null) ui.loadingText.text = error ?? "Loading game data...";
                return;
            }
            ExpireEvents(); // events nobody answered in time fail
            for (int i = effects.Count - 1; i >= 0; i--) if (Time.time >= effects[i].end) effects.RemoveAt(i);
            ClampReputation();
            if (Time.time >= nextTick)
            {
                nextTick = Time.time + 1;
                RunProductionTick();
            }
            if (Time.time >= nextEvent && events.Count > 0)
            { var next = events[UnityEngine.Random.Range(0, events.Count)]; eventQueue.Add(QueueEvent(next)); nextEvent = Time.time + eventInterval; PlaySound(string.IsNullOrEmpty(next.sound) ? "event" : next.sound); }
            if (Time.time >= nextGold && !goldVisible)
            { goldVisible = true; } // the cooldown only starts once this ladybug has been clicked
            if (Time.time >= nextSave) { SaveProgress(); nextSave = Time.time + 15; }
            RefreshCanvas();
            RefreshModifiers();
        }

        // One production tick. seconds = how much full-rate activity it stands for (1 for a live tick; offline progress
        // uses bigger steps). consumedLog, when given, collects how much of each currency the buildings burned.
        // Buildings with consumeable > 0 pay for their input first (served in sheet order, so earlier rows win when
        // several buildings share a currency). A building that can only partly pay runs at that fraction of its
        // output and burns only what it could pay for; one that can't pay at all is idle for the tick.
        void RunProductionTick(float seconds = 1f, Dictionary<Currency, float> consumedLog = null)
        {
            float boost = IncomeMultiplier * seconds;
            var originals = new float[buildings.Count];
            var stock = new Dictionary<Currency, float>(); // working balances of the currencies being consumed
            for (int i = 0; i < buildings.Count; i++)
            {
                var row = buildings[i];
                var asset = buildingAssets[row.id];
                originals[i] = asset.YieldAmount.Amount;
                float fed = 1;
                int count = BuildingCount(row);
                Currency input;
                if (count > 0 && row.consumeable > 0 && consumeAssets.TryGetValue(row.id, out input))
                {
                    float available;
                    if (!stock.TryGetValue(input, out available)) available = Balance(input);
                    float demand = row.consumeable * count * seconds;
                    fed = Mathf.Clamp01(available / demand);
                    stock[input] = available - demand * fed;
                    if (consumedLog != null)
                    {
                        float burned;
                        consumedLog.TryGetValue(input, out burned);
                        consumedLog[input] = burned + demand * fed;
                    }
                }
                // fed = share of its input it could pay for (this is what it burns). Output runs from the sheet's
                // productionW/Oconsumption share when it paid for nothing, up to 100% when fully fed.
                float factor = row.productionWithoutConsumption + (1f - row.productionWithoutConsumption) * fed;
                satisfaction[asset] = fed;
                outputFactor[asset] = factor;
                var value = asset.YieldAmount;
                value.Amount = originals[i] * boost * factor;
                asset.YieldAmount = value;
            }
            foreach (var pair in stock) manager.State.CurrencyCurrentTotals[pair.Key] = Mathf.Max(0, pair.Value);
            manager.Tick();
            for (int i = 0; i < buildings.Count; i++)
            { var asset = buildingAssets[buildings[i].id]; var value = asset.YieldAmount; value.Amount = originals[i]; asset.YieldAmount = value; }
        }

        float Satisfaction(Building asset) { float value; return satisfaction.TryGetValue(asset, out value) ? value : 1; }
        float OutputFactor(Building asset) { float value; return outputFactor.TryGetValue(asset, out value) ? value : 1; }

        // Total amount of a currency burned per second by all owned buildings (as fed during the last tick).
        float ConsumptionPerSecond(Currency asset)
        {
            float total = 0;
            foreach (var row in buildings)
            {
                Currency input;
                if (row.consumeable <= 0 || !consumeAssets.TryGetValue(row.id, out input) || input != asset) continue;
                total += row.consumeable * BuildingCount(row) * Satisfaction(buildingAssets[row.id]);
            }
            return total;
        }

        // What owned buildings would burn per second of a currency if every one of them were fully fed.
        // Unlike ConsumptionPerSecond this doesn't shrink when buildings are starved, so it is the figure to buy against.
        float ConsumptionDemandPerSecond(Currency asset)
        {
            float total = 0;
            foreach (var row in buildings)
            {
                Currency input;
                if (row.consumeable <= 0 || !consumeAssets.TryGetValue(row.id, out input) || input != asset) continue;
                total += row.consumeable * BuildingCount(row);
            }
            return total;
        }

        // What ONE building of this kind makes per second with the building upgrades the player owns, fully fed.
        // (The ladybug, pests-killed, reputation and debuff multipliers apply on top of this and aren't included.)
        float EarnPerSecond(Building building)
        {
            float value = building.YieldAmount.Amount;
            foreach (var upgrade in manager.State.EarnedUpgrades)
                foreach (var perk in upgrade.UpgradePerk)
                {
                    if (perk.TargetBuilding != building) continue;
                    value = perk.Operation == Operation.Add ? value + perk.Amount : value * perk.Amount;
                }
            return value;
        }

        // e.g. "1.5 Money/s each"
        string EarnText(Building building)
        {
            return Format(EarnPerSecond(building)) + " " + building.YieldAmount.Currency.name + "/s each";
        }

        string BuildingTooltip(BuildingData data, bool unlocked, Currency unlockCurrency, bool starved)
        {
            var text = new StringBuilder(data.name);
            if (!string.IsNullOrWhiteSpace(data.tooltip)) text.Append('\n').Append(data.tooltip);
            text.Append("\nEarns ").Append(EarnText(buildingAssets[data.id]));
            Currency input;
            if (data.consumeable > 0 && consumeAssets.TryGetValue(data.id, out input))
            {
                text.Append("\nConsumes ").Append(Format(data.consumeable)).Append(' ').Append(input.name).Append("/s each");
                int floorPercent = Mathf.RoundToInt(data.productionWithoutConsumption * 100f);
                text.Append(floorPercent > 0 ? "\nWithout it: " + floorPercent + "% output" : "\nStops without it");
                if (starved) text.Append("\nNot enough ").Append(input.name).Append(" - producing ").Append(Mathf.RoundToInt(OutputFactor(buildingAssets[data.id]) * 100)).Append("%");
            }
            if (!unlocked) text.Append("\nUnlock at ").Append(Format(data.unlockAt)).Append(" lifetime ").Append(unlockCurrency.name);
            return text.ToString();
        }

        void ClickCookie()
        {
            EnsureMusic();
            foreach (var row in currencyDefinitions)
            {
                if (row.clickAmount <= 0) continue;
                var source = clickAssets[row.id];
                source.Amount = row.clickAmount * IncomeMultiplier; // bonus for killing new tier of pests
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

        // Price of the next single building, from the game's own double-precision numbers (huge costs included).
        float BuildingPrice(BuildingData row, int owned)
        {
            return (float)Math.Min((double)row.cost * Math.Pow(1 + manager.Config.BuildingCostIncrease, owned), float.MaxValue);
        }

        float BuildingBatchValue(BuildingData row, int count, bool selling)
        {
            if (selling && count < buildingBatch) return float.PositiveInfinity;
            int start = selling ? count - buildingBatch : count;
            double total = 0;
            double growth = 1 + manager.Config.BuildingCostIncrease;
            for (int i = 0; i < buildingBatch; i++) total += (double)row.cost * Math.Pow(growth, start + i);
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
                manager.State.CurrencyCurrentTotals[costCurrency] = Mathf.Max(0f, Balance(costCurrency) - cost);
                manager.State.EarnedBuildings[asset] = count + buildingBatch;
                ShowToast("Bought " + buildingBatch + " " + row.name + " for " + Format(cost) + " " + costCurrency.name + ".");
            }
            PlaySound("buy");
            SaveProgress();
        }

        // Market batch: the three R buttons drive it when they are all wired in the canvas;
        // otherwise the market follows the shop's own x1 / x10 / x100 batch.
        bool HasMarketBatchButtons { get { return ui != null && ui.buyOneR != null && ui.buyTenR != null && ui.butHundredR != null; } }
        int MarketBatch { get { return HasMarketBatchButtons ? marketBatch : buildingBatch; } }

        // How many purchases one click makes. Normally that is the market batch (x1 / x10 / x100).
        // When buildings consume the item's output currency, the batch instead means "seconds of consumption to cover":
        // the count is what those buildings would use in that many seconds, rounded up to whole purchases.
        // Either way it is capped at what the player can afford, and never below 1 so the row always shows a price.
        float MarketCount(StoreItemData row, Currency input, Currency output)
        {
            float need = ConsumptionDemandPerSecond(output);
            float wanted = need > 0 ? Mathf.Ceil(need * MarketBatch / row.outputAmount) : MarketBatch;
            wanted = Mathf.Clamp(wanted, 1f, 1e9f);
            float affordable = Mathf.Floor(Balance(input) / row.inputAmount);
            return Mathf.Max(1f, Mathf.Min(wanted, affordable));
        }

        // Pay inputAmount x count of the input currency, receive OutputAmount x count of the output currency.
        void TransactStoreItem(StoreItemData row)
        {
            var input = currencyAssets[row.inputCurrency];
            var output = currencyAssets[row.outputCurrency];
            float count = MarketCount(row, input, output);
            float cost = row.inputAmount * count;
            float gain = row.outputAmount * count;
            if (Balance(input) < cost) return;
            manager.State.CurrencyCurrentTotals[input] = Balance(input) - cost;
            manager.State.CurrencyCurrentTotals[output] = Balance(output) + gain;
            manager.State.CurrencyHistoricalTotals[output] = Historical(output) + gain;
            ShowToast("Bought " + Format(gain) + " " + output.name + " for " + Format(cost) + " " + input.name + ".");
            PlaySound("buy");
            SaveProgress();
        }

        string MarketTooltip(StoreItemData data, Currency input, Currency output, float count)
        {
            float cost = data.inputAmount * count;
            float gain = data.outputAmount * count;
            var text = new StringBuilder(data.name);
            if (!string.IsNullOrWhiteSpace(data.tooltip)) text.Append('\n').Append(data.tooltip);
            text.Append("\nRate: ").Append(Format(data.inputAmount)).Append(' ').Append(input.name)
                .Append(" for ").Append(Format(data.outputAmount)).Append(' ').Append(output.name);
            text.Append("\nThis buy: ").Append(Format(cost)).Append(' ').Append(input.name)
                .Append(" for ").Append(Format(gain)).Append(' ').Append(output.name);
            return text.ToString();
        }

        // An event waiting for an answer. correct is 1 or 2 (the sheet's Correct Option, or random when that is 0).
        // flipped swaps which button shows which option. deadline is the moment it counts as failed.
        class QueuedEvent
        {
            public EventData data;
            public int correct;
            public bool flipped;
            public int[] order; // choice events: order[button] = the answer that button shows (0 gamble, 1 sacrifice, 2 wrong)
            public float deadline;
        }

        QueuedEvent QueueEvent(EventData source)
        {
            var queued = new QueuedEvent
            {
                data = source,
                correct = source.correctOption != 0 ? source.correctOption : UnityEngine.Random.Range(1, 3),
                flipped = UnityEngine.Random.value < 0.5f, // the answers land on random sides
                deadline = eventTimeLimit > 0 ? Time.time + eventTimeLimit : float.PositiveInfinity
            };
            if (source.IsChoice)
            {
                queued.order = new[] { 0, 1, 2 };
                // without a third button in the scene, only the first two answers can be shuffled and the wrong answer stays last
                int shuffled = ui != null && ui.optionThree != null ? 3 : 2;
                for (int i = shuffled - 1; i > 0; i--)
                {
                    int j = UnityEngine.Random.Range(0, i + 1);
                    int swap = queued.order[i];
                    queued.order[i] = queued.order[j];
                    queued.order[j] = swap;
                }
            }
            return queued;
        }

        // Seconds left to answer, or -1 when events have no time limit.
        float EventTimeLeft(QueuedEvent queued)
        {
            return float.IsPositiveInfinity(queued.deadline) ? -1f : Mathf.Max(0f, queued.deadline - Time.time);
        }

        // An event nobody answered in time fails, exactly like a wrong answer.
        void ExpireEvents()
        {
            bool expired = false;
            for (int i = eventQueue.Count - 1; i >= 0; i--)
            {
                if (Time.time < eventQueue[i].deadline) continue;
                var late = eventQueue[i];
                eventQueue.RemoveAt(i);
                if (late.data.IsChoice) ApplyOutcome(late.data, late.data.wrong, "Out of time!", false); // doing nothing is the wrong answer
                else FinishEvent(late.data, false, "Out of time!");
                expired = true;
            }
            if (!expired) return;
            SaveProgress();
        }

        // side is the button pressed: 1 = left, 2 = right. A flipped event shows its options the other way round.
        void ResolveEvent(int side)
        {
            if (eventQueue.Count == 0) return;
            var current = eventQueue[eventQueue.Count - 1];
            if (current.data.IsChoice) { ResolveChoice(current, side); return; }
            if (side > 2) return; // classic events only have two answers
            int choice = current.flipped ? 3 - side : side;
            eventQueue.RemoveAt(eventQueue.Count - 1);
            FinishEvent(current.data, choice == current.correct, choice == current.correct ? "Good call!" : "Wrong call.");
            SaveProgress();
        }

        // Applies an event's result and says what happened in the toast.
        //   success: a currency reward (see EventReward), plus any Success Building from the sheet
        //   failure: the wrong_penalty_percent loss of the event's currency, plus any Failure Building from the sheet
        void FinishEvent(EventData data, bool success, string lead)
        {
            var eventCurrency = currencyAssets[data.currencyId];
            var message = new StringBuilder(lead).Append(' ');
            if (success)
            {
                float reward = EventReward(eventCurrency);
                var paidIn = eventCurrency;
                if (reward <= 0f && eventCurrency != currency)
                {
                    // none of that resource is being produced, so the reward would be 0: pay in the main currency (cash) instead
                    paidIn = currency;
                    reward = EventReward(currency);
                }
                if (reward > 0f) GrantCurrency(paidIn, reward);
                message.Append('+').Append(Format(reward)).Append(' ').Append(paidIn.name);
                string giveTo = EventBuilding(data.successBuilding);
                int gained = ChangeBuildings(giveTo, data.successCount);
                if (gained > 0) message.Append(", +").Append(gained).Append(' ').Append(BuildingName(giveTo));
                Currency gainedReputation;
                if (data.reputationGain > 0f && currencyAssets.TryGetValue(ReputationId, out gainedReputation))
                {
                    manager.State.CurrencyCurrentTotals[gainedReputation] = Balance(gainedReputation) + data.reputationGain;
                    message.Append(", +").Append(Format(data.reputationGain)).Append(' ').Append(gainedReputation.name);
                }
            }
            else
            {
                float balance = Balance(eventCurrency);
                float loss = Mathf.Max(0f, Mathf.Min(balance, balance * wrongPenalty)); // nothing is lost from a balance of zero or less
                manager.State.CurrencyCurrentTotals[eventCurrency] = balance - loss;
                message.Append("Lost ").Append(Format(loss)).Append(' ').Append(eventCurrency.name);
                Currency reputation;
                if (currencyAssets.TryGetValue(ReputationId, out reputation))
                {
                    float reputationLost = ReputationLoss(data, reputation);
                    if (reputationLost > 0f)
                    {
                        manager.State.CurrencyCurrentTotals[reputation] = Balance(reputation) - reputationLost; // may go below zero
                        message.Append(", -").Append(Format(reputationLost)).Append(' ').Append(reputation.name);
                    }
                }
                string takeFrom = EventBuilding(data.failureBuilding);
                int lost = ChangeBuildings(takeFrom, -data.failureCount);
                if (lost > 0) message.Append(", -").Append(lost).Append(' ').Append(BuildingName(takeFrom));
                if (eventDebuffSeconds > 0f && wrongPenalty > 0f)
                {
                    effects.Add(new TimedEffect { end = Time.time + eventDebuffSeconds, factor = Mathf.Max(0f, 1f - wrongPenalty) }); // a new debuff for every failure
                    message.Append(", income -").Append(Mathf.RoundToInt(wrongPenalty * 100f)).Append("% for ").Append(Mathf.RoundToInt(eventDebuffSeconds)).Append('s');
                }
            }
            message.Append('.');
            ClampReputation();
            ShowToast(message.ToString());
            PlaySound(success ? "success" : "failure");
        }

        // Reads one outcome (Win / Lose / Sacrifice / Wrong) of a three-option event from its six columns.
        // direction is the way a blank building count goes: +1 gives one building, -1 takes one away.
        static EventOutcome ReadOutcome(Dictionary<string, string> row, string prefix, int direction, bool blankCashIsDefault)
        {
            return new EventOutcome
            {
                cashPercent = Csv.Signed(row, prefix + " Cash %"),
                cashIsDefault = blankCashIsDefault && Csv.Get(row, prefix + " Cash %").Length == 0, // a blank Win cash = Settings event_multiplier
                reputation = Csv.Signed(row, prefix + " Reputation"),
                building = Csv.Get(row, prefix + " Building"),
                buildingChange = Mathf.RoundToInt(Csv.Signed(row, prefix + " Building Count", direction)),
                incomePercent = Csv.Signed(row, prefix + " Income %"),
                incomeSeconds = Csv.Number(row, prefix + " Income Seconds")
            };
        }

        // Three-option events. side is the button pressed (1..3) and order says which answer that button shows:
        //   0: the gamble        succeeds with the sheet's chance (Win outcome), otherwise the Lose outcome
        //   1: the sacrifice     pays its cost (a building or currency) and gets the Sacrifice outcome
        //   2: the wrong answer  only the Wrong outcome. Running out of time counts as this answer too.
        void ResolveChoice(QueuedEvent queued, int side)
        {
            var data = queued.data;
            int answer = queued.order[Mathf.Clamp(side, 1, 3) - 1];
            if (answer == 1 && !CanSacrifice(data)) return; // can't pay: the click does nothing and the event stays up
            eventQueue.Remove(queued);
            if (answer == 0)
            {
                bool won = UnityEngine.Random.value < data.chance;
                ApplyOutcome(data, won ? data.win : data.lose, won ? "Success!" : "It failed.", won);
            }
            else if (answer == 1)
            {
                string paid = PaySacrifice(data);
                ApplyOutcome(data, data.sacrifice, "Sacrificed " + paid + ".", data.sacrifice.incomePercent > 0f);
            }
            else ApplyOutcome(data, data.wrong, "Wrong call.", false);
            SaveProgress();
        }

        // Applies one outcome: a cash bonus or loss, reputation, buildings given or taken, and a timed change to all income.
        // Only what the sheet fills in happens. The toast lists the changes.
        void ApplyOutcome(EventData data, EventOutcome o, string lead, bool good)
        {
            var eventCurrency = currencyAssets[data.currencyId];
            var parts = new List<string>();

            float cashPercent = o.cashIsDefault ? eventRewardPercent : o.cashPercent;
            if (cashPercent > 0f)
            {
                var paidIn = eventCurrency;
                float reward = cashPercent / 100f * MaxPerSecond(paidIn) * bonusDuration;
                if (reward <= 0f && eventCurrency != currency)
                {
                    paidIn = currency; // none of that resource is being produced: pay the main currency instead
                    reward = cashPercent / 100f * MaxPerSecond(paidIn) * bonusDuration;
                }
                if (reward > 0f) { GrantCurrency(paidIn, reward); parts.Add("+" + Format(reward) + " " + paidIn.name); }
            }
            else if (cashPercent < 0f)
            {
                float balance = Balance(eventCurrency);
                float loss = Mathf.Max(0f, Mathf.Min(balance, balance * -cashPercent / 100f));
                if (loss > 0f)
                {
                    manager.State.CurrencyCurrentTotals[eventCurrency] = balance - loss;
                    parts.Add("-" + Format(loss) + " " + eventCurrency.name);
                }
            }

            Currency reputation;
            if (o.reputation != 0f && currencyAssets.TryGetValue(ReputationId, out reputation))
            {
                manager.State.CurrencyCurrentTotals[reputation] = Balance(reputation) + o.reputation;
                ClampReputation();
                parts.Add((o.reputation > 0f ? "+" : "-") + Format(Mathf.Abs(o.reputation)) + " " + reputation.name);
            }

            if (!string.IsNullOrEmpty(o.building) && o.buildingChange != 0)
            {
                string target = EventBuilding(o.building);
                int changed = ChangeBuildings(target, o.buildingChange);
                if (changed > 0) parts.Add((o.buildingChange > 0 ? "+" : "-") + changed + " " + BuildingName(target));
            }

            if (o.incomePercent != 0f)
            {
                float seconds = o.incomeSeconds > 0f ? o.incomeSeconds : eventDebuffSeconds;
                if (seconds > 0f)
                {
                    effects.Add(new TimedEffect { end = Time.time + seconds, factor = Mathf.Max(0f, 1f + o.incomePercent / 100f) });
                    parts.Add("income " + (o.incomePercent > 0f ? "+" : "-") + Mathf.RoundToInt(Mathf.Abs(o.incomePercent)) + "% for " + Mathf.RoundToInt(seconds) + "s");
                }
            }

            ShowToast(parts.Count > 0 ? lead + " " + string.Join(", ", parts.ToArray()) + "." : lead);
            PlaySound(good ? "success" : "failure");
        }

        int OwnedCount(string id)
        {
            Building asset;
            int count = 0;
            if (buildingAssets.TryGetValue(id, out asset)) manager.State.EarnedBuildings.TryGetValue(asset, out count);
            return count;
        }

        // The sacrifice option is only usable while the player owns at least the minimum of what it costs.
        bool CanSacrifice(EventData data)
        {
            if (data.sacrificeIsBuilding) return OwnedCount(data.sacrificeId) >= Mathf.CeilToInt(data.sacrificeMin);
            Currency asset;
            return currencyAssets.TryGetValue(data.sacrificeId, out asset) && Balance(asset) >= data.sacrificeMin;
        }

        // Takes the sacrifice: a whole number of buildings between min and max, or an amount of a currency between min and max
        // (never more than the player has). Returns what was paid, for the toast.
        string PaySacrifice(EventData data)
        {
            if (data.sacrificeIsBuilding)
            {
                int roll = UnityEngine.Random.Range(Mathf.CeilToInt(data.sacrificeMin), Mathf.FloorToInt(data.sacrificeMax) + 1);
                int taken = Mathf.Min(roll, OwnedCount(data.sacrificeId));
                ChangeBuildings(data.sacrificeId, -taken);
                return taken + " " + BuildingName(data.sacrificeId);
            }
            var asset = currencyAssets[data.sacrificeId];
            float amount = Mathf.Min(UnityEngine.Random.Range(data.sacrificeMin, data.sacrificeMax), Mathf.Max(0f, Balance(asset)));
            manager.State.CurrencyCurrentTotals[asset] = Balance(asset) - amount;
            ClampReputation();
            return Format(amount) + " " + asset.name;
        }

        // "3-5 Mercenary" or "4 Mercenary"
        string SacrificeText(EventData data)
        {
            string amount = data.sacrificeMax > data.sacrificeMin ? Format(data.sacrificeMin) + "-" + Format(data.sacrificeMax) : Format(data.sacrificeMin);
            Currency asset;
            string name = data.sacrificeIsBuilding ? BuildingName(data.sacrificeId) : (currencyAssets.TryGetValue(data.sacrificeId, out asset) ? asset.name : data.sacrificeId);
            return amount + " " + name;
        }

        // Button text for answer 0 / 1 / 2 of a choice event, with the chance or the cost spelled out from the sheet's numbers.
        string ChoiceLabel(EventData data, int answer)
        {
            if (answer == 0) return data.option1 + "\n(" + Mathf.RoundToInt(data.chance * 100f) + "% chance)";
            if (answer == 1) return data.option2 + "\n(costs " + SacrificeText(data) + ")";
            return data.option3;
        }

        bool ChoiceUsable(EventData data, int answer) { return answer != 1 || CanSacrifice(data); }

        void SetOption(Button button, Text label, string text, bool usable)
        {
            if (button == null) return;
            button.gameObject.SetActive(true);
            button.interactable = usable;
            if (label != null) label.text = text;
        }

        // Reputation an event takes when it fails: a fixed number of points, or a percent of the current reputation
        // (a percent never takes anything from zero or less).
        float ReputationLoss(EventData data, Currency reputation)
        {
            if (data.reputationLoss <= 0f) return 0f;
            return data.reputationLossIsPercent ? Mathf.Max(0f, Balance(reputation)) * data.reputationLoss / 100f : data.reputationLoss;
        }

        // What a correct answer pays in a currency: event_multiplier (a percent) of the most the buildings could make
        // over bonus_duration_seconds. "Most" means every building at full output (as if fully fed), bonuses included.
        float EventReward(Currency asset) { return eventRewardPercent / 100f * MaxPerSecond(asset) * bonusDuration; }

        // The building an event acts on: the sheet's building id, or for "random" one of the building kinds the player
        // currently owns (each owned kind equally likely, however many of it they have). Null when they own nothing.
        string EventBuilding(string setting)
        {
            if (setting != RandomBuilding) return setting;
            var owned = new List<string>();
            foreach (var row in buildings) if (BuildingCount(row) > 0) owned.Add(row.id);
            return owned.Count > 0 ? owned[UnityEngine.Random.Range(0, owned.Count)] : null;
        }

        // Gives (positive) or takes (negative) owned buildings without charging or refunding anything.
        // Returns how many were actually added or removed; removal stops at none owned.
        int ChangeBuildings(string id, int change)
        {
            Building asset;
            if (string.IsNullOrEmpty(id) || change == 0 || !buildingAssets.TryGetValue(id, out asset)) return 0;
            int owned;
            manager.State.EarnedBuildings.TryGetValue(asset, out owned);
            int updated = Mathf.Max(0, owned + change);
            manager.State.EarnedBuildings[asset] = updated;
            return Mathf.Abs(updated - owned);
        }

        // The building's id as written in Buildings.csv (matching ignores case), the keyword "random", or null if there is no such building.
        string BuildingIdFor(string id)
        {
            if (string.Equals(id, RandomBuilding, StringComparison.OrdinalIgnoreCase)) return RandomBuilding;
            foreach (var row in buildings) if (string.Equals(row.id, id, StringComparison.OrdinalIgnoreCase)) return row.id;
            return null;
        }

        string BuildingName(string id)
        {
            foreach (var row in buildings) if (row.id == id) return row.name;
            return id;
        }

        void ShowToast(string message) { toast = message; toastUntil = Time.time + 4; }
        void EnsureMusic() { if (music != null && music.clip != null && !music.isPlaying) music.Play(); }
        static readonly string[] NumberSuffixes = { "", "K", "M", "B", "T", "Qa", "Qi", "Sx", "Sp", "Oc", "No", "Dc" };

        static string Format(float value)
        {
            if (float.IsNaN(value)) return "0";
            if (float.IsInfinity(value)) return value > 0f ? "MAX" : "0";
            double scaled = value;
            int tier = 0;
            while (scaled >= 1000.0 && tier < NumberSuffixes.Length - 1) { scaled /= 1000.0; tier++; }
            return scaled.ToString(tier == 0 ? "0.#" : "0.##", CultureInfo.InvariantCulture) + NumberSuffixes[tier];
        }

        // Wipes the saved game so the next load starts from zero (used by the pause menu's Restart button).
        // Saving is switched off first, so the autosave or the quit save can't write the old progress back.
        public void ResetProgress()
        {
            savingBlocked = true;
            PlayerPrefs.DeleteKey("COMP2165_" + Label("game_id", "clicker"));
            PlayerPrefs.DeleteKey("overallBonus");
            PlayerPrefs.DeleteKey(LastSeenKey);
            PlayerPrefs.Save();
        }

        void SaveProgress()
        {
            if (manager == null || savingBlocked) return;
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
            PlayerPrefs.SetFloat("overallBonus", overallBonus);
            PlayerPrefs.SetString(LastSeenKey, DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)); // when the player was last here
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
                        manager.State.CurrencyCurrentTotals[asset] = AllowedBalance(asset, item.amount);
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

        // ---- Offline progress -----------------------------------------------------------------------------------
        // While the game isn't running, the empire keeps working at offline_income_percent of its normal rate: buildings earn
        // and burn their consumed currencies by the same rules as a live tick (including running short of pesticides), for up
        // to offline_max_hours. It runs on start-up and when the app comes back from being paused, then reports what happened.
        string LastSeenKey { get { return "COMP2165_lastSeen_" + Label("game_id", "clicker"); } }

        void ApplyOfflineProgress()
        {
            if (offlinePercent <= 0f || savingBlocked) return;
            long ticks;
            if (!long.TryParse(PlayerPrefs.GetString(LastSeenKey, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks)
                || ticks <= 0 || ticks > DateTime.UtcNow.Ticks) return;   // never seen before, or the clock went backwards
            double away = (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds;
            if (away < offlineMinimumSeconds) return;
            double counted = offlineMaxHours > 0f ? Math.Min(away, offlineMaxHours * 3600.0) : away;

            int steps = Mathf.Clamp(Mathf.CeilToInt((float)counted / 60f), 1, 600);
            float chunk = (float)counted / steps * (offlinePercent / 100f); // seconds of full-rate work per step
            float earnedBefore = Historical(currency);
            var burnedLog = new Dictionary<Currency, float>();
            for (int i = 0; i < steps; i++) RunProductionTick(chunk, burnedLog);
            float earned = Historical(currency) - earnedBefore;

            Currency pesticide;
            if (!currencyAssets.TryGetValue(pesticideCurrencyId, out pesticide))
            {
                // id renamed or mistyped: report whichever non-main currency the buildings burned most
                float most = 0f;
                foreach (var pair in burnedLog) if (pair.Key != currency && pair.Value > most) { most = pair.Value; pesticide = pair.Key; }
            }
            float pesticides = 0f;
            string pesticideName = "Pesticides";
            if (pesticide != null)
            {
                burnedLog.TryGetValue(pesticide, out pesticides);
                pesticideName = pesticide.name;
            }
            ShowOfflinePanel(away, counted, earned, pesticides, pesticideName);
            SaveProgress(); // also stamps "now" as the last-seen time, so the same absence can't pay out twice
        }

        void ShowOfflinePanel(double away, double counted, float earned, float pesticides, string pesticideName)
        {
            string time = "You were away for " + FormatDuration(away) + (counted < away - 1 ? " (counted up to " + FormatDuration(counted) + ")" : "");
            string money = "Earned while away: " + Format(earned) + " " + currency.name;
            string used = pesticideName + " used: " + Format(pesticides);
            if (ui.offlinePanel == null) { ShowToast("Welcome back! " + money + ". " + used + "."); return; }
            if (ui.offlineTimeText != null) ui.offlineTimeText.text = time;
            if (ui.offlineMoneyText != null) ui.offlineMoneyText.text = money;
            if (ui.offlinePesticideText != null) ui.offlinePesticideText.text = used;
            ui.offlinePanel.SetActive(true);
        }

        static string FormatDuration(double seconds)
        {
            int total = (int)Math.Min(seconds, int.MaxValue / 2.0);
            int h = total / 3600, m = total % 3600 / 60, s = total % 60;
            return h > 0 ? h + "h " + m + "m" : m > 0 ? m + "m " + s + "s" : s + "s";
        }

        void OnApplicationPause(bool paused)
        {
            if (!ready) return;
            if (paused) SaveProgress();         // remembers when the player left
            else ApplyOfflineProgress();        // coming back: pay out what the empire made meanwhile
        }
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
                nextGold = Time.time + goldInterval; // the next ladybug comes one interval after this click
                ShowToast(GoldenDisplayName + " bonus: " + Format(goldBonus) + "x!"); PlaySound("gold");
            });
            // Events can't be closed and there is no stack button or count text any more. Their fields stay on
            // ClickerCanvasView, but the objects are switched off here (null-safe, so deleting them in the scene is fine too).
            if (ui.closeEvent != null) { ui.closeEvent.interactable = false; ui.closeEvent.gameObject.SetActive(false); }
            if (ui.stackButton != null) ui.stackButton.gameObject.SetActive(false);
            if (ui.stackButtonText != null) ui.stackButtonText.gameObject.SetActive(false);
            if (ui.eventCount != null) ui.eventCount.gameObject.SetActive(false);
            // welcome-back (offline income) panel: hidden until the player comes back from being away
            if (ui.offlinePanel != null) ui.offlinePanel.SetActive(false);
            if (ui.offlineCloseButton != null) ui.offlineCloseButton.onClick.AddListener(() => { if (ui.offlinePanel != null) ui.offlinePanel.SetActive(false); });
            ui.optionOne.onClick.AddListener(() => { if (ready) ResolveEvent(1); });
            ui.optionTwo.onClick.AddListener(() => { if (ready) ResolveEvent(2); });
            if (ui.optionThree != null) ui.optionThree.onClick.AddListener(() => { if (ready) ResolveEvent(3); });
            else Debug.LogWarning("ClickerCanvasView.optionThree isn't assigned: three-option events will only offer their first two answers.");
            ui.buyMode.onClick.AddListener(() => sellingBuildings = false);
            ui.sellMode.onClick.AddListener(() => sellingBuildings = true);
            ui.batchOne.onClick.AddListener(() => buildingBatch = 1);
            ui.batchTen.onClick.AddListener(() => buildingBatch = 10);
            ui.batchHundred.onClick.AddListener(() => buildingBatch = 100);
            ui.eventOverlay.SetActive(false);
            ui.toastPanel.SetActive(false);
            ui.goldenButton.gameObject.SetActive(false);
            ui.buildingTemplate.gameObject.SetActive(false);
            ui.upgradeTemplate.gameObject.SetActive(false);
            // market: the R buttons set the market's own batch; the template row is hidden like the shop templates
            if (ui.buyOneR != null) ui.buyOneR.onClick.AddListener(() => marketBatch = 1);
            if (ui.buyTenR != null) ui.buyTenR.onClick.AddListener(() => marketBatch = 10);
            if (ui.butHundredR != null) ui.butHundredR.onClick.AddListener(() => marketBatch = 100);
            if (ui.buildingMarket != null) ui.buildingMarket.gameObject.SetActive(false);
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
                    var upgradeAsset = upgradeAssets[data.id];
                    var upgradeCurrency = upgradeAsset.Cost.Currency;
                    if (manager.State.EarnedUpgrades.Contains(upgradeAsset) || Balance(upgradeCurrency) < data.cost) return;
                    manager.State.CurrencyCurrentTotals[upgradeCurrency] = Mathf.Max(0f, Balance(upgradeCurrency) - data.cost);
                    manager.State.EarnedUpgrades.Add(upgradeAsset);
                    PlaySound("buy"); SaveProgress();
                });
                upgradeRows.Add(data.id, row);
            }
            if (storeItems.Count > 0 && ui.buildingMarket == null)
                Debug.LogWarning("StoreItems loaded, but ClickerCanvasView.buildingMarket is not assigned, so the market is hidden.");
            if (ui.buildingMarket != null)
            {
                Transform marketParent = ui.buildingMarket.transform.parent; // rows are created next to the template
                foreach (var data in storeItems)
                {
                    var row = Instantiate(ui.buildingMarket, marketParent);
                    row.name = "Market - " + data.name;
                    row.AutoBind(); // the template is inactive, so Awake hasn't run on the clone yet
                    if (!row.IsReady)
                    {
                        Debug.LogError("Market row template needs a title Text, a Button and a Text inside the button (assign them on ClickerMarketRow).");
                        Destroy(row.gameObject);
                        break;
                    }
                    if (ui.layoutVersion >= 3) row.UseCompactLayout(18);
                    else row.UseTitleOnlyLayout(24);
                    row.tooltip = shopTooltip;
                    row.tooltipText = string.Empty;
                    row.action.onClick.AddListener(() => { if (ready) TransactStoreItem(data); });
                    marketRows.Add(data.id, row);
                }
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
                float used = ConsumptionPerSecond(asset);
                if (used > 0) production.Append("  (-").Append(Format(used)).Append("/s used)");
            }
            ui.money.text = balances.ToString();
            ui.production.text = production.Append("  •  ").Append(Format(Multiplier)).Append("x active").ToString();
            ui.background.texture = Image("background");
            ui.background.enabled = ui.background.texture != null;
            //ui.cookie.texture = Image("clickable") ?? fallbackCookie; redundant due to new manager
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
                bool visible = ShopVisible(BuildingPrice(data, count), costCurrency, count > 0, data == buildings[0]);
                row.gameObject.SetActive(visible);
                if (!visible) continue;
                var unlockCurrency = currencyAssets[data.unlockCurrency];
                bool unlocked = Historical(unlockCurrency) >= data.unlockAt;
                float value = BuildingBatchValue(data, count, sellingBuildings);
                row.icon.texture = Image(data.image) ?? Icon(data.id, data.id);
                bool starved = count > 0 && data.consumeable > 0 && Satisfaction(asset) < 0.999f;
                row.title.text = data.name + "  ×" + count + (starved ? "  (Underutilized)" : "");
                row.tooltipText = BuildingTooltip(data, unlocked, unlockCurrency, starved);
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

            if (HasMarketBatchButtons)
            {
                ui.buyOneR.interactable = marketBatch != 1;
                ui.buyTenR.interactable = marketBatch != 10;
                ui.butHundredR.interactable = marketBatch != 100;
            }
            bool coverMode = false; // true when buildings consume something the market sells
            foreach (var stock in storeItems)
                if (ConsumptionDemandPerSecond(currencyAssets[stock.outputCurrency]) > 0) coverMode = true;
            if (ui.marketBatchStatus != null)
                ui.marketBatchStatus.text = coverMode ? "Covering " + MarketBatch + "s of consumption per buy" : "Buying " + MarketBatch + " at a time";
            foreach (var data in storeItems)
            {
                ClickerMarketRow row;
                if (!marketRows.TryGetValue(data.id, out row)) continue;
                var input = currencyAssets[data.inputCurrency];
                var output = currencyAssets[data.outputCurrency];
                float count = MarketCount(data, input, output);
                float cost = data.inputAmount * count;
                float gain = data.outputAmount * count;
                row.gameObject.SetActive(true);
                var art = Image(data.image);
                if (row.icon != null)
                {
                    row.icon.gameObject.SetActive(art != null);
                    if (art != null) row.icon.texture = art;
                }
                row.title.text = data.name + "\n+" + Format(gain) + " " + output.name;
                row.tooltipText = MarketTooltip(data, input, output, count);
                row.action.interactable = Balance(input) >= cost;
                row.actionText.text = "BUY\n" + Format(cost) + " " + input.name;
            }

            bool showEvent = eventQueue.Count > 0; // an event stays up until it is answered or runs out of time
            if (showEvent && shopTooltip != null) shopTooltip.Hide();
            ui.eventOverlay.SetActive(showEvent);
            if (showEvent)
            {
                var queued = eventQueue[eventQueue.Count - 1];
                var current = queued.data;
                ui.eventTitle.text = current.title;
                ui.eventDescription.text = current.description;
                ui.eventImage.texture = Image(current.image) ?? Icon("event", "all");
                // the answers sit on random buttons, fixed for each event
                if (current.IsChoice)
                {
                    SetOption(ui.optionOne, ui.optionOneText, ChoiceLabel(current, queued.order[0]), ChoiceUsable(current, queued.order[0]));
                    SetOption(ui.optionTwo, ui.optionTwoText, ChoiceLabel(current, queued.order[1]), ChoiceUsable(current, queued.order[1]));
                    SetOption(ui.optionThree, ui.optionThreeText, ChoiceLabel(current, queued.order[2]), ChoiceUsable(current, queued.order[2]));
                }
                else
                {
                    SetOption(ui.optionOne, ui.optionOneText, queued.flipped ? current.option2 : current.option1, true);
                    SetOption(ui.optionTwo, ui.optionTwoText, queued.flipped ? current.option1 : current.option2, true);
                    if (ui.optionThree != null) ui.optionThree.gameObject.SetActive(false);
                }
                float timeLeft = EventTimeLeft(queued);
                string timeText = timeLeft < 0 ? string.Empty : "Time left: " + Mathf.CeilToInt(timeLeft) + "s";
                string waitingText = eventQueue.Count > 1 ? (eventQueue.Count - 1) + " more waiting" : string.Empty;
                if (ui.eventTimerText != null)
                {
                    ui.eventTimerText.text = timeText;
                    ui.eventQueueText.text = waitingText;
                }
                else ui.eventQueueText.text = timeText.Length > 0 && waitingText.Length > 0 ? timeText + "  •  " + waitingText : timeText + waitingText;
            }
            bool showToast = !string.IsNullOrEmpty(toast) && Time.time < toastUntil;
            ui.toastPanel.SetActive(showToast);
            if (showToast) ui.toastText.text = toast;
        }

        float PerSecond(Currency asset) { return Production(asset, false); }

        // The most the buildings could make per second: every building at full output (as if fully fed), all bonuses included.
        float MaxPerSecond(Currency asset) { return Production(asset, true); }

        float Production(Currency asset, bool atFullOutput)
        {
            float total = 0;
            foreach (var owned in manager.State.EarnedBuildings)
            {
                var building = owned.Key;
                if (building.YieldAmount.Currency != asset) continue;
                // each building's upgrades only touch that building's own output
                float contribution = building.YieldAmount.Amount * owned.Value * IncomeMultiplier * (atFullOutput ? 1f : OutputFactor(building));
                foreach (var upgrade in manager.State.EarnedUpgrades)
                {
                    foreach (var perk in upgrade.UpgradePerk)
                    {
                        if (perk.TargetBuilding != building) continue;
                        contribution = perk.Operation == Operation.Add ? contribution + perk.Amount : contribution * perk.Amount;
                    }
                }
                total += contribution;
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

        // The modifiers panel: the ladybug (golden cookie) bonus, event boosts and the overall bonus, plus their combined effect.
        void RefreshModifiers()
        {
            if (ui.modifierText == null) return;
            ui.modifierText.text = ModifierSummary(ui.modifierText.supportRichText);
        }

        // Active values are highlighted in the accent colour (the palette's text_title colour).
        //   Ladybug       : the golden cookie bonus (gold_multiplier) while it lasts
        //   Overall bonus : shown as +% (overallBonus 1 = +0%); it applies to all currency generation, clicks included
        string ModifierSummary(bool rich)
        {
            string open = rich ? "<color=#" + ColorUtility.ToHtmlStringRGB(modifierAccent) + ">" : string.Empty;
            string close = rich ? "</color>" : string.Empty;
            var text = new StringBuilder();

            text.Append(GoldenDisplayName).Append(": ");
            if (Time.time < goldEnd)
                text.Append(open).Append('x').Append(Format((float)Math.Min(GoldenFactor, 1e12))).Append(close)
                    .Append(" (").Append(Mathf.CeilToInt(goldEnd - Time.time)).Append("s left)");
            else text.Append("none");


            float percent = (overallBonus - 1f) * 100f;
            text.Append("\nPests killed bonus: ");
            if (Mathf.Abs(percent) > 0.001f)
                text.Append(open).Append(percent > 0 ? "+" : string.Empty).Append(percent.ToString("0.##", CultureInfo.InvariantCulture)).Append('%').Append(close);
            else text.Append("+0%");

            float reputationPercent = ReputationBonus * 100f;
            text.Append("\nReputation bonus: ");
            if (Mathf.Abs(reputationPercent) > 0.001f)
                text.Append(open).Append(reputationPercent > 0 ? "+" : string.Empty).Append(reputationPercent.ToString("0.##", CultureInfo.InvariantCulture)).Append('%').Append(close);
            else text.Append("+0%");

            int activeEffects = 0;
            float effectLeft = 0;
            for (int i = 0; i < effects.Count; i++)
            {
                if (Time.time >= effects[i].end) continue;
                activeEffects++;
                effectLeft = Mathf.Max(effectLeft, effects[i].end - Time.time);
            }
            text.Append("\nEvent effect: ");
            if (activeEffects > 0)
            {
                int effectPercent = Mathf.RoundToInt((EffectFactor - 1f) * 100f);
                text.Append(open).Append(effectPercent > 0 ? "+" : string.Empty).Append(effectPercent).Append('%').Append(close)
                    .Append(" (").Append(activeEffects).Append(activeEffects == 1 ? " active, " : " stacked, ").Append(Mathf.CeilToInt(effectLeft)).Append("s left)");
            }
            else text.Append("none");

            float total = IncomeMultiplier;
            bool boosted = Mathf.Abs(total - 1f) > 0.001f;
            text.Append("\nTotal production: ").Append(boosted ? open : string.Empty).Append('x').Append(Format(total)).Append(boosted ? close : string.Empty);
            return text.ToString();
        }

    }
}