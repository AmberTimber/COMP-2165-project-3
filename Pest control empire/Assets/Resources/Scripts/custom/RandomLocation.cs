using UnityEngine;
using UnityEngine.UI;

public class RandomLocation : MonoBehaviour
{
    private Button buttonRef;
    private RectTransform movingButton;
    private RectTransform screenSize;
    private float width;
    private float height;

    private void Awake()
    {
        buttonRef = gameObject.GetComponent<Button>();
        movingButton = gameObject.GetComponent<RectTransform>();
        Canvas canvas = FindFirstObjectByType<Canvas>();
        screenSize = canvas.GetComponent<RectTransform>();
        width = (screenSize.rect.width/2) - 10;
        height = (screenSize.rect.height/2) - 10;
        MoveRandom();
    }

    private void OnEnable()
    {
        buttonRef.onClick.AddListener(MoveRandom);
    }

    private void OnDisable()
    {
        buttonRef.onClick.RemoveListener(MoveRandom);
    }

    private void MoveRandom()
    {
        float newWidth = Random.Range(-width, width);
        float newHeight = Random.Range(-height, height);
        Vector2 newPos = new Vector2(newWidth, newHeight);
        movingButton.anchoredPosition = newPos;
    }
}
