using UnityEngine;

namespace GetLost.PlayerTools
{
    /// <summary>
    /// Tool-manager wrapper for the existing CompassNeedle behaviour.
    /// Keep CompassNeedle focused on needle physics; this component owns
    /// whether the physical/UI compass is equipped.
    /// </summary>
    [DisallowMultipleComponent]
    public class CompassTool : MonoBehaviour, IPlayerTool
    {
        [SerializeField]
        private GameObject compassRoot;

        [SerializeField]
        private bool startVisibleWithoutManager = false;

        public PlayerToolType ToolType => PlayerToolType.Compass;
        public bool IsEquipped { get; private set; }

        private void Awake()
        {
            SetVisible(startVisibleWithoutManager);
            IsEquipped = startVisibleWithoutManager;
        }

        public void Equip()
        {
            IsEquipped = true;
            SetVisible(true);
        }

        public void Unequip()
        {
            IsEquipped = false;
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            if (compassRoot != null)
                compassRoot.SetActive(visible);
        }
    }
}
