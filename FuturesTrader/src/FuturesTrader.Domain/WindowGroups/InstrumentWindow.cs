namespace FuturesTrader.Domain.WindowGroups;

/// <summary>
/// 合约窗口：镜像旧软件 Users.xml 的 &lt;Instrument&gt; 元素全部属性。
/// InstrumentCode = 元素文本（如 ag2608），GroupId = Group 属性。
/// 默认值与 Users.xml 实证一致（Width=271, Height=1000, ValLeft=1, ValRight=2,
/// RowHeight=12, RboB=true, CntrbySprdFctn=1, CbBgds=true, CbZdtLock=true）。
/// 属性名采用 C# 惯例，XML 属性名映射在 UsersXmlWindowGroupRepository 中完成。
/// </summary>
public sealed record InstrumentWindow
{
    /// <summary>合约代码（Users.xml Instrument 元素文本，如 ag2608）。</summary>
    public string InstrumentCode { get; init; } = string.Empty;

    /// <summary>所属分组号（Users.xml Group 属性），0 表示未分组。</summary>
    public int GroupId { get; init; }

    public int Top { get; init; }
    public int Left { get; init; }
    public int Height { get; init; } = 1000;
    public int Width { get; init; } = 271;
    public int ValLeft { get; init; } = 1;
    public int ValRight { get; init; } = 2;
    public int RowHeight { get; init; } = 12;

    /// <summary>卖一边界向上的空方显示行数；运行时由共享 Window 配置覆盖此布局快照值。</summary>
    public int AskQuoteRowCount { get; init; } = 30;

    /// <summary>买一边界向下的多方显示行数；运行时由共享 Window 配置覆盖此布局快照值。</summary>
    public int BidQuoteRowCount { get; init; } = 30;

    /// <summary>卖一价靠左/买一价靠右开关（RBOA 属性）。</summary>
    public bool RboA { get; init; }

    /// <summary>RBOB 属性，默认 true（与 Users.xml ag/jd 族一致）。</summary>
    public bool RboB { get; init; } = true;

    public bool CbNearby { get; init; }
    public bool CbOnlyOpen { get; init; }

    /// <summary>
    /// 旧控件 CBOC。B 平仓时，非零 RunMode 且该值为 false 会先请求撤销同方向开仓挂单；
    /// 旧 XML 只在 RunMode=1/2 写出该字段。旧 DFM 未设置 Checked，默认 false。
    /// </summary>
    public bool CbOc { get; init; }

    /// <summary>GroupEX 属性（旧软件保留字段，当前固定 0）。</summary>
    public int GroupEx { get; init; }

    public string CntrbySprdId { get; init; } = string.Empty;
    public int CntrbySprdPt { get; init; }
    public string CntrbySprdIdEx { get; init; } = string.Empty;
    public int CntrbySprdPtEx { get; init; }
    public int CntrbySprdFctn { get; init; } = 1;

    public bool NarrowMode { get; init; }
    public bool CbCntrbySprd { get; init; }
    public bool CbCntrbySprdEx { get; init; }
    public bool CbCdLock { get; init; }

    /// <summary>CBBGDS 属性，默认 true。</summary>
    public bool CbBgds { get; init; } = true;

    /// <summary>CBZDTlock 属性，默认 true（涨跌停锁定）。</summary>
    public bool CbZdtLock { get; init; } = true;
}
