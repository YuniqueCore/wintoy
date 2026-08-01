using System.Runtime.InteropServices;
using System.Reflection;
using FluentAssertions;
using FuturesTrader.Infrastructure.Trading.Ctp;
using FuturesTrader.Infrastructure.Trading.Ctp.Native;

namespace FuturesTrader.Infrastructure.Tests.Trading.Ctp;

/// <summary>
/// CTP Trader 6.7.13 ABI 的离线回归测试。
/// <para>
/// 测试只构造托管伪 vtable，不加载交易 DLL、不开网络连接，也不会调用报单或撤单 API。
/// 它把已验证的 6.7.13 头文件布局固定为契约，避免版本升级后再次发生静默的槽位错配。
/// </para>
/// </summary>
public sealed class CtpTraderNativeAbiTests
{
    [Fact]
    public void Api_vtable_core_indices_match_the_ctp_6_7_13_header()
    {
        // 6.7.13 保留 GetFrontInfo@4；SPI 新增回调不会影响 TraderApi 的这些槽位。
        ThostTraderApiNative.ApiVtable.Release.Should().Be(0);
        ThostTraderApiNative.ApiVtable.Init.Should().Be(1);
        ThostTraderApiNative.ApiVtable.Join.Should().Be(2);
        ThostTraderApiNative.ApiVtable.GetTradingDay.Should().Be(3);
        ThostTraderApiNative.ApiVtable.RegisterFront.Should().Be(5);
        ThostTraderApiNative.ApiVtable.RegisterSpi.Should().Be(8);
        ThostTraderApiNative.ApiVtable.SubscribePrivateTopic.Should().Be(9);
        ThostTraderApiNative.ApiVtable.SubscribePublicTopic.Should().Be(10);
        ThostTraderApiNative.ApiVtable.ReqAuthenticate.Should().Be(11);
        ThostTraderApiNative.ApiVtable.ReqUserLogin.Should().Be(16);
        ThostTraderApiNative.ApiVtable.ReqOrderInsert.Should().Be(26);
        ThostTraderApiNative.ApiVtable.ReqOrderAction.Should().Be(29);
        ThostTraderApiNative.ApiVtable.ReqSettlementInfoConfirm.Should().Be(31);
        ThostTraderApiNative.ApiVtable.ReqQryInvestorPosition.Should().Be(45);
        ThostTraderApiNative.ApiVtable.ReqQryTradingAccount.Should().Be(46);
        ThostTraderApiNative.ApiVtable.ReqQryUserSession.Should().Be(51);
        ThostTraderApiNative.ApiVtable.ReqQryInstrument.Should().Be(54);
    }

    [Fact]
    public void Spi_vtable_core_indices_match_the_ctp_6_7_13_header()
    {
        ThostTraderApiNative.SpiVtable.OnFrontConnected.Should().Be(0);
        ThostTraderApiNative.SpiVtable.OnFrontDisconnected.Should().Be(1);
        ThostTraderApiNative.SpiVtable.OnHeartBeatWarning.Should().Be(2);
        ThostTraderApiNative.SpiVtable.OnRspAuthenticate.Should().Be(3);
        ThostTraderApiNative.SpiVtable.OnRtnPrivateSeqNo.Should().Be(4);
        ThostTraderApiNative.SpiVtable.OnRspUserLogin.Should().Be(5);
        ThostTraderApiNative.SpiVtable.OnRspOrderInsert.Should().Be(12);
        ThostTraderApiNative.SpiVtable.OnRspOrderAction.Should().Be(15);
        ThostTraderApiNative.SpiVtable.OnRspSettlementInfoConfirm.Should().Be(17);
        ThostTraderApiNative.SpiVtable.OnRspQryInvestorPosition.Should().Be(31);
        ThostTraderApiNative.SpiVtable.OnRspQryTradingAccount.Should().Be(32);
        ThostTraderApiNative.SpiVtable.OnRspQryUserSession.Should().Be(37);
        ThostTraderApiNative.SpiVtable.OnRspQryInstrument.Should().Be(40);
        ThostTraderApiNative.SpiVtable.OnRspError.Should().Be(75);
        ThostTraderApiNative.SpiVtable.OnRtnOrder.Should().Be(76);
        ThostTraderApiNative.SpiVtable.OnRtnTrade.Should().Be(77);
        ThostTraderApiNative.SpiVtable.OnErrRtnOrderInsert.Should().Be(78);
        ThostTraderApiNative.SpiVtable.OnErrRtnOrderAction.Should().Be(79);
        ThostTraderApiNative.SpiVtable.SlotCount.Should().Be(178);
    }

