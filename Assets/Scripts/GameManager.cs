using GetLost.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace GetLost.Game
{
    public enum GameState
    {
        Playing,
        PlayerDead,
        Restarting
    }

    /// <summary>
    /// Scene-level game manager for prototype game-state flow.
    ///
    /// Currently handles:
    /// - Playing / Dead / Restarting state
    /// - Listening for player death
    /// - Showing optional restart UI
    /// - Restarting the current scene with R
    ///
    /// Keep this on a dedicated GameManager GameObject, not on the Player.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameManager : MonoBehaviour
    {
        [Header("Player")]
        [SerializeField] private PlayerDamageController playerDamageController;

        [Tooltip("If enabled, GameManager searches the scene for PlayerDamageController when one is not assigned.")]
        [SerializeField] private bool autoFindPlayer = true;

        [Header("Death / Restart")]
        [Tooltip("Optional GameObject containing your death/restart UI.")]
        [SerializeField] private GameObject deathUI;

        [Tooltip("Allow R to restart after the player dies.")]
        [SerializeField] private bool allowKeyboardRestart = true;

        [Tooltip("Short delay before restart input becomes active.")]
        [SerializeField, Min(0f)] private float restartInputDelay = 1f;

        [Tooltip("Hide Death UI automatically when the scene starts.")]
        [SerializeField] private bool hideDeathUIOnAwake = true;

        [Header("Debug")]
        [SerializeField] private bool logStateChanges;

        private float restartAvailableTime;

        public GameState State { get; private set; } = GameState.Playing;

        public bool IsPlayerDead =>
            State == GameState.PlayerDead;

        public bool CanRestart =>
            State == GameState.PlayerDead &&
            Time.unscaledTime >= restartAvailableTime;

        private void Awake()
        {
            if (hideDeathUIOnAwake &&
                deathUI != null)
            {
                deathUI.SetActive(false);
            }

            ResolvePlayer();

            SetState(GameState.Playing);
        }

        private void OnEnable()
        {
            ResolvePlayer();
            SubscribeToPlayer();
        }

        private void OnDisable()
        {
            UnsubscribeFromPlayer();
        }

        private void Update()
        {
            if (!allowKeyboardRestart ||
                !CanRestart ||
                Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.rKey.wasPressedThisFrame)
                RestartCurrentScene();
        }

        private void ResolvePlayer()
        {
            if (playerDamageController != null)
                return;

            if (!autoFindPlayer)
                return;

            playerDamageController =
                FindAnyObjectByType<PlayerDamageController>();
        }

        private void SubscribeToPlayer()
        {
            if (playerDamageController == null)
                return;

            playerDamageController.Died -= HandlePlayerDied;
            playerDamageController.Died += HandlePlayerDied;
        }

        private void UnsubscribeFromPlayer()
        {
            if (playerDamageController == null)
                return;

            playerDamageController.Died -= HandlePlayerDied;
        }

        private void HandlePlayerDied()
        {
            if (State != GameState.Playing)
                return;

            restartAvailableTime =
                Time.unscaledTime +
                restartInputDelay;

            SetState(GameState.PlayerDead);

            if (deathUI != null)
                deathUI.SetActive(true);
        }

        public void RestartCurrentScene()
        {
            if (State == GameState.Restarting)
                return;

            if (State == GameState.PlayerDead &&
                Time.unscaledTime < restartAvailableTime)
            {
                return;
            }

            SetState(GameState.Restarting);

            Time.timeScale = 1f;

            // Restore any runtime terrain changes before reloading the scene.
            // This prevents modified TerrainData from becoming the new baseline
            // when the scene is restarted during Play Mode.
            Tom.PathCreator.TerrainRuntimeBackupRegistry.RestoreAll();

            Scene activeScene =
                SceneManager.GetActiveScene();

            SceneManager.LoadScene(
                activeScene.buildIndex);
        }

        public void SetPlayerDamageController(
            PlayerDamageController controller)
        {
            if (playerDamageController == controller)
                return;

            UnsubscribeFromPlayer();

            playerDamageController = controller;

            SubscribeToPlayer();
        }

        private void SetState(GameState newState)
        {
            State = newState;

            if (logStateChanges)
            {
                Debug.Log(
                    $"[GameManager] State -> {State}",
                    this);
            }
        }

        [ContextMenu("TEST - Simulate Player Death")]
        private void TestSimulatePlayerDeath()
        {
            if (!Application.isPlaying)
                return;

            HandlePlayerDied();
        }

        [ContextMenu("TEST - Restart Scene")]
        private void TestRestartScene()
        {
            if (!Application.isPlaying)
                return;

            restartAvailableTime = 0f;
            RestartCurrentScene();
        }
    }
}
