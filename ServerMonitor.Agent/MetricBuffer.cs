namespace ServerMonitor.Agent;

/// <summary>
/// Очередь замеров, которые ещё не приняты сервером.
/// <para>
/// При переполнении выбрасывается самый старый замер: дыра в середине истории терпима,
/// а вот не знать, что происходит с машиной прямо сейчас — нет.
/// </para>
/// <para>
/// Буфер живёт в памяти и перезапуск агента не переживает. Это осознанное упрощение:
/// файл на диске потребовал бы формата, ротации и обработки повреждённых записей.
/// </para>
/// </summary>
public class MetricBuffer
{
    private readonly Queue<MetricReport> _items = new();
    private readonly int _capacity;

    // Добавляет цикл сбора, читает отправка — доступ к очереди нужно синхронизировать.
    private readonly Lock _sync = new();

    public MetricBuffer(int capacity)
    {
        _capacity = Math.Max(1, capacity);
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

    /// <summary>Копия текущего содержимого — её отправляют на сервер.</summary>
    public IReadOnlyList<MetricReport> Snapshot()
    {
        lock (_sync)
        {
            return _items.ToList();
        }
    }

    /// <summary>
    /// Убирает из начала очереди ровно столько элементов, сколько сервер подтвердил.
    /// Именно столько, а не «всё»: пока летел запрос, цикл мог добавить новый замер.
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
