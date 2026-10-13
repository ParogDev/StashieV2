using ExileCore;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using Stashie.Classes;
using System;
using System.Collections;
using System.Linq;
using System.Windows.Forms;
using static Stashie.StashieCore;

namespace Stashie.Compartments;

internal class ActionsHandler
{
    public static int GetIndexOfCurrentVisibleTab() => Main.GameController.Game.IngameState.IngameUi.StashElement.IndexVisibleStash;

    public static void CleanUp()
    {
        Input.KeyUp(Keys.LControlKey);
        Input.KeyUp(Keys.Shift);
    }

    public static void HandleSwitchToTabEvent(object tab)
    {
        switch (tab)
        {
            case int index:
                Main.CoroutineWorker = new Coroutine(ActionCoRoutine.ProcessSwitchToTab(index), Main, CoroutineName);
                break;

            case string name:
                if (!RenamedAllStashNames.Contains(name))
                {
                    HudLog.Fault($"can't switch to tab '{name}': no stash tab has that name ({RenamedAllStashNames.Count} names known)", "switch-tab-name");
                    break;
                }

                var tempIndex = RenamedAllStashNames.IndexOf(name);
                Main.CoroutineWorker = new Coroutine(ActionCoRoutine.ProcessSwitchToTab(tempIndex), Main, CoroutineName);
                HudLog.Quiet($"Switching to tab with index: {tempIndex} ('{name}').");
                break;

            default:
                HudLog.Fault($"switch to tab: the argument is {tab?.GetType().Name ?? "null"}, not a string or an integer", "switch-tab-arg");
                break;
        }

        Core.ParallelRunner.Run(Main.CoroutineWorker);
    }

    public static IEnumerator SwitchToTab(int tabIndex)
    {
        Main.VisibleStashIndex = GetIndexOfCurrentVisibleTab();
        var travelDistance = Math.Abs(tabIndex - Main.VisibleStashIndex);
        if (travelDistance == 0)
            yield break;

        yield return SwitchToTabViaArrowKeys(tabIndex);

        yield return Delay();
    }

    public static IEnumerator SwitchToTabViaArrowKeys(int tabIndex, int numberOfTries = 1)
    {
        if (numberOfTries >= 3)
        {
            yield break;
        }

        var indexOfCurrentVisibleTab = GetIndexOfCurrentVisibleTab();
        var travelDistance = tabIndex - indexOfCurrentVisibleTab;
        var tabIsToTheLeft = travelDistance < 0;
        travelDistance = Math.Abs(travelDistance);

        if (tabIsToTheLeft)
        {
            yield return PressKey(Keys.Left, travelDistance);
        }
        else
        {
            yield return PressKey(Keys.Right, travelDistance);
        }

        if (GetIndexOfCurrentVisibleTab() != tabIndex)
        {
            yield return Delay(20);
            yield return SwitchToTabViaArrowKeys(tabIndex, numberOfTries + 1);
        }
    }

    public static IEnumerator PressKey(Keys key, int repetitions = 1)
    {
        for (var i = 0; i < repetitions; i++)
        {
            yield return Input.KeyPress(key);
        }
    }

    public static IEnumerator Delay(int ms = 0)
    {
        yield return new WaitTime(Main.Settings.ExtraDelay.Value + ms);
    }

    public static InventoryType GetTypeOfCurrentVisibleStash()
    {
        var stashPanelVisibleStash = Main.GameController.Game.IngameState.IngameUi?.StashElement?.VisibleStash;
        return stashPanelVisibleStash?.InvType ?? InventoryType.InvalidInventory;
    }

    public static IEnumerator StashItemsIncrementer()
    {
        Main.CoroutineIteration++;

        yield return StashItems();
    }

    public static IEnumerator StashItems()
    {
        Main.PublishEvent("stashie_start_drop_items", null);

        Main.VisibleStashIndex = GetIndexOfCurrentVisibleTab();
        if (Main.VisibleStashIndex < 0)
        {
            HudLog.Fault($"VisibleStashIndex was invalid: {Main.VisibleStashIndex}, stopping.", "visible-stash-index");
            yield break;
        }

        var itemsSortedByStash = Main.DropItems.OrderBy(x => x.SkipSwitchTab || x.StashIndex == Main.VisibleStashIndex ? 0 : 1).ThenBy(x => x.StashIndex).ToList();
        
        // If bulk stash is enabled, filter out duplicates of stackable currency and fragments so we can right-click stash them in bulk
        if (Main.Settings.EnableBulkStash)        
            itemsSortedByStash = itemsSortedByStash
                .GroupBy(x => (x.ItemData.Name, x.ItemData.ClassName == "StackableCurrency" || x.ItemData.ClassName == "Fragment"))
                .Select(g => g.First())
                .ToList();

        Input.KeyDown(Keys.LControlKey);
        HudLog.Quiet($"Want to drop {itemsSortedByStash.Count} items.");
        foreach (var stashResult in itemsSortedByStash)
        {
            Main.CoroutineIteration++;
            Main.CoroutineWorker?.UpdateTicks(Main.CoroutineIteration);
            var maxTryTime = Main.DebugTimer.ElapsedMilliseconds + 2000;

            //move to correct tab
            if (!stashResult.SkipSwitchTab)
                yield return SwitchToTab(stashResult.StashIndex);

            yield return new WaitFunctionTimed(() => Main.GameController.IngameState.IngameUi.StashElement.AllInventories[Main.VisibleStashIndex] != null, true, 2000,
                $"Error while loading tab, Index: {Main.VisibleStashIndex}"); //maybe replace waittime with Setting option

            yield return new WaitFunctionTimed(() => GetTypeOfCurrentVisibleStash() != InventoryType.InvalidInventory, true, 2000,
                $"Error with inventory type, Index: {Main.VisibleStashIndex}"); //maybe replace waittime with Setting option

            yield return StashItem(stashResult);

            Main.DebugTimer.Restart();
        }
    }

    public static IEnumerator StashItem(FilterResult stashResult)
    {
        Input.SetCursorPos(stashResult.ClickPos + Main.ClickWindowOffset);
        yield return new WaitTime(Main.Settings.HoverItemDelay);
        var shiftUsed = false;

        if (stashResult.ShiftForStashing)
        {
            Input.KeyDown(Keys.ShiftKey);
            shiftUsed = true;
        }        
            
        // If bulk stash is enabled, we ctrl-right click stackable currency and fragments to stash them in bulk
        if (Main.Settings.EnableBulkStash && (stashResult.ItemData.ClassName == "StackableCurrency" || stashResult.ItemData.ClassName == "Fragment")) 
            Input.Click(MouseButtons.Right);
        else 
            Input.Click(MouseButtons.Left);
        
        if (Input.IsKeyDown(Keys.ShiftKey) || shiftUsed)           
            Input.KeyUp(Keys.ShiftKey);

        yield return new WaitTime(Main.Settings.StashItemDelay);
    }
}