    /// <summary>
    /// CThostFtdcInputOrderField 在 6.7.13 的真实 InstrumentID 位于尾部，前部 char[31]
    /// 是 reserve1。这个断言防止未来又把旧版字段顺序复制回来而把报单写进错误偏移。
    /// </summary>
    [Fact]
    public void Input_order_layout_matches_the_ctp_6_7_13_header()
    {
        Marshal.SizeOf<CThostFtdcInputOrderField>().Should().Be(408);
        Marshal.OffsetOf<CThostFtdcInputOrderField>(nameof(CThostFtdcInputOrderField.reserve1)).Should().Be(24);
        Marshal.OffsetOf<CThostFtdcInputOrderField>(nameof(CThostFtdcInputOrderField.OrderRef)).Should().Be(55);
        Marshal.OffsetOf<CThostFtdcInputOrderField>(nameof(CThostFtdcInputOrderField.LimitPrice)).Should().Be(96);
        Marshal.OffsetOf<CThostFtdcInputOrderField>(nameof(CThostFtdcInputOrderField.MinVolume)).Should().Be(120);
        Marshal.OffsetOf<CThostFtdcInputOrderField>(nameof(CThostFtdcInputOrderField.reserve2)).Should().Be(234);
        Marshal.OffsetOf<CThostFtdcInputOrderField>(nameof(CThostFtdcInputOrderField.InstrumentID)).Should().Be(271);
        Marshal.OffsetOf<CThostFtdcInputOrderField>(nameof(CThostFtdcInputOrderField.IPAddress)).Should().Be(352);
        Marshal.OffsetOf<CThostFtdcInputOrderField>(nameof(CThostFtdcInputOrderField.OrderMemo)).Should().Be(385);
        Marshal.OffsetOf<CThostFtdcInputOrderField>(nameof(CThostFtdcInputOrderField.SessionReqSeq)).Should().Be(400);
    }

    /// <summary>
    /// ReqOrderAction 同样采用 6.7.13 尾部 InstrumentID；三元组撤单不能把合约号落在 reserve1。
    /// </summary>
    [Fact]
    public void Input_order_action_layout_matches_the_ctp_6_7_13_header()
    {
        Marshal.SizeOf<CThostFtdcInputOrderActionField>().Should().Be(336);
        Marshal.OffsetOf<CThostFtdcInputOrderActionField>(nameof(CThostFtdcInputOrderActionField.OrderRef)).Should().Be(28);
        Marshal.OffsetOf<CThostFtdcInputOrderActionField>(nameof(CThostFtdcInputOrderActionField.LimitPrice)).Should().Be(88);
        Marshal.OffsetOf<CThostFtdcInputOrderActionField>(nameof(CThostFtdcInputOrderActionField.reserve1)).Should().Be(116);
        Marshal.OffsetOf<CThostFtdcInputOrderActionField>(nameof(CThostFtdcInputOrderActionField.reserve2)).Should().Be(164);
        Marshal.OffsetOf<CThostFtdcInputOrderActionField>(nameof(CThostFtdcInputOrderActionField.InstrumentID)).Should().Be(201);
        Marshal.OffsetOf<CThostFtdcInputOrderActionField>(nameof(CThostFtdcInputOrderActionField.IPAddress)).Should().Be(282);
        Marshal.OffsetOf<CThostFtdcInputOrderActionField>(nameof(CThostFtdcInputOrderActionField.OrderMemo)).Should().Be(315);
        Marshal.OffsetOf<CThostFtdcInputOrderActionField>(nameof(CThostFtdcInputOrderActionField.SessionReqSeq)).Should().Be(328);
    }

