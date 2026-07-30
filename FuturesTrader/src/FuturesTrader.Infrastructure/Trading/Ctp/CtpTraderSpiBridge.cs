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
/// vtable (164 * 4 = 656 字节): [0..163] = 函数指针
/// </code>
/// </para>
/// <para>
/// <b>vtable 完整布局</b>（6.7.11 共 164 槽，见 <see cref="ThostTraderApiNative.SpiVtable"/>）：
/// 6.7.11 在槽位 36 新增 OnRspQryUserSession，导致 [36-163] 相比 6.7.7 全部 +1。
/// <code>
/// [0]  OnFrontConnected          [1]  OnFrontDisconnected       [2]  OnHeartBeatWarning
/// [3]  OnRspAuthenticate         [4]  OnRspUserLogin
/// [5-10] OnRspUserLogout..OnRspGenUserText    [11] OnRspOrderInsert
/// [12-13] OnRspParkedOrder*      [14] OnRspOrderAction           [15] OnRspQryMaxOrderVolume
/// [16] OnRspSettlementInfoConfirm
/// [17-29] OnRspRemoveParkedOrder..OnRspQryTrade
/// [30] OnRspQryInvestorPosition  [31] OnRspQryTradingAccount
/// [32-35] OnRspQryInvestor..OnRspQryInstrumentCommissionRate
/// [36] OnRspQryUserSession（6.7.11 新增，Noop 占位）
/// [37-38] OnRspQryExchange..OnRspQryProduct
/// [39] OnRspQryInstrument
/// [40-72] OnRspQryDepthMarketData..OnRspQryTransferSerial
/// [73] OnRspQryAccountregister   [74] OnRspError                 [75] OnRtnOrder
/// [76] OnRtnTrade                [77] OnErrRtnOrderInsert        [78] OnErrRtnOrderAction
/// [79-82] OnRtnInstrumentStatus..OnRtnErrorConditionalOrder
/// [83-163] 6.7.x 扩展槽（SPBM/RCAMS/RULE/OffsetSetting 等，Noop 占位）
/// </code>
/// </para>
/// <para>
/// <b>签名分类</b>（决定委托类型，x86 __thiscall 栈布局必须精确匹配）：
/// <list type="bullet">
///   <item>5 参 Rsp(this,IntPtr,IntPtr,int,bool)：[3-73] 大多数 OnRsp*/OnRspQry*（含查询回调）</item>
///   <item>4 参 RspError(this,IntPtr,int,bool)：[74] OnRspError</item>
///   <item>2 参 Rtn(this,IntPtr)：[75-76][79-82] OnRtn*</item>
///   <item>3 参 ErrRtn(this,IntPtr,IntPtr)：[77-78] OnErrRtn*</item>
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
    private readonly RspDelegate _onRspQryInvestorPosition;
    private readonly RspDelegate _onRspQryTradingAccount;
    private readonly RspDelegate _onRspQryInstrument;
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
        _onRspQryInvestorPosition = new RspDelegate(HandleRspQryInvestorPosition);
        _onRspQryTradingAccount = new RspDelegate(HandleRspQryTradingAccount);
        _onRspQryInstrument = new RspDelegate(HandleRspQryInstrument);
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
    /// <summary>持仓查询回调：(pField, bIsLast, nRequestID)。pField 指向 CThostFtdcInvestorPositionField，bIsLast=true 为批次末尾。</summary>
    public event Action<IntPtr, bool, int>? RspQryInvestorPosition;
    /// <summary>资金账户查询回调：(pField, bIsLast, nRequestID)。pField 指向 CThostFtdcTradingAccountField。</summary>
    public event Action<IntPtr, bool, int>? RspQryTradingAccount;
    /// <summary>合约元数据查询回调：(pField, bIsLast, nRequestID)。pField 指向 CThostFtdcInstrumentField。</summary>
    public event Action<IntPtr, bool, int>? RspQryInstrument;
    public event Action<bool, string, int>? RspOrderInsert;
    public event Action<bool, string, int>? RspOrderAction;
    public event Action<int, string, int>? RspError;
    public event Action<IntPtr>? RtnOrder;
    public event Action<IntPtr>? RtnTrade;

    // ===== vtable 构造：填充全部 164 槽（6.7.11） =====

    private void BuildVtable()
    {
        int slots = ThostTraderApiNative.SpiVtable.SlotCount;
        _vtable = Marshal.AllocHGlobal(slots * IntPtr.Size);
        _spiObject = Marshal.AllocHGlobal(IntPtr.Size);

        // 真实处理槽（vtable 索引见 ThostTraderApiNative.SpiVtable）
        WriteSlot(ThostTraderApiNative.SpiVtable.OnFrontConnected, _onFrontConnected);
        WriteSlot(ThostTraderApiNative.SpiVtable.OnFrontDisconnected, _onFrontDisconnected);
        WriteSlot(ThostTraderApiNative.SpiVtable.OnRspAuthenticate, _onRspAuthenticate);
        WriteSlot(ThostTraderApiNative.SpiVtable.OnRspUserLogin, _onRspUserLogin);
        WriteSlot(ThostTraderApiNative.SpiVtable.OnRspOrderInsert, _onRspOrderInsert);
        WriteSlot(ThostTraderApiNative.SpiVtable.OnRspOrderAction, _onRspOrderAction);
        WriteSlot(ThostTraderApiNative.SpiVtable.OnRspSettlementInfoConfirm, _onRspSettlementInfoConfirm);
        WriteSlot(ThostTraderApiNative.SpiVtable.OnRspQryInvestorPosition, _onRspQryInvestorPosition);
        WriteSlot(ThostTraderApiNative.SpiVtable.OnRspQryTradingAccount, _onRspQryTradingAccount);
        WriteSlot(ThostTraderApiNative.SpiVtable.OnRspQryInstrument, _onRspQryInstrument);
        WriteSlot(ThostTraderApiNative.SpiVtable.OnRspError, _onRspError);
        WriteSlot(ThostTraderApiNative.SpiVtable.OnRtnOrder, _onRtnOrder);
        WriteSlot(ThostTraderApiNative.SpiVtable.OnRtnTrade, _onRtnTrade);

        // Noop 槽：2参int [2] OnHeartBeatWarning
        WriteSlot(ThostTraderApiNative.SpiVtable.OnHeartBeatWarning, NoopInt);

        // Noop 槽：5参Rsp模式（避开真实槽 30/31/39）
        for (int i = 5; i <= 10; i++) WriteSlot(i, NoopRsp);
        for (int i = 12; i <= 13; i++) WriteSlot(i, NoopRsp);
        WriteSlot(15, NoopRsp);
        for (int i = 17; i <= 29; i++) WriteSlot(i, NoopRsp);
        for (int i = 32; i <= 38; i++) WriteSlot(i, NoopRsp);  // 含 [36] OnRspQryUserSession（6.7.11 新增，Noop 占位）
        for (int i = 40; i <= 73; i++) WriteSlot(i, NoopRsp);  // 含 [73] OnRspQryAccountregister

        // Noop 槽：3参ErrRtn模式 [77-78]
        WriteSlot(ThostTraderApiNative.SpiVtable.OnErrRtnOrderInsert, NoopErrRtn);
        WriteSlot(ThostTraderApiNative.SpiVtable.OnErrRtnOrderAction, NoopErrRtn);

        // Noop 槽：2参Rtn模式 [79-82]
        for (int i = 79; i <= 82; i++) WriteSlot(i, NoopRtn);

        // Noop 槽：[83-163] 6.7.x 扩展槽（SPBM/RCAMS/RULE/OffsetSetting 等）
        // 签名混合（Rsp/Rtn/ErrRtn），按 vnpy_ctp define 分类填充，避免签名不匹配导致栈失衡
        for (int i = 83; i <= 163; i++) WriteSlot(i, NoopRsp);  // 先全填 5参 Rsp
        // 覆盖 Rtn 槽位（2参）
        foreach (var i in new[] { 83, 87, 90, 91, 93, 96, 105, 106, 107, 108, 109, 110, 111, 112, 113, 119, 120, 124, 125, 126, 160 })
            WriteSlot(i, NoopRtn);
        // 覆盖 ErrRtn 槽位（3参）
        foreach (var i in new[] { 84, 85, 86, 88, 89, 92, 94, 95, 97, 114, 115, 116, 117, 118, 161, 162 })
            WriteSlot(i, NoopErrRtn);

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

    private void HandleRspQryInvestorPosition(IntPtr _, IntPtr pField, IntPtr pRspInfo, int nRequestID, [MarshalAs(UnmanagedType.U1)] bool bIsLast)
    {
        var (success, error) = ParseRspInfo(pRspInfo);
        if (!success)
        {
            // 查询错误：仍触发事件（pField 可能为 Zero），上层按需处理
            RspQryInvestorPosition?.Invoke(IntPtr.Zero, bIsLast, nRequestID);
            return;
        }
        RspQryInvestorPosition?.Invoke(pField, bIsLast, nRequestID);
    }

    private void HandleRspQryTradingAccount(IntPtr _, IntPtr pField, IntPtr pRspInfo, int nRequestID, [MarshalAs(UnmanagedType.U1)] bool bIsLast)
    {
        var (success, error) = ParseRspInfo(pRspInfo);
        if (!success)
        {
            RspQryTradingAccount?.Invoke(IntPtr.Zero, bIsLast, nRequestID);
            return;
        }
        RspQryTradingAccount?.Invoke(pField, bIsLast, nRequestID);
    }

    private void HandleRspQryInstrument(IntPtr _, IntPtr pField, IntPtr pRspInfo, int nRequestID, [MarshalAs(UnmanagedType.U1)] bool bIsLast)
    {
        var (success, error) = ParseRspInfo(pRspInfo);
        if (!success)
        {
            RspQryInstrument?.Invoke(IntPtr.Zero, bIsLast, nRequestID);
            return;
        }
        RspQryInstrument?.Invoke(pField, bIsLast, nRequestID);
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
