using System;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.System.String;

namespace NunuWindboundBard.Core;

/// <summary>Native game chat submission, restricted to explicit built-in emote
/// commands. Invoked only on Dalamud's framework thread after safety checks.</summary>
public static unsafe class ShowmasterEmoteSender
{
    public static bool TrySend(string emote, out string reason)
    {
        if (!ShowmasterCodec.IsAllowed(emote))
        { reason = "Unapproved emote action."; return false; }
        try
        {
            var ui = UIModule.Instance();
            if (ui == null) { reason = "Game chat module not available."; return false; }
            var command = Utf8String.FromString("/" + emote + " motion");
            if (command == null) { reason = "Cannot allocate emote command."; return false; }
            try { ui->ProcessChatBoxEntry(command); }
            finally { command->Dtor(true); }
            reason = "Submitted /" + emote + " motion to the game.";
            return true;
        }
        catch (Exception error)
        {
            reason = "Game rejected emote command: " + error.Message;
            return false;
        }
    }
}
