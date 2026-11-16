using NunuWindboundBard.Core;

int checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
    Console.WriteLine("PASS " + label);
    checks++;
}

const string macro = """
  // Comment
  /macrolock
  /echo start <wait.1.5>
  //m100
  /wait 2
  /windbound hud
  //m200
  unsolicited chat
  """;
var sequence = ShortcutCommands.Parse(macro, ShortcutMode.All, 0);
Check(sequence.Locked && sequence.Steps.Length == 5, "Multiline macro: skip comments, invalid macro slots and bare chat");
Check(sequence.Steps[0].Command == "/echo start" && sequence.Steps[1].Seconds == 1.5f,
    "Inline waits preserve the actual slash command");
Check(sequence.Steps[2].Kind == ShortcutStepKind.Macro && sequence.Steps[2].Macro == 100 &&
    sequence.Steps[3].Seconds == 2 && sequence.Steps[4].Command == "/windbound hud", "Command order, shared macro and wait");
Check(ShortcutCommands.Parse(macro, ShortcutMode.Cycle, 1).Steps.Single().Macro == 100, "Cycle selects a single actionable line");
Check(ShortcutCommands.Parse(macro, ShortcutMode.Cycle, 3).Steps.Single().Command == "/echo start", "Cycle wraps");
Check(ShortcutCommands.Parse(macro, ShortcutMode.Random, 0).Steps.Single().Kind != ShortcutStepKind.Wait, "Random selects an action");
Check(ShortcutCommands.Parse("/wait Infinity\n/wait -1\n/m\u0000status", ShortcutMode.All, 0).Steps.Length == 0,
    "Malformed waits and control characters are ignored");

var partySequence = ShortcutCommands.Parse("//pm07\n//PM107\n//m107\n/wait 2\n//pm199", ShortcutMode.All, 0);
Check(partySequence.Steps.Select(step => (step.Kind, step.Macro)).SequenceEqual(new[]
{
    (ShortcutStepKind.PartyMacro, 7), (ShortcutStepKind.PartyMacro, 107),
    (ShortcutStepKind.Macro, 107), (ShortcutStepKind.Wait, 0), (ShortcutStepKind.PartyMacro, 199),
}), "Party slots and local macros retain their target and sequence order");
Check(ShortcutCommands.Parse("//pm7\n//m7", ShortcutMode.Cycle, 0).Steps.Single().Kind == ShortcutStepKind.PartyMacro &&
    ShortcutCommands.Parse("//pm7\n//m7", ShortcutMode.Cycle, 1).Steps.Single().Kind == ShortcutStepKind.Macro,
    "Cycle can select party or local macro steps independently");
Check(ShortcutCommands.Parse("//pm0\n//pm99\n//pm100", ShortcutMode.All, 0).Steps.Length == 3 &&
    ShortcutCommands.Parse("//pm200\n//pm-1\n//pm+7\n//pm7 /echo injection\n//pm7\u0000", ShortcutMode.All, 0).Steps.Length == 0,
    "Party macro steps enforce the native slot range and reject additional text");

var model = new ShortcutBarSettings();
var bar = ShortcutBarSettings.CreateExample();
bar.FunctionKey = 3;
var set = new ShortcutConditionSet
{
    Required = ShortcutContext.Performing | ShortcutContext.Follower,
    Excluded = ShortcutContext.InCombat,
};
model.Conditions.Add(set);
model.Bars.Add(bar);
bar.ConditionId = set.Id;
Check(model.IsAllowed(bar, ShortcutContext.Performing | ShortcutContext.Follower), "Matching rule shows bar");
Check(!model.IsAllowed(bar, ShortcutContext.Performing), "Required conditions all match by default");
Check(!model.IsAllowed(bar, ShortcutContext.Performing | ShortcutContext.Follower | ShortcutContext.InCombat), "Exclusions take precedence");
set.MatchAny = true;
Check(model.IsAllowed(bar, ShortcutContext.Performing), "Any rule accepts an individual match");
Check(ShortcutBarSettings.TryImport(model.Export(bar), out var shared) && shared?.Bar is not null, "Share round trip");
var imported = shared?.Bar ?? throw new InvalidOperationException("No imported bar");
Check(!imported.Enabled && imported.FunctionKey == 0 && imported.Id != bar.Id, "Imported bars require explicit activation and a new hotkey");
Check(shared!.Conditions?.Id == imported.ConditionId && imported.ConditionId != set.Id &&
    shared.Conditions!.Matches(ShortcutContext.Follower), "Imported condition sets are independent and preserved");
Check(imported.Items[2].Items[1].Commands == "/windbound midipanic" && imported.Items[2].Id != bar.Items[2].Id,
    "Nested categories preserve commands and receive fresh IDs");
var shortcut = new ShortcutItem { Name = "a###b", CustomIcon = "../evil.png" };
Check(ShortcutBarSettings.TryImport(ShortcutBarSettings.Export(shortcut), out var item) &&
    item?.Item is { CustomIcon: "", Name: "a   b" }, "Import sanitizes widget labels and icon paths");
Check(ShortcutBarSettings.CleanIconName("My Icon-42.png") == "My Icon-42.png" &&
    ShortcutBarSettings.CleanIconName("C:/X.png") == "", "Custom images use local filenames");
Check(!ShortcutBarSettings.TryImport("{\"Format\":\"QoLBar\"}", out _) &&
    !ShortcutBarSettings.TryImport(new string('x', ShortcutBarSettings.MaxShareLength + 1), out _), "Reject foreign and oversize clipboard data");
Console.WriteLine($"{checks} shortcut bar checks passed.");
