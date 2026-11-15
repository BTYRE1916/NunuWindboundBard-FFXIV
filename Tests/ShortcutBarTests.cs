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
