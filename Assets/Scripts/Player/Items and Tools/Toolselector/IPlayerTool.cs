namespace GetLost.PlayerTools
{
    public enum PlayerToolType
    {
        None = 0,
        Spyglass = 1,
        FieldCamera = 2,
        Compass = 3,
        Torch = 4,
        Map = 5,
        Radio = 6,
        Notebook = 7
    }

    public interface IPlayerTool
    {
        PlayerToolType ToolType { get; }

        bool IsEquipped { get; }

        void Equip();

        void Unequip();
    }
}
