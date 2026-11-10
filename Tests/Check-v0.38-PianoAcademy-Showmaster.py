from pathlib import Path
import re
root=Path(__file__).resolve().parents[1]
src=root/'src/NunuWindboundBard'
need={
 'MainWindow.cs':['DrawPianoAcademy();','DrawShowmaster();','PracticeMidiReceived = OnAcademyMidi'],
 'Plugin.cs':['main.PollPianoAcademy();','main.PollShowmaster();'],
 'MainWindow.PianoAcademy.cs':['DrawAcademyPianoRoll','PianoPracticeMode.WaitForNotes','PianoPracticeMode.PlayAlong','PracticeMonitoring'],
 'MainWindow.Showmaster.cs':['ReceiveShowmasterStart','ShowmasterSafe','AllowLeaderShowmaster','StartGroupShowmaster'],
 'Core/LiveMidiInputService.cs':['!PracticeMonitoring && enabled','PracticeMidiReceived?.Invoke(raw)'],
 'Core/GamePartyCueProtocol.cs':['ShowmasterStart','ShowmasterStop','ShowmasterCodec.TryDecode'],
 'Core/Showmaster.cs':['ShowmasterCodec','ShowmasterPlayer','/'],
}
for name,parts in need.items():
 code=(src/name).read_text(encoding='utf-8')
 for part in parts:
  assert part in code, f'{name} missing {part}'
for forbidden in ['PerformanceOutput.', 'Plugin.PartyChat', 'NativePerformAction']:
 assert forbidden not in (src/'MainWindow.PianoAcademy.cs').read_text(), f'unsafe Academy reference {forbidden}'
assert '"/" + emote + " motion"' in (src/'Core/ShowmasterEmoteSender.cs').read_text()
assert 'ShowmasterCodec.IsAllowed(emote)' in (src/'Core/ShowmasterEmoteSender.cs').read_text()
assert 'CanNativeUnequip' in (src/'Core/InstrumentSwitcher.cs').read_text()
assert (root/'NunuWindboundBard.sln').exists()
print('PASS: source wiring, guardrails, native MIDI output isolation, former features preserved')
