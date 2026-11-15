using System;
using System.Collections.Generic;
using NunuWindboundBard.Core;

static void Check(bool ok, string message)
{
    if (!ok) throw new Exception("FAIL " + message);
}

var party = new[]
{
    new PartyChatMemberIdentity(100, "Nunubu Nubu", 39, "Omega"),
    new PartyChatMemberIdentity(200, "Edea Sonata", 39, "Omega"),
    new PartyChatMemberIdentity(300, "Veits Lyre", 80, "Spriggan")
};
Check(PartyChatSenderMatcher.TryResolve("Edea Sonata", null, 0, party, out var id, out _) && id == 200,
    "ordinary FFXIV sender text");
Check(PartyChatSenderMatcher.TryResolve("Edea Sonata@Omega", null, 0, party, out id, out _) && id == 200,
    "world suffix joined by @");
Check(PartyChatSenderMatcher.TryResolve("Veits Lyre Spriggan", null, 0, party, out id, out _) && id == 300,
    "world suffix separated by space");
Check(PartyChatSenderMatcher.TryResolve("Veits Lyre (Spriggan)", null, 0, party, out id, out _) && id == 300,
    "world suffix in parentheses");
Check(PartyChatSenderMatcher.TryResolve("Veits Lyre·Spriggan", null, 0, party, out id, out _) && id == 300,
    "world suffix via mid-dot");
Check(PartyChatSenderMatcher.TryResolve("sender link", "Edea Sonata", 39, party, out id, out _) && id == 200,
    "game-provided player link identity");
Check(!PartyChatSenderMatcher.TryResolve("Edea Sonata", "Edea Sonata", 80, party, out _, out _),
    "wrong world ID must not impersonate Edea");
Check(!PartyChatSenderMatcher.TryResolve("Not In Party", null, 0, party, out _, out _),
    "outsider rejected");
Check(!PartyChatSenderMatcher.TryResolve("Edea Sonata@Spriggan", null, 0, party, out _, out _),
    "wrong world name rejected");
Check(!PartyChatSenderMatcher.TryResolve("Edea Sonata - spam", null, 0, party, out _, out _),
    "arbitrary prefix rejected");
var ambiguous = new List<PartyChatMemberIdentity>(party)
{
    new PartyChatMemberIdentity(400, "Edea Sonata", 80, "Spriggan")
};
Check(!PartyChatSenderMatcher.TryResolve("Edea Sonata", null, 0, ambiguous, out _, out _),
    "identical names without a world are ambiguous");
Check(PartyChatSenderMatcher.TryResolve("Edea Sonata@Omega", null, 0, ambiguous, out id, out _) && id == 200,
    "same name but unique world resolved");
Check(PartyChatSenderMatcher.TryResolve("sender link", "Edea Sonata", 80, ambiguous, out id, out _) && id == 400,
    "player link world ID resolves duplicate names");
Console.WriteLine("PASS: ordinary and cross-world senders, player link world IDs, outsider/ambiguity rejection.");
