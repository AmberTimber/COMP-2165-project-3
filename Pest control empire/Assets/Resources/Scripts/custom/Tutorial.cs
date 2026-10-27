using COMP2165.Clicker;
using UnityEngine;
using UnityEngine.UI;

// First-start tutorial. A highlight frame is moved over whatever is being explained, and a message box is placed beside it:
//   1. The pest: "click it to earn money". No Next button; it moves on by itself once you can afford a building.
//   2. A building: what it earns and what it consumes (built from the sheet's numbers). Press Next.
//   3. The market: money is exchanged for the pesticides that buildings consume. Press Next to finish.
// Everything it shows is assigned in the Inspector; this script only moves and fills it in. It runs once (it remembers
// in PlayerPrefs) and is skipped for a player who already owns a building. While it runs, events and ladybugs are held back.
//
// Scene setup: put this on an object that stays active. Make Tutorial Root a (disabled) panel covering the whole canvas that
// contains the Highlight Frame and the Message Panel (the message panel holds the texts and the Next button).
public class Tutorial : MonoBehaviour
{
    [Header("Tutorial UI")]
    [Tooltip("Parent of everything below. Switched on only while the tutorial runs. It must be a different object from this script's.")]
    [SerializeField] GameObject tutorialRoot;
    [Tooltip("An outline or arrow image that is moved and sized over whatever is being pointed at. Optional. Its graphics ignore clicks.")]
    [SerializeField] RectTransform highlightFrame;
    [Tooltip("The box holding the texts and the Next button. It is placed beside the highlighted object. Optional (then it stays where you put it).")]
    [SerializeField] RectTransform messagePanel;
    [SerializeField] Text titleText;
    [SerializeField] Text messageText;
    [SerializeField] Button nextButton;
    [SerializeField] Text nextButtonText;

    [Header("What to point at")]
    [Tooltip("The pest on the left. Found automatically through PestHit when left empty.")]
    [SerializeField] RectTransform pestTarget;
    [Tooltip("The market section at the bottom right. When empty, the area holding the market rows is used.")]
    [SerializeField] RectTransform marketTarget;
    [Tooltip("The building to show off (an id from Buildings.csv). Blank = the first building. The tutorial waits until the player can afford it.")]
    [SerializeField] string buildingId = "";

    [Header("Text")]
    [SerializeField] string clickPestTitle = "Earn some money";
    [SerializeField, TextArea(2, 6)] string clickPestMessage = "Click the pest to earn money. Every click pays a little, so keep going until you can afford your first building.";
    [SerializeField] string buildingTitle = "Buildings";
    [Tooltip("{building} is replaced by a description of the building from the sheet: what it earns and what it consumes.")]
    [SerializeField, TextArea(2, 8)] string buildingMessage = "Buildings earn money for you every second, even while you aren't clicking. Some of them also consume pesticides to keep working, and slow down if they run short.\n\n{building}\n\nPress BUY to get one, then press Next.";
    [SerializeField] string marketTitle = "The market";
    [SerializeField, TextArea(2, 6)] string marketMessage = "Buildings that consume pesticides need a supply of them. The market is where you exchange your money for pesticides, so buy some here to keep your buildings producing money.";
    [SerializeField] string nextLabel = "Next";

    [Header("Options")]
    [Tooltip("Space between the highlighted object and the message box, and the margin around the highlight frame.")]
    [SerializeField] float panelGap = 24f;
    [SerializeField] float framePadding = 8f;
    [Tooltip("For testing: show the tutorial every time, ignoring the saved 'already shown' flag.")]
    [SerializeField] bool alwaysShow;

    enum Phase { Waiting, ClickPest, Building, Market, Done }

    const string DoneKey = "COMP2165_tutorialDone";
    ClickerGame game;
    ClickerCanvasView view;
    Phase phase = Phase.Waiting;
    readonly Vector3[] corners = new Vector3[4];

    void Awake()
    {
        game = FindFirstObjectByType<ClickerGame>();
        view = FindFirstObjectByType<ClickerCanvasView>();
        if (tutorialRoot == null || messageText == null || nextButton == null || game == null)
        {
            Debug.LogError("Tutorial: assign Tutorial Root, Message Text and Next Button, and make sure the scene has a ClickerGame.");
            enabled = false;
            return;
        }
        tutorialRoot.SetActive(false);
        nextButton.onClick.AddListener(Next);
        if (pestTarget == null)
        {
            var pest = FindFirstObjectByType<PestHit>();
            if (pest != null) pestTarget = pest.transform as RectTransform;
        }
        if (!alwaysShow && PlayerPrefs.GetInt(DoneKey, 0) == 1) enabled = false;
    }

    void Update()
    {
        switch (phase)
        {
            case Phase.Waiting:
                if (!game.IsReady) return;
                if (!alwaysShow && game.HasProgress) { Finish(); return; } // a returning player doesn't need it
                Begin();
                break;
            case Phase.ClickPest:
                if (game.CanBuyBuilding(buildingId)) ShowBuildingStep();
                else Guide(pestTarget);
                break;
            case Phase.Building:
                Guide(BuildingTarget());
                break;
            case Phase.Market:
                Guide(MarketTarget());
                break;
        }
    }

