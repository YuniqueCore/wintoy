using System.Runtime.InteropServices;
using FuturesTrader.Infrastructure.MarketData.Ctp.Native;
using FuturesTrader.Infrastructure.Trading.Ctp.Native;

namespace FuturesTrader.Infrastructure.Trading.Ctp;

/// <summary>
/// CTP 交易 SPI 回调桥接：在非托管内存构造一个伪 C++ <c>CThostFtdcTraderSpi</c> 对象，
/// 使 CTP 能通过 vtable 调用我们的 C# 委托。架构与 <see cref="CtpMdSpiBridge"/> 一致。
/// <para>
/// <b>内存布局</b>（x86，IntPtr.Size=4）：
/// <code>
/// 伪 SPI 对象 (4 字节):  [0] = &amp;vtable
/// vtable (82 * 4 = 328 字节): [0..81] = 函数指针
/// </code>
/// </para>
/// <para>
/// <b>vtable 完整布局</b>（6.7.11 共 82 槽，见 <see cref="ThostTraderApiNative.SpiVtable"/>）：
/// <code>
/// [0]  OnFrontConnected          [1]  OnFrontDisconnected       [2]  OnHeartBeatWarning
/// [3]  OnRspAuthenticate         [4]  OnRspUserLogin
/// [5-10] OnRspUserLogout..OnRspGenUserText    [11] OnRspOrderInsert
/// [12-13] OnRspParkedOrder*      [14] OnRspOrderAction           [15] OnRspQryMaxOrderVolume
/// [16] OnRspSettlementInfoConfirm [17-72] OnRspRemoveParkedOrder..OnRspQryAccountregister
/// [73] OnRspError                [74] OnRtnOrder                 [75] OnRtnTrade
/// [76] OnErrRtnOrderInsert       [77] OnErrRtnOrderAction
/// [78-81] OnRtnInstrumentStatus..OnRtnErrorConditionalOrder
/// </code>
/// </para>
/// <para>
/// <b>签名分类</b>（决定委托类型，x86 __thiscall 栈布局必须精确匹配）：
/// <list type="bullet">
///   <item>5 参 Rsp(this,IntPtr,IntPtr,int,bool)：[3-72] 大多数 OnRsp*/OnRspQry*</item>
///   <item>4 参 RspError(this,IntPtr,int,bool)：[73] OnRspError</item>
///   <item>2 参 Rtn(this,IntPtr)：[74-75][78-81] OnRtn*</item>
///   <item>3 参 ErrRtn(this,IntPtr,IntPtr)：[76-77] OnErrRtn*</item>
///   <item>1 参(this)：[0] OnFrontConnected</item>
///   <item>2 参(this,int)：[1-2] OnFrontDisconnected/OnHeartBeatWarning</item>
/// </list>
/// </para>
/// <para>
/// <b>生命周期</b>：所有委托必须作为字段持有，防止 GC 回收函数指针 → 悬挂指针崩溃。
/// Noop 委托用 static readonly 共享（无实例状态），真实处理委托为实例字段（需访问事件）。
/// <see cref="CtpTradingService"/> 在 <c>Release</c> 前调 <c>RegisterSpi(null)</c> 解除 CTP 引用。
/// </para>
/// <para><b>bool 参数</b>：C++ bool = 1 字节，必须 <c>[MarshalAs(UnmanagedType.U1)]</c>，否则栈垃圾误判 true。</para>
/// </summary>
internal sealed class CtpTraderSpiBridge : IDisposable
{
    private IntPtr _spiObject;
    private IntPtr _vtable;
    private bool _disposed;

    // ===== 真实处理委托（实例字段，需访问实例事件） =====
    private readonly FrontConnectedDelegate _onFrontConnected;
    private readonly FrontDisconnectedDelegate _onFrontDisconnected;
    private readonly RspDelegate _onRspAuthenticate;
    private readonly RspDelegate _onRspUserLogin;
    private readonly RspDelegate _onRspOrderInsert;
    private readonly RspDelegate _onRspOrderAction;
    private readonly RspDelegate _onRspSettlementInfoConfirm;
    private readonly RspErrorDelegate _onRspError;
    private readonly RtnDelegate _onRtnOrder;
    private readonly RtnDelegate _onRtnTrade;

    // ===== Noop 委托（static readonly，共享单例，无实例状态） =====
    private static readonly FrontDisconnectedDelegate NoopInt = new(NoopIntHandler);
    private static readonly RspDelegate NoopRsp = new(NoopRspHandler);
    private static readonly RtnDelegate NoopRtn = new(NoopRtnHandler);
    private static readonly ErrRtnDelegate NoopErrRtn = new(NoopErrRtnHandler);

