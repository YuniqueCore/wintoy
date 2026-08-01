using System.Runtime.InteropServices;

namespace FuturesTrader.Infrastructure.Trading.Ctp.Native;

/// <summary>
/// CTP 交易 API（<c>CThostFtdcTraderApi</c>）的 P/Invoke 封装：直连 <c>thosttraderapi_se.dll</c>（6.7.13，64 位 x64）。
/// 架构与 <see cref="FuturesTrader.Infrastructure.MarketData.Ctp.Native.ThostMdApiNative"/> 完全一致：
/// 静态工厂 Cdecl P/Invoke + 实例方法 vtable 调用（__thiscall：this 在 ECX，其余压栈）。
/// <para>
/// <b>CreateFtdcTraderApi 签名</b>（dumpbin 实证 mangling）：
/// <c>?CreateFtdcTraderApi@CThostFtdcTraderApi@@SAPAV1@PBD_N@Z</c>
/// → <c>(const char* flowPath, bool bIsProductionMode) -&gt; CThostFtdcTraderApi*</c>（2 参数，与 MdApi 的 4 参数不同）。
/// </para>
/// <para>
/// <b>TraderApi vtable 布局</b>（6.7.13 <c>ThostFtdcTraderApi.h</c>，无 virtual 析构 → 无析构槽）：
/// 6.7.13 仍保留 [4] <c>GetFrontInfo</c>，因此本类实际调用的 API 槽位与 6.7.11 一致；
/// 不能因为 SPI 新增回调而把 API 槽位整体前移。该版本的 ABI 变化是 [9]
/// <c>SubscribePrivateTopic</c> 新增 <c>nSeqNo</c> 参数，SPI 在认证回调后新增
/// <c>OnRtnPrivateSeqNo</c>（见 <see cref="SpiVtable"/>）。
/// <code>
/// [0]  Release()
/// [1]  Init()
/// [2]  Join() -&gt; int
/// [3]  GetTradingDay() -&gt; const char*
/// [4]  GetFrontInfo(...)
/// [5]  RegisterFront(char*)
/// [6]  RegisterNameServer(char*)
/// [7]  RegisterFensUserInfo(...)
/// [8]  RegisterSpi(CThostFtdcTraderSpi*)
/// [9]  SubscribePrivateTopic(THOST_TE_RESUME_TYPE, int nSeqNo = 1)
/// [10] SubscribePublicTopic(THOST_TE_RESUME_TYPE)
/// [11] ReqAuthenticate(CThostFtdcReqAuthenticateField*, int)
/// [12-15] RegisterUserSystemInfo / SubmitUserSystemInfo / RegisterWechat* / SubmitWechat*
/// [16] ReqUserLogin(CThostFtdcReqUserLoginField*, int)
/// [17-25] ReqUserLogout / ReqUserPasswordUpdate / ... / ReqUserLoginWithOTP
/// [26] ReqOrderInsert(CThostFtdcInputOrderField*, int)
/// [27-28] ReqParkedOrderInsert / ReqParkedOrderAction
/// [29] ReqOrderAction(CThostFtdcInputOrderActionField*, int)
/// [30] ReqQryMaxOrderVolume
/// [31] ReqSettlementInfoConfirm(CThostFtdcSettlementInfoConfirmField*, int)
/// [32-44] ReqRemoveParkedOrder / ... / ReqQryTrade
/// [45] ReqQryInvestorPosition(CThostFtdcQryInvestorPositionField*, int)
/// [46] ReqQryTradingAccount(CThostFtdcQryTradingAccountField*, int)
/// [47-50] ReqQryInvestor / ReqQryTradingCode / ReqQryInstrumentMarginRate / ReqQryInstrumentCommissionRate
/// [51] ReqQryUserSession（6.7.11 新增）
/// [52-53] ReqQryExchange / ReqQryProduct
/// [54] ReqQryInstrument(CThostFtdcQryInstrumentField*, int)
/// </code>
/// </para>
/// </summary>
internal static class ThostTraderApiNative
{
    private const string DllName = "thosttraderapi_se.dll";

    /// <summary>MSVC x64 mangled name（PE 导出表实证）：2 参数（flowPath + bIsProductionMode）。</summary>
    private const string CreateFtdcTraderApiEntryPoint =
        "?CreateFtdcTraderApi@CThostFtdcTraderApi@@SAPEAV1@PEBD_N@Z";