    void Begin()
    {
        tutorialRoot.SetActive(true);
        game.holdBonuses = true; // no events or ladybugs while the player is being taught
        if (highlightFrame != null) foreach (var graphic in highlightFrame.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        ShowStep(Phase.ClickPest, clickPestTitle, clickPestMessage, false);
    }

    void ShowBuildingStep()
    {
        ShowStep(Phase.Building, buildingTitle, buildingMessage.Replace("{building}", game.DescribeBuilding(buildingId)), true);
    }

    void Next()
    {
        if (phase == Phase.Building) ShowStep(Phase.Market, marketTitle, marketMessage, true);
        else if (phase == Phase.Market) Finish();
    }

    void Finish()
    {
        phase = Phase.Done;
        tutorialRoot.SetActive(false);
        game.holdBonuses = false;
        PlayerPrefs.SetInt(DoneKey, 1);
        PlayerPrefs.Save();
        enabled = false;
    }

    void ShowStep(Phase next, string title, string message, bool needsNext)
    {
        phase = next;
        if (titleText != null) titleText.text = title;
        messageText.text = message;
        if (nextButtonText != null) nextButtonText.text = nextLabel;
        nextButton.gameObject.SetActive(needsNext);
    }

    RectTransform BuildingTarget()
    {
        var row = game.BuildingRowRect(buildingId);
        if (row != null) return row;
        return view != null && view.buildingList != null ? view.buildingList.transform as RectTransform : null;
    }

    RectTransform MarketTarget()
    {
        if (marketTarget != null) return marketTarget;
        return view != null && view.buildingMarket != null ? view.buildingMarket.transform.parent as RectTransform : null;
    }

    // Moves the highlight frame over the target and the message box beside it. Done every frame, so it follows layout changes.
    void Guide(RectTransform target)
    {
        if (highlightFrame != null)
        {
            highlightFrame.gameObject.SetActive(target != null);
            if (target != null)
            {
                Vector2 min, max;
                Corners(target, highlightFrame.parent as RectTransform, out min, out max);
                highlightFrame.anchorMin = highlightFrame.anchorMax = highlightFrame.pivot = new Vector2(0.5f, 0.5f);
                highlightFrame.localPosition = new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, 0f);
                highlightFrame.sizeDelta = (max - min) + Vector2.one * framePadding * 2f;
                highlightFrame.localScale = Vector3.one * (1f + 0.03f * Mathf.Sin(Time.unscaledTime * 5f)); // a gentle pulse
            }
        }
        if (messagePanel != null) Place(target);
    }

    // The target's corners in the local space of `space`.
    void Corners(RectTransform target, RectTransform space, out Vector2 min, out Vector2 max)
    {
        target.GetWorldCorners(corners);
        min = space.InverseTransformPoint(corners[0]);
        max = space.InverseTransformPoint(corners[2]);
    }

    // Puts the message box to the left of the target, else the right, else below, else above: the first spot that fits on screen.
    void Place(RectTransform target)
    {
        var space = messagePanel.parent as RectTransform;
        messagePanel.anchorMin = messagePanel.anchorMax = messagePanel.pivot = new Vector2(0.5f, 0.5f);
        Rect bounds = space.rect;
        Vector2 size = messagePanel.rect.size;
        if (target == null) { messagePanel.localPosition = bounds.center; return; }

        Vector2 min, max;
        Corners(target, space, out min, out max);
        Vector2 middle = (min + max) * 0.5f;
        Vector2[] spots =
        {
            new Vector2(min.x - panelGap - size.x * 0.5f, middle.y),
            new Vector2(max.x + panelGap + size.x * 0.5f, middle.y),
            new Vector2(middle.x, min.y - panelGap - size.y * 0.5f),
            new Vector2(middle.x, max.y + panelGap + size.y * 0.5f)
        };
        Vector2 chosen = spots[0];
        bool found = false;
        foreach (var spot in spots)
        {
            var box = new Rect(spot - size * 0.5f, size);
            if (bounds.Contains(box.min) && bounds.Contains(box.max)) { chosen = spot; found = true; break; }
        }
        if (!found) // nothing fits beside it: keep the first choice but pull it back on screen
        {
            chosen.x = Mathf.Clamp(chosen.x, bounds.xMin + size.x * 0.5f, bounds.xMax - size.x * 0.5f);
            chosen.y = Mathf.Clamp(chosen.y, bounds.yMin + size.y * 0.5f, bounds.yMax - size.y * 0.5f);
        }
        messagePanel.localPosition = new Vector3(chosen.x, chosen.y, 0f);
    }

    void OnDestroy()
    {
        if (game != null) game.holdBonuses = false;
    }

    // Makes the tutorial play again next time (also available from this component's context menu).
    [ContextMenu("Forget that the tutorial was shown")]
    void ForgetTutorial() { ClearSavedProgress(); }

    public static void ClearSavedProgress()
    {
        PlayerPrefs.DeleteKey(DoneKey);
        PlayerPrefs.Save();
    }
}
