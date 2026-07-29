namespace FuturesTrader.Presentation.ViewModels;

/// <summary>
/// 配置编辑器状态机：用联合类型让"非法状态无法表达"（替代零散 bool）。
/// 例如 Loading 与 Error 不可能同时成立，由编译器保证。
/// </summary>
public abstract record ConfigEditorState
{
    /// <summary>初始未加载。</summary>
    public sealed record Idle : ConfigEditorState;

    /// <summary>正在加载配置文件。</summary>
    public sealed record Loading : ConfigEditorState;

    /// <summary>已加载，可编辑可保存。</summary>
    public sealed record Loaded : ConfigEditorState;

    /// <summary>正在保存。</summary>
    public sealed record Saving : ConfigEditorState;

    /// <summary>出错，Message 含可显示给用户的错误信息。</summary>
    public sealed record Error(string Message) : ConfigEditorState;
}