    private const string GetApiVersionEntryPoint =
        "?GetApiVersion@CThostFtdcTraderApi@@SAPEBDXZ";

    // ===== 静态工厂（Cdecl，无 this 指针） =====

    /// <summary>
    /// 创建 TraderApi 实例。返回 C++ 对象指针（IntPtr.Zero 表示失败）。
    /// 2 参数：flowPath + bIsProductionMode（与 MdApi 的 4 参数不同，Trader 无 UDP/多播）。
    /// </summary>
    [DllImport(DllName, EntryPoint = CreateFtdcTraderApiEntryPoint,
        CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.SysInt)]
    private static extern IntPtr CreateFtdcTraderApiNative(
        string pszFlowPath,
        [MarshalAs(UnmanagedType.U1)] bool bIsProductionMode);

    /// <summary>公开包装：由调用方显式选择 bIsProductionMode，避免隐式使用仿真 API 模式。</summary>
    public static IntPtr CreateFtdcTraderApi(string flowPath, bool isProductionMode) =>
        CreateFtdcTraderApiNative(flowPath ?? string.Empty, isProductionMode);

    /// <summary>
    /// 获取 API 版本字符串（静态，Cdecl）。返回 const char* 指向 DLL 内部静态缓冲区。
    /// <para>
    /// 注意：用 <see cref="IntPtr"/> 接收而非 <c>LPStr</c> marshaller——后者在 .NET 10 预览版
    /// 会触发堆损坏（0xC0000374）。手动 <see cref="Marshal.PtrToStringAnsi(IntPtr)"/> 只读不写、
    /// 不释放（所有权属 DLL），最安全。
    /// </para>
    /// </summary>
    [DllImport(DllName, EntryPoint = GetApiVersionEntryPoint,
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr GetApiVersionNative();

    public static string GetApiVersion()
    {
        IntPtr ptr = GetApiVersionNative();
        return ptr == IntPtr.Zero ? "unknown" : Marshal.PtrToStringAnsi(ptr) ?? "unknown";
    }

    // ===== CThostFtdcTraderApi vtable 索引（见类注释） =====

    public static class ApiVtable
    {
        public const int Release = 0;
        public const int Init = 1;
        public const int Join = 2;
        public const int GetTradingDay = 3;
        public const int RegisterFront = 5;
        public const int RegisterSpi = 8;
        public const int SubscribePrivateTopic = 9;
        public const int SubscribePublicTopic = 10;
        public const int ReqAuthenticate = 11;
        public const int ReqUserLogin = 16;
        public const int ReqOrderInsert = 26;
        public const int ReqOrderAction = 29;
        public const int ReqSettlementInfoConfirm = 31;
        // 查询家族（6.7.13 与 6.7.11 的既有槽位一致）
        public const int ReqQryInvestorPosition = 45;
        public const int ReqQryTradingAccount = 46;
        public const int ReqQryUserSession = 51;
        public const int ReqQryInstrument = 54;
    }

    // ===== CThostFtdcTraderSpi vtable 索引（用于 SpiBridge 构造伪 C++ 对象） =====
    // 6.7.13 共 178 槽（0-177），无 virtual 析构。
    // 该版本在 OnRspAuthenticate 后的 [4] 新增 OnRtnPrivateSeqNo，故所有旧 [4-163]
    // SPI 槽均后移一位；末尾另增加短信、价差申请和套保确认相关回调 [165-177]。

    public static class SpiVtable
    {
        public const int OnFrontConnected = 0;
        public const int OnFrontDisconnected = 1;
        public const int OnHeartBeatWarning = 2;
        public const int OnRspAuthenticate = 3;
        public const int OnRtnPrivateSeqNo = 4;
        public const int OnRspUserLogin = 5;
        // [6-11] OnRspUserLogout / OnRspUserPasswordUpdate / ... / OnRspGenUserText（5参 Rsp 模式）
        public const int OnRspOrderInsert = 12;
        // [13-14] OnRspParkedOrderInsert / OnRspParkedOrderAction
        public const int OnRspOrderAction = 15;
        // [16] OnRspQryMaxOrderVolume
        public const int OnRspSettlementInfoConfirm = 17;
        // [18-30] OnRspRemoveParkedOrder / ... / OnRspQryTrade（5参 Rsp 模式）
        public const int OnRspQryInvestorPosition = 31;   // 持仓查询回调
        public const int OnRspQryTradingAccount = 32;     // 资金账户查询回调
        // [33-36] OnRspQryInvestor / OnRspQryTradingCode / OnRspQryInstrumentMarginRate / OnRspQryInstrumentCommissionRate
        public const int OnRspQryUserSession = 37;        // Noop 占位，保持对齐
        // [38-39] OnRspQryExchange / OnRspQryProduct
        public const int OnRspQryInstrument = 40;         // 合约元数据查询回调
        // [41-74] OnRspQryDepthMarketData / ... / OnRspQryAccountregister（5参 Rsp 模式）
        public const int OnRspQryAccountregister = 74;
        public const int OnRspError = 75;
        public const int OnRtnOrder = 76;
        public const int OnRtnTrade = 77;
        public const int OnErrRtnOrderInsert = 78;
        public const int OnErrRtnOrderAction = 79;
        // [80-83] OnRtnInstrumentStatus / OnRtnBulletin / OnRtnTradingNotice / OnRtnErrorConditionalOrder（2参 Rtn 模式）
        // [84-164] 既有扩展槽（SPBM/RCAMS/RULE/OffsetSetting 等，均 Noop 占位避免越界）
        // [165-177] 6.7.13 新增短信、价差申请和套保确认回调。
        /// <summary>vtable 槽位数（含未使用槽），用于分配函数指针数组大小。6.7.13 最大槽位 177。</summary>
        public const int SlotCount = 178;
    }

    // ===== 流订阅重传方式（THOST_TE_RESUME_TYPE） =====

    /// <summary>从上次收到的续传（推荐，避免重复数据又不会丢漏）。</summary>
    public const int TertResume = 1;

    // ===== vtable 调用委托类型（__thiscall：this 在 ECX，其余压栈） =====

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    public delegate void ReleaseDelegate(IntPtr thisPtr);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    public delegate void InitDelegate(IntPtr thisPtr);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    public delegate int JoinDelegate(IntPtr thisPtr);

    // 返回 const char* 用 IntPtr 接收（避免 LPStr marshaller 在 .NET 10 预览版堆损坏）
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    public delegate IntPtr GetTradingDayDelegate(IntPtr thisPtr);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall, CharSet = CharSet.Ansi)]
    public delegate void RegisterFrontDelegate(IntPtr thisPtr, [MarshalAs(UnmanagedType.LPStr)] string pszFrontAddress);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    public delegate void RegisterSpiDelegate(IntPtr thisPtr, IntPtr pSpi);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    public delegate void SubscribePrivateTopicDelegate(IntPtr thisPtr, int nResumeType, int nSeqNo);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    public delegate void SubscribePublicTopicDelegate(IntPtr thisPtr, int nResumeType);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    public delegate int ReqAuthenticateDelegate(IntPtr thisPtr, IntPtr pReqAuthenticateField, int nRequestID);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    public delegate int ReqUserLoginDelegate(IntPtr thisPtr, IntPtr pReqUserLoginField, int nRequestID);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    public delegate int ReqOrderInsertDelegate(IntPtr thisPtr, IntPtr pInputOrder, int nRequestID);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    public delegate int ReqOrderActionDelegate(IntPtr thisPtr, IntPtr pInputOrderAction, int nRequestID);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    public delegate int ReqSettlementInfoConfirmDelegate(IntPtr thisPtr, IntPtr pSettlementInfoConfirm, int nRequestID);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    public delegate int ReqQryInvestorPositionDelegate(IntPtr thisPtr, IntPtr pQryInvestorPosition, int nRequestID);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    public delegate int ReqQryTradingAccountDelegate(IntPtr thisPtr, IntPtr pQryTradingAccount, int nRequestID);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    public delegate int ReqQryInstrumentDelegate(IntPtr thisPtr, IntPtr pQryInstrument, int nRequestID);

