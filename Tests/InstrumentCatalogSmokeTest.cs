using NunuWindboundBard.Core;

static void Expect(bool truth, string text)
{
    if (!truth) throw new Exception("Instrument catalog failure: " + text);
}

Expect(InstrumentCatalog.All.Count == 28, "expected 28 instruments");
Expect(InstrumentCatalog.All.Select(i => i.Id).SequenceEqual(Enumerable.Range(1, 28)), "instrument IDs must be contiguous 1–28");
Expect(InstrumentCatalog.Find(2)?.DisplayName == "Piano", "Piano is Perform row 2");
Expect(InstrumentCatalog.Find(20)?.DisplayName == "Violin", "Violin is Perform row 20");
Expect(InstrumentCatalog.Find(23)?.DisplayName == "Double Bass", "Double Bass is Perform row 23");
for (int i = 24; i <= 28; i++)
{
    var guitar = InstrumentCatalog.Find(i)!;
    Expect(guitar.IsGuitar, "electric-guitar group detection");
    Expect(guitar.GuitarTone == i - 24, "electric-guitar tone index");
}
Expect(InstrumentCatalog.Valid(0), "keep current is valid");
Expect(!InstrumentCatalog.Valid(29), "unlisted instrument must be rejected");
Console.WriteLine("InstrumentCatalog: 28 FFXIV instruments, 5 guitar variants and keep-current sentinel OK.");
