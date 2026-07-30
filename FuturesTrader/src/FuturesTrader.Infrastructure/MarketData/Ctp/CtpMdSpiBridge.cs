using System.Runtime.InteropServices;
using FuturesTrader.Infrastructure.MarketData.Ctp.Native;

namespace FuturesTrader.Infrastructure.MarketData.Ctp;

/// <summary>
/// CTP 行情 SPI 回调桥接：在非托管内存构造一个伪 C++ <c>CThostFtdcMdSpi</c> 对象，
/// 使 CTP 能通过 vtable 调用我们的 C# 委托。CTP 调用回调时执行：
/// <code>vtable[idx](pSpi, args...)  // this 在 ECX（__thiscall），其余压栈</code>
/// <para>
/// 内存布局（x86，IntPtr.Size=4）：
/// <code>
/// 伪 SPI 对象 (4 字节):  [0] = &amp;vtable
/// vtable (13 * 4 = 52 字节): [0..12] = 函数指针
/// </code>
/// 传给 <c>RegisterSpi</c> 的就是"伪 SPI 对象"指针，CTP 把它当 <c>CThostFtdcMdSpi*</c> 用。
/// </para>
/// <para>
/// <b>生命周期</b>：所有委托必须作为字段持有，防止 GC 回收导致函数指针失效（悬挂指针 → 崩溃）。
/// 释放时按"先释放 vtable/对象内存，再放任委托被 GC"顺序，期间 CTP 不可再持此 SPI 引用
/// （由 <see cref="CtpMarketDataService"/> 在 <c>Release</c> 前调 <c>RegisterSpi(null)</c> 保证）。
/// </para>
/// <para>
/// <b>回调线程</b>：所有 <c>OnXxx</c> 在 CTP 工作线程触发，<see cref="CtpMarketDataService"/> 订阅
/// <see cref="DepthMarketDataReceived"/> 等事件后需自行跨线程同步（Subject.OnNext 已线程安全）。
/// </para>
/// <para>
/// <b>bool 参数</b>：C++ <c>bool</c> 是 1 字节，.NET 默认 marshal 为 4 字节 BOOL。
/// 必须显式 <c>[MarshalAs(UnmanagedType.U1)]</c>，否则读到栈槽高位垃圾字节 → 误判为 true。
/// </para>
/// </summary>
internal sealed class CtpMdSpiBridge : IDisposable
{
    private IntPtr _spiObject;          // 伪 SPI 对象（含 vtable 指针）
    private IntPtr _vtable;             // vtable 函数指针数组
    private bool _disposed;

    // ====== 必须持有所有委托引用，防止 GC 回收函数指针 ======
    // vtable[0]..[12]，按 SpiVtable 索引顺序排列。未使用的槽也填空回调，避免 CTP 误调时崩 AV。

    private readonly OnFrontConnectedDelegate _onFrontConnected;
    private readonly OnFrontDisconnectedDelegate _onFrontDisconnected;
    private readonly OnHeartBeatWarningDelegate _onHeartBeatWarning;
    private readonly OnRspUserLoginDelegate _onRspUserLogin;
    private readonly NoopRspLogoutDelegate _onRspUserLogout;
    private readonly NoopRspQryMulticastDelegate _onRspQryMulticastInstrument;
    private readonly OnRspErrorDelegate _onRspError;
    private readonly NoopRspSubMarketDataDelegate _onRspSubMarketData;
    private readonly NoopRspUnSubMarketDataDelegate _onRspUnSubMarketData;
    private readonly NoopRspSubForQuoteRspDelegate _onRspSubForQuoteRsp;
    private readonly NoopRspUnSubForQuoteRspDelegate _onRspUnSubForQuoteRsp;
    private readonly OnRtnDepthMarketDataDelegate _onRtnDepthMarketData;
    private readonly NoopRtnForQuoteRspDelegate _onRtnForQuoteRsp;

    public CtpMdSpiBridge()
    {
        // 先创建委托（持引用），再构造 vtable，避免逆序
        _onFrontConnected = new OnFrontConnectedDelegate(HandleFrontConnected);
        _onFrontDisconnected = new OnFrontDisconnectedDelegate(HandleFrontDisconnected);
        _onHeartBeatWarning = new OnHeartBeatWarningDelegate(HandleHeartBeatWarning);
        _onRspUserLogin = new OnRspUserLoginDelegate(HandleRspUserLogin);
        _onRspUserLogout = new NoopRspLogoutDelegate(NoopLogout);
        _onRspQryMulticastInstrument = new NoopRspQryMulticastDelegate(NoopRspQryMulticast);
        _onRspError = new OnRspErrorDelegate(HandleRspError);
        _onRspSubMarketData = new NoopRspSubMarketDataDelegate(NoopRspSubMarketData);
        _onRspUnSubMarketData = new NoopRspUnSubMarketDataDelegate(NoopRspUnSubMarketData);
        _onRspSubForQuoteRsp = new NoopRspSubForQuoteRspDelegate(NoopRspSubForQuoteRsp);
        _onRspUnSubForQuoteRsp = new NoopRspUnSubForQuoteRspDelegate(NoopRspUnSubForQuoteRsp);
        _onRtnDepthMarketData = new OnRtnDepthMarketDataDelegate(HandleRtnDepthMarketData);
        _onRtnForQuoteRsp = new NoopRtnForQuoteRspDelegate(NoopRtnForQuoteRsp);

        BuildVtable();
    }

