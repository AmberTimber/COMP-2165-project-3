using COMP2165.Clicker;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Pause menu. Escape (or the optional pause button) freezes the game and shows the menu:
//   Resume  - unpauses.   Restart - asks for confirmation, then wipes all saved progress and reloads the scene.
//   Credits - shows the credits panel.   Quit - closes the game (stops Play mode in the editor).
// Everything it shows is assigned in the Inspector; this script only wires the buttons and shows or hides the panels.
// The game's timers (events, debuffs, ladybug, production) run on scaled time, so setting the time scale to 0 pauses them all.
// Give the pause panel a full-screen Image with Raycast Target on, so clicks can't reach the game behind it.
public class PauseMenu : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] GameObject pausePanel;
    [SerializeField] GameObject creditsPanel;
    [Tooltip("Its own panel holding the Confirm and Cancel buttons. Shown when Restart is pressed.")]
    [SerializeField] GameObject restartConfirmPanel;

    [Header("Buttons")]
    [SerializeField] Button resumeButton;
    [SerializeField] Button restartButton;
    [SerializeField] Button creditsButton;
    [SerializeField] Button quitButton;
    [Tooltip("The button inside the credits panel that goes back to the pause menu.")]
    [SerializeField] Button closeCreditsButton;
    [Tooltip("On the confirmation panel: really restart. This is the button that wipes the game data.")]
    [SerializeField] Button confirmRestartButton;
    [Tooltip("On the confirmation panel: cancel. Nothing is reset.")]
    [SerializeField] Button cancelRestartButton;
    [Tooltip("Optional: an on-screen button that opens the menu, for mouse and touch players.")]
    [SerializeField] Button pauseButton;

    [Header("Credits")]
    [Tooltip("The credit text objects (Text or TextMeshPro) inside the credits panel. They are switched on when the credits open.")]
    [SerializeField] GameObject[] creditItems;

    [Header("Options")]
    [Tooltip("Also silence all audio while paused.")]
    [SerializeField] bool pauseAudio = true;
    [Tooltip("Found automatically when left empty.")]
    [SerializeField] ClickerGame game;

    bool paused;
    float previousTimeScale = 1f;

    void Awake()
    {
        if (game == null) game = FindFirstObjectByType<ClickerGame>();
        if (pausePanel == null) Debug.LogWarning("PauseMenu: assign the Pause Panel.");
        Time.timeScale = 1f;            // every scene (re)load starts unpaused
        AudioListener.pause = false;
        Show(pausePanel, false);
        Show(creditsPanel, false);
        Show(restartConfirmPanel, false);
        Hook(resumeButton, Resume);
        Hook(restartButton, RequestRestart);
        Hook(confirmRestartButton, ConfirmRestart);
        Hook(cancelRestartButton, CancelRestart);
        Hook(creditsButton, OpenCredits);
        Hook(closeCreditsButton, CloseCredits);
        Hook(quitButton, QuitGame);
        Hook(pauseButton, Pause);
    }

    void Update()
    {
        if (!EscapePressed()) return;
        if (restartConfirmPanel != null && restartConfirmPanel.activeSelf) CancelRestart();   // Escape backs out of the confirmation first
        else if (creditsPanel != null && creditsPanel.activeSelf) CloseCredits();
        else if (paused) Resume();
        else Pause();
    }

    static bool EscapePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Escape);
#else
        return false;
#endif
    }

    public void Pause()
    {
        if (paused) return;
        paused = true;
        previousTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        if (pauseAudio) AudioListener.pause = true;
        Show(pausePanel, true);
        Show(creditsPanel, false);
        Show(restartConfirmPanel, false);
    }

    public void Resume()
    {
        paused = false;
        Time.timeScale = previousTimeScale;
        AudioListener.pause = false;
        Show(creditsPanel, false);
        Show(restartConfirmPanel, false);
        Show(pausePanel, false);
    }

    // The Restart button only asks. Nothing is reset until the player presses Confirm.
    public void RequestRestart()
    {
        if (restartConfirmPanel == null)
        {
            Debug.LogWarning("PauseMenu: no Restart Confirm Panel assigned, so Restart happens immediately.");
            ConfirmRestart();
            return;
        }
        Show(creditsPanel, false);
        Show(restartConfirmPanel, true);
    }

    // Cancel: closes the confirmation and resets nothing.
    public void CancelRestart() { Show(restartConfirmPanel, false); }

    // Confirm: wipes the saved game and the pest's progress, then reloads the scene so everything starts from zero.
    public void ConfirmRestart()
    {
        Show(restartConfirmPanel, false);
        Scene scene = SceneManager.GetActiveScene();
        if (scene.buildIndex < 0)
        {
            Debug.LogError("PauseMenu: add this scene in File > Build Settings so Restart can reload it. Nothing was reset.");
            return;
        }
        if (game != null) game.ResetProgress();
        PestHit.ClearSavedProgress();
        paused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        SceneManager.LoadScene(scene.buildIndex);
    }

    public void OpenCredits()
    {
        Show(creditsPanel, true);
        if (creditItems != null) foreach (var item in creditItems) Show(item, true);
    }

    public void CloseCredits() { Show(creditsPanel, false); }

    public void QuitGame()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void OnDestroy()
    {
        if (!paused) return;
        Time.timeScale = previousTimeScale;
        AudioListener.pause = false;
    }

    static void Show(GameObject target, bool visible) { if (target != null) target.SetActive(visible); }
    static void Hook(Button button, UnityAction action) { if (button != null) button.onClick.AddListener(action); }
}