    /// <summary>
    /// OnRtnOrder 使用的快照必须从 6.7.13 尾部读取 InstrumentID/ExchangeInstID，
    /// 否则回报的合约、FrontID、订单状态都会静默错位。
    /// </summary>
    [Fact]
    public void Order_return_layout_matches_the_ctp_6_7_13_header()
    {
        Marshal.SizeOf<CThostFtdcOrderField>().Should().Be(872);
        Marshal.OffsetOf<CThostFtdcOrderField>(nameof(CThostFtdcOrderField.reserve1)).Should().Be(24);
        Marshal.OffsetOf<CThostFtdcOrderField>(nameof(CThostFtdcOrderField.OrderRef)).Should().Be(55);
        Marshal.OffsetOf<CThostFtdcOrderField>(nameof(CThostFtdcOrderField.reserve2)).Should().Be(216);
        Marshal.OffsetOf<CThostFtdcOrderField>(nameof(CThostFtdcOrderField.TraderID)).Should().Be(247);
        Marshal.OffsetOf<CThostFtdcOrderField>(nameof(CThostFtdcOrderField.OrderSysID)).Should().Be(296);
        Marshal.OffsetOf<CThostFtdcOrderField>(nameof(CThostFtdcOrderField.OrderStatus)).Should().Be(318);
        Marshal.OffsetOf<CThostFtdcOrderField>(nameof(CThostFtdcOrderField.FrontID)).Should().Be(420);
        Marshal.OffsetOf<CThostFtdcOrderField>(nameof(CThostFtdcOrderField.StatusMsg)).Should().Be(439);
        Marshal.OffsetOf<CThostFtdcOrderField>(nameof(CThostFtdcOrderField.reserve3)).Should().Be(619);
        Marshal.OffsetOf<CThostFtdcOrderField>(nameof(CThostFtdcOrderField.InstrumentID)).Should().Be(656);
        Marshal.OffsetOf<CThostFtdcOrderField>(nameof(CThostFtdcOrderField.ExchangeInstID)).Should().Be(737);
        Marshal.OffsetOf<CThostFtdcOrderField>(nameof(CThostFtdcOrderField.IPAddress)).Should().Be(818);
        Marshal.OffsetOf<CThostFtdcOrderField>(nameof(CThostFtdcOrderField.SessionReqSeq)).Should().Be(864);
    }

    [Fact]
    public void Trade_return_layout_matches_the_ctp_6_7_13_header()
    {
        Marshal.SizeOf<CThostFtdcTradeField>().Should().Be(496);
        Marshal.OffsetOf<CThostFtdcTradeField>(nameof(CThostFtdcTradeField.reserve1)).Should().Be(24);
        Marshal.OffsetOf<CThostFtdcTradeField>(nameof(CThostFtdcTradeField.OrderRef)).Should().Be(55);
        Marshal.OffsetOf<CThostFtdcTradeField>(nameof(CThostFtdcTradeField.OrderSysID)).Should().Be(115);
        Marshal.OffsetOf<CThostFtdcTradeField>(nameof(CThostFtdcTradeField.reserve2)).Should().Be(159);
        Marshal.OffsetOf<CThostFtdcTradeField>(nameof(CThostFtdcTradeField.Price)).Should().Be(192);
        Marshal.OffsetOf<CThostFtdcTradeField>(nameof(CThostFtdcTradeField.InstrumentID)).Should().Be(334);
        Marshal.OffsetOf<CThostFtdcTradeField>(nameof(CThostFtdcTradeField.ExchangeInstID)).Should().Be(415);
    }

