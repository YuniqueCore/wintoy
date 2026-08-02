using FuturesTrader.Domain.MarketData;

namespace FuturesTrader.Presentation.Services;

/// <summary>
/// 当前会话合约元数据的线程安全只读缓存。浮动栏接收交易端合约流后写入，
/// Settings 等展示面复用同一份名称、期权到期日和价格步长元数据。
/// </summary>
public sealed class InstrumentCatalogCache
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Instrument> _instruments = new(StringComparer.OrdinalIgnoreCase);

    public event Action<Instrument>? InstrumentUpdated;

    public Instrument? Find(string instrumentCode)
    {
        lock (_sync)
            return _instruments.GetValueOrDefault(instrumentCode);
    }

    public void Upsert(Instrument instrument)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        if (string.IsNullOrWhiteSpace(instrument.InstrumentId)) return;
        lock (_sync) _instruments[instrument.InstrumentId] = instrument;
        InstrumentUpdated?.Invoke(instrument);
    }
}
