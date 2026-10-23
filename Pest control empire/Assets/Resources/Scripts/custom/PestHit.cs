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
        newColor = GetColor("texturePos", Color.white);
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
                newColor = Random.ColorHSV();
                SetColor("NewColor", newColor);
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
    }

    private void HitPest()
    {
        // every click upgrade the player owns makes each click hit harder (see ClickerGame.ClickDamage)
        hitCount += gameDataRef != null ? Mathf.Max(1, gameDataRef.ClickDamage) : 1;
        PlayerPrefs.SetInt("hits", hitCount);
        healthDisplay.value = hitCount;
        healthTXT.text = "Health: " + (requiredHits - hitCount).ToString();
    }

    // Save Color
    public static void SetColor(string key, Color color)
    {
        string hex = ColorUtility.ToHtmlStringRGBA(color);
        PlayerPrefs.SetString(key, hex);
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