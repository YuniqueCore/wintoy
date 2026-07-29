using FuturesTrader.Domain.Configuration;

namespace FuturesTrader.Application.Abstractions;

/// <summary>
/// 配置仓库抽象：读写旧软件 GBK config.ini，映射到 <see cref="CloudConfig"/> 领域模型。
/// 基础设施层提供具体实现（如 INI 读写）。仅暴露加载/保存两个命令；
/// 迁移工具专用的 ToJson/FromJson 不进接口，保留在具体类上。
/// </summary>
public interface IConfigRepository
{
    /// <summary>从指定路径加载配置。文件不存在抛 <see cref="FileNotFoundException"/>。</summary>
    CloudConfig Load(string path);

    /// <summary>将配置写回指定路径（保留 GBK 编码以兼容旧软件）。</summary>
    void Save(string path, CloudConfig config);
}
