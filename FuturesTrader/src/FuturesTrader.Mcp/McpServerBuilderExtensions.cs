using FuturesTrader.Mcp;
using ModelContextProtocol.Server;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// MCP 注册扩展：将 <see cref="ConfigTools"/> 暴露的工具挂到 MCP 服务器构建器上。
/// 仅注册工具，不绑定传输（传输由宿主决定：stdio / HTTP），保持 Mcp 项目轻量可复用。
/// 用法：<code>services.AddMcpServer().WithHttpTransport(...).WithFuturesTraderConfigTools();</code>
/// </summary>
public static class McpServerBuilderExtensions
{
    /// <summary>注册 FuturesTrader 配置读写工具（get_config / update_*_config / save_config）。</summary>
    public static IMcpServerBuilder WithFuturesTraderConfigTools(this IMcpServerBuilder builder)
        => builder.WithToolsFromAssembly();
}
