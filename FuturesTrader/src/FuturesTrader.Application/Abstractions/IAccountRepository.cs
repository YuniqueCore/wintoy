using FuturesTrader.Domain.Connections;

namespace FuturesTrader.Application.Abstractions;

/// <summary>
/// 交易账号仓库：读取 Users.xml 顶层 &lt;User&gt; 元素的连接信息（不含 WindowHistory）。
/// 与 <c>IWindowGroupRepository</c> 互补：后者读 WindowHistory，本接口读账号凭据。
/// </summary>
public interface IAccountRepository
{
    /// <summary>加载全部交易账号（Users.xml 所有 &lt;User&gt; 元素）。</summary>
    IReadOnlyList<AccountEntry> Load(string usersXmlPath);

    /// <summary>保存指定账号的连接信息（更新对应 &lt;User&gt; 元素，保留 WindowHistory）。</summary>
    void Save(string usersXmlPath, AccountEntry account);
}
