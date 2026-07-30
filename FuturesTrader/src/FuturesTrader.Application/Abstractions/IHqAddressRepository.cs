using FuturesTrader.Domain.Connections;

namespace FuturesTrader.Application.Abstractions;

/// <summary>
/// 行情上游地址仓库：读写 HQAddress.xml。
/// </summary>
public interface IHqAddressRepository
{
    /// <summary>加载全部行情上游地址。</summary>
    IReadOnlyList<HqAddressEntry> Load(string path);

    /// <summary>保存行情上游地址列表（覆盖写）。</summary>
    void Save(string path, IReadOnlyList<HqAddressEntry> entries);
}
