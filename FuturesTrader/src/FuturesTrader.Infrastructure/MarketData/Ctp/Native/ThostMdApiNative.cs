using System.Runtime.InteropServices;

namespace FuturesTrader.Infrastructure.MarketData.Ctp.Native;

/// <summary>
/// CTP 行情 API（<c>CThostFtdcMdApi</c>）的 P/Invoke 封装：直连 <c>thostmduserapi_se.dll</c>（6.7.13，64 位 x64）。
/// <para>
/// CTP API 是 C++ 虚函数接口，无法直接 P/Invoke。本类通过两条路径访问：
/// <list type="number">
///   <item><b>静态工厂</b>：<c>CreateFtdcMdApi</c> 是普通 C 函数（虽然静态成员），用 <c>Cdecl</c> 直接 P/Invoke。
///     入口名使用 MSVC mangled name <c>?CreateFtdcMdApi@CThostFtdcMdApi@@SAPAV1@PBD_N1_N@Z</c>，
///     解码后签名为 <c>(const char*, bool, bool, bool) -&gt; CThostFtdcMdApi*</c>（4 参数：flowPath/isUsingUdp/isMulticast/isProductionMode，
///     6.7.11 增 <c>bIsProductionMode</c>，已通过 DLL 二进制扫描实证）。</item>
///   <item><b>实例方法</b>：通过 vtable 调用。返回的 <see cref="IntPtr"/> 是 C++ 对象指针，第一字段是 vtable 指针。
///     用 <see cref="Marshal.ReadIntPtr"/> 取 vtable[N]，再用 <see cref="Marshal.GetDelegateForFunctionPointer"/>
///     转成委托调用。x86 下 __thiscall：this 在 ECX，其余参数从右向左压栈。</item>
/// </list>
/// </para>
/// <para>
/// <b>vtable 布局</b>（来自 6.7.x <c>ThostFtdcMdApi.h</c>，类无 virtual 析构 → 无析构槽）：
/// <code>
/// [0]  Release()
/// [1]  Init()
/// [2]  Join() -&gt; int
/// [3]  GetTradingDay() -&gt; const char*
/// [4]  RegisterFront(char*)
/// [5]  RegisterNameServer(char*)        // 6.7.x 起新增
/// [6]  RegisterFensUserInfo(...)
/// [7]  RegisterSpi(CThostFtdcMdSpi*)
/// [8]  SubscribeMarketData(char**, int) -&gt; int
/// [9]  UnSubscribeMarketData(char**, int) -&gt; int
/// [10] SubscribeForQuoteRsp
/// [11] UnSubscribeForQuoteRsp
/// [12] ReqUserLogin(CThostFtdcReqUserLoginField*, int) -&gt; int
/// [13] ReqUserLogout
/// [14] ReqQryMulticastInstrument
/// </code>
/// </para>
/// <para><b>线程模型</b>：CTP 内部维护工作线程，所有 SPI 回调（见 <see cref="CtpMdSpiBridge"/>）在该线程触发。
/// 调用方（CtpMarketDataService）需自行处理跨线程同步。Init() 后 API 立即返回，回调异步到达。</para>
/// </summary>
internal static class ThostMdApiNative
{
    private const string DllName = "thostmduserapi_se.dll";

    /// <summary>MSVC x64 mangled name（PE 导出表实证）：4 参数（flowPath + 3 bools）。</summary>
    private const string CreateFtdcMdApiEntryPoint =
        "?CreateFtdcMdApi@CThostFtdcMdApi@@SAPEAV1@PEBD_N1_N@Z";

    private const string GetApiVersionEntryPoint =
        "?GetApiVersion@CThostFtdcMdApi@@SAPEBDXZ";

    // ===== 静态工厂（Cdecl，无 this 指针） =====

    /// <summary>
    /// 创建 MdApi 实例。返回 C++ 对象指针（ IntPtr.Zero 表示失败）。
    /// 三个 bool：isUsingUdp / isMulticast / isProductionMode（6.7.11 增）。
    /// </summary>
    [DllImport(DllName, EntryPoint = CreateFtdcMdApiEntryPoint,
        CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.SysInt)]
    private static extern IntPtr CreateFtdcMdApiNative(
        string pszFlowPath,
        [MarshalAs(UnmanagedType.U1)] bool bIsUsingUdp,
        [MarshalAs(UnmanagedType.U1)] bool bIsMulticast,
        [MarshalAs(UnmanagedType.U1)] bool bIsProductionMode);

    /// <summary>
    /// 公开包装：flowPath 用 ANSI（CTP 内部按 GBK 处理路径，纯 ASCII 路径无差异）；
    /// UDP/多播保持关闭，生产标志由调用方显式传入，避免把生产 API 静默降级为仿真模式。
    /// </summary>
    public static IntPtr CreateFtdcMdApi(string flowPath, bool isProductionMode) =>
        CreateFtdcMdApiNative(flowPath ?? string.Empty, false, false, isProductionMode);

