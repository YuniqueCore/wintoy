using System.Runtime.CompilerServices;
using System.Text;

namespace FuturesTrader.Infrastructure.Tests;

/// <summary>
/// 测试程序集加载时自动注册 GBK 编码 provider。
/// 用 [ModuleInitializer] 避免在每个测试类构造函数里重复注册（DRY）。
/// 生产代码的 ConfigRepository 内部已自行注册，这里仅为测试辅助方法 WriteGbkIni 服务。
/// </summary>
internal static class EncodingInitializer
{
    [ModuleInitializer]
    public static void RegisterGbkEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}
