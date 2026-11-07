using System;
using System.Collections.Concurrent;
using System.Linq;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.System.String;

namespace NunuWindboundBard.Core;

/// <summary>
/// FFXIV party chat is a command transport only (never a musical start clock).
/// Only a verified party leader can request file/instrument changes. Replies
/// must resolve to a unique party member, including cross-world player links.
/// </summary>
public readonly record struct PartyCueEnvelope(GamePartyCue Cue, ulong SenderContentId);

public sealed unsafe class GamePartyChatSync : IDisposable
{
    private readonly IChatGui chat;
    private readonly IPartyList party;
    private readonly IObjectTable objects;
    private readonly ConcurrentQueue<PartyCueEnvelope> pending = new();
    private long seen;
    private long accepted;
    private long rejected;
    private string lastChatSender = "-";
    private string lastCue = "-";
    private string lastRejection = "No Windbound party commands rejected.";
    private string lastChannel = "-";
    public bool Enabled { get; set; }
    public bool ReceiveEnabled { get; set; }
    public string Status { get; private set; } = "Party chat synchronisation off.";
    public string Diagnostics =>
        $"Party sync enabled={Enabled}; receiver={ReceiveEnabled}; party members={party.Length}; " +
        $"leader index={party.PartyLeaderIndex}; queued={pending.Count}\n" +
        $"Windbound messages seen={seen}; accepted={accepted}; rejected={rejected}; " +
        $"last channel={lastChannel}\n" +
        $"Last sender: {lastChatSender}; last cue: {lastCue}\n" +
        $"Last rejection: {lastRejection}\nTransport status: {Status}";

    public GamePartyChatSync(IChatGui chat, IPartyList party, IObjectTable objects)
    {
        this.chat = chat;
        this.party = party;
        this.objects = objects;
        chat.ChatMessage += OnChatMessage;
    }

    private void Reject(string reason)
    {
        rejected++;
        lastRejection = reason;
        Status = "Windbound party command ignored: " + reason;
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        // Read the type first: do not respond to echoes in tells, say, linkshells
        // or channels that merely contain a pasted !windbound string.
        if (message.LogKind is not (XivChatType.Party or XivChatType.CrossParty)) return;
        try
        {
            string rawText = message.Message.TextValue?.Trim() ?? "";
            if (!rawText.StartsWith(GamePartyCueProtocol.Prefix, StringComparison.OrdinalIgnoreCase)) return;
            seen++;
            lastChannel = message.LogKind.ToString();
            lastCue = rawText.Length > 42 ? rawText[..42] + "..." : rawText;
            lastChatSender = message.Sender.TextValue ?? "";
            if (!Enabled || !ReceiveEnabled)
            { Reject("This client's party-command receiver is disabled. Select Follower and enable Native Game Ensemble + Follow leader."); return; }
            // Another plugin may hide party chat from the default log with
            // PreventOriginal(). We still process a verified real party-leader
            // message; the user's local chat display settings are irrelevant.
            if (party.Length < 2 || party.PartyLeaderIndex >= (uint)party.Length)
            { Reject("Cannot identify party leader in the current FFXIV party list."); return; }
            var leader = party[(int)party.PartyLeaderIndex];
            if (leader is null || leader.ContentId == 0)
            { Reject("Party leader has no stable ContentId in the party list."); return; }
            if (!GamePartyCueProtocol.TryParse(rawText, out var cue))
            { Reject("Malformed or incompatible command. Check that both clients run the same Windbound version."); return; }

            var playerLink = message.Sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
            string? playerName = playerLink?.PlayerName;
            uint linkWorldId = playerLink is null ? 0 : playerLink.World.RowId;
            var roster = party.Where(m => m is not null && m.ContentId != 0)
                .Select(m =>
                {
                    string world = "";
                    try { world = m.World.Value.Name.ToString(); }
                    catch { /* World name unavailable: player link ID still verifies when present. */ }
                    return new PartyChatMemberIdentity(m.ContentId, m.Name.TextValue, m.World.RowId, world);
                }).ToArray();
            if (!PartyChatSenderMatcher.TryResolve(lastChatSender, playerName, linkWorldId, roster,
                out ulong senderId, out string error))
            { Reject(error); return; }

            bool isReply = cue.Kind is GamePartyCueKind.Ready or GamePartyCueKind.Failed or
                GamePartyCueKind.NativeFileReady or GamePartyCueKind.NativeFileFailed or
                GamePartyCueKind.NativeTracksReady or GamePartyCueKind.NativeTracksFailed or
                GamePartyCueKind.StageReport;
            if (!isReply && senderId != leader.ContentId)
            { Reject("Command sender is a party member, but is NOT the actual party leader."); return; }
            if (isReply && senderId == 0)
            { Reject("Could not verify the follower sending the READY/FAIL reply."); return; }
            pending.Enqueue(new PartyCueEnvelope(cue, senderId));
            accepted++;
            Status = "Accepted verified " + (cue.Kind switch
            {
                GamePartyCueKind.NativeSetInstruments => "SET PARTY INSTRUMENTS",
                GamePartyCueKind.NativeFile => "SYNC MIDI FILE",
                GamePartyCueKind.NativeFileReady => "MIDI LOADED",
                GamePartyCueKind.NativeFileFailed => "MIDI FAILED",
                GamePartyCueKind.NativeSetTracks => "SET PARTY TRACKS",
                GamePartyCueKind.NativeTracksReady => "TRACKS READY",
                GamePartyCueKind.NativeTracksFailed => "TRACKS FAILED",
                GamePartyCueKind.StageProbe => "STAGE STATUS REQUEST",
                GamePartyCueKind.StageReport => "STAGE STATUS REPORT",
                GamePartyCueKind.NativeStopUnequip => "STOP + STOW",
                GamePartyCueKind.Ready => "INSTRUMENT READY",
                GamePartyCueKind.Failed => "INSTRUMENT FAILED",
                _ => cue.Kind.ToString()
            }) + $" from {lastChatSender}.";
        }
        catch (Exception ex)
        {
            Reject("Chat processing error: " + ex.Message);
        }
    }

