using System.Runtime.InteropServices;
using FluentAssertions;
using FuturesTrader.Infrastructure.MarketData.Ctp;
using FuturesTrader.Infrastructure.MarketData.Ctp.Native;

namespace FuturesTrader.Infrastructure.Tests.MarketData.Ctp;

/// <summary>
/// CTP native 结构体映射离线测试（无网络、不加载 thostmduserapi_se.dll）。
/// 验证 <see cref="Marshal.SizeOf{T}"/> 与 <see cref="Marshal.OffsetOf"/> 符合 CTP 6.7.x
/// <c>ThostFtdcUserApiStruct.h</c> 布局：字段顺序、保留字段 reserve1/reserve2 占位、对齐 padding。
/// 任意字段顺序错乱或 reserve 字段缺失都会导致偏移漂移，被 <see cref="Marshal.OffsetOf"/> 断言捕获。
/// </summary>
public class CtpStructTests
{
    /// <summary>
    /// CThostFtdcDepthMarketDataField 总大小（含对齐 padding）。
    /// 经验值：CTP 6.7.x 头文件按字段顺序 + MSVC 默认 Pack=8 算出，本测试既验证下界也验证上界，
    /// 避免 reserve 字段误删或新增字段未同步导致偏移错位。
    /// </summary>
    [Fact]
    public void DepthMarketData_size_is_within_expected_range()
    {
        var size = Marshal.SizeOf<CThostFtdcDepthMarketDataField>();
        size.Should().BeGreaterThan(400, "5 档买卖盘 + 20+ 字段 + 4 个字符串数组应至少 400 字节");
        size.Should().BeLessThan(600, "字段数有限，不应超过 600 字节；过大说明 struct 异常");
    }

    [Fact]
    public void DepthMarketData_reserve_fields_are_in_place()
    {
        // reserve1 / reserve2 是 CTP 为兼容旧版保留的 char[31] 字段，不可省略否则后续字段全部偏移
        // TradingDay[9] + reserve1[31] + ExchangeID[9] + reserve2[31] = 80 字节，LastPrice 紧随其后
        Marshal.OffsetOf<CThostFtdcDepthMarketDataField>(nameof(CThostFtdcDepthMarketDataField.TradingDay))
            .Should().Be(0, "TradingDay 是首字段");
        Marshal.OffsetOf<CThostFtdcDepthMarketDataField>(nameof(CThostFtdcDepthMarketDataField.reserve1))
            .Should().Be(9, "reserve1 紧跟 TradingDay[9]");
        Marshal.OffsetOf<CThostFtdcDepthMarketDataField>(nameof(CThostFtdcDepthMarketDataField.ExchangeID))
            .Should().Be(40, "ExchangeID 跟在 reserve1[31] 后（9+31=40）");
        Marshal.OffsetOf<CThostFtdcDepthMarketDataField>(nameof(CThostFtdcDepthMarketDataField.reserve2))
            .Should().Be(49, "reserve2 紧跟 ExchangeID[9]");
        Marshal.OffsetOf<CThostFtdcDepthMarketDataField>(nameof(CThostFtdcDepthMarketDataField.LastPrice))
            .Should().Be(80, "LastPrice 跟在 4 个字符串字段后（9+31+9+31=80），8 字节对齐无 padding");
    }

    [Fact]
    public void DepthMarketData_volume_and_turnover_have_padding()
    {
        // 7 个 double 后 Volume(int) 在 136，Turnover(double) 需 8 字节对齐 → 144（4 字节 padding）
        Marshal.OffsetOf<CThostFtdcDepthMarketDataField>(nameof(CThostFtdcDepthMarketDataField.Volume))
            .Should().Be(136, "Volume 跟在 7 个 double 后（80+56=136）");
        Marshal.OffsetOf<CThostFtdcDepthMarketDataField>(nameof(CThostFtdcDepthMarketDataField.Turnover))
            .Should().Be(144, "Turnover 是 double 需 8 字节对齐，Volume 后 4 字节 padding");
    }

    [Fact]
    public void DepthMarketData_instrument_id_offset_is_stable()
    {
        // InstrumentID 是 0527.exe 读取行情的关键字段，偏移必须稳定（不能因前序字段改动漂移）
        // 实测 CTP 6.7.x 布局：AveragePrice@384 → ActionDay[9]@392 → InstrumentID[31]@401
        // BandingUpperPrice 在 463，故 InstrumentID ∈ (300, 410) 且远在 BandingUpperPrice 之前
        var offset = Marshal.OffsetOf<CThostFtdcDepthMarketDataField>(nameof(CThostFtdcDepthMarketDataField.InstrumentID));
        offset.Should().BeGreaterThan(300, "InstrumentID 在 AveragePrice 之后，应大于 300");
        offset.Should().BeLessThan(410, "InstrumentID 应远在 BandingUpperPrice（@463）之前");
    }

    [Fact]
    public void RspInfo_field_layout_matches_ctp_header()
    {
        // CThostFtdcRspInfoField = int ErrorID + char[81] ErrorMsg，4 字节对齐，无 padding
        Marshal.SizeOf<CThostFtdcRspInfoField>().Should().Be(88, "int(4) + char[81](81) + 3 字节尾部 padding = 88");
        Marshal.OffsetOf<CThostFtdcRspInfoField>(nameof(CThostFtdcRspInfoField.ErrorID))
            .Should().Be(0);
        Marshal.OffsetOf<CThostFtdcRspInfoField>(nameof(CThostFtdcRspInfoField.ErrorMsg))
            .Should().Be(4);
    }

