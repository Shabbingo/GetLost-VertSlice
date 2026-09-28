using System;
using System.Collections.Generic;
using UnityEngine;

namespace GetLost.PlayerTools
{
    [DisallowMultipleComponent]
    public class PlayerToolManager : MonoBehaviour
    {
        [Header("Tools")]
        [Tooltip(
            "Components implementing IPlayerTool. Leave empty when Auto Find Tools is enabled."
        )]
        [SerializeField]
        private List<MonoBehaviour> toolComponents =
            new List<MonoBehaviour>();

        [SerializeField]
        private bool autoFindTools = true;

        [Header("Starting Tool")]
        [SerializeField]
        private PlayerToolType startingTool =
            PlayerToolType.None;

        [Header("Behaviour")]
        [SerializeField]
        private bool selectingSameToolUnequips = false;

        [Header("Runtime Debug")]
        [SerializeField]
        private PlayerToolType currentToolType =
            PlayerToolType.None;

        [SerializeField]
        private string currentToolName = "None";

        private readonly Dictionary<PlayerToolType, IPlayerTool>
            tools = new Dictionary<PlayerToolType, IPlayerTool>();

        private IPlayerTool currentTool;

        public PlayerToolType CurrentToolType => currentToolType;
        public IPlayerTool CurrentTool => currentTool;
        public bool HasToolEquipped => currentTool != null;

        public event Action<PlayerToolType, PlayerToolType>
            ToolChanged;

        private void Awake()
        {
            BuildToolRegistry();
            UnequipAllImmediate();

            if (startingTool != PlayerToolType.None)
                SelectTool(startingTool);
        }

        private void BuildToolRegistry()
        {
            tools.Clear();

            if (autoFindTools)
                FindToolsInChildren();

            for (int i = 0; i < toolComponents.Count; i++)
            {
                MonoBehaviour component = toolComponents[i];

                if (component == null)
                    continue;

                if (component is not IPlayerTool tool)
                {
                    Debug.LogWarning(
                        $"[PlayerToolManager] '{component.name}' / " +
                        $"{component.GetType().Name} does not implement IPlayerTool.",
                        component
                    );
                    continue;
                }

                RegisterTool(tool);
            }
        }

        private void FindToolsInChildren()
        {
            MonoBehaviour[] behaviours =
                GetComponentsInChildren<MonoBehaviour>(true);

            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];

                if (behaviour == null || behaviour == this)
                    continue;

                if (behaviour is not IPlayerTool)
                    continue;

                if (!toolComponents.Contains(behaviour))
                    toolComponents.Add(behaviour);
            }
        }

        private void RegisterTool(IPlayerTool tool)
        {
            if (tool == null || tool.ToolType == PlayerToolType.None)
                return;

            if (tools.ContainsKey(tool.ToolType))
            {
                Debug.LogWarning(
                    $"[PlayerToolManager] Multiple tools registered as " +
                    $"'{tool.ToolType}'. Only the first will be used.",
                    this
                );
                return;
            }

            tools.Add(tool.ToolType, tool);
        }

        public bool SelectTool(PlayerToolType toolType)
        {
            if (toolType == PlayerToolType.None)
            {
                UnequipCurrentTool();
                return true;
            }

            if (currentTool != null &&
                currentTool.ToolType == toolType)
            {
                if (selectingSameToolUnequips)
                    UnequipCurrentTool();

                return true;
            }

            if (!tools.TryGetValue(
                    toolType,
                    out IPlayerTool nextTool))
            {
                Debug.LogWarning(
                    $"[PlayerToolManager] No registered tool for '{toolType}'.",
                    this
                );
                return false;
            }

            PlayerToolType previousType =
                currentTool != null
                    ? currentTool.ToolType
                    : PlayerToolType.None;

            currentTool?.Unequip();

            currentTool = nextTool;
            currentTool.Equip();

            currentToolType = currentTool.ToolType;
            currentToolName = currentToolType.ToString();

            ToolChanged?.Invoke(previousType, currentToolType);

            Debug.Log(
                $"[Player Tools] {previousType} -> {currentToolType}",
                this
            );

            return true;
        }

        public void SelectSpyglass() =>
            SelectTool(PlayerToolType.Spyglass);

        public void SelectFieldCamera() =>
            SelectTool(PlayerToolType.FieldCamera);

        public void SelectCompass() =>
            SelectTool(PlayerToolType.Compass);

        public void SelectTorch() =>
            SelectTool(PlayerToolType.Torch);

        public void SelectMap() =>
            SelectTool(PlayerToolType.Map);

        public void SelectRadio() =>
            SelectTool(PlayerToolType.Radio);

        public void SelectNotebook() =>
            SelectTool(PlayerToolType.Notebook);

        public void UnequipCurrentTool()
        {
            if (currentTool == null)
                return;

            PlayerToolType previousType =
                currentTool.ToolType;

            currentTool.Unequip();
            currentTool = null;

            currentToolType = PlayerToolType.None;
            currentToolName = "None";

            ToolChanged?.Invoke(
                previousType,
                PlayerToolType.None
            );

            Debug.Log(
                $"[Player Tools] {previousType} -> None",
                this
            );
        }

        private void UnequipAllImmediate()
        {
            foreach (
                KeyValuePair<PlayerToolType, IPlayerTool> pair
                in tools)
            {
                pair.Value?.Unequip();
            }

            currentTool = null;
            currentToolType = PlayerToolType.None;
            currentToolName = "None";
        }

        public bool IsSelected(PlayerToolType toolType)
        {
            return currentTool != null &&
                   currentTool.ToolType == toolType;
        }

        public bool HasTool(PlayerToolType toolType)
        {
            return tools.ContainsKey(toolType);
        }

        public bool TryGetTool(
            PlayerToolType toolType,
            out IPlayerTool tool)
        {
            return tools.TryGetValue(toolType, out tool);
        }
    }
}
