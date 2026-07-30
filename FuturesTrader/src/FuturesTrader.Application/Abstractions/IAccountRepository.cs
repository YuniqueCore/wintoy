using FuturesTrader.Domain.Connections;

namespace FuturesTrader.Application.Abstractions;

/// <summary>
/// 交易账号仓库：读写 Users.xml 顶层 &lt;User&gt; 元素的连接信息（title/address/brokerid/userid/appid/shouquan）。
/// 与 <c>IWindowGroupRepository</c> 互补：后者读 WindowHistory，本接口读账号凭据。
/// <para>
/// 完整 CRUD：Load 读全部、Add 新增、Update 更新连接字段、Delete 删除整条（含 WindowHistory）。
/// 所有写操作以 <c>UserId</c> 作为主键（业务主键，非 XML 位置）。
/// </para>
/// </summary>
public interface IAccountRepository
{
    /// <summary>加载全部交易账号（Users.xml 所有 &lt;User&gt; 元素）。文件不存在返回空列表。</summary>
    IReadOnlyList<AccountEntry> Load(string usersXmlPath);

    /// <summary>
    /// 新增一个账号到 Users.xml（追加新 &lt;User&gt; 元素，不含 WindowHistory）。
    /// <paramref name="account"/>.<c>UserId</c> 已存在时抛 <see cref="InvalidOperationException"/>。
    /// 必填校验：UserId 不能为空。
    /// </summary>
    void Add(string usersXmlPath, AccountEntry account);

    /// <summary>
    /// 更新指定账号的连接信息（按 UserId 匹配，仅改连接字段，保留 WindowHistory）。
    /// 找不到 UserId 时抛 <see cref="InvalidOperationException"/>。
    /// </summary>
    void Update(string usersXmlPath, AccountEntry account);

    /// <summary>
    /// 删除指定账号（按 UserId 匹配，整条 &lt;User&gt; 元素移除，含 WindowHistory）。
    /// 找不到 UserId 时为 no-op（幂等）。
    /// </summary>
    void Delete(string usersXmlPath, string userId);
}