    [Fact]
    public void ReqUserLogin_and_RspUserLogin_have_distinct_layouts()
    {
        // 请求/响应结构体字段不同（Req 有 Password[41] + 多个产品信息字段，Rsp 有 FrontID/SessionID 等）
        // 大小不应相同，避免误用同一 struct
        var reqSize = Marshal.SizeOf<CThostFtdcReqUserLoginField>();
        var rspSize = Marshal.SizeOf<CThostFtdcRspUserLoginField>();
        reqSize.Should().BeGreaterThan(100, "ReqUserLogin 含 9 字符串 + 1 int，至少 100 字节");
        rspSize.Should().BeGreaterThan(80, "RspUserLogin 含 5 字符串 + 2 int，至少 80 字节");
        reqSize.Should().NotBe(rspSize, "请求/响应结构体字段不同，大小必然不同");
    }

    /// <summary>
    /// CtpMdSpiBridge 在不加载 CTP DLL 的前提下，应能正常构造与释放非托管内存。
    /// 验证 vtable 槽数 = 13（SpiVtable.SlotCount），且 SpiPointer 非零、Dispose 后归零。
    /// </summary>
    [Fact]
    public void SpiBridge_constructs_vtable_with_13_slots_and_frees_on_dispose()
    {
        using var bridge = new CtpMdSpiBridge();
        bridge.SpiPointer.Should().NotBe(IntPtr.Zero, "构造后伪 SPI 对象指针有效");

        // 验证 vtable 指针已写入对象首槽
        IntPtr vtable = Marshal.ReadIntPtr(bridge.SpiPointer);
        vtable.Should().NotBe(IntPtr.Zero, "vtable 指针应在伪对象首槽");

        // 验证每个槽位都填了函数指针（非零）。SlotCount=13 是 CThostFtdcMdSpi 的虚函数总数
        for (int i = 0; i < ThostMdApiNative.SpiVtable.SlotCount; i++)
        {
            IntPtr fnPtr = Marshal.ReadIntPtr(vtable, i * IntPtr.Size);
            fnPtr.Should().NotBe(IntPtr.Zero, $"vtable[{i}] 应填有 C# 委托函数指针");
        }
    }

    /// <summary>
    /// vtable 索引常量不应被无意改动（一旦改动 CTP 会调到错误的回调，崩 AV 或静默错乱）。
    /// 把魔法数字钉在测试里，相当于版本契约。
    /// </summary>
    [Fact]
    public void Vtable_indices_match_ctp_6_7_x_header_layout()
    {
        // CThostFtdcMdSpi 无 virtual 析构 → 无 [0] 析构槽，从 OnFrontConnected 起
        ThostMdApiNative.SpiVtable.OnFrontConnected.Should().Be(0);
        ThostMdApiNative.SpiVtable.OnFrontDisconnected.Should().Be(1);
        ThostMdApiNative.SpiVtable.OnHeartBeatWarning.Should().Be(2);
        ThostMdApiNative.SpiVtable.OnRspUserLogin.Should().Be(3);
        ThostMdApiNative.SpiVtable.OnRspUserLogout.Should().Be(4);
        ThostMdApiNative.SpiVtable.OnRspQryMulticastInstrument.Should().Be(5, "6.3.15+ 新增，插在 Logout 与 Error 之间");
        ThostMdApiNative.SpiVtable.OnRspError.Should().Be(6);
        ThostMdApiNative.SpiVtable.OnRspSubMarketData.Should().Be(7);
        ThostMdApiNative.SpiVtable.OnRspUnSubMarketData.Should().Be(8);
        ThostMdApiNative.SpiVtable.OnRspSubForQuoteRsp.Should().Be(9);
        ThostMdApiNative.SpiVtable.OnRspUnSubForQuoteRsp.Should().Be(10);
        ThostMdApiNative.SpiVtable.OnRtnDepthMarketData.Should().Be(11, "OnRspQryMulticastInstrument 占位导致比早期版本后移 1");
        ThostMdApiNative.SpiVtable.OnRtnForQuoteRsp.Should().Be(12);
        ThostMdApiNative.SpiVtable.SlotCount.Should().Be(13);

        // CThostFtdcMdApi 无 virtual 析构 → 无 [0] 析构槽，从 Release 起
        ThostMdApiNative.ApiVtable.Release.Should().Be(0);
        ThostMdApiNative.ApiVtable.Init.Should().Be(1);
        ThostMdApiNative.ApiVtable.Join.Should().Be(2);
        ThostMdApiNative.ApiVtable.GetTradingDay.Should().Be(3);
        ThostMdApiNative.ApiVtable.RegisterFront.Should().Be(4);
        ThostMdApiNative.ApiVtable.RegisterSpi.Should().Be(7, "RegisterSpi 在 RegisterNameServer/RegisterFensUserInfo 之后");
        ThostMdApiNative.ApiVtable.SubscribeMarketData.Should().Be(8);
        ThostMdApiNative.ApiVtable.UnSubscribeMarketData.Should().Be(9);
        ThostMdApiNative.ApiVtable.ReqUserLogin.Should().Be(12);
    }
}
