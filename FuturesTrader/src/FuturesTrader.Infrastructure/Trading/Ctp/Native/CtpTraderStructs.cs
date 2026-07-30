using System.Runtime.InteropServices;

namespace FuturesTrader.Infrastructure.Trading.Ctp.Native;

/// <summary>
/// CTP 交易数据结构 C# 映射，对齐 <c>ThostFtdcUserApiStruct.h</c>（CTP 6.7.x）。
/// 字段顺序与类型严格按官方头文件，自然对齐（MSVC 默认 8 字节，.NET Sequential Pack=0 一致）。
/// <para>
/// <b>类型映射</b>：
/// <list type="bullet">
///   <item><c>char[N]</c> → <c>[MarshalAs(UnmanagedType.ByValTStr, SizeConst=N)] string</c>（GBK 由 marshaler 处理）</item>
///   <item><c>char</c>（单字节枚举，如 Direction/OffsetFlag）→ <c>byte</c>（1 字节，避免 Unicode marshal 问题）</item>
///   <item><c>TThostFtdcBoolType</c> → <c>int</c>（CTP 用 int 而非 C++ bool）</item>
///   <item><c>double/int</c> → 直接对应</item>
/// </list>
/// 共享结构（RspInfoField/ReqUserLoginField/RspUserLoginField）复用
/// <see cref="FuturesTrader.Infrastructure.MarketData.Ctp.Native"/> 命名空间下的现有定义。
/// </para>
/// </summary>

/// <summary>
/// 客户端认证请求结构，对齐 <c>CThostFtdcReqAuthenticateField</c>（CTP 6.7.x）。
/// 认证流程：ReqAuthenticate → OnRspAuthenticate → ReqUserLogin。
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct CThostFtdcReqAuthenticateField
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)]
    public string BrokerID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
    public string UserID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)]
    public string UserProductInfo;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 17)]
    public string AuthCode;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 31)]
    public string AppID;
}

/// <summary>
/// 报单录入请求结构，对齐 <c>CThostFtdcInputOrderField</c>（CTP 6.7.x）。
/// 用于 <c>ReqOrderInsert</c>。限价单示例：OrderPriceType='2'(LimitPrice)、TimeCondition='3'(GFD)、
/// VolumeCondition='1'(AnyVolume)、ContingentCondition='1'(Immediately)、ForceCloseReason='0'(NotForceClose)。
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct CThostFtdcInputOrderField
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)]
    public string BrokerID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 13)]
    public string InvestorID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 31)]
    public string InstrumentID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 13)]
    public string OrderRef;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
    public string UserID;

    /// <summary>报单价格条件：'0'=AnyPrice(市价) '1'=LimitPrice(限价) '2'=BestPrice 等。</summary>
    public byte OrderPriceType;

    /// <summary>买卖方向：'0'=Buy '1'=Sell。</summary>
    public byte Direction;

    /// <summary>组合开平标志 char[5]：[0]为主腿标志 '0'=Open '1'=Close '3'=CloseToday '4'=CloseYesterday。</summary>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 5)]
    public string CombOffsetFlag;

    /// <summary>组合投机套保标志 char[5]：[0]='1'=Speculation(投机) '2'=Arbitrage '3'=Hedge。</summary>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 5)]
    public string CombHedgeFlag;

    /// <summary>价格（限价单的申报价格）。</summary>
    public double LimitPrice;

    /// <summary>数量（手数）。</summary>
    public int VolumeTotalOriginal;

    /// <summary>有效期类型：'1'=IOC '2'=GFS '3'=GFD(当日有效) '4'=GTD '5'=GTC。</summary>
    public byte TimeCondition;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string GTDDate;

    /// <summary>成交量类型：'1'=AnyVolume '2'=MinVolume '3'=AllOrNone。</summary>
    public byte VolumeCondition;

    /// <summary>最小成交量（VolumeCondition=MinVolume 时生效）。</summary>
    public int MinVolume;

    /// <summary>触发条件：'1'=Immediately(立即) 其余为条件单触发。</summary>
    public byte ContingentCondition;

    /// <summary>止损价（条件单用，限价单填 0）。</summary>
    public double StopPrice;

    /// <summary>强平原因：'0'=NotForceClose(非强平，普通报单填此)。</summary>
    public byte ForceCloseReason;

    /// <summary>自动挂起标志（TThostFtdcBoolType = int，填 0）。</summary>
    public int IsAutoSuspend;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
    public string BusinessUnit;

    /// <summary>请求编号（对应响应 nRequestID）。</summary>
    public int RequestID;

    /// <summary>用户强评标志（TThostFtdcBoolType = int，填 0）。</summary>
    public int UserForceClose;

    /// <summary>互换单标志（TThostFtdcBoolType = int，填 0）。</summary>
    public int IsSwapOrder;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string ExchangeID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 17)]
    public string InvestUnitID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 13)]
    public string AccountID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4)]
    public string CurrencyID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)]
    public string ClientID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 17)]
    public string IPAddress;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
    public string MacAddress;
}

