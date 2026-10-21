using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Every click or tap plays a sound and stamps a splat image at the pointer, rotated at random.
// Put this on any GameObject (the Canvas is a good choice), then assign Click Sound and Splat Image.
// Works with the new Input System and the old Input Manager. Mouse, touch and pen all count; a gamepad has
// no pointer position, so it isn't included.
[RequireComponent(typeof(AudioSource))]
public class ClickSplat : MonoBehaviour
{
    [Header("Required")]
    public AudioClip clickSound;
    public Texture splatImage;   // any imported image works (Default or Sprite); use a PNG with transparency

    [Header("Sound")]
    [Range(0f, 1f)] public float volume = 1f;

    [Header("Splat")]
    [Tooltip("Where splats are created. Leave empty to use this object's Canvas (or the first Canvas in the scene).")]
    public RectTransform splatParent;
    [Tooltip("Height of a splat in canvas units. The width follows the image's proportions.")]
    public float size = 128f;
    public Color tint = Color.white;
    [Tooltip("Seconds a splat stays on screen. 0 = it stays until the limit below recycles it.")]
    public float lifetime = 3f;
    [Tooltip("The last part of the lifetime is spent fading out.")]
    public float fadeTime = 1f;
    [Tooltip("Once this many splats are on screen, the oldest one is reused for the next click.")]
    public int maxSplats = 50;

    class Splat
    {
        public RawImage image;
        public float spawnTime;
    }

    readonly List<Splat> active = new List<Splat>();   // oldest first
    readonly Stack<Splat> pool = new Stack<Splat>();   // retired splats waiting to be reused
    AudioSource audioSource;
    Canvas canvas;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();

        if (splatParent == null)
        {
            var found = GetComponentInParent<Canvas>();
            if (found == null) found = FindFirstObjectByType<Canvas>();
            if (found != null) splatParent = found.transform as RectTransform;
        }
        var parentCanvas = splatParent != null ? splatParent.GetComponentInParent<Canvas>() : null;
        if (parentCanvas == null)
        {
            Debug.LogError("ClickSplat: no Canvas found. Put this on a Canvas or assign Splat Parent.");
            enabled = false;
            return;
        }
        canvas = parentCanvas.rootCanvas;

        if (clickSound == null) Debug.LogWarning("ClickSplat: no Click Sound assigned, so clicks will be silent.");
        if (splatImage == null) Debug.LogWarning("ClickSplat: no Splat Image assigned, so no splats will appear.");
    }

    void Update()
    {
        Vector2 screenPosition;
        if (TryGetClick(out screenPosition)) Click(screenPosition);
        if (lifetime > 0f) AgeSplats();
    }

    // At most one click per frame is handled.
    static bool TryGetClick(out Vector2 screenPosition)
    {
#if ENABLE_INPUT_SYSTEM
        var pointer = Pointer.current; // whichever of mouse, pen or touch was used last
        if (pointer != null && pointer.press.wasPressedThisFrame)
        {
            screenPosition = pointer.position.ReadValue();
            return true;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(0))
        {
            screenPosition = Input.mousePosition;
            return true;
        }
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            if (touch.phase == TouchPhase.Began)
            {
                screenPosition = touch.position;
                return true;
            }
        }
#endif
        screenPosition = Vector2.zero;
        return false;
    }

    void Click(Vector2 screenPosition)
    {
        if (clickSound != null) audioSource.PlayOneShot(clickSound, volume);
        if (splatImage != null) SpawnSplat(screenPosition);
    }

    void SpawnSplat(Vector2 screenPosition)
    {
        Camera cam = null;
        if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) cam = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(splatParent, screenPosition, cam, out local)) return;

        Splat splat = GetSplat();
        RectTransform rect = splat.image.rectTransform;
        float aspect = splatImage.height > 0 ? (float)splatImage.width / splatImage.height : 1f;
        splat.image.texture = splatImage;
        splat.image.color = tint;
        rect.sizeDelta = new Vector2(size * aspect, size);
        rect.localPosition = new Vector3(local.x, local.y, 0f);
        rect.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)); // random rotation for every click
        rect.SetAsLastSibling(); // newest splat on top
        splat.image.gameObject.SetActive(true);
        splat.spawnTime = Time.unscaledTime;
        active.Add(splat);
    }

    Splat GetSplat()
    {
        if (pool.Count > 0) return pool.Pop();
        if (active.Count >= Mathf.Max(1, maxSplats))
        {
            Splat oldest = active[0];
            active.RemoveAt(0);
            return oldest;
        }
        return CreateSplat();
    }

    Splat CreateSplat()
    {
        var go = new GameObject("Splat", typeof(RectTransform), typeof(RawImage), typeof(LayoutElement));
        go.transform.SetParent(splatParent, false);
        go.GetComponent<LayoutElement>().ignoreLayout = true; // a layout group on the parent can't move it
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        var image = go.GetComponent<RawImage>();
        image.raycastTarget = false; // never block clicks on the buttons underneath
        return new Splat { image = image };
    }

    // Fades splats out over the last fadeTime seconds of their life, then retires them to the pool.
    void AgeSplats()
    {
        float fade = Mathf.Min(fadeTime, lifetime);
        for (int i = active.Count - 1; i >= 0; i--)
        {
            Splat splat = active[i];
            float age = Time.unscaledTime - splat.spawnTime;
            if (age >= lifetime)
            {
                splat.image.gameObject.SetActive(false);
                active.RemoveAt(i);
                pool.Push(splat);
            }
            else if (fade > 0f && age > lifetime - fade)
            {
                Color color = tint;
                color.a *= (lifetime - age) / fade;
                splat.image.color = color;
            }
        }
    }

    void OnDestroy()
    {
        foreach (var splat in active) if (splat.image != null) Destroy(splat.image.gameObject);
        foreach (var splat in pool) if (splat.image != null) Destroy(splat.image.gameObject);
    }
}
