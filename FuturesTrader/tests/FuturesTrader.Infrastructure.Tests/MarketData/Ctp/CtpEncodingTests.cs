using FluentAssertions;
using FuturesTrader.Infrastructure.MarketData.Ctp.Native;

namespace FuturesTrader.Infrastructure.Tests.MarketData.Ctp;

/// <summary>
/// <see cref="CtpEncoding"/> GBK 编解码测试：验证中英文混合字符串往返一致，
/// 以及 <c>\0</c> 终止符在 C 字符串缓冲中的正确截断。
/// 这些是 CTP P/Invoke 字段读写的底层正确性保证（错编码会导致合约代码/错误信息乱码）。
/// </summary>
public class CtpEncodingTests
{
    [Fact]
    public void GbkToString_decodes_ascii_correctly()
    {
        var bytes = new byte[] { (byte)'a', (byte)'g', (byte)'2', (byte)'6', (byte)'0', (byte)'8' };
        CtpEncoding.GbkToString(bytes).Should().Be("ag2608", "ASCII 字符 GBK 编码与 UTF-8 一致");
    }

    [Fact]
    public void GbkToString_truncates_at_null_terminator()
    {
        // CTP 字符串字段是定长 char[N]，未填满部分为 \0；解码必须在第一个 \0 处截断
        var bytes = new byte[] { (byte)'a', (byte)'g', 0, (byte)'x', (byte)'y' };
        CtpEncoding.GbkToString(bytes).Should().Be("ag", "\\0 后的字节不应进入结果");
    }

    [Fact]
    public void GbkToString_handles_no_null_terminator()
    {
        // 缓冲全填满（无 \0）时，整体解码
        var bytes = new byte[] { (byte)'c', (byte)'u', (byte)'2', (byte)'6', (byte)'0', (byte)'9' };
        CtpEncoding.GbkToString(bytes).Should().Be("cu2609");
    }

    [Fact]
    public void GbkToString_decodes_chinese_gbk_bytes()
    {
        // "白银" 的 GBK 编码是 B0 D7 D2 F8
        var bytes = new byte[] { 0xB0, 0xD7, 0xD2, 0xF8 };
        CtpEncoding.GbkToString(bytes).Should().Be("白银", "GBK 双字节中文应正确解码");
    }

    [Fact]
    public void StringToGbk_writes_null_terminated_and_pads()
    {
        // 写入短字符串到长缓冲，应填 \0 终止 + 后续保持 \0
        var buffer = new byte[10];
        buffer.AsSpan().Fill(0xFF); // 预填非零垃圾
        int written = CtpEncoding.StringToGbk("ag", buffer);

        written.Should().Be(2, "实际写入 2 字节（不含终止符）");
        buffer[0].Should().Be((byte)'a');
        buffer[1].Should().Be((byte)'g');
        buffer[2].Should().Be(0, "终止符");
        // buffer[3..] 应被 \0 覆盖（按 StringToGbk 实现：剩余空间填 \0）
        for (int i = 3; i < buffer.Length; i++)
            buffer[i].Should().Be(0, "剩余空间应填 \\0");
    }

    [Fact]
    public void StringToGbk_truncates_when_buffer_too_small()
    {
        var buffer = new byte[4]; // 容纳 "ag2608" 不下
        int written = CtpEncoding.StringToGbk("ag2608", buffer);
        written.Should().Be(3, "应只写入 buffer.Length-1=3 字节，留 1 字节给 \\0");
        buffer[0].Should().Be((byte)'a');
        buffer[1].Should().Be((byte)'g');
        buffer[2].Should().Be((byte)'2');
        buffer[3].Should().Be(0, "末字节为 \\0 终止");
    }

    [Fact]
    public void RoundTrip_preserves_mixed_ascii_and_chinese()
    {
        // CTP 错误信息可能是 "合约不存在" 等中文，RoundTrip 应稳定
        const string original = "ag2608 白银合约";
        var roundTripped = CtpEncoding.RoundTrip(original);
        roundTripped.Should().Be(original, "GBK 编码往返应保持原值");
    }

    [Fact]
    public void GetGbkEncoding_returns_same_instance_for_codepage_936()
    {
        var enc1 = CtpEncoding.GetGbkEncoding();
        var enc2 = CtpEncoding.GetGbkEncoding();
        enc1.CodePage.Should().Be(936, "GBK = CodePage 936");
        enc1.Should().BeSameAs(enc2, "Encoding.GetEncoding 缓存实例");
    }
}
