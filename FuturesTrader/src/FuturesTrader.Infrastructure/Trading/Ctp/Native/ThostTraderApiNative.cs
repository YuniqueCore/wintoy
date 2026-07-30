using System.Runtime.InteropServices;

namespace FuturesTrader.Infrastructure.Trading.Ctp.Native;

/// <summary>
/// CTP 交易 API（<c>CThostFtdcTraderApi</c>）的 P/Invoke 封装：直连 <c>thosttraderapi_se.dll</c>（6.7.11，32 位 x86）。
/// 架构与 <see cref="FuturesTrader.Infrastructure.MarketData.Ctp.Native.ThostMdApiNative"/> 完全一致：
/// 静态工厂 Cdecl P/Invoke + 实例方法 vtable 调用（__thiscall：this 在 ECX，其余压栈）。
/// <para>
/// <b>CreateFtdcTraderApi 签名</b>（dumpbin 实证 mangling）：
/// <c>?CreateFtdcTraderApi@CThostFtdcTraderApi@@SAPAV1@PBD_N@Z</c>
/// → <c>(const char* flowPath, bool bIsProductionMode) -&gt; CThostFtdcTraderApi*</c>（2 参数，与 MdApi 的 4 参数不同）。
/// </para>
/// <para>
/// <b>TraderApi vtable 布局</b>（6.7.11 <c>ThostFtdcTraderApi.h</c>，无 virtual 析构 → 无析构槽）：
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
/// [9]  SubscribePrivateTopic(THOST_TE_RESUME_TYPE)
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
/// </code>
/// </para>
/// </summary>
internal static class ThostTraderApiNative
{
    private const string DllName = "thosttraderapi_se.dll";

    /// <summary>MSVC mangled name，dumpbin 实证：2 参数（flowPath + bIsProductionMode）。</summary>
    private const string CreateFtdcTraderApiEntryPoint =
        "?CreateFtdcTraderApi@CThostFtdcTraderApi@@SAPAV1@PBD_N@Z";

    private const string GetApiVersionEntryPoint =
        "?GetApiVersion@CThostFtdcTraderApi@@SAPBDXZ";

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

    /// <summary>公开包装：bIsProductionMode=false（SimNow/测试均传 false）。</summary>
    public static IntPtr CreateFtdcTraderApi(string flowPath) =>
        CreateFtdcTraderApiNative(flowPath ?? string.Empty, false);

    /// <summary>获取 API 版本字符串（静态，Cdecl）。</summary>
    [DllImport(DllName, EntryPoint = GetApiVersionEntryPoint,
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.LPStr)]
    private static extern string? GetApiVersionNative();

    public static string GetApiVersion() => GetApiVersionNative() ?? "unknown";

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
    }

    // ===== CThostFtdcTraderSpi vtable 索引（用于 SpiBridge 构造伪 C++ 对象） =====
    // 6.7.11 共 82 槽（0-81），无 virtual 析构。见 CtpTraderSpiBridge 类注释的完整布局。

    public static class SpiVtable
    {
        public const int OnFrontConnected = 0;
        public const int OnFrontDisconnected = 1;
        public const int OnHeartBeatWarning = 2;
        public const int OnRspAuthenticate = 3;
        public const int OnRspUserLogin = 4;
        // [5-10] OnRspUserLogout / OnRspUserPasswordUpdate / ... / OnRspGenUserText（5参 Rsp 模式）
        public const int OnRspOrderInsert = 11;
        // [12-13] OnRspParkedOrderInsert / OnRspParkedOrderAction
        public const int OnRspOrderAction = 14;
        // [15] OnRspQryMaxOrderVolume
        public const int OnRspSettlementInfoConfirm = 16;
        // [17-72] OnRspRemoveParkedOrder / ... / OnRspQryAccountregister（5参 Rsp 模式）
        public const int OnRspError = 73;
        public const int OnRtnOrder = 74;
        public const int OnRtnTrade = 75;
        public const int OnErrRtnOrderInsert = 76;
        public const int OnErrRtnOrderAction = 77;
        // [78-81] OnRtnInstrumentStatus / OnRtnBulletin / OnRtnTradingNotice / OnRtnErrorConditionalOrder（2参 Rtn 模式）
        /// <summary>vtable 槽位数（含未使用槽），用于分配函数指针数组大小。</summary>
        public const int SlotCount = 82;
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

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    [return: MarshalAs(UnmanagedType.LPStr)]
    public delegate string GetTradingDayDelegate(IntPtr thisPtr);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall, CharSet = CharSet.Ansi)]
    public delegate void RegisterFrontDelegate(IntPtr thisPtr, [MarshalAs(UnmanagedType.LPStr)] string pszFrontAddress);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    public delegate void RegisterSpiDelegate(IntPtr thisPtr, IntPtr pSpi);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    public delegate void SubscribeTopicDelegate(IntPtr thisPtr, int nResumeType);

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

    public static string GetTradingDay(IntPtr apiPtr) =>
        GetVtableMethod<GetTradingDayDelegate>(apiPtr, ApiVtable.GetTradingDay)(apiPtr) ?? string.Empty;

    public static void RegisterFront(IntPtr apiPtr, string frontAddress) =>
        GetVtableMethod<RegisterFrontDelegate>(apiPtr, ApiVtable.RegisterFront)(apiPtr, frontAddress);

    public static void RegisterSpi(IntPtr apiPtr, IntPtr spiPtr) =>
        GetVtableMethod<RegisterSpiDelegate>(apiPtr, ApiVtable.RegisterSpi)(apiPtr, spiPtr);

    public static void SubscribePrivateTopic(IntPtr apiPtr, int resumeType) =>
        GetVtableMethod<SubscribeTopicDelegate>(apiPtr, ApiVtable.SubscribePrivateTopic)(apiPtr, resumeType);

    public static void SubscribePublicTopic(IntPtr apiPtr, int resumeType) =>
        GetVtableMethod<SubscribeTopicDelegate>(apiPtr, ApiVtable.SubscribePublicTopic)(apiPtr, resumeType);

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
}
