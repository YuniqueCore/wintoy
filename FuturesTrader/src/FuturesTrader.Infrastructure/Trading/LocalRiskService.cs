using FuturesTrader.Application.Abstractions;
using FuturesTrader.Domain.Configuration;
using FuturesTrader.Domain.Trading;
using Microsoft.Extensions.Logging;

namespace FuturesTrader.Infrastructure.Trading;

/// <summary>
/// <see cref="ILocalRiskService"/> 的本地风控实现：在报单/撤单提交 CTP 前做本地校验。
/// 对齐 0527.exe config.ini [Order] 段参数：<see cref="OrderConfig.RiskOpen"/> 总开关，
/// <see cref="OrderConfig.MaxInputCount"/> 报单数限制，<see cref="OrderConfig.MaxPositionCount"/> 持仓数限制，
/// <see cref="OrderConfig.Spck"/>/<see cref="OrderConfig.Gzck"/> 撤单开关，
/// <see cref="OrderConfig.MaxCancelGz"/>/<see cref="OrderConfig.MaxCancelSp"/>/<see cref="OrderConfig.MaxCancelQq"/> 撤单数限制。
/// <para>
/// <b>品种分类</b>（决定撤单限额）：
/// <list type="bullet">
///   <item><b>股指(GZ)</b>：IF/IH/IC/IM 开头的合约（沪深300/上证50/中证500/中证1000股指期货）</item>
///   <item><b>期权(QQ)</b>：合约代码含 "-C-" / "-P-"（看涨/看跌期权）或以 C/P 结尾的期权合约</item>
///   <item><b>商品(SP)</b>：其余所有合约</item>
/// </list>
/// </para>
/// </summary>
public sealed class LocalRiskService : ILocalRiskService
{
    private readonly OrderConfig _config;
    private readonly ILogger<LocalRiskService> _logger;
    private int _gzCancelCount;
    private int _spCancelCount;
    private int _qqCancelCount;

    public LocalRiskService(OrderConfig config, ILogger<LocalRiskService>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<LocalRiskService>.Instance;
    }

    /// <inheritdoc />
    public (bool Allowed, string? Reason) CheckOrder(OrderRequest request, int currentOrderCount, int currentPositionCount)
    {
        if (!_config.RiskOpen)
            return (true, null);

        // 报单数限制
        if (_config.MaxInputCount > 0 && currentOrderCount >= _config.MaxInputCount)
        {
            var reason = $"本地风控：报单数已达上限 {_config.MaxInputCount}";
            _logger.LogWarning("风控拒绝报单：{Reason}（当前 {Count}）", reason, currentOrderCount);
            return (false, reason);
        }

        // 持仓数限制（仅开仓时检查，平仓不增加持仓）
        if (_config.MaxPositionCount > 0 && request.OffsetFlag == OffsetFlag.Open &&
            currentPositionCount >= _config.MaxPositionCount)
        {
            var reason = $"本地风控：持仓数已达上限 {_config.MaxPositionCount}";
            _logger.LogWarning("风控拒绝报单：{Reason}（当前 {Count}）", reason, currentPositionCount);
            return (false, reason);
        }

        // 数量必须 > 0
        if (request.Volume <= 0)
            return (false, "本地风控：报单数量必须 > 0");

        return (true, null);
    }

    /// <inheritdoc />
    public (bool Allowed, string? Reason) CheckCancel(string instrumentId, RiskCancelCounters cancelCounts)
    {
        if (!_config.RiskOpen)
            return (true, null);

        var category = ClassifyInstrument(instrumentId);
        var (enabled, maxCount, currentCount) = category switch
        {
            InstrumentCategory.Gz => (_config.Gzck, _config.MaxCancelGz, cancelCounts.GzCount),
            InstrumentCategory.Qq => (true, _config.MaxCancelQq, cancelCounts.QqCount), // 期权无单独开关，始终限撤
            _ => (_config.Spck, _config.MaxCancelSp, cancelCounts.SpCount)
        };

        // 撤单开关关闭 → 放行（不限制撤单）
        if (!enabled)
            return (true, null);

        // 撤单数限制（0 = 不限制）
        if (maxCount > 0 && currentCount >= maxCount)
        {
            var reason = $"本地风控：{CategoryToCode(category)} 撤单数已达上限 {maxCount}（当前 {currentCount}）";
            _logger.LogWarning("风控拒绝撤单：{Reason}", reason);
            return (false, reason);
        }

        return (true, null);
    }

    /// <inheritdoc />
    public void RecordCancel(string instrumentId)
    {
        var category = ClassifyInstrument(instrumentId);
        switch (category)
        {
            case InstrumentCategory.Gz:
                Interlocked.Increment(ref _gzCancelCount);
                break;
            case InstrumentCategory.Qq:
                Interlocked.Increment(ref _qqCancelCount);
                break;
            default:
                Interlocked.Increment(ref _spCancelCount);
                break;
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        Interlocked.Exchange(ref _gzCancelCount, 0);
        Interlocked.Exchange(ref _spCancelCount, 0);
        Interlocked.Exchange(ref _qqCancelCount, 0);
        _logger.LogInformation("本地风控计数器已重置");
    }

    /// <summary>当前撤单计数快照。</summary>
    public RiskCancelCounters CurrentCounters => new()
    {
        GzCount = _gzCancelCount,
        SpCount = _spCancelCount,
        QqCount = _qqCancelCount
    };

    /// <summary>
    /// 按合约代码分类品种类型。
    /// 股指：IF/IH/IC/IM 开头；期权：含 "-C-"/"-P-" 或以 C/P+数字结尾；其余为商品。
    /// </summary>
    private static InstrumentCategory ClassifyInstrument(string instrumentId)
    {
        if (string.IsNullOrEmpty(instrumentId)) return InstrumentCategory.Sp;

        // 股指期货：IF/IH/IC/IM
        if (instrumentId.Length >= 2)
        {
            var prefix = instrumentId[..2];
            if (prefix is "IF" or "IH" or "IC" or "IM")
                return InstrumentCategory.Gz;
        }

        // 期权：含 -C- / -P-（沪深交易所期权格式如 IO2609-C-4000）
        if (instrumentId.Contains("-C-", StringComparison.Ordinal) ||
            instrumentId.Contains("-P-", StringComparison.Ordinal))
            return InstrumentCategory.Qq;

        return InstrumentCategory.Sp;
    }

    private enum InstrumentCategory { Sp, Gz, Qq }

    /// <summary>品种类型 → 业务约定缩写（与 OrderConfig 注释 SP/GZ/QQ 一致）。</summary>
    private static string CategoryToCode(InstrumentCategory c) => c switch
    {
        InstrumentCategory.Gz => "GZ",
        InstrumentCategory.Qq => "QQ",
        _ => "SP"
    };
}
