using System;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.System.String;

namespace NunuWindboundBard.Core;

/// <summary>Native game chat submission, restricted to explicit built-in emote
/// commands. Invoked only on Dalamud's framework thread after safety checks.</summary>
public static unsafe class ShowmasterEmoteSender
{
    /// <summary>Only commands obtained from FFXIV's own emote sheet are allowed.</summary>
    public static bool TrySend(ShowmasterEmoteEntry entry, out string reason)
    {
        if (!entry.Unlocked || !ShowmasterEmoteCatalog.SafeCommand(entry.Command))
        { reason = "Emote is locked or does not have a safe player slash command."; return false; }
        return Submit(entry.Command, out reason);
    }

    /// <summary>Legacy v0.38 fixed commands; new routines use emote-sheet IDs.</summary>
    public static bool TrySend(string emote, out string reason)
    {
        if (!ShowmasterCodec.IsAllowed(emote))
        { reason = "Unapproved emote action."; return false; }
        return Submit("/" + emote, out reason);
    }

    private static bool Submit(string slashCommand, out string reason)
    {
        if (!ShowmasterEmoteCatalog.SafeCommand(slashCommand))
        { reason = "Invalid emote slash command."; return false; }
        try
        {
            var ui = UIModule.Instance();
            if (ui == null) { reason = "Game chat module not available."; return false; }
            var command = Utf8String.FromString(slashCommand + " motion");
            if (command == null) { reason = "Cannot allocate emote command."; return false; }
            try { ui->ProcessChatBoxEntry(command); }
            finally { command->Dtor(true); }
            reason = "Submitted " + slashCommand + " motion to the game.";
            return true;
        }
        catch (Exception error)
        {
            reason = "Game rejected emote command: " + error.Message;
            return false;
        }
    }
}
