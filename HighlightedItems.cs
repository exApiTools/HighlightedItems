using System.Windows.Forms;
using HighlightedItems.Utils;
using ExileCore;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.PoEMemory.MemoryObjects;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExileCore.Shared.Enums;
using ImGuiNET;
using System;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using ExileCore.PoEMemory.Components;

namespace HighlightedItems;

public class HighlightedItems : BaseSettingsPlugin<Settings>
{
    private IEnumerator<bool> _currentOperation;

    private bool MoveCancellationRequested => Settings.CancelWithRightMouseButton && (Control.MouseButtons & MouseButtons.Right) != 0;
    private IngameState InGameState => GameController.IngameState;
    private SharpDX.Vector2 WindowOffset => GameController.Window.GetWindowRectangleTimeCache.TopLeft;

    public override bool Initialise()
    {
        base.Initialise();
        Name = "HighlightedItems";

        var pickBtn = Path.Combine(DirectoryFullName, "images\\pick.png").Replace('\\', '/');
        var pickLBtn = Path.Combine(DirectoryFullName, "images\\pickL.png").Replace('\\', '/');
        Graphics.InitImage(pickBtn, false);
        Graphics.InitImage(pickLBtn, false);

        return true;
    }

    public override void DrawSettings()
    {
        base.DrawSettings();
        DrawIgnoredCellsSettings();
    }

    public override void Render()
    {
        if (_currentOperation != null && _currentOperation.MoveNext())
        {
            DebugWindow.LogMsg("Running the inventory dump procedure...");
            return;
        }

        if (!Settings.Enable)
            return;

        var (inventory, rectElement) = (InGameState.IngameUi.StashElement, InGameState.IngameUi.GuildStashElement) switch
        {
            ({ IsVisible: true, VisibleStash: { InventoryUIElement: { } invRect } visibleStash }, _) => (visibleStash, invRect),
            (_, { IsVisible: true, VisibleStash: { InventoryUIElement: { } invRect } visibleStash }) => (visibleStash, invRect),
            _ => (null, null)
        };

        if (inventory != null)
        {
            //Determine Stash Pickup Button position and draw
            var stashRect = rectElement.GetClientRect();
            var pickButtonRect = new SharpDX.RectangleF(stashRect.BottomRight.X - 43, stashRect.BottomRight.Y + 10, 37, 37);

            Graphics.DrawImage("pick.png", pickButtonRect);

            var highlightedItems = GetHighlightedItems(inventory);

            int? stackSizes = 0;
            foreach (var item in highlightedItems)
            {
                stackSizes += item.Item?.GetComponent<Stack>()?.Size;
            }

            var countText = Settings.ShowStackSizes && highlightedItems.Count != stackSizes && stackSizes != null
                ? Settings.ShowStackCountWithSize
                    ? $"{stackSizes} / {highlightedItems.Count}"
                    : $"{stackSizes}"
                : $"{highlightedItems.Count}";

            var countPos = new Vector2(pickButtonRect.Left - 2, pickButtonRect.Center.Y - 11);
            Graphics.DrawText($"{countText}", countPos with { Y = countPos.Y + 2 }, SharpDX.Color.Black, 10, "FrizQuadrataITC:22", FontAlign.Right);
            Graphics.DrawText($"{countText}", countPos with { X = countPos.X - 2 }, SharpDX.Color.White, 10, "FrizQuadrataITC:22", FontAlign.Right);

            if (IsButtonPressed(pickButtonRect) || Keyboard.IsKeyPressed(Settings.HotKey.Value))
            {
                var orderedItems = highlightedItems
                    .OrderBy(stashItem => stashItem.GetClientRectCache.X)
                    .ThenBy(stashItem => stashItem.GetClientRectCache.Y)
                    .ToList();
                _currentOperation = MoveItemsToInventory(orderedItems).GetEnumerator();
            }
        }

        var inventoryPanel = InGameState.IngameUi.InventoryPanel;
        if (inventoryPanel.IsVisible && Settings.DumpButtonEnable && IsStashTargetOpened)
        {
            //Determine Inventory Pickup Button position and draw
            var inventoryRect = inventoryPanel.Children[2].GetClientRect();
            var pickButtonRect =
                new SharpDX.RectangleF(inventoryRect.TopLeft.X + 18, inventoryRect.TopLeft.Y - 37, 37, 37);

            Graphics.DrawImage("pickL.png", pickButtonRect);
            if (IsButtonPressed(pickButtonRect))
            {
                var inventoryItems = GameController.IngameState.ServerData.PlayerInventories[0].Inventory.InventorySlotItems
                    .OrderBy(x => x.PosX)
                    .ThenBy(x => x.PosY)
                    .ToList();

                _currentOperation = MoveItemsToStash(inventoryItems).GetEnumerator();
            }
        }
    }

