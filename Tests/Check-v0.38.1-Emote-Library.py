from pathlib import Path
import re
root=Path(__file__).resolve().parents[1]
src=root/'src/NunuWindboundBard'
needs={
    'Plugin.cs':['IDataManager Data','IUnlockState UnlockState','main.PollShowmaster'],
    'Core/ShowmasterEmoteCatalog.cs':['GetExcelSheet<Emote>()','IsEmoteUnlocked(emote)','SafeCommand','AvailabilityMask'],
    'Core/Showmaster.cs':['EmoteId','EncodeV2','TryDecodeV2','SkippedUnavailable','TickWithSteps'],
    'Core/ShowmasterEmoteSender.cs':['SafeCommand','TrySend(ShowmasterEmoteEntry entry','ProcessChatBoxEntry'],
    'Core/GamePartyCueProtocol.cs':['ShowmasterStartV2','ShowmasterCheck','ShowmasterAvailability', 'ShowmasterAvailabilityReply'],
    'MainWindow.Showmaster.cs':['CHECK BAND EMOTES','ReceiveShowmasterCheck','ReceiveShowmasterAvailability',
                                  'StartGroupShowmaster','ShowmasterUseBowFallback','showCatalog.Unlocked','ShowmasterEmoteSelectable'],
    'Core/GamePartyChatSync.cs':['GamePartyCueKind.ShowmasterAvailabilityReply'],
    'MainWindow.GameEnsemble.cs':['GamePartyCueKind.ShowmasterStartV2',
                                   'GamePartyCueKind.ShowmasterAvailabilityReply','ReceiveShowmasterCheck'],
    'Configuration.cs':['ShowmasterUseBowFallback'],
    'NunuWindboundBard.csproj':['0.38.1.0'],
}
for path,need in needs.items():
    text=(src/path).read_text(encoding='utf-8')
    for word in need:
        assert word in text, f'{path} missing {word}'
# Never include arbitrary macro execution, memory edits or game unlock modification.
text=(src/'Core/ShowmasterEmoteCatalog.cs').read_text(encoding='utf-8')
assert 'IsEmoteUnlocked' in text
for forbidden in ['SetEmoteUnlocked', 'SetUnlockLink', 'SetUnlockLinkUnlocked', 'IsEmoteUnlocked =']:
    assert forbidden not in text
sender=(src/'Core/ShowmasterEmoteSender.cs').read_text(encoding='utf-8')
assert 'SafeCommand' in sender and ' motion' in sender
codec=(src/'Core/Showmaster.cs').read_text(encoding='utf-8')
assert 'packed.Length > 72' in codec and 'steps.Count is < 1 or > 12' in codec
assert 'StageLeader' in (src/'MainWindow.Showmaster.cs').read_text(encoding='utf-8')
assert (root/'NunuWindboundBard.sln').exists()
print('PASS — v0.38.1 native emote library, member permissions, group capability checks, and playlist isolation')