    [Fact]
    public void Spi_bridge_populates_every_ctp_6_7_13_vtable_slot()
    {
        using var bridge = new CtpTraderSpiBridge();
        bridge.SpiPointer.Should().NotBe(IntPtr.Zero);

        var vtable = Marshal.ReadIntPtr(bridge.SpiPointer);
        vtable.Should().NotBe(IntPtr.Zero);
        for (int index = 0; index < ThostTraderApiNative.SpiVtable.SlotCount; index++)
        {
            Marshal.ReadIntPtr(vtable, index * IntPtr.Size)
                .Should().NotBe(IntPtr.Zero, $"SPI vtable[{index}] 必须持有兼容签名的委托");
        }
    }

    [Fact]
    public void Spi_vtable_slot_5_dispatches_the_user_login_response()
    {
        using var bridge = new CtpTraderSpiBridge();
        var callbackReceived = false;
        bridge.RspUserLogin += (success, error) =>
        {
            callbackReceived = success && string.IsNullOrEmpty(error);
        };

        var vtable = Marshal.ReadIntPtr(bridge.SpiPointer);
        var rspDelegateType = typeof(CtpTraderSpiBridge).GetNestedType(
            "RspDelegate",
            BindingFlags.NonPublic);
        rspDelegateType.Should().NotBeNull("登录响应槽必须使用桥接器的原始 RspDelegate 类型");
        var callback = Marshal.GetDelegateForFunctionPointer(
            Marshal.ReadIntPtr(vtable, ThostTraderApiNative.SpiVtable.OnRspUserLogin * IntPtr.Size),
            rspDelegateType!);

        callback.DynamicInvoke(bridge.SpiPointer, IntPtr.Zero, IntPtr.Zero, 1, true);

        callbackReceived.Should().BeTrue(
            "6.7.13 把 OnRspUserLogin 放在 [5]；[4] 已由 OnRtnPrivateSeqNo 占用");
    }

    [Fact]
    public void Subscribe_private_topic_passes_resume_type_and_default_sequence_number()
    {
        IntPtr apiObject = IntPtr.Zero;
        IntPtr vtable = IntPtr.Zero;
        IntPtr capturedThis = IntPtr.Zero;
        var capturedResumeType = int.MinValue;
        var capturedSequenceNumber = int.MinValue;
        var callback = new ThostTraderApiNative.SubscribePrivateTopicDelegate((thisPtr, resumeType, sequenceNumber) =>
        {
            capturedThis = thisPtr;
            capturedResumeType = resumeType;
            capturedSequenceNumber = sequenceNumber;
        });

        try
        {
            var bytes = (ThostTraderApiNative.ApiVtable.SubscribePrivateTopic + 1) * IntPtr.Size;
            vtable = Marshal.AllocHGlobal(bytes);
            apiObject = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(
                vtable,
                ThostTraderApiNative.ApiVtable.SubscribePrivateTopic * IntPtr.Size,
                Marshal.GetFunctionPointerForDelegate(callback));
            Marshal.WriteIntPtr(apiObject, vtable);

            ThostTraderApiNative.SubscribePrivateTopic(apiObject, ThostTraderApiNative.TertResume);

            capturedThis.Should().Be(apiObject);
            capturedResumeType.Should().Be(ThostTraderApiNative.TertResume);
            capturedSequenceNumber.Should().Be(1, "CTP 6.7.13 的 nSeqNo 默认值为 1");
        }
        finally
        {
            GC.KeepAlive(callback);
            if (apiObject != IntPtr.Zero)
                Marshal.FreeHGlobal(apiObject);
            if (vtable != IntPtr.Zero)
                Marshal.FreeHGlobal(vtable);
        }
    }

}
