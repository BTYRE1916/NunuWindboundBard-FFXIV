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
Check(iconIds.All(x => x is >= 751 and <= 778), "Only instrument artwork (not generic Performance icon 750)");
Check(iconIds[0] == 751, "Harp uses its own icon, not generic Performance");
Check(iconIds[1] == 752, "Piano is not drawn with the Harp icon");
Check(iconIds[2] == 753, "Lute is not drawn with the Piano icon");
Check(iconIds[3] == 754, "Fiddle is not drawn with the Lute icon");
Check(iconIds[23] == 774, "Overdriven electric guitar icon");
Check(iconIds[^1] == 778, "Electric Guitar Special is the last playable icon");
for (int i = 0; i < InstrumentCatalog.All.Count; i++)
    Check(iconIds[i] == (uint)(751 + i), $"Instrument {i + 1} icon is correctly aligned");
Console.WriteLine("PASS: 28 instrument icons aligned (Harp=751..Guitar Special=778); Keep Current has none.");
