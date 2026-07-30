using System.Text;

namespace FuturesTrader.Infrastructure.MarketData.Ctp.Native;

/// <summary>
/// CTP GBK 编码工具：CTP DLL 的 char[] 字段用 GBK（CodePage 936）编码，C# string 是 UTF-16。
/// 提供 struct 内 ByValTStr 字段的 GBK 往返：读时 GBK→string，写时 string→GBK。
/// 注意：ByValTStr 在 C# 中由 marshaler 按 CharSet 处理；本类用于手动 ptr 操作场景
/// （如 SpiBridge 直接读 unmanaged 内存）。
/// </summary>
public static class CtpEncoding
{
    private static readonly Encoding Gbk = GetGbkEncoding();

    /// <summary>获取 GBK 编码（CodePage 936），自动注册 CodePagesEncodingProvider。</summary>
    public static Encoding GetGbkEncoding()
    {
        // 确保 GBK provider 已注册（生产代码 ConfigRepository/EncodingInitializer 也注册，此处幂等）
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(936);
    }

    /// <summary>把 GBK 字节缓冲区解码为 string（在第一个 \0 处截断）。</summary>
    public static string GbkToString(ReadOnlySpan<byte> bytes)
    {
        // 找到第一个 \0（C 字符串终止符）
        int len = bytes.IndexOf((byte)0);
        if (len < 0) len = bytes.Length;
        return Gbk.GetString(bytes[..len]);
    }

    /// <summary>把 string 编码为 GBK 字节，写入 buffer，剩余空间全填 \0（含终止符）。
    /// 与 <c>ByValTStr</c> marshaler 行为一致，避免缓冲区残留垃圾被 C 端按 C 字符串误读。
    /// 返回实际写入字节数（不含终止符与 padding）。</summary>
    public static int StringToGbk(string value, Span<byte> buffer)
    {
        if (buffer.Length == 0) return 0;
        var bytes = Gbk.GetBytes(value);
        var copyLen = Math.Min(bytes.Length, buffer.Length - 1); // 留 1 字节给 \0
        bytes.AsSpan(0, copyLen).CopyTo(buffer);
        buffer[copyLen..].Clear(); // 终止符 + 剩余空间全部填 \0
        return copyLen;
    }

    /// <summary>GBK 字符串往返：编码后解码应得到原值（验证编码器一致性）。</summary>
    public static string RoundTrip(string value)
    {
        var bytes = Gbk.GetBytes(value);
        return Gbk.GetString(bytes);
    }
}