/// <summary>
/// 报单回报结构，对齐 <c>CThostFtdcOrderField</c>（CTP 6.7.x）。
/// 用于 <c>OnRtnOrder</c> 回调。每次报单状态变化推送一个完整快照。
/// 字段顺序严格按 <c>ThostFtdcUserApiStruct.h</c>，含 63 个字段（6.7.x 含 IP/Mac 扩展）。
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct CThostFtdcOrderField
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)]
    public string BrokerID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 13)]
    public string InvestorID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 31)]
    public string InstrumentID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 13)]
    public string OrderRef;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
    public string UserID;

    public byte OrderPriceType;
    public byte Direction;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 5)]
    public string CombOffsetFlag;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 5)]
    public string CombHedgeFlag;

    public double LimitPrice;
    public int VolumeTotalOriginal;
    public byte TimeCondition;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string GTDDate;

    public byte VolumeCondition;
    public int MinVolume;
    public byte ContingentCondition;
    public double StopPrice;
    public byte ForceCloseReason;
    public int IsAutoSuspend;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
    public string BusinessUnit;

    public int RequestID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 13)]
    public string OrderLocalID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string ExchangeID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)]
    public string ParticipantID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)]
    public string ClientID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 31)]
    public string ExchangeInstID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)]
    public string TraderID;

    public int InstallID;
    public byte OrderSubmitStatus;
    public int NotifySequence;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string TradingDay;

    public int SettlementID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
    public string OrderSysID;

    public byte OrderSource;
    /// <summary>报单状态：'0'=AllTraded '1'=PartTradedQueueing '5'=Canceled 'a'=Unknown 'b'=NotTouched 等。</summary>
    public byte OrderStatus;
    public byte OrderType;
    public int VolumeTraded;
    public int VolumeTotal;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string InsertDate;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string InsertTime;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string ActiveTime;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string SuspendTime;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string UpdateTime;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string CancelTime;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)]
    public string ActiveTraderID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)]
    public string ClearingPartID;

    public int SequenceNo;
    public int FrontID;
    public int SessionID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)]
    public string UserProductInfo;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 81)]
    public string StatusMsg;

    public int UserForceClose;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
    public string ActiveUserID;

    public int BrokerOrderSeq;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
    public string RelativeOrderSysID;

    public int ZCETotalTradedVolume;
    public int IsSwapOrder;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string BranchID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 17)]
    public string InvestUnitID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 13)]
    public string AccountID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4)]
    public string CurrencyID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 17)]
    public string IPAddress;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
    public string MacAddress;
}

/// <summary>
/// 成交回报结构，对齐 <c>CThostFtdcTradeField</c>（CTP 6.7.x）。
/// 用于 <c>OnRtnTrade</c> 回调。每笔成交推送一个结构。
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct CThostFtdcTradeField
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)]
    public string BrokerID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 13)]
    public string InvestorID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 31)]
    public string InstrumentID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 13)]
    public string OrderRef;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
    public string UserID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string ExchangeID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
    public string TradeID;

    /// <summary>买卖方向：'0'=Buy '1'=Sell。</summary>
    public byte Direction;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
    public string OrderSysID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)]
    public string ParticipantID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)]
    public string ClientID;

    public byte TradingRole;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 31)]
    public string ExchangeInstID;

    /// <summary>开平标志：'0'=Open '1'=Close '3'=CloseToday '4'=CloseYesterday。</summary>
    public byte OffsetFlag;

    /// <summary>投机套保标志：'1'=Speculation。</summary>
    public byte HedgeFlag;

    /// <summary>成交价格。</summary>
    public double Price;

    /// <summary>成交数量（手数）。</summary>
    public int Volume;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string TradeDate;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string TradeTime;

    public byte TradeType;
    public byte PriceSource;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)]
    public string TraderID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 13)]
    public string OrderLocalID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)]
    public string ClearingPartID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
    public string BusinessUnit;

    public int SequenceNo;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string TradingDay;

    public int SettlementID;
    public int BrokerOrderSeq;
    public byte TradeSource;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 17)]
    public string InvestUnitID;
}

/// <summary>
/// 报单操作请求结构（撤单），对齐 <c>CThostFtdcInputOrderActionField</c>（CTP 6.7.x）。
/// 用于 <c>ReqOrderAction</c>。撤单方式二选一：
/// <list type="bullet">
///   <item>方式一：ExchangeID + OrderSysID（交易所报单编号）</item>
///   <item>方式二：FrontID + SessionID + OrderRef（本地报单引用，推荐）</item>
/// </list>
/// <see cref="ActionFlag"/> 只支持 '0'=Delete。
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct CThostFtdcInputOrderActionField
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)]
    public string BrokerID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 13)]
    public string InvestorID;

    /// <summary>报单操作引用（递增序号，用于关联响应）。</summary>
    public int OrderActionRef;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 13)]
    public string OrderRef;

    public int RequestID;
    public int FrontID;
    public int SessionID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string ExchangeID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
    public string OrderSysID;

    /// <summary>操作标志：'0'=Delete（撤单，CTP 只支持删除不支持修改）。</summary>
    public byte ActionFlag;

    public double LimitPrice;
    public int VolumeChange;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
    public string UserID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 31)]
    public string InstrumentID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 17)]
    public string InvestUnitID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 17)]
    public string IPAddress;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
    public string MacAddress;
}

/// <summary>
/// 投资者结算结果确认结构，对齐 <c>CThostFtdcSettlementInfoConfirmField</c>（CTP 6.7.x）。
/// 用于 <c>ReqSettlementInfoConfirm</c>。每日交易前需确认上一日结算单（仅需一次）。
/// 只需填 BrokerID + InvestorID，ConfirmDate/ConfirmTime 由 CTP 回填。
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct CThostFtdcSettlementInfoConfirmField
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)]
    public string BrokerID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 13)]
    public string InvestorID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string ConfirmDate;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string ConfirmTime;

    public int SettlementID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 13)]
    public string AccountID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4)]
    public string CurrencyID;
}
