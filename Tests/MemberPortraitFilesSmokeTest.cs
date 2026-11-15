using System;
using System.Collections.Generic;
using System.IO;
using NunuWindboundBard.Core;

static void Check(bool yes, string name)
{
    if (!yes) throw new Exception("FAILED: " + name);
}

ulong id = 0x1234ABCDEF123456;
string name = MemberPortraitFiles.NewName(id);
Check(name.StartsWith("1234ABCDEF123456_"), "filename based on stable ContentId");
Check(name.EndsWith(".png"), "PNG thumbnail name");
Check(name.Length == 16 + 1 + 32 + 4, "filename length");
string baseDir = Path.Combine(Path.GetTempPath(), "PortraitTest");
var entries = new Dictionary<string, string> { [MemberPortraitFiles.ContentKey(id)] = name };
Check(MemberPortraitFiles.Resolve(baseDir, entries, id) ==
    Path.Combine(baseDir, "BandPortraits", name), "accepted owned filename");
entries[MemberPortraitFiles.ContentKey(id)] = "../../secrets.png";
Check(MemberPortraitFiles.Resolve(baseDir, entries, id) is null, "reject traversal");
entries[MemberPortraitFiles.ContentKey(id)] =
    "0000000000000001_1234567890abcdef1234567890abcdef.png";
Check(MemberPortraitFiles.Resolve(baseDir, entries, id) is null, "reject another member's filename");
Check(MemberPortraitFiles.Resolve(baseDir, entries, 0) is null, "reject empty identity");
Console.WriteLine("PASS: local portrait cache filenames, stable member identity and path validation.");
