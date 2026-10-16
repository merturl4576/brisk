using Brisk.Ui;

namespace Brisk.Tests.Ui;

/// Timers a test moves by hand: Advance fires every due tick in time order.
public sealed class ManualTimers : IUiTimers
{
    private sealed class Entry : IDisposable
    {
        public DateTime Due;
        public TimeSpan? Period;
        public Action Tick = () => { };
        public bool Gone;
        public void Dispose() => Gone = true;
    }

    private readonly List<Entry> _entries = new();

    public DateTime UtcNow { get; private set; } = new(2026, 10, 16, 20, 0, 0, DateTimeKind.Utc);

    public IDisposable Every(TimeSpan period, Action tick) => Add(new Entry { Due = UtcNow + period, Period = period, Tick = tick });

    public IDisposable After(TimeSpan delay, Action tick) => Add(new Entry { Due = UtcNow + delay, Tick = tick });

    public int Live => _entries.Count(e => !e.Gone);

    private Entry Add(Entry e)
    {
        _entries.Add(e);
        return e;
    }

    public void Advance(TimeSpan by)
    {
        var end = UtcNow + by;
        while (true)
        {
            var next = _entries.Where(e => !e.Gone && e.Due <= end).OrderBy(e => e.Due).FirstOrDefault();
            if (next is null) break;
            UtcNow = next.Due;
            if (next.Period is { } p) next.Due += p; else next.Gone = true;
            next.Tick();
        }
        UtcNow = end;
    }
}