    /// <summary>
    /// 获取 API 版本字符串（静态，Cdecl）。返回 const char* 指向 DLL 内静态缓冲。
    /// 用 <see cref="IntPtr"/> 接收而非 <c>LPStr</c> marshaller（后者在 .NET 10 预览版触发堆损坏 0xC0000374）。
    /// </summary>
    [DllImport(DllName, EntryPoint = GetApiVersionEntryPoint,
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr GetApiVersionNative();

    /// <summary>公开包装：返回形如 "v6.7.13_xxx" 的版本号。</summary>
    public static string GetApiVersion()
    {
        IntPtr ptr = GetApiVersionNative();
        return ptr == IntPtr.Zero ? "unknown" : Marshal.PtrToStringAnsi(ptr) ?? "unknown";
    }

    // ===== CThostFtdcMdApi vtable 索引（注释见类注释） =====

    public static class ApiVtable
    {
        public const int Release = 0;
        public const int Init = 1;
        public const int Join = 2;
        public const int GetTradingDay = 3;
        public const int RegisterFront = 4;
        // [5]=RegisterNameServer / [6]=RegisterFensUserInfo 未使用
        public const int RegisterSpi = 7;
        public const int SubscribeMarketData = 8;
        public const int UnSubscribeMarketData = 9;
        public const int ReqUserLogin = 12;
    }

    // ===== CThostFtdcMdSpi vtable 索引（用于 SpiBridge 构造伪 C++ 对象） =====
    // 类无 virtual 析构 → 无 [0] 析构槽，从 OnFrontConnected 起。

    public static class SpiVtable
    {
        public const int OnFrontConnected = 0;
        public const int OnFrontDisconnected = 1;
        public const int OnHeartBeatWarning = 2;
        public const int OnRspUserLogin = 3;
        public const int OnRspUserLogout = 4;
        public const int OnRspQryMulticastInstrument = 5;
        public const int OnRspError = 6;
        public const int OnRspSubMarketData = 7;
        public const int OnRspUnSubMarketData = 8;
        public const int OnRspSubForQuoteRsp = 9;
        public const int OnRspUnSubForQuoteRsp = 10;
        public const int OnRtnDepthMarketData = 11;
        public const int OnRtnForQuoteRsp = 12;
        /// <summary>vtable 槽位数（含未使用槽），用于分配函数指针数组大小。</summary>
        public const int SlotCount = 13;
    }

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
    public delegate int SubscribeMarketDataDelegate(IntPtr thisPtr, IntPtr ppInstrumentIDs, int nCount);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    public delegate int ReqUserLoginDelegate(IntPtr thisPtr, IntPtr pReqUserLoginField, int nRequestID);

    // ===== vtable 调用辅助：读取 vtable[N] 并转委托 =====

    /// <summary>读取 C++ 对象 vtable[idx] 的函数指针，并转为指定委托类型。</summary>
    public static T GetVtableMethod<T>(IntPtr apiPtr, int idx) where T : Delegate
    {
        // C++ 对象布局：[0] = vtable 指针；vtable[N] = 第 N 个虚函数
        IntPtr vtable = Marshal.ReadIntPtr(apiPtr);
        IntPtr funcPtr = Marshal.ReadIntPtr(vtable, idx * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(funcPtr);
    }

    // ===== 高层 API 调用包装（每次都重新取 vtable，无缓存，避免对象释放后用旧指针） =====

    public static void Release(IntPtr apiPtr) =>
        GetVtableMethod<ReleaseDelegate>(apiPtr, ApiVtable.Release)(apiPtr);

    public static void Init(IntPtr apiPtr) =>
        GetVtableMethod<InitDelegate>(apiPtr, ApiVtable.Init)(apiPtr);

    public static int Join(IntPtr apiPtr) =>
        GetVtableMethod<JoinDelegate>(apiPtr, ApiVtable.Join)(apiPtr);

    /// <summary>获取交易日（GBK 字符串）。返回值由 CTP 内部缓冲持有，调用方不应释放。</summary>
    public static string GetTradingDay(IntPtr apiPtr)
    {
        IntPtr ptr = GetVtableMethod<GetTradingDayDelegate>(apiPtr, ApiVtable.GetTradingDay)(apiPtr);
        return ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
    }

    public static void RegisterFront(IntPtr apiPtr, string frontAddress) =>
        GetVtableMethod<RegisterFrontDelegate>(apiPtr, ApiVtable.RegisterFront)(apiPtr, frontAddress);

    public static void RegisterSpi(IntPtr apiPtr, IntPtr spiPtr) =>
        GetVtableMethod<RegisterSpiDelegate>(apiPtr, ApiVtable.RegisterSpi)(apiPtr, spiPtr);

    /// <summary>订阅行情。ppInstrumentIDs 是 char** 指针数组（每个元素指向 GBK 合约代码字符串）。</summary>
    public static int SubscribeMarketData(IntPtr apiPtr, IntPtr ppInstrumentIDs, int nCount) =>
        GetVtableMethod<SubscribeMarketDataDelegate>(apiPtr, ApiVtable.SubscribeMarketData)(apiPtr, ppInstrumentIDs, nCount);

    /// <summary>退订行情。参数布局同 <see cref="SubscribeMarketData"/>。</summary>
    public static int UnSubscribeMarketData(IntPtr apiPtr, IntPtr ppInstrumentIDs, int nCount) =>
        GetVtableMethod<SubscribeMarketDataDelegate>(apiPtr, ApiVtable.UnSubscribeMarketData)(apiPtr, ppInstrumentIDs, nCount);

    /// <summary>用户登录请求（MdApi 登录无需认证，传全 0 的 ReqUserLoginField 即可）。</summary>
    public static int ReqUserLogin(IntPtr apiPtr, IntPtr pReqUserLoginField, int nRequestID) =>
        GetVtableMethod<ReqUserLoginDelegate>(apiPtr, ApiVtable.ReqUserLogin)(apiPtr, pReqUserLoginField, nRequestID);
}