    // ===== vtable 调用辅助 =====

    /// <summary>读取 C++ 对象 vtable[idx] 的函数指针，并转为指定委托类型。</summary>
    public static T GetVtableMethod<T>(IntPtr apiPtr, int idx) where T : Delegate
    {
        IntPtr vtable = Marshal.ReadIntPtr(apiPtr);
        IntPtr funcPtr = Marshal.ReadIntPtr(vtable, idx * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(funcPtr);
    }

    // ===== 高层 API 调用包装 =====

    public static void Release(IntPtr apiPtr) =>
        GetVtableMethod<ReleaseDelegate>(apiPtr, ApiVtable.Release)(apiPtr);

    public static void Init(IntPtr apiPtr) =>
        GetVtableMethod<InitDelegate>(apiPtr, ApiVtable.Init)(apiPtr);

    public static int Join(IntPtr apiPtr) =>
        GetVtableMethod<JoinDelegate>(apiPtr, ApiVtable.Join)(apiPtr);

    public static string GetTradingDay(IntPtr apiPtr)
    {
        IntPtr ptr = GetVtableMethod<GetTradingDayDelegate>(apiPtr, ApiVtable.GetTradingDay)(apiPtr);
        return ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
    }

    public static void RegisterFront(IntPtr apiPtr, string frontAddress) =>
        GetVtableMethod<RegisterFrontDelegate>(apiPtr, ApiVtable.RegisterFront)(apiPtr, frontAddress);

    public static void RegisterSpi(IntPtr apiPtr, IntPtr spiPtr) =>
        GetVtableMethod<RegisterSpiDelegate>(apiPtr, ApiVtable.RegisterSpi)(apiPtr, spiPtr);

    public static void SubscribePrivateTopic(IntPtr apiPtr, int resumeType, int nSeqNo = 1) =>
        GetVtableMethod<SubscribePrivateTopicDelegate>(apiPtr, ApiVtable.SubscribePrivateTopic)(apiPtr, resumeType, nSeqNo);

    public static void SubscribePublicTopic(IntPtr apiPtr, int resumeType) =>
        GetVtableMethod<SubscribePublicTopicDelegate>(apiPtr, ApiVtable.SubscribePublicTopic)(apiPtr, resumeType);

    public static int ReqAuthenticate(IntPtr apiPtr, IntPtr pReqAuthenticateField, int nRequestID) =>
        GetVtableMethod<ReqAuthenticateDelegate>(apiPtr, ApiVtable.ReqAuthenticate)(apiPtr, pReqAuthenticateField, nRequestID);

    public static int ReqUserLogin(IntPtr apiPtr, IntPtr pReqUserLoginField, int nRequestID) =>
        GetVtableMethod<ReqUserLoginDelegate>(apiPtr, ApiVtable.ReqUserLogin)(apiPtr, pReqUserLoginField, nRequestID);

    public static int ReqOrderInsert(IntPtr apiPtr, IntPtr pInputOrder, int nRequestID) =>
        GetVtableMethod<ReqOrderInsertDelegate>(apiPtr, ApiVtable.ReqOrderInsert)(apiPtr, pInputOrder, nRequestID);

    public static int ReqOrderAction(IntPtr apiPtr, IntPtr pInputOrderAction, int nRequestID) =>
        GetVtableMethod<ReqOrderActionDelegate>(apiPtr, ApiVtable.ReqOrderAction)(apiPtr, pInputOrderAction, nRequestID);

    public static int ReqSettlementInfoConfirm(IntPtr apiPtr, IntPtr pSettlementInfoConfirm, int nRequestID) =>
        GetVtableMethod<ReqSettlementInfoConfirmDelegate>(apiPtr, ApiVtable.ReqSettlementInfoConfirm)(apiPtr, pSettlementInfoConfirm, nRequestID);

    public static int ReqQryInvestorPosition(IntPtr apiPtr, IntPtr pQryInvestorPosition, int nRequestID) =>
        GetVtableMethod<ReqQryInvestorPositionDelegate>(apiPtr, ApiVtable.ReqQryInvestorPosition)(apiPtr, pQryInvestorPosition, nRequestID);

    public static int ReqQryTradingAccount(IntPtr apiPtr, IntPtr pQryTradingAccount, int nRequestID) =>
        GetVtableMethod<ReqQryTradingAccountDelegate>(apiPtr, ApiVtable.ReqQryTradingAccount)(apiPtr, pQryTradingAccount, nRequestID);

    public static int ReqQryInstrument(IntPtr apiPtr, IntPtr pQryInstrument, int nRequestID) =>
        GetVtableMethod<ReqQryInstrumentDelegate>(apiPtr, ApiVtable.ReqQryInstrument)(apiPtr, pQryInstrument, nRequestID);
}
