using COMP2165.Clicker;
using UnityEngine;
using UnityEngine.UI;

public class PestHit : MonoBehaviour
{
    private static ClickerGame gameDataRef;
    private int hitCount;
    public int initialRequiredHits = 10;
    private int requiredHits;
    private Button clicker;
    private RawImage pestImg;
    public Texture[] textures;
    private int textureCount;
    // slider stuff
    public Slider healthDisplay;
    public Text healthTXT;
    // reward for defeating a pest (currency id from Currencies.csv)
    public string rewardCurrency = "reputation";
    public float rewardAmount = 1;
    public Color newColor;
    // pests change colour each time the whole texture cycle completes; the colour is always kept bright
    [Range(0f, 1f)] public float minBrightness = 0.8f;
    [Range(0f, 1f)] public float minSaturation = 0.35f;
    const string ColorKey = "pestColor"; // saved and loaded under this one key (loading used to read "texturePos")


    private void OnEnable()
    {
        clicker.onClick.AddListener(HitPest);
    }

    private void OnDisable()
    {
        clicker.onClick.RemoveListener(HitPest);
    }

    private void Awake()
    {
        if (gameDataRef == null)
        {
            gameDataRef = FindFirstObjectByType<ClickerGame>();
        }
        clicker = gameObject.GetComponent<Button>();
        hitCount = PlayerPrefs.GetInt("hits", 0);
        requiredHits = PlayerPrefs.GetInt("hitsRequired", initialRequiredHits);
        // texture change
        textureCount = PlayerPrefs.GetInt("texturePos", 0);
        pestImg = gameObject.GetComponent<RawImage>();
        pestImg.texture = textures[textureCount];
        // slider
        healthDisplay.maxValue = requiredHits;
        healthDisplay.minValue = 0;
        healthDisplay.value = hitCount;
        // text
        healthTXT.text = "Health: " + (requiredHits - hitCount).ToString();
        // color
        newColor = Brighten(GetColor(ColorKey, Color.white)); // an old dark colour is lifted to the minimum brightness
        pestImg.color = newColor;

    }
    // Update is called once per frame
    void Update()
    {
        if (hitCount >= requiredHits)
        {
            int overflow = hitCount - requiredHits; // damage beyond the kill carries over to the next pest
            gameDataRef.overallBonus += 0.1f;
            gameDataRef.AddCurrency(rewardCurrency, rewardAmount); // each defeated pest earns reputation
            requiredHits = (int)System.Math.Min(requiredHits * 3L, 1000000000L);
            hitCount = overflow;
            PlayerPrefs.SetInt("hitsRequired", requiredHits);
            PlayerPrefs.SetInt("hits", hitCount);
            // texture
            textureCount++;
            if (textureCount > textures.Length - 1)
            {
                textureCount = 0;
                newColor = Random.ColorHSV(0f, 1f, minSaturation, 1f, minBrightness, 1f); // any hue, never dark
                SetColor(ColorKey, newColor);
                pestImg.color = newColor;
            }
            pestImg.texture = textures[textureCount];
            PlayerPrefs.SetInt("texturePos", textureCount);
            // slider
            healthDisplay.maxValue = requiredHits;
            healthDisplay.value = hitCount;
        }
    }

    // Forgets this pest's saved progress (used by the pause menu's Restart button).
    public static void ClearSavedProgress()
    {
        PlayerPrefs.DeleteKey("hits");
        PlayerPrefs.DeleteKey("hitsRequired");
        PlayerPrefs.DeleteKey("texturePos");
        PlayerPrefs.DeleteKey(ColorKey);
    }

    private void HitPest()
    {
        // every click upgrade the player owns makes each click hit harder (see ClickerGame.ClickDamage)
        hitCount += gameDataRef != null ? Mathf.Max(1, gameDataRef.ClickDamage) : 1;
        PlayerPrefs.SetInt("hits", hitCount);
        healthDisplay.value = hitCount;
        healthTXT.text = "Health: " + (requiredHits - hitCount).ToString();
    }

    // Lifts a colour that is darker than minBrightness up to it, keeping its hue and saturation.
    private Color Brighten(Color color)
    {
        float hue, saturation, value;
        Color.RGBToHSV(color, out hue, out saturation, out value);
        if (value >= minBrightness) return color;
        Color lifted = Color.HSVToRGB(hue, saturation, minBrightness);
        lifted.a = color.a;
        return lifted;
    }

    // Save Color
    public static void SetColor(string key, Color color)
    {
        string hex = ColorUtility.ToHtmlStringRGBA(color);
        PlayerPrefs.SetString(key, hex);
        PlayerPrefs.Save();
    }

    // Load Color
    public static Color GetColor(string key, Color defaultColor)
    {
        if (!PlayerPrefs.HasKey(key))
            return defaultColor;

        string hex = PlayerPrefs.GetString(key);
        if (ColorUtility.TryParseHtmlString("#" + hex, out Color loadedColor))
        {
            return loadedColor;
        }

        return defaultColor;
    }
}