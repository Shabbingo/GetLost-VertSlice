using GetLost.PlayerTools;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

[DefaultExecutionOrder(-100)]
public class PauseMenuController : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("The root GameObject of your ESC / pause menu.")]
    [SerializeField] private GameObject pauseMenuRoot;

    [Header("Input")]
    [Tooltip("Optional Input System action for Pause. If left empty, Escape is read directly.")]
    [SerializeField] private InputActionReference pauseAction;

    [Header("Optional - Disable While Paused")]
    [Tooltip("Put player look, movement, map, spyglass, etc. behaviours here if they can still respond while Time.timeScale is 0.")]
    [SerializeField] private Behaviour[] disableWhilePaused;

    [Header("Behaviour")]
    [SerializeField] private bool pauseTime = true;

    private bool isPaused;
    private float previousTimeScale = 1f;

    private CursorLockMode previousCursorLockMode;
    private bool previousCursorVisible;

    private bool[] previousBehaviourStates;
    private PlayerInputContextManager inputContext;

    public bool IsPaused => isPaused;

    private void Awake()
    {
        EnsureEventSystem();
        inputContext = FindAnyObjectByType<PlayerInputContextManager>();

        if (pauseMenuRoot != null)
            pauseMenuRoot.SetActive(false);
    }

    private void OnEnable()
    {
        if (pauseAction != null && pauseAction.action != null)
        {
            pauseAction.action.performed += OnPausePerformed;
            pauseAction.action.Enable();
        }
    }

    private void OnDisable()
    {
        if (pauseAction != null && pauseAction.action != null)
        {
            pauseAction.action.performed -= OnPausePerformed;
            pauseAction.action.Disable();
        }

        if (isPaused)
            Resume();
    }

    private void Update()
    {
        // Simple fallback so this works even if you don't want to make
        // another Input Action just for Escape.
        if ((pauseAction == null || pauseAction.action == null) &&
            Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame &&
            !AnotherMenuOwnsInput())
        {
            TogglePause();
        }
    }

    private void OnPausePerformed(InputAction.CallbackContext context)
    {
        if (AnotherMenuOwnsInput())
            return;
        TogglePause();
    }

    private bool AnotherMenuOwnsInput()
    {
        if (inputContext == null)
            inputContext = FindAnyObjectByType<PlayerInputContextManager>();
        return inputContext != null && inputContext.IsMenuOpen;
    }

    public void TogglePause()
    {
        if (isPaused)
            Resume();
        else
            Pause();
    }

    public void Pause()
    {
        if (isPaused)
            return;

        isPaused = true;

        previousCursorLockMode = Cursor.lockState;
        previousCursorVisible = Cursor.visible;

        if (pauseTime)
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        StoreAndDisableBehaviours();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (pauseMenuRoot != null)
            pauseMenuRoot.SetActive(true);
    }

    public void Resume()
    {
        if (!isPaused)
            return;

        if (pauseMenuRoot != null)
            pauseMenuRoot.SetActive(false);

        RestoreBehaviours();

        if (pauseTime)
            Time.timeScale = previousTimeScale;

        Cursor.lockState = previousCursorLockMode;
        Cursor.visible = previousCursorVisible;

        isPaused = false;
    }

    public void Restart()
    {
        Time.timeScale = 1f;

        Scene currentScene = SceneManager.GetActiveScene();

        if (currentScene.buildIndex >= 0)
            SceneManager.LoadScene(currentScene.buildIndex);
        else
            SceneManager.LoadScene(currentScene.name);
    }

    public void QuitGame()
    {
        Time.timeScale = 1f;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null)
            return;

        GameObject eventSystemObject = new GameObject(
            "Pause Menu Event System",
            typeof(EventSystem),
            typeof(InputSystemUIInputModule));
        eventSystemObject.transform.SetAsFirstSibling();
    }

    private void StoreAndDisableBehaviours()
    {
        if (disableWhilePaused == null)
            return;

        previousBehaviourStates = new bool[disableWhilePaused.Length];

        for (int i = 0; i < disableWhilePaused.Length; i++)
        {
            Behaviour behaviour = disableWhilePaused[i];

            if (behaviour == null || behaviour == this)
                continue;

            previousBehaviourStates[i] = behaviour.enabled;
            behaviour.enabled = false;
        }
    }

    private void RestoreBehaviours()
    {
        if (disableWhilePaused == null || previousBehaviourStates == null)
            return;

        for (int i = 0; i < disableWhilePaused.Length; i++)
        {
            Behaviour behaviour = disableWhilePaused[i];

            if (behaviour == null || behaviour == this)
                continue;

            if (i < previousBehaviourStates.Length)
                behaviour.enabled = previousBehaviourStates[i];
        }

        previousBehaviourStates = null;
    }
}
