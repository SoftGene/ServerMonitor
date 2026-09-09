namespace ServerMonitor.Agent;

/// <summary>
/// A queue of readings the server has not accepted yet.
/// <para>
/// When it overflows the oldest reading is discarded: a hole in the middle of the history is
/// tolerable, not knowing what the machine is doing right now is not.
/// </para>
/// <para>
/// The contents survive a restart: <see cref="BufferStore"/> keeps them on disk, because an
/// outage and a restart are failures that arrive together far more often than separately.
/// </para>
/// </summary>
public class MetricBuffer
{
    private readonly Queue<MetricReport> _items = new();
    private readonly int _capacity;

    // The collect loop adds, the send path reads — access to the queue has to be synchronised.
    private readonly Lock _sync = new();

    public MetricBuffer(int capacity)
    {
        _capacity = Math.Max(1, capacity);
    }

    /// <summary>
    /// Replaces the contents with what a previous run left behind.
    /// </summary>
    /// <remarks>
    /// Trimmed to the current capacity, keeping the newest: the setting may have been lowered
    /// since the file was written, and restoring more than the configured limit would quietly
    /// defeat the point of having one.
    /// </remarks>
    public void Restore(IReadOnlyList<MetricReport> items)
    {
        lock (_sync)
        {
            _items.Clear();

            foreach (var item in items.Skip(Math.Max(0, items.Count - _capacity)))
            {
                _items.Enqueue(item);
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _items.Count;
            }
        }
    }

    public void Add(MetricReport report)
    {
        lock (_sync)
        {
            while (_items.Count >= _capacity)
            {
                _items.Dequeue();
            }

            _items.Enqueue(report);
        }
    }

    /// <summary>A copy of the current contents — this is what gets sent to the server.</summary>
    public IReadOnlyList<MetricReport> Snapshot()
    {
        lock (_sync)
        {
            return _items.ToList();
        }
    }

    /// <summary>
    /// Removes from the front exactly as many items as the server acknowledged.
    /// Exactly that many rather than "everything": the loop may have added a new reading
    /// while the request was in flight.
    /// </summary>
    public void Remove(int count)
    {
        lock (_sync)
        {
            for (var i = 0; i < count && _items.Count > 0; i++)
            {
                _items.Dequeue();
            }
        }
    }
}
