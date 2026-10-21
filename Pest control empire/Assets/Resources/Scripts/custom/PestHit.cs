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
        healthTXT.text = "Health: " + (requiredHits - hitCount).ToString();
    }
    // Update is called once per frame
    void Update()
    {
        if (hitCount >= requiredHits)
        {
            gameDataRef.overallBonus += 0.1f;
            gameDataRef.AddCurrency(rewardCurrency, rewardAmount); // each defeated pest earns reputation
            requiredHits *= 3;
            hitCount = 0;
            PlayerPrefs.SetInt("hitsRequired", requiredHits);
            // texture
            textureCount++;
            if (textureCount > textures.Length - 1)
            {
                textureCount = 0;
            }
            pestImg.texture = textures[textureCount];
            PlayerPrefs.SetInt("texturePos", textureCount);
            // slider
            healthDisplay.maxValue = requiredHits;
        }
    }

    private void HitPest()
    {
        hitCount++;
        PlayerPrefs.SetInt("hits", hitCount);
        healthDisplay.value = hitCount;
        healthTXT.text = "Health: " + (requiredHits - hitCount).ToString();
    }
}