    public bool TryDequeue(out PartyCueEnvelope cue) => pending.TryDequeue(out cue);

    public bool TryBroadcast(string text, out string reason)
    {
        if (!Enabled) { reason = "Enable Native Game Ensemble + follow party leader in both clients."; return false; }
        if (party.Length < 2) { reason = "Join an FFXIV party first."; return false; }
        if (party.PartyLeaderIndex >= (uint)party.Length ||
            party[(int)party.PartyLeaderIndex]?.ContentId != PartyRoster.GetLocalContentId(objects, party))
        { reason = "Only the actual FFXIV party leader can broadcast commands."; return false; }
        if (string.IsNullOrWhiteSpace(text) || text.Length > 130 ||
            !GamePartyCueProtocol.TryParse(text, out var cue) ||
            cue.Kind is GamePartyCueKind.Ready or GamePartyCueKind.Failed or
                GamePartyCueKind.NativeFileReady or GamePartyCueKind.NativeFileFailed or
                GamePartyCueKind.NativeTracksReady or GamePartyCueKind.NativeTracksFailed or
                GamePartyCueKind.StageReport)
        { reason = "Invalid leader party command."; return false; }
        return TrySendPartyMessage(text, out reason);
    }

    public bool TryReply(string text, out string reason)
    {
        if (!Enabled) { reason = "Party sync disabled."; return false; }
        if (party.Length < 2 || PartyRoster.GetLocalContentId(objects, party) == 0)
        { reason = "Cannot identify my character in the FFXIV party."; return false; }
        if (!GamePartyCueProtocol.TryParse(text, out var cue) ||
            cue.Kind is not (GamePartyCueKind.Ready or GamePartyCueKind.Failed or
                GamePartyCueKind.NativeFileReady or GamePartyCueKind.NativeFileFailed or
                GamePartyCueKind.NativeTracksReady or GamePartyCueKind.NativeTracksFailed or
                GamePartyCueKind.StageReport))
        { reason = "Invalid follower READY/FAIL reply."; return false; }
        return TrySendPartyMessage(text, out reason);
    }

    private bool TrySendPartyMessage(string text, out string reason)
    {
        try
        {
            var ui = UIModule.Instance();
            if (ui == null) { reason = "FFXIV chat interface is unavailable."; return false; }
            var entry = Utf8String.FromString("/p " + text);
            if (entry == null) { reason = "Could not allocate party chat command."; return false; }
            try { ui->ProcessChatBoxEntry(entry); }
            finally { entry->Dtor(true); }
            // Important: calling ProcessChatBoxEntry doesn't prove the server
            // delivered a message! Only a remote READY reply proves delivery.
            reason = "Submitted party command to FFXIV chat (not yet confirmed by recipients).";
            Status = reason;
            return true;
        }
        catch (Exception ex)
        {
            reason = "Could not submit FFXIV party command: " + ex.Message;
            Status = reason;
            return false;
        }
    }

    public void Dispose()
    {
        chat.ChatMessage -= OnChatMessage;
        while (pending.TryDequeue(out _)) { }
    }
}
