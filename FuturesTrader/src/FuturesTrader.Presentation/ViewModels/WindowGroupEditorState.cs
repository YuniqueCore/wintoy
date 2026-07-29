namespace FuturesTrader.Presentation.ViewModels;

/// <summary>
/// 窗口分组编辑器状态机：用联合类型让"非法状态无法表达"（沿用 ConfigEditorState 模式）。
/// Loading 与 Error 不可能同时成立，由编译器保证。
/// </summary>
public abstract record WindowGroupEditorState
{
    /// <summary>初始未加载。</summary>
    public sealed record Idle : WindowGroupEditorState;

    /// <summary>正在加载 Users.xml + window-groups.json。</summary>
    public sealed record Loading : WindowGroupEditorState;

    /// <summary>已加载，可编辑（绑定/解绑/重命名）可保存。</summary>
    public sealed record Loaded : WindowGroupEditorState;

    /// <summary>正在保存。</summary>
    public sealed record Saving : WindowGroupEditorState;

    /// <summary>出错，Message 含可显示给用户的错误信息。</summary>
    public sealed record Error(string Message) : WindowGroupEditorState;
}
