namespace FuturesTrader.Application.Options;

/// <summary>
/// 业务数据文件路径统一选项：绑定 appsettings.json 的 "DataFiles" 节。
/// <para>
/// 设计目的：把原本散落在 ConfigFile/WindowLayout/Login 三节的 0527.exe 兼容数据文件路径
///（config.ini / HQAddress.xml / Users.xml / window-groups.json）集中到 <b>单一 DataFiles 节</b>，
/// 让用户在 appsettings.json 中只看一处即可管理所有业务数据文件位置。
/// </para>
/// <para>
/// 兼容策略：为保持各 Repository 接口（path 参数式）与调用方不变，Host 层 PostConfigure 时
/// 会把 DataFiles 的路径回填到 ConfigFileOptions/LoginOptions/WindowLayoutOptions 的对应字段。
/// 即 DataFiles 是<b>唯一路径源</b>，各 Options 字段仅作向后兼容的视图。
/// </para>
/// <para>
/// 不纳入本类的路径：MarketData:FlowPath / Trading:FlowPath（CTP 会话流目录，与 Provider 强相关）、
/// Sound:BasePath（音效资源目录）。这些是运行时资源路径，非 0527.exe 兼容数据文件。
/// </para>
/// <para>
/// PostConfigure 会将所有相对路径基于 <c>AppContext.BaseDirectory</c> 绝对化，确保任意工作目录启动均可解析。
/// </para>
/// </summary>
public sealed class DataFileOptions
{
    /// <summary>config.ini 路径（GBK 编码，Window/Order/User 段）。0527.exe 兼容硬约束：保留原格式。</summary>
    public string ConfigIni { get; set; } = "data/config.ini";

    /// <summary>HQAddress.xml 路径（行情上游地址列表）。</summary>
    public string HqAddressXml { get; set; } = "data/HQAddress.xml";

    /// <summary>Users.xml 路径（多账号凭据 + 窗口历史，UTF-8）。</summary>
    public string UsersXml { get; set; } = "data/Users.xml";

    /// <summary>window-groups.json 路径（旁挂存储 20 个组名）。</summary>
    public string GroupsJson { get; set; } = "data/window-groups.json";
}