    public CtpTraderSpiBridge()
    {
        _onFrontConnected = new FrontConnectedDelegate(HandleFrontConnected);
        _onFrontDisconnected = new FrontDisconnectedDelegate(HandleFrontDisconnected);
        _onRspAuthenticate = new RspDelegate(HandleRspAuthenticate);
        _onRspUserLogin = new RspDelegate(HandleRspUserLogin);
        _onRspOrderInsert = new RspDelegate(HandleRspOrderInsert);
        _onRspOrderAction = new RspDelegate(HandleRspOrderAction);
        _onRspSettlementInfoConfirm = new RspDelegate(HandleRspSettlementInfoConfirm);
        _onRspError = new RspErrorDelegate(HandleRspError);
        _onRtnOrder = new RtnDelegate(HandleRtnOrder);
        _onRtnTrade = new RtnDelegate(HandleRtnTrade);

        BuildVtable();
    }

    public IntPtr SpiPointer => _spiObject;

    // ===== 对外事件（CTP 工作线程触发） =====

    public event Action? FrontConnected;
    public event Action<int>? FrontDisconnected;
    public event Action<bool, string>? RspAuthenticate;
    public event Action<bool, string>? RspUserLogin;
    public event Action<bool, string>? RspSettlementInfoConfirm;
    public event Action<bool, string, int>? RspOrderInsert;
    public event Action<bool, string, int>? RspOrderAction;
    public event Action<int, string, int>? RspError;
    public event Action<IntPtr>? RtnOrder;
    public event Action<IntPtr>? RtnTrade;

    // ===== vtable 构造：填充全部 82 槽 =====

    private void BuildVtable()
    {
        int slots = ThostTraderApiNative.SpiVtable.SlotCount;
        _vtable = Marshal.AllocHGlobal(slots * IntPtr.Size);
        _spiObject = Marshal.AllocHGlobal(IntPtr.Size);

        // 真实处理槽
        WriteSlot(ThostTraderApiNative.SpiVtable.OnFrontConnected, _onFrontConnected);
        WriteSlot(ThostTraderApiNative.SpiVtable.OnFrontDisconnected, _onFrontDisconnected);
        WriteSlot(ThostTraderApiNative.SpiVtable.OnRspAuthenticate, _onRspAuthenticate);
        WriteSlot(ThostTraderApiNative.SpiVtable.OnRspUserLogin, _onRspUserLogin);
        WriteSlot(ThostTraderApiNative.SpiVtable.OnRspOrderInsert, _onRspOrderInsert);
        WriteSlot(ThostTraderApiNative.SpiVtable.OnRspOrderAction, _onRspOrderAction);
        WriteSlot(ThostTraderApiNative.SpiVtable.OnRspSettlementInfoConfirm, _onRspSettlementInfoConfirm);
        WriteSlot(ThostTraderApiNative.SpiVtable.OnRspError, _onRspError);
        WriteSlot(ThostTraderApiNative.SpiVtable.OnRtnOrder, _onRtnOrder);
        WriteSlot(ThostTraderApiNative.SpiVtable.OnRtnTrade, _onRtnTrade);

        // Noop 槽：5参Rsp模式 [2] OnHeartBeatWarning（2参int，用 NoopInt）
        WriteSlot(ThostTraderApiNative.SpiVtable.OnHeartBeatWarning, NoopInt);

        // Noop 槽：5参Rsp模式 [5-10] [12-13] [15] [17-72]
        for (int i = 5; i <= 10; i++) WriteSlot(i, NoopRsp);
        for (int i = 12; i <= 13; i++) WriteSlot(i, NoopRsp);
        WriteSlot(15, NoopRsp);
        for (int i = 17; i <= 72; i++) WriteSlot(i, NoopRsp);

        // Noop 槽：3参ErrRtn模式 [76-77]
        WriteSlot(ThostTraderApiNative.SpiVtable.OnErrRtnOrderInsert, NoopErrRtn);
        WriteSlot(ThostTraderApiNative.SpiVtable.OnErrRtnOrderAction, NoopErrRtn);

        // Noop 槽：2参Rtn模式 [78-81]
        for (int i = 78; i <= 81; i++) WriteSlot(i, NoopRtn);

        // 伪 SPI 对象首槽 = vtable 指针
        Marshal.WriteIntPtr(_spiObject, _vtable);
    }

