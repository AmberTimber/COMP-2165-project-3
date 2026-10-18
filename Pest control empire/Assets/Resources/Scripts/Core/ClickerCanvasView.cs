using UnityEngine;
using UnityEngine.UI;

namespace COMP2165.Clicker
{
    // References to editable objects in the Clicker scene. Move and style these in Unity's Canvas.
    public class ClickerCanvasView : MonoBehaviour
    {
        public int layoutVersion;
        public RawImage background, cookie, goldenCookie, eventImage;
        public Text gameTitle, money, production, gameDescription, eventCount, stackButtonText;
        public Text upgradesTabText, batchStatus, eventTitle, eventDescription, eventQueueText, toastText, loadingText;
        public Button cookieButton, goldenButton, stackButton, buildingsTab, upgradesTab;
        public Button buyMode, sellMode, batchOne, batchTen, batchHundred;
        public Button closeEvent, optionOne, optionTwo;
        public Text optionOneText, optionTwoText;
        public GameObject buildingControls, buildingList, upgradeList, eventOverlay, toastPanel, loadingPanel;
        public RectTransform buildingContent, upgradeContent;
        public ClickerShopRow buildingTemplate, upgradeTemplate;
    }

}
