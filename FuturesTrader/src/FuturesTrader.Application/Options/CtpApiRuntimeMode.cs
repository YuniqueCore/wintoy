namespace FuturesTrader.Application.Options;

/// <summary>
/// CTP 原生 API 创建参数中的运行环境。它控制 CreateFtdc*Api 的
/// <c>bIsProductionMode</c>，与连接到哪一个前置地址是两个独立概念。
/// </summary>
public enum CtpApiRuntimeMode
{
    Production,
    Simulation
}
