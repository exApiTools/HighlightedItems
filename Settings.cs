using System.Windows.Forms;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;

namespace HighlightedItems;

public class Settings : ISettings
{
    public bool[,] IgnoredCells { get; set; } = new bool[5, 12]
    {
        { false, false, false, false, false, false, false, false, false, false, false, false },
        { false, false, false, false, false, false, false, false, false, false, false, false },
        { false, false, false, false, false, false, false, false, false, false, false, false },
        { false, false, false, false, false, false, false, false, false, false, false, false },
        { false, false, false, false, false, false, false, false, false, false, false, false }
    };

    public ToggleNode Enable { get; set; } = new(true);

    [Menu("Enable Inventory Dump Button")]
    public ToggleNode DumpButtonEnable { get; set; } = new(true);

    [Menu("Show Stack Sizes")]
    public ToggleNode ShowStackSizes { get; set; } = new(true);

    [Menu("Show Stack Count Next to Stack Size")]
    public ToggleNode ShowStackCountWithSize { get; set; } = new(true);

    [Menu("Hotkey")]
    public HotkeyNode HotKey { get; set; } = new(Keys.F1);

    public RangeNode<int> ExtraDelay { get; set; } = new(20, 0, 100);

    [Menu("Use Thread.Sleep", "Is a little faster, but HUD will hang while clicking")]
    public ToggleNode UseThreadSleep { get; set; } = new ToggleNode(false);

    [Menu("Idle mouse delay", "Wait this long after the user lets go of the button and stops moving the mouse")]
    public RangeNode<int> IdleMouseDelay { get; set; } = new RangeNode<int>(200, 0, 1000);

    public ToggleNode CancelWithRightMouseButton { get; set; } = new ToggleNode(true);

    public ToggleNode VerifyTargetInventoryIsOpened { get; set; } = new ToggleNode(true);

    public ToggleNode VerifyButtonIsNotObstructed { get; set; } = new ToggleNode(true);
}