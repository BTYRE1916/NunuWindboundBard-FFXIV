using System;
using System.Linq;
using NunuWindboundBard.Core;

static void Check(bool value, string name)
{
    if (!value) throw new InvalidOperationException("Instrument icon test failed: " + name);
}

Check(InstrumentCatalog.All.Count == 28, "All 28 Bard performance instruments retained");
Check(InstrumentCatalog.GameIconId(0) == 0, "Keep current has no instrument icon");
Check(InstrumentCatalog.GameIconId(-1) == 0, "Negative ID has no icon");
Check(InstrumentCatalog.GameIconId(29) == 0, "Out-of-range ID has no icon");
var iconIds = InstrumentCatalog.All.Select(x => InstrumentCatalog.GameIconId(x.Id)).ToArray();
Check(iconIds.Length == iconIds.Distinct().Count(), "Icon IDs unique");
Check(iconIds.All(x => x is >= 750 and <= 779), "FFXIV performance icon range");
Check(iconIds[0] == 750 && iconIds[^1] == 777, "First and last instrument icons");
Console.WriteLine("PASS: Instrument game-icon mapping covers 28 playable instruments, with safe null fallback.");
