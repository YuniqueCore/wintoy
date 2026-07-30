namespace FuturesTrader.Domain.Trading;

/// <summary>
/// 报单录入请求值对象：UI 下单表单 → <see cref="ITradingService.SendOrderAsync"/>。
/// 对齐 CTP <c>CThostFtdcInputOrderField</c> 的业务子集（去掉 hedge/condition/expire 等高级字段）。
/// 不可变 record，由 <see cref="OrderViewModel"/> 构造后传入服务层。
/// </summary>
public sealed record OrderRequest
{
    /// <summary>合约代码（如 ag2608）。</summary>
    public required string InstrumentId { get; init; }

    /// <summary>买卖方向。</summary>
    public required Direction Direction { get; init; }

    /// <summary>开平标志。</summary>
    public required OffsetFlag OffsetFlag { get; init; }

    /// <summary>申报价格（限价单）。0 表示市价（CTP 传 <c>THOST_LIMITORDER</c> + 涨跌停价由调用方算）。</summary>
    public required decimal Price { get; init; }

    /// <summary>申报数量（手数，必须 &gt; 0）。</summary>
    public required int Volume { get; init; }

    /// <summary>
    /// 报单引用（可选，留空则由服务层自动生成递增序号）。
    /// CTP 用 OrderRef 标识同一会话内的报单，格式为数字字符串。
    /// </summary>
    public string? OrderRef { get; init; }

    /// <summary>最小变动价位（用于价格校验，由调用方从合约元数据填入）。</summary>
    public decimal PriceTick { get; init; }
}