    private IEnumerable<bool> MoveItemsCommonPreamble(CancellationTokenSource cts)
    {
        while (Control.MouseButtons == MouseButtons.Left)
        {
            if (MoveCancellationRequested)
            {
                cts.Cancel();
                yield break;
            }

            yield return false;
        }

        if (Settings.IdleMouseDelay.Value == 0)
        {
            yield break;
        }

        var mousePos = Mouse.GetCursorPosition();
        var sw = Stopwatch.StartNew();
        yield return false;
        while (true)
        {
            if (MoveCancellationRequested)
            {
                cts.Cancel();
                yield break;
            }

            var newPos = Mouse.GetCursorPosition();
            if (mousePos != newPos)
            {
                mousePos = newPos;
                sw.Restart();
            }
            else if (sw.ElapsedMilliseconds >= Settings.IdleMouseDelay.Value)
            {
                yield break;
            }
            else
            {
                yield return false;
            }
        }
    }

    private IEnumerable<bool> MoveItemsToStash(List<ServerInventory.InventSlotItem> items)
    {
        var cts = new CancellationTokenSource();
        foreach (var _ in MoveItemsCommonPreamble(cts))
        {
            yield return false;
        }

        if (cts.Token.IsCancellationRequested)
        {
            yield break;
        }

        var prevMousePos = Mouse.GetCursorPosition();
        foreach (var item in items)
        {
            if (MoveCancellationRequested)
            {
                yield break;
            }

            if (!CheckIgnoreCells(item))
            {
                if (!InGameState.IngameUi.InventoryPanel.IsVisible)
                {
                    DebugWindow.LogMsg("HighlightedItems: Inventory Panel closed, aborting loop");
                    break;
                }

                if (!IsStashTargetOpened)
                {
                    DebugWindow.LogMsg("HighlightedItems: Target inventory closed, aborting loop");
                    break;
                }

                foreach (var _ in MoveItem(item.GetClientRect().Center))
                {
                    yield return false;
                }
            }
        }

        Mouse.moveMouse(prevMousePos);
        foreach (var _ in Wait(MouseMoveDelay, true))
        {
            yield return false;
        }
    }

    private bool IsStashTargetOpened =>
        !Settings.VerifyTargetInventoryIsOpened
        || InGameState.IngameUi.StashElement.IsVisible
        || InGameState.IngameUi.SellWindow.IsVisible
        || InGameState.IngameUi.SellWindowHideout.IsVisible
        || InGameState.IngameUi.TradeWindow.IsVisible
        || InGameState.IngameUi.GuildStashElement.IsVisible;

    private bool IsStashSourceOpened =>
        !Settings.VerifyTargetInventoryIsOpened
        || InGameState.IngameUi.StashElement.IsVisible
        || InGameState.IngameUi.GuildStashElement.IsVisible;

    private IEnumerable<bool> MoveItemsToInventory(IList<NormalInventoryItem> items)
    {
        var cts = new CancellationTokenSource();
        foreach (var _ in MoveItemsCommonPreamble(cts))
        {
            yield return false;
        }

        if (cts.Token.IsCancellationRequested)
        {
            yield break;
        }

        var prevMousePos = Mouse.GetCursorPosition();
        foreach (var item in items)
        {
            if (MoveCancellationRequested)
            {
                yield break;
            }

            if (!IsStashSourceOpened)
            {
                DebugWindow.LogMsg("HighlightedItems: Stash Panel closed, aborting loop");
                break;
            }

            if (!InGameState.IngameUi.InventoryPanel.IsVisible)
            {
                DebugWindow.LogMsg("HighlightedItems: Inventory Panel closed, aborting loop");
                break;
            }

            if (IsInventoryFull())
            {
                DebugWindow.LogMsg("HighlightedItems: Inventory full, aborting loop");
                break;
            }

            foreach (var _ in MoveItem(item.GetClientRect().Center))
            {
                yield return false;
            }
        }

        Mouse.moveMouse(prevMousePos);
        foreach (var _ in Wait(MouseMoveDelay, true))
        {
            yield return false;
        }
    }


    private IList<NormalInventoryItem> GetHighlightedItems(Inventory stash)
    {
        try
        {
            var stashItems = stash.VisibleInventoryItems;

            var highlightedItems = stashItems
                .Where(stashItem => stashItem.isHighlighted)
                .ToList();

            return highlightedItems.ToList();
        }
        catch
        {
            return new List<NormalInventoryItem>();
        }
    }