    /// <summary>获取传给 <c>RegisterSpi</c> 的伪 SPI 指针。释放后为 <see cref="IntPtr.Zero"/>。</summary>
    public IntPtr SpiPointer => _spiObject;

    // ===== 对外事件（在 CTP 工作线程触发，订阅方需自行同步） =====

    /// <summary>前端连接已建立（应在此发起 ReqUserLogin）。</summary>
    public event Action? FrontConnected;

    /// <summary>前端连接断开（nReason 见 CTP 文档：0x1001 网络读失败 等）。</summary>
    public event Action<int>? FrontDisconnected;

    /// <summary>登录响应：success=true 表示登录成功。</summary>
    public event Action<bool, string>? RspUserLogin;

    /// <summary>错误应答（nRequestID=-1 表示通用错误）。</summary>
    public event Action<int, string, int>? RspError;

    /// <summary>深度行情到达。指针指向 <c>CThostFtdcDepthMarketDataField</c>，由 CTP 缓冲持有，回调内同步读取。</summary>
    public event Action<IntPtr>? DepthMarketDataReceived;

    // ===== vtable 构造 =====

    private void BuildVtable()
    {
        _vtable = Marshal.AllocHGlobal(ThostMdApiNative.SpiVtable.SlotCount * IntPtr.Size);
        _spiObject = Marshal.AllocHGlobal(IntPtr.Size);

        WriteVtableSlot(ThostMdApiNative.SpiVtable.OnFrontConnected, _onFrontConnected);
        WriteVtableSlot(ThostMdApiNative.SpiVtable.OnFrontDisconnected, _onFrontDisconnected);
        WriteVtableSlot(ThostMdApiNative.SpiVtable.OnHeartBeatWarning, _onHeartBeatWarning);
        WriteVtableSlot(ThostMdApiNative.SpiVtable.OnRspUserLogin, _onRspUserLogin);
        WriteVtableSlot(ThostMdApiNative.SpiVtable.OnRspUserLogout, _onRspUserLogout);
        WriteVtableSlot(ThostMdApiNative.SpiVtable.OnRspQryMulticastInstrument, _onRspQryMulticastInstrument);
        WriteVtableSlot(ThostMdApiNative.SpiVtable.OnRspError, _onRspError);
        WriteVtableSlot(ThostMdApiNative.SpiVtable.OnRspSubMarketData, _onRspSubMarketData);
        WriteVtableSlot(ThostMdApiNative.SpiVtable.OnRspUnSubMarketData, _onRspUnSubMarketData);
        WriteVtableSlot(ThostMdApiNative.SpiVtable.OnRspSubForQuoteRsp, _onRspSubForQuoteRsp);
        WriteVtableSlot(ThostMdApiNative.SpiVtable.OnRspUnSubForQuoteRsp, _onRspUnSubForQuoteRsp);
        WriteVtableSlot(ThostMdApiNative.SpiVtable.OnRtnDepthMarketData, _onRtnDepthMarketData);
        WriteVtableSlot(ThostMdApiNative.SpiVtable.OnRtnForQuoteRsp, _onRtnForQuoteRsp);

        // 伪 SPI 对象首槽 = vtable 指针（C++ 对象多态的第一字段）
        Marshal.WriteIntPtr(_spiObject, _vtable);
    }

    private void WriteVtableSlot(int idx, Delegate d)
    {
        IntPtr fnPtr = Marshal.GetFunctionPointerForDelegate(d);
        Marshal.WriteIntPtr(_vtable, idx * IntPtr.Size, fnPtr);
    }

    // ===== 回调实现（转发到事件） =====

    private void HandleFrontConnected(IntPtr _) => FrontConnected?.Invoke();

    private void HandleFrontDisconnected(IntPtr _, int nReason) => FrontDisconnected?.Invoke(nReason);

    private void HandleHeartBeatWarning(IntPtr _, int nTimeLapse)
    {
        // 心跳警告仅作日志用，本桥不转发（订阅方按需扩展）
    }

