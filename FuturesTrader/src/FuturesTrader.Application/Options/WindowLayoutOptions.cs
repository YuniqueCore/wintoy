namespace FuturesTrader.Application.Options;

/// <summary>
/// 窗口布局文件路径选项，从 appsettings.json 的 "WindowLayout" 节绑定。
/// UsersXmlPath 指向旧软件 Users.xml（窗口→分组绑定），GroupsJsonPath 指向旁挂的
/// window-groups.json（20 个组名，不污染 legacy XML），UserId 为空串时取第一个 User。
/// </summary>
public sealed class WindowLayoutOptions
{
    /// <summary>旧软件 Users.xml 路径（UTF-8 编码，含 &lt;WindowHistory&gt;）。PostConfigure 会将相对路径绝对化。</summary>
    public string UsersXmlPath { get; set; } = string.Empty;

    /// <summary>window-groups.json 路径（旁挂存储 20 个组名）。PostConfigure 会将相对路径绝对化。</summary>
    public string GroupsJsonPath { get; set; } = string.Empty;

    /// <summary>用户 ID（对应 Users.xml &lt;userid&gt;），空串表示取第一个 User。</summary>
    public string UserId { get; set; } = string.Empty;
}
