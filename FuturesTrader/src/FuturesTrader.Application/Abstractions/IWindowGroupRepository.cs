using FuturesTrader.Application.Options;
using FuturesTrader.Domain.WindowGroups;

namespace FuturesTrader.Application.Abstractions;

/// <summary>
/// 窗口布局仓库抽象：读写 WindowLayout（Users.xml 窗口绑定 + window-groups.json 组名）。
/// 与 <see cref="IConfigRepository"/> 同为无状态设计：路径作参数，仓库本身不持有状态。
/// 实现负责备份轮转（.bkp1/.bkp2/.bkp3）与编码处理。
/// </summary>
public interface IWindowGroupRepository
{
    /// <summary>从 Users.xml + window-groups.json 加载窗口布局。文件不存在抛 FileNotFoundException。</summary>
    WindowLayout Load(WindowLayoutOptions options);

    /// <summary>将窗口布局写回 Users.xml（窗口绑定）+ window-groups.json（组名），保留兄弟元素。</summary>
    void Save(WindowLayoutOptions options, WindowLayout layout);
}
