using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GetLost.PlayerTools
{
    /// <summary>
    /// Central owner of player/menu cursor state.
    ///
    /// There are two kinds of menu registration:
    ///
    /// RegisterMenu:
    ///     Blocks gameplay-only contextual input such as the radial wheel.
    ///     ALT can temporarily free the cursor while this menu is open.
    ///
    /// RegisterCursorMenu:
    ///     Blocks gameplay-only contextual input AND owns a permanently free
    ///     cursor for as long as at least one cursor menu remains registered.
    ///     Use this for normal clickable UI such as Mission Selection.
    ///
    /// This prevents individual menus from fighting over Cursor.visible and
    /// Cursor.lockState.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerInputContextManager : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField]
        private InputActionReference contextualAltAction;

        [Header("Cursor")]
        [SerializeField]
        private Behaviour[] disableWhileMenuCursorActive;

        [Header("Diagnostics")]
        [SerializeField]
        private bool logContextChanges = false;

        [Header("Runtime Debug")]
        [SerializeField]
        private bool menuOpen;

        [SerializeField]
        private bool menuCursorActive;

        [SerializeField]
        private int registeredMenuCount;

        [SerializeField]
        private int registeredCursorMenuCount;

        private readonly HashSet<Object> menuOwners =
            new HashSet<Object>();

        private readonly HashSet<Object> cursorMenuOwners =
            new HashSet<Object>();

        private readonly List<Object> ownersToRemove =
            new List<Object>();

        private bool[] previousBehaviourStates;

        private CursorLockMode previousCursorLockMode;
        private bool previousCursorVisible;

        public bool IsMenuOpen => menuOwners.Count > 0;
        public bool IsMenuCursorActive => menuCursorActive;
        public int RegisteredMenuCount => menuOwners.Count;
        public int RegisteredCursorMenuCount => cursorMenuOwners.Count;

        private void OnEnable()
        {
            if (contextualAltAction?.action == null)
                return;

            contextualAltAction.action.started -= OnAltStarted;
            contextualAltAction.action.canceled -= OnAltCanceled;

            contextualAltAction.action.started += OnAltStarted;
            contextualAltAction.action.canceled += OnAltCanceled;

            contextualAltAction.action.Enable();
        }

        private void OnDisable()
        {
            if (contextualAltAction?.action != null)
            {
                contextualAltAction.action.started -= OnAltStarted;
                contextualAltAction.action.canceled -= OnAltCanceled;
            }

            // Restore the state we owned before discarding ownership.
            if (menuCursorActive)
                SetMenuCursorActive(false);

            cursorMenuOwners.Clear();
            menuOwners.Clear();

            RefreshDebugState();
        }

        private void Update()
        {
            PruneInactiveMenuOwners();
            RefreshCursorOwnership();
            RefreshDebugState();
        }

        /// <summary>
        /// Registers a menu that blocks gameplay contextual input.
        /// The cursor is only freed while contextual ALT is held.
        /// </summary>
        public void RegisterMenu(Object owner)
        {
            if (owner == null)
                return;

            bool added = menuOwners.Add(owner);

            if (added && logContextChanges)
            {
                Debug.Log(
                    $"[Input Context] Registered menu: {DescribeOwner(owner)}. " +
                    $"Count = {menuOwners.Count}",
                    this
                );
            }

            RefreshCursorOwnership();
            RefreshDebugState();
        }

        /// <summary>
        /// Registers a normal clickable UI menu. While any cursor menu is
        /// registered the cursor stays visible and unlocked regardless of ALT.
        /// </summary>
        public void RegisterCursorMenu(Object owner)
        {
            if (owner == null)
                return;

            bool menuAdded = menuOwners.Add(owner);
            bool cursorAdded = cursorMenuOwners.Add(owner);

            if ((menuAdded || cursorAdded) && logContextChanges)
            {
                Debug.Log(
                    $"[Input Context] Registered CURSOR menu: {DescribeOwner(owner)}. " +
                    $"Menus = {menuOwners.Count}, CursorMenus = {cursorMenuOwners.Count}",
                    this
                );
            }

            RefreshCursorOwnership();
            RefreshDebugState();
        }

        public void UnregisterMenu(Object owner)
        {
            if (owner == null)
                return;

            bool removed = menuOwners.Remove(owner);
            bool cursorRemoved = cursorMenuOwners.Remove(owner);

            if ((removed || cursorRemoved) && logContextChanges)
            {
                Debug.Log(
                    $"[Input Context] Unregistered menu: {DescribeOwner(owner)}. " +
                    $"Menus = {menuOwners.Count}, CursorMenus = {cursorMenuOwners.Count}",
                    this
                );
            }

            RefreshCursorOwnership();
            RefreshDebugState();
        }

        public void UnregisterCursorMenu(Object owner)
        {
            UnregisterMenu(owner);
        }

        public bool IsMenuRegistered(Object owner)
        {
            return owner != null &&
                   menuOwners.Contains(owner);
        }

        public bool IsCursorMenuRegistered(Object owner)
        {
            return owner != null &&
                   cursorMenuOwners.Contains(owner);
        }

        /// <summary>
        /// Safety/reset command useful at explicit UI -> gameplay transitions.
        /// Normal UI code should still unregister its own owner.
        /// </summary>
        [ContextMenu("Clear Registered Menus")]
        public void ClearRegisteredMenus()
        {
            if (logContextChanges &&
                menuOwners.Count > 0)
            {
                Debug.Log(
                    $"[Input Context] Clearing {menuOwners.Count} registered menu owner(s).",
                    this
                );
            }

            cursorMenuOwners.Clear();
            menuOwners.Clear();

            RefreshCursorOwnership();
            RefreshDebugState();
        }

        [ContextMenu("Log Registered Menus")]
        public void LogRegisteredMenus()
        {
            PruneInactiveMenuOwners();

            if (menuOwners.Count == 0)
            {
                Debug.Log(
                    "[Input Context] No menus registered.",
                    this
                );

                return;
            }

            foreach (Object owner in menuOwners)
            {
                string kind =
                    cursorMenuOwners.Contains(owner)
                        ? "CURSOR MENU"
                        : "MENU";

                Debug.Log(
                    $"[Input Context] Registered {kind}: {DescribeOwner(owner)}",
                    this
                );
            }
        }

        private void PruneInactiveMenuOwners()
        {
            if (menuOwners.Count == 0)
                return;

            ownersToRemove.Clear();

            foreach (Object owner in menuOwners)
            {
                if (owner == null)
                {
                    ownersToRemove.Add(owner);
                    continue;
                }

                Component component = owner as Component;

                if (component != null &&
                    !component.gameObject.activeInHierarchy)
                {
                    ownersToRemove.Add(owner);
                    continue;
                }

                Behaviour behaviour = owner as Behaviour;

                if (behaviour != null &&
                    !behaviour.isActiveAndEnabled)
                {
                    ownersToRemove.Add(owner);
                }
            }

            for (int i = 0;
                 i < ownersToRemove.Count;
                 i++)
            {
                Object owner = ownersToRemove[i];

                menuOwners.Remove(owner);
                cursorMenuOwners.Remove(owner);

                if (logContextChanges)
                {
                    Debug.Log(
                        $"[Input Context] Pruned inactive menu: {DescribeOwner(owner)}. " +
                        $"Menus = {menuOwners.Count}, CursorMenus = {cursorMenuOwners.Count}",
                        this
                    );
                }
            }

            ownersToRemove.Clear();
        }

        private void OnAltStarted(
            InputAction.CallbackContext context)
        {
            RefreshCursorOwnership();
        }

        private void OnAltCanceled(
            InputAction.CallbackContext context)
        {
            // A forced cursor menu keeps ownership even after ALT is released.
            RefreshCursorOwnership();
        }

        private void RefreshCursorOwnership()
        {
            bool forcedCursor =
                cursorMenuOwners.Count > 0;

            bool contextualCursor =
                IsMenuOpen &&
                contextualAltAction?.action != null &&
                contextualAltAction.action.IsPressed();

            SetMenuCursorActive(
                forcedCursor || contextualCursor
            );
        }

        private void SetMenuCursorActive(bool active)
        {
            if (menuCursorActive == active)
                return;

            menuCursorActive = active;

            if (active)
            {
                previousCursorLockMode =
                    Cursor.lockState;

                previousCursorVisible =
                    Cursor.visible;

                DisableGameplayBehaviours();

                Cursor.lockState =
                    CursorLockMode.None;

                Cursor.visible = true;

                if (logContextChanges)
                {
                    Debug.Log(
                        "[Input Context] Cursor ownership ACQUIRED.",
                        this
                    );
                }
            }
            else
            {
                RestoreGameplayBehaviours();

                Cursor.lockState =
                    previousCursorLockMode;

                Cursor.visible =
                    previousCursorVisible;

                if (logContextChanges)
                {
                    Debug.Log(
                        "[Input Context] Cursor ownership RELEASED.",
                        this
                    );
                }
            }

            RefreshDebugState();
        }

        private void DisableGameplayBehaviours()
        {
            if (disableWhileMenuCursorActive == null)
                return;

            previousBehaviourStates =
                new bool[
                    disableWhileMenuCursorActive.Length
                ];

            for (int i = 0;
                 i < disableWhileMenuCursorActive.Length;
                 i++)
            {
                Behaviour behaviour =
                    disableWhileMenuCursorActive[i];

                if (behaviour == null ||
                    behaviour == this)
                {
                    continue;
                }

                previousBehaviourStates[i] =
                    behaviour.enabled;

                behaviour.enabled = false;
            }
        }

        private void RestoreGameplayBehaviours()
        {
            if (disableWhileMenuCursorActive == null ||
                previousBehaviourStates == null)
            {
                previousBehaviourStates = null;
                return;
            }

            for (int i = 0;
                 i < disableWhileMenuCursorActive.Length;
                 i++)
            {
                Behaviour behaviour =
                    disableWhileMenuCursorActive[i];

                if (behaviour == null ||
                    behaviour == this)
                {
                    continue;
                }

                if (i < previousBehaviourStates.Length)
                {
                    behaviour.enabled =
                        previousBehaviourStates[i];
                }
            }

            previousBehaviourStates = null;
        }

        private void RefreshDebugState()
        {
            menuOpen = IsMenuOpen;
            registeredMenuCount = menuOwners.Count;
            registeredCursorMenuCount = cursorMenuOwners.Count;
        }

        private static string DescribeOwner(Object owner)
        {
            if (owner == null)
                return "<null>";

            Component component =
                owner as Component;

            if (component != null)
            {
                return
                    $"{component.GetType().Name} on {component.gameObject.name}";
            }

            return
                $"{owner.GetType().Name} ({owner.name})";
        }
    }
}