    private void HandleRspUserLogin(
        IntPtr _,
        IntPtr pRspUserLogin,
        IntPtr pRspInfo,
        int nRequestID,
        [MarshalAs(UnmanagedType.U1)] bool bIsLast)
    {
        bool success = true;
        string error = string.Empty;
        if (pRspInfo != IntPtr.Zero)
        {
            var info = Marshal.PtrToStructure<CThostFtdcRspInfoField>(pRspInfo);
            if (info.ErrorID != 0)
            {
                success = false;
                error = $"CTP MdApi 登录失败 ErrorID={info.ErrorID} Msg={info.ErrorMsg}";
            }
        }
        RspUserLogin?.Invoke(success, error);
    }

    private void HandleRspError(
        IntPtr _,
        IntPtr pRspInfo,
        int nRequestID,
        [MarshalAs(UnmanagedType.U1)] bool bIsLast)
    {
        int errorId = 0;
        string errorMsg = "未知错误";
        if (pRspInfo != IntPtr.Zero)
        {
            var info = Marshal.PtrToStructure<CThostFtdcRspInfoField>(pRspInfo);
            errorId = info.ErrorID;
            errorMsg = info.ErrorMsg ?? "未知错误";
        }
        RspError?.Invoke(errorId, errorMsg, nRequestID);
    }

    private void HandleRtnDepthMarketData(IntPtr _, IntPtr pDepthMarketData)
    {
        if (pDepthMarketData == IntPtr.Zero) return;
        // 指针在 CTP 缓冲内，回调返回后可能被复用 → 订阅方必须在回调内同步消费
        DepthMarketDataReceived?.Invoke(pDepthMarketData);
    }

    // ===== 未使用的回调（空实现，仅占 vtable 槽） =====

    private static void NoopLogout(IntPtr _, IntPtr p1, IntPtr p2, int n, bool b) { }
    private static void NoopRspQryMulticast(IntPtr _, IntPtr p1, IntPtr p2, int n, bool b) { }
    private static void NoopRspSubMarketData(IntPtr _, IntPtr p1, IntPtr p2, int n, bool b) { }
    private static void NoopRspUnSubMarketData(IntPtr _, IntPtr p1, IntPtr p2, int n, bool b) { }
    private static void NoopRspSubForQuoteRsp(IntPtr _, IntPtr p1, IntPtr p2, int n, bool b) { }
    private static void NoopRspUnSubForQuoteRsp(IntPtr _, IntPtr p1, IntPtr p2, int n, bool b) { }
    private static void NoopRtnForQuoteRsp(IntPtr _, IntPtr p1) { }

    // ===== 委托类型（__thiscall：this 在 ECX，其余压栈；bool 用 U1 = 1 字节） =====

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void OnFrontConnectedDelegate(IntPtr thisPtr);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void OnFrontDisconnectedDelegate(IntPtr thisPtr, int nReason);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void OnHeartBeatWarningDelegate(IntPtr thisPtr, int nTimeLapse);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void OnRspUserLoginDelegate(
        IntPtr thisPtr,
        IntPtr pRspUserLogin,
        IntPtr pRspInfo,
        int nRequestID,
        [MarshalAs(UnmanagedType.U1)] bool bIsLast);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void NoopRspLogoutDelegate(IntPtr thisPtr, IntPtr p1, IntPtr p2, int n, [MarshalAs(UnmanagedType.U1)] bool b);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void NoopRspQryMulticastDelegate(IntPtr thisPtr, IntPtr p1, IntPtr p2, int n, [MarshalAs(UnmanagedType.U1)] bool b);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void OnRspErrorDelegate(
        IntPtr thisPtr,
        IntPtr pRspInfo,
        int nRequestID,
        [MarshalAs(UnmanagedType.U1)] bool bIsLast);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void NoopRspSubMarketDataDelegate(IntPtr thisPtr, IntPtr p1, IntPtr p2, int n, [MarshalAs(UnmanagedType.U1)] bool b);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void NoopRspUnSubMarketDataDelegate(IntPtr thisPtr, IntPtr p1, IntPtr p2, int n, [MarshalAs(UnmanagedType.U1)] bool b);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void NoopRspSubForQuoteRspDelegate(IntPtr thisPtr, IntPtr p1, IntPtr p2, int n, [MarshalAs(UnmanagedType.U1)] bool b);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void NoopRspUnSubForQuoteRspDelegate(IntPtr thisPtr, IntPtr p1, IntPtr p2, int n, [MarshalAs(UnmanagedType.U1)] bool b);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void OnRtnDepthMarketDataDelegate(IntPtr thisPtr, IntPtr pDepthMarketData);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void NoopRtnForQuoteRspDelegate(IntPtr thisPtr, IntPtr p1);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_spiObject != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_spiObject);
            _spiObject = IntPtr.Zero;
        }
        if (_vtable != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_vtable);
            _vtable = IntPtr.Zero;
        }
    }
}
