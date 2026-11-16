using System.Diagnostics;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using GameFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace NunuWindboundBard.Core;

internal sealed partial class ShortcutBarRuntime
{
    private readonly Plugin plugin;
    private ShortcutSequence? requested;
    private ShortcutSequence? running;
    private volatile string status = "";
    private volatile bool canActivate;
    private int stepIndex;
    private int clearRequested;
    private long readyAt;

    internal ShortcutBarRuntime(Plugin plugin) => this.plugin = plugin;

    internal string Status => status;
    internal bool CanActivate => canActivate;
    internal void Request(ShortcutSequence sequence)
    {
        if (sequence.Steps.Length == 0)
        {
            status = "This shortcut has no valid commands.";
            return;
        }
        Interlocked.Exchange(ref requested, sequence);
    }

    internal void Cancel()
    {
        Interlocked.Exchange(ref requested, null);
        Interlocked.Exchange(ref clearRequested, 1);
    }

    internal static ShortcutContext Context(Plugin plugin)
    {
        var context = (EnsembleRole)plugin.Config.EnsembleMode switch
        {
            EnsembleRole.Conductor => ShortcutContext.Conductor,
            EnsembleRole.Follower => ShortcutContext.Follower,
            _ => ShortcutContext.Solo,
        };
        if (plugin.IsPerforming) context |= ShortcutContext.Performing;
        if (Plugin.Conditions[ConditionFlag.InCombat]) context |= ShortcutContext.InCombat;
        if (Plugin.Conditions[ConditionFlag.BoundByDuty] || Plugin.Conditions[ConditionFlag.BoundByDuty56])
            context |= ShortcutContext.InDuty;
        if (Plugin.Party.Length > 1) context |= ShortcutContext.InParty;
        return context;
    }

    private static unsafe bool IsReadyForInput()
    {
        if (!CanRun()) return false;
        var game = GameFramework.Instance();
        if (game == null || game->WindowInactive) return false;
        var ui = RaptureAtkModule.Instance();
        return ui != null && !ui->IsTextInputActive();
    }

    private static bool CanRun() =>
        Plugin.ClientState.IsLoggedIn &&
        !Plugin.Conditions[ConditionFlag.BetweenAreas] &&
        !Plugin.Conditions[ConditionFlag.BetweenAreas51] &&
        !Plugin.Conditions[ConditionFlag.WatchingCutscene] &&
        !Plugin.Conditions[ConditionFlag.WatchingCutscene78] &&
        !Plugin.Conditions[ConditionFlag.OccupiedInCutSceneEvent];

    internal void Update(bool hasBars)
    {
        RefreshPartyMacroPermission();
        canActivate = hasBars && IsReadyForInput();
        var next = Interlocked.Exchange(ref requested, null);
        if (Interlocked.Exchange(ref clearRequested, 0) != 0)
        {
            running = null;
            status = "Shortcut sequence stopped.";
        }
        if (next is null && running is null) return;
        if (!CanRun())
        {
            running = null;
            status = "Shortcut sequence stopped while the game was unavailable.";
            return;
        }

        if (next is not null)
        {
            if (!canActivate)
                status = "Shortcut activation was cancelled when game input changed.";
            else if (running?.Locked == true && stepIndex < running.Steps.Length)
                status = "A locked shortcut sequence is running.";
            else
            {
                running = next;
                stepIndex = 0;
                readyAt = 0;
            }
        }

        if (running is not { } active || Stopwatch.GetTimestamp() < readyAt) return;
        if (stepIndex >= active.Steps.Length)
        {
            running = null;
            return;
        }

        var step = active.Steps[stepIndex++];
        if (step.Kind == ShortcutStepKind.Wait)
        {
            readyAt = Stopwatch.GetTimestamp() + (long)(step.Seconds * Stopwatch.Frequency);
            return;
        }

        try
        {
            if (step.Kind == ShortcutStepKind.PartyMacro)
            {
                BroadcastPartyMacro(step.Macro);
                return;
            }
            if (step.Kind == ShortcutStepKind.Macro)
                ExecuteMacro(step.Macro);
            else
                SendCommand(step.Command);
            status = "Shortcut command submitted.";
        }
        catch (Exception error)
        {
            running = null;
            status = "Shortcut stopped: " + error.Message;
            Plugin.Log.Warning("Shortcut command failed: " + error.Message);
        }
    }

    private static unsafe void SendCommand(string text)
    {
        if (Plugin.Commands.ProcessCommand(text)) return;
        var ui = UIModule.Instance();
        if (ui == null) throw new InvalidOperationException("FFXIV chat is unavailable.");
        var message = Utf8String.FromString(text);
        if (message == null) throw new InvalidOperationException("FFXIV could not allocate the command.");
        try { ui->ProcessChatBoxEntry(message); }
        finally { message->Dtor(true); }
    }

    private static unsafe RaptureMacroModule.Macro* FindMacro(int index)
    {
        var macros = RaptureMacroModule.Instance();
        if (macros == null) throw new InvalidOperationException("FFXIV macros are unavailable.");
        var macro = macros->GetMacro((uint)(index / 100), (uint)(index % 100));
        if (macro == null || macros->GetLineCount(macro) == 0)
            throw new InvalidOperationException("That FFXIV macro slot is empty.");
        return macro;
    }

    private static unsafe void ExecuteMacro(int index)
    {
        var shell = RaptureShellModule.Instance();
        if (shell == null) throw new InvalidOperationException("FFXIV macros are unavailable.");
        shell->ExecuteMacro(FindMacro(index));
    }
}