    private void WriteSlot(int idx, Delegate d)
    {
        IntPtr fnPtr = Marshal.GetFunctionPointerForDelegate(d);
        Marshal.WriteIntPtr(_vtable, idx * IntPtr.Size, fnPtr);
    }

    // ===== 真实回调实现（转发到事件） =====

    private void HandleFrontConnected(IntPtr _) => FrontConnected?.Invoke();

    private void HandleFrontDisconnected(IntPtr _, int nReason) => FrontDisconnected?.Invoke(nReason);

    private void HandleRspAuthenticate(IntPtr _, IntPtr pField, IntPtr pRspInfo, int nRequestID, [MarshalAs(UnmanagedType.U1)] bool bIsLast)
    {
        var (success, error) = ParseRspInfo(pRspInfo);
        RspAuthenticate?.Invoke(success, error);
    }

    private void HandleRspUserLogin(IntPtr _, IntPtr pRspUserLogin, IntPtr pRspInfo, int nRequestID, [MarshalAs(UnmanagedType.U1)] bool bIsLast)
    {
        var (success, error) = ParseRspInfo(pRspInfo);
        RspUserLogin?.Invoke(success, error);
    }

    private void HandleRspSettlementInfoConfirm(IntPtr _, IntPtr pField, IntPtr pRspInfo, int nRequestID, [MarshalAs(UnmanagedType.U1)] bool bIsLast)
    {
        var (success, error) = ParseRspInfo(pRspInfo);
        RspSettlementInfoConfirm?.Invoke(success, error);
    }

    private void HandleRspOrderInsert(IntPtr _, IntPtr pInputOrder, IntPtr pRspInfo, int nRequestID, [MarshalAs(UnmanagedType.U1)] bool bIsLast)
    {
        var (success, error) = ParseRspInfo(pRspInfo);
        RspOrderInsert?.Invoke(success, error, nRequestID);
    }

    private void HandleRspOrderAction(IntPtr _, IntPtr pInputOrderAction, IntPtr pRspInfo, int nRequestID, [MarshalAs(UnmanagedType.U1)] bool bIsLast)
    {
        var (success, error) = ParseRspInfo(pRspInfo);
        RspOrderAction?.Invoke(success, error, nRequestID);
    }

    private void HandleRspError(IntPtr _, IntPtr pRspInfo, int nRequestID, [MarshalAs(UnmanagedType.U1)] bool bIsLast)
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

    private void HandleRtnOrder(IntPtr _, IntPtr pOrder)
    {
        if (pOrder == IntPtr.Zero) return;
        RtnOrder?.Invoke(pOrder);
    }

    private void HandleRtnTrade(IntPtr _, IntPtr pTrade)
    {
        if (pTrade == IntPtr.Zero) return;
        RtnTrade?.Invoke(pTrade);
    }

    // ===== 辅助：解析 RspInfo 为 (success, error) =====

    private static (bool Success, string Error) ParseRspInfo(IntPtr pRspInfo)
    {
        if (pRspInfo == IntPtr.Zero) return (true, string.Empty);
        var info = Marshal.PtrToStructure<CThostFtdcRspInfoField>(pRspInfo);
        if (info.ErrorID == 0) return (true, string.Empty);
        return (false, $"CTP ErrorID={info.ErrorID} Msg={info.ErrorMsg}");
    }

    // ===== Noop 处理器（空实现，仅占 vtable 槽防止 AV） =====

    private static void NoopIntHandler(IntPtr _, int n) { }
    private static void NoopRspHandler(IntPtr _, IntPtr p1, IntPtr p2, int n, [MarshalAs(UnmanagedType.U1)] bool b) { }
    private static void NoopRtnHandler(IntPtr _, IntPtr p1) { }
    private static void NoopErrRtnHandler(IntPtr _, IntPtr p1, IntPtr p2) { }

    // ===== 委托类型（__thiscall：this 在 ECX，其余压栈；bool 用 U1 = 1 字节） =====

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void FrontConnectedDelegate(IntPtr thisPtr);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void FrontDisconnectedDelegate(IntPtr thisPtr, int nReason);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void RspDelegate(
        IntPtr thisPtr, IntPtr pField, IntPtr pRspInfo, int nRequestID,
        [MarshalAs(UnmanagedType.U1)] bool bIsLast);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void RspErrorDelegate(
        IntPtr thisPtr, IntPtr pRspInfo, int nRequestID,
        [MarshalAs(UnmanagedType.U1)] bool bIsLast);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void RtnDelegate(IntPtr thisPtr, IntPtr pField);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void ErrRtnDelegate(IntPtr thisPtr, IntPtr pField, IntPtr pRspInfo);

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
