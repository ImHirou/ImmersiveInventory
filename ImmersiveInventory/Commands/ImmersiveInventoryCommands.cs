using System;
using System.Linq;
using ImmersiveInventory.Config;
using ImmersiveInventory.Gui;
using ImmersiveInventory.Rules;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace ImmersiveInventory.Commands;

/// <summary> <c>.immersiveinventory reload | list | debug</c> </summary>
internal static class ImmersiveInventoryCommands
{
    private const string Domain = ImmersiveInventoryModSystem.Domain;

    public static void Register(ICoreClientAPI api, Func<bool> reload, RuleRegistry rules, CursorController controller)
    {
        api.ChatCommands.Create(Domain)
            .WithDescription(Lang.Get($"{Domain}:command-description"))
            .RequiresPrivilege(Privilege.chat)
            .BeginSubCommand("reload")
                .WithDescription(Lang.Get($"{Domain}:command-reload-description"))
                .HandleWith(_ => reload()
                    ? TextCommandResult.Success(Lang.Get($"{Domain}:command-reload-done", rules.Rules.Count))
                    : TextCommandResult.Error(Lang.Get($"{Domain}:command-reload-failed", ImmersiveInventoryConfig.FileName, ImmersiveInventoryModSystem.BrokenConfigFileName)))
            .EndSubCommand()
            .BeginSubCommand("list")
                .WithDescription(Lang.Get($"{Domain}:command-list-description"))
                .HandleWith(_ => TextCommandResult.Success(Lang.Get($"{Domain}:command-list-done",
                    string.Join(", ", rules.Rules.Select(rule => rule.Profile == null ? $"{rule.Code} ({Lang.Get($"{Domain}:debug-physics-off")})" : rule.Code)))))
            .EndSubCommand()
            .BeginSubCommand("debug")
                .WithDescription(Lang.Get($"{Domain}:command-debug-description"))
                .HandleWith(_ =>
                {
                    controller.Debug = !controller.Debug;
                    // Pick the stack on the cursor up again, so its rule is reported right away.
                    controller.Reset();
                    return TextCommandResult.Success(Lang.Get(controller.Debug ? $"{Domain}:command-debug-on" : $"{Domain}:command-debug-off"));
                })
            .EndSubCommand();
    }
}
