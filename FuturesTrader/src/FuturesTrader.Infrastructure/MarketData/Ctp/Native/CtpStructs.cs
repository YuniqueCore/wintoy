using System.Runtime.InteropServices;

namespace FuturesTrader.Infrastructure.MarketData.Ctp.Native;

/// <summary>
/// CTP 行情数据结构 C# 映射，对齐 <c>CThostFtdcDepthMarketDataField</c>（CTP 6.7.x，
/// 见 ThostFtdcUserApiStruct.h）。字段顺序与类型严格按官方头文件，自然对齐（MSVC 默认 8 字节）。
/// 字符串字段用 <c>[MarshalAs(UnmanagedType.ByValTStr)]</c> + <c>SizeConst</c>，由 CtpEncoding 做 GBK 往返。
/// 注：reserve1/reserve2 是 CTP 为兼容旧版保留的无效字段（OldInstrumentID/OldExchangeInstID），不可省略否则偏移错位。
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct CThostFtdcDepthMarketDataField
{
    /// <summary>交易日 char[9]。</summary>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string TradingDay;

    /// <summary>保留字段（旧合约代码）char[31]。</summary>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 31)]
    public string reserve1;

    /// <summary>交易所代码 char[9]。</summary>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string ExchangeID;

    /// <summary>保留字段（旧交易所合约代码）char[31]。</summary>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 31)]
    public string reserve2;

    public double LastPrice;
    public double PreSettlementPrice;
    public double PreClosePrice;
    public double PreOpenInterest;   // TThostFtdcLargeVolumeType = double（6.6+）
    public double OpenPrice;
    public double HighestPrice;
    public double LowestPrice;
    public int Volume;               // TThostFtdcVolumeType = int
    public double Turnover;          // TThostFtdcMoneyType = double
    public double OpenInterest;      // TThostFtdcLargeVolumeType = double
    public double ClosePrice;
    public double SettlementPrice;
    public double UpperLimitPrice;
    public double LowerLimitPrice;
    public double PreDelta;          // TThostFtdcRatioType = double
    public double CurrDelta;

    /// <summary>最后修改时间 char[9]（HH:mm:ss）。</summary>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string UpdateTime;

    public int UpdateMillisec;

    // 5 档买卖盘（价格 double / 量 int 交替）
    public double BidPrice1; public int BidVolume1;
    public double AskPrice1; public int AskVolume1;
    public double BidPrice2; public int BidVolume2;
    public double AskPrice2; public int AskVolume2;
    public double BidPrice3; public int BidVolume3;
    public double AskPrice3; public int AskVolume3;
    public double BidPrice4; public int BidVolume4;
    public double AskPrice4; public int AskVolume4;
    public double BidPrice5; public int BidVolume5;
    public double AskPrice5; public int AskVolume5;

    public double AveragePrice;

    /// <summary>业务日期 char[9]。</summary>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string ActionDay;

    /// <summary>合约代码 char[31]。</summary>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 31)]
    public string InstrumentID;

    /// <summary>合约在交易所的代码 char[31]。</summary>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 31)]
    public string ExchangeInstID;

    public double BandingUpperPrice;
    public double BandingLowerPrice;
}

/// <summary>
/// CTP 响应信息结构，对齐 <c>CThostFtdcRspInfoField</c>。ErrorID=0 表示成功。
/// ErrorMsg 是 GBK char[81]，需用 CtpEncoding 解码。
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct CThostFtdcRspInfoField
{
    public int ErrorID;             // TThostFtdcErrorIDType = int

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 81)]
    public string ErrorMsg;
}

/// <summary>
/// CTP 登录响应结构，对齐 <c>CThostFtdcRspUserLoginField</c>（仅行情登录实际使用 TradingDay/BrokerID/UserID，
/// FrontID/SessionID 在行情端通常为 0）。
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct CThostFtdcRspUserLoginField
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string TradingDay;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string LoginTime;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)]
    public string BrokerID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
    public string UserID;

    public int FrontID;             // TThostFtdcFrontIDType = int
    public int SessionID;           // TThostFtdcSessionIDType = int

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
    public string SystemName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 41)]
    public string MaxOrderRef;
}

/// <summary>
/// CTP 登录请求结构，对齐 6.7.13 <c>CThostFtdcReqUserLoginField</c>。
/// <para>
/// 交易 API 与行情 API 复用此结构。即使某些行情前置允许空账号登录，也必须完整保留
/// MAC、动态密码、保留 IP、短信码等尾部字段；省略字段会使原生 API 按 6.7.13 布局读出
/// 托管分配块之外的数据。
/// </para>
/// 字段顺序按对接方提供的 <c>6.7.13_API接口说明.chm</c> 内
/// <c>ThostFtdcUserApiStruct.h</c>，字符数组用 ByValTStr + GBK marshaler。
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct CThostFtdcReqUserLoginField
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
    public string TradingDay;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)]
    public string BrokerID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
    public string UserID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 41)]
    public string Password;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)]
    public string UserProductInfo;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)]
    public string InterfaceProductInfo;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)]
    public string ProtocolInfo;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
    public string MacAddress;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 41)]
    public string OneTimePassword;

    /// <summary>6.7.x 兼容保留字段（OldIPAddressType[16]），不可省略。</summary>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
    public string reserve1;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 36)]
    public string LoginRemark;

    public int ClientIPPort;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string ClientIPAddress;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 17)]
    public string SMSCode;
}