    private bool IsInventoryFull()
    {
        var inventoryItems = GameController.IngameState.ServerData.PlayerInventories[0].Inventory.InventorySlotItems;

        // quick sanity check
        if (inventoryItems.Count < 12)
        {
            return false;
        }

        // track each inventory slot
        bool[,] inventorySlot = new bool[12, 5];

        // iterate through each item in the inventory and mark used slots
        foreach (var inventoryItem in inventoryItems)
        {
            int x = inventoryItem.PosX;
            int y = inventoryItem.PosY;
            int height = inventoryItem.SizeY;
            int width = inventoryItem.SizeX;
            for (int row = x; row < x + width; row++)
            {
                for (int col = y; col < y + height; col++)
                {
                    inventorySlot[row, col] = true;
                }
            }
        }

        // check for any empty slots
        for (int x = 0; x < 12; x++)
        {
            for (int y = 0; y < 5; y++)
            {
                if (inventorySlot[x, y] == false)
                {
                    return false;
                }
            }
        }

        // no empty slots, so inventory is full
        return true;
    }

    private static readonly TimeSpan KeyDelay = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan MouseMoveDelay = TimeSpan.FromMilliseconds(20);
    private TimeSpan MouseDownDelay => TimeSpan.FromMilliseconds(5 + Settings.ExtraDelay.Value);
    private static readonly TimeSpan MouseUpDelay = TimeSpan.FromMilliseconds(5);

    private IEnumerable<bool> MoveItem(SharpDX.Vector2 itemPosition)
    {
        itemPosition += WindowOffset;
        Keyboard.KeyDown(Keys.LControlKey);
        foreach (var _ in Wait(KeyDelay, true))
        {
            yield return false;
        }

        Mouse.moveMouse(itemPosition);
        foreach (var _ in Wait(MouseMoveDelay, true))
        {
            yield return false;
        }

        Mouse.LeftDown();
        foreach (var _ in Wait(MouseDownDelay, true))
        {
            yield return false;
        }

        Mouse.LeftUp();
        foreach (var _ in Wait(MouseUpDelay, true))
        {
            yield return false;
        }

        Keyboard.KeyUp(Keys.LControlKey);
        foreach (var _ in Wait(KeyDelay, false))
        {
            yield return false;
        }
    }

    private IEnumerable<bool> Wait(TimeSpan period, bool canUseThreadSleep)
    {
        if (canUseThreadSleep && Settings.UseThreadSleep)
        {
            Thread.Sleep(period);
            yield break;
        }

        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < period)
        {
            yield return false;
        }
    }

    private bool IsButtonPressed(SharpDX.RectangleF buttonRect)
    {
        if (Control.MouseButtons == MouseButtons.Left &&
            CanClickButtons)
        {
            if (buttonRect.Contains(Mouse.GetCursorPosition() - WindowOffset))
            {
                return true;
            }
        }

        return false;
    }

    private bool CanClickButtons => !Settings.VerifyButtonIsNotObstructed || !ImGui.GetIO().WantCaptureMouse;

    private bool CheckIgnoreCells(ServerInventory.InventSlotItem inventItem)
    {
        var inventPosX = inventItem.PosX;
        var inventPosY = inventItem.PosY;

        if (inventPosX < 0 || inventPosX >= 12)
            return true;
        if (inventPosY < 0 || inventPosY >= 5)
            return true;

        return Settings.IgnoredCells[inventPosY, inventPosX]; //No need to check all item size
    }

    private void DrawIgnoredCellsSettings()
    {
        ImGui.BeginChild("##IgnoredCellsMain", new Vector2(ImGui.GetContentRegionAvail().X, 204f), true,
            ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.Text("Ignored Inventory Slots (checked = ignored)");

        var contentRegionAvail = ImGui.GetContentRegionAvail();
        ImGui.BeginChild("##IgnoredCellsCels", new Vector2(contentRegionAvail.X, contentRegionAvail.Y), true,
            ImGuiWindowFlags.NoScrollWithMouse);

        for (int y = 0; y < 5; ++y)
        {
            for (int x = 0; x < 12; ++x)
            {
                bool isCellIgnored = Settings.IgnoredCells[y, x];
                if (ImGui.Checkbox($"##{y}_{x}IgnoredCells", ref isCellIgnored))
                    Settings.IgnoredCells[y, x] = isCellIgnored;
                if (x < 11)
                    ImGui.SameLine();
            }
        }

        ImGui.EndChild();
        ImGui.EndChild();
    }
}