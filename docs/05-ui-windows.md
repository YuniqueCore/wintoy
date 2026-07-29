# 05 - UI 窗体与功能映射

> 本文整理 0527.exe 的全部 Delphi VCL 窗体、控件、事件处理器，并给出 WPF 重构映射。
> 证据来源：DFM 资源解析（`dfm-forms-0527.txt` / `dfm-controls-summary.txt`）+ Delphi RTTI + SysLog + Users.xml 字段。

## 1. 结论先行

- 共识别 **11 个 Delphi 窗体类**，覆盖**主面板 / 合约管理 / 监控 / 下单控制 / 持仓 / 配置 / 用户 / 提示音 / 平仓记录 / 点位 / 价差记录** 11 类业务场景。
- 全部窗体共享一套**价差居中（CntrbySprd）**控件族，是该软件的核心视觉语言——以最新价为中心、上下对称展示买卖盘。
- TForm1 是空容器，真正承载业务的是 **TConfigINI**（持有 TFDConnection / TIdHTTP / TXMLDocument / TTrayIcon / TFontDialog 等核心组件）。
- 持仓窗口是**独立进程** `PositionWin.exe`，通过命名管道通信，不在 11 个 VCL 窗体内。

## 2. 窗体总览

| # | Delphi 类 | 实例名 | 业务定位 | 复杂度 |
|---|---|---|---|---|
| 1 | `TForm1` | Form1 | 主窗体（空容器，托盘入口） | ★ |
| 2 | `TConfigINI` | - | **配置主面板**（持有数据库/HTTP/XML/托盘等核心组件） | ★★★★★ |
| 3 | `TUserEdit` | - | 用户账号编辑（brokerid/userid/appid/shouquan） | ★★★ |
| 4 | `TInstrumentsWin` | InstrumentsWin | 合约选择/筛选 | ★★★★ |
| 5 | `TMonitoredWin` | MonitoredWin | 监控列表（自选合约 + 产品） | ★★★ |
| 6 | `TYYCtrlWin` | YYCtrlWin | 下单控制面板（手数/价差/阈值） | ★★★ |
| 7 | `TYYWin` | YYWin | **交易主面板**（行情深度 + 下单 + 持仓） | ★★★★★ |
| 8 | `TPointWindow` | PointWindow | 点位管理 | ★★★ |
| 9 | `TSoundWin` | SoundWin | 提示音配置 | ★★ |
| 10 | `TCloseRecordWin` | CloseRecordWin | 平仓记录 | ★ |
| 11 | `TJCJLWin` | JCJLWin | 价差/基础记录窗口 | ★★★ |
| - | `PositionWin` (独立进程) | - | 持仓窗口（命名管道通信） | ★★★★ |

> **窗体继承链推测**：TUserEdit / TSoundWin / TPointWindow / TJCJLWin / TMonitoredWin / TInstrumentsWin 共享几乎相同的 TXCntrbySprd* 控件族与事件处理器，疑均继承自同一基类（如 `TCntrbySprdForm`），是 Delphi VCL 视觉继承的典型用法。

## 3. 核心组件清单（TConfigINI 持有）

从 `dfm-controls-summary.txt` 中 TConfigINI 段确认的**非可视组件**：

| 组件类 | 实例/字段 | 用途 |
|---|---|---|
| `TFDConnection` | - | FireDAC SQLite 数据库连接（market_data 表） |
| `TFDPhysSQLiteDriverLink` | - | FireDAC SQLite 驱动链接 |
| `TIdHTTP` | - | Indy HTTP 客户端（调用 ASPX 接口） |
| `TIdTCPClient` | - | Indy TCP 客户端（长连接推送？） |
| `TXMLDocument` | - | XML 解析（HQAddress/Users/Instruments） |
| `TTrayIcon` / `TrayIcon1` | - | 系统托盘图标，`TrayIcon1Click` 事件 |
| `TFontDialog` | - | 字体选择对话框（配置 `MainFont`） |
| `TPopupMenu` / `TMenuItem` | - | 右键菜单 |
| `TTimer` × 3 | - | 定时器（行情刷新/抢单触发/远程命令轮询） |
| `TMemo` | - | 多行文本（日志显示？） |

> 这些组件**只实例化一次**（在 TConfigINI 中），其他窗体通过引用使用。重构时改为单例服务（`IServiceProvider` 注入）。

## 4. 共享控件族：CntrbySprd（价差居中）

以下控件在 6+ 个窗体中重复出现，是该软件的核心 UI 语言：

| 控件类/字段 | 推测语义 | Users.xml 对应字段 |
|---|---|---|
| `TXCntrbySprdID` | 价差筛选品种 ID（如 "ag"） | `CntrbySprdID` |
| `TXCntrbySprdPT` | 价差筛选价位 | `CntrbySprdPT` |
| `TXCntrbySprdFctn` | 价差筛选系数 | `CntrbySprdFctn` |
| `TXCntrbySprdIDEX` | 扩展价差品种 ID | `CntrbySprdIDEX` |
| `TXCntrbySprdPTEX` | 扩展价差价位 | `CntrbySprdPTEX` |
| `CBCntrbySprd` (TCheckBox) | 启用主价差筛选 | `CBCntrbySprd` |
| `CBCntrbySprdEX` (TCheckBox) | 启用扩展价差筛选 | `CBCntrbySprdEX` |

**事件处理器**（每个控件都注册了 4 类）：
- `MouseDown` — 鼠标按下（开始拖拽价位）
- `KeyPress` — 键盘输入价位
- `Change` — 值变更
- `ContextPopup` — 右键菜单

> 这套控件族是软件"按价差居中显示买卖盘"功能的基础，WPF 重构时必须保留交互模式。

## 5. 各窗体详解

### 5.1 TForm1 — 主窗体（空容器）

- **控件数**：~28 个（极少，主要持有子窗体引用）
- **核心子窗体引用**：TConfigINI / TInstrumentsWin / TMonitoredWin / TYYCtrlWin / TUserEdit / TPointWindow / TSoundWin / TJCJLWin
- **托盘交互**：`TrayIcon1Click`、`TPopupMenu`、`TMenuItem`
- **HTTP/DB 组件**：TIdHTTP、TIdTCPClient、TFDConnection、TFDPhysSQLiteDriverLink、TXMLDocument
- **定位**：Delphi 工程的默认主窗体，启动后隐藏到托盘，作为子窗体容器与全局组件持有者。
- **登录字段**：`TXuser`、`TXPass`（密码，含 `TXPassKeyPress` 事件）、`TXappid`、`TXname`、`Txdizhi`（地址）、`Txgongsi`（公司）、`TXshouquan`（授权码）

> ⚠️ 字符串表里 TForm1 与 TConfigINI 的组件清单几乎相同，TForm1 与 TConfigINI 实际可能是**同一窗体的两个名字**（开发期重构改名残留）。WPF 重构时合并为单一 `MainWindow`。

### 5.2 TConfigINI — 配置主面板

- **控件数**：~100 个（最复杂窗体）
- **核心组件**：见 §3
- **业务字段**：`TxProductID`（品种 ID）、`TxThreshold`（阈值）、`TxPeriod`（周期）、`TxHyChaXun`（合约查询，含 Change/Click/KeyPress 事件）
- **菜单**：`TPopupMenu` + `TMenuItem`（右键配置项）
- **定位**：软件的控制中心，承载全部配置与连接管理。SysLog 显示的"默认行情服务 / 开盘抢单频率 / 字体 / 列宽"等参数均在此窗体编辑。

### 5.3 TUserEdit — 用户账号编辑

- **控件数**：~95 个
- **核心字段**：`TXuser`、`TXPass`、`TXappid`、`TXname`、`Txdizhi`、`Txgongsi`、`TXshouquan`、`TXchuanggao`（创新高?）、`TXhanggao`（?）
- **数据源**：`Users.xml` 的 `<User>` 段
- **事件**：`TXPassKeyPress`（密码输入回车提交）、`TXUpDownKeyPress`（数值微调）
- **定位**：管理多个期货账户（brokerid+userid+appid+shouquan+交易地址）。一个 `<User>` 节点对应一个账户配置。

### 5.4 TInstrumentsWin — 合约选择/筛选

- **控件数**：~153 个（最复杂的业务窗体）
- **核心控件**：
  - `TListBox` × 4 — 合约分类列表
  - `TStringGrid` × 4 — 合约表格
  - `TListView` × 2 — 详情视图
  - `TMemo` × 1 — 多行文本（合约信息？）
  - `TTimer` × 1 — 实时刷新
- **筛选字段**：`TxProductID`、`TxPriceTick`（最小变动价位，含 `KeyDown` 事件）、`TxThreshold`、`TxPeriod`、`TxHyChaXun`（合约查询）
- **价差族**：完整的 7 个 TXCntrbySprd* 控件
- **排序**：`TRadioButton` × 6（多种排序方式：按价差/按成交量/按涨跌幅 等）
- **定位**：从 30493 个合约中筛选订阅目标，是订阅行情前的必经界面。

### 5.5 TMonitoredWin — 监控列表

- **控件数**：~149 个
- **核心控件**（从 `dfm-forms-0527.txt` 实证）：
  - `SGMonitoredInstrument` (TStringGrid) — 监控的合约表格，含 `OnDblClick=SGMonitoredInstrumentDblClick`
  - `SGMonitoredProduct` (TStringGrid) — 监控的产品表格
    - `Width=445, Height=900, ColCount=4, DefaultColWidth=96, DefaultRowHeight=25, FixedCols=0, RowCount=200`
    - `OnMouseDown=SGMonitoredProductMouseDown`
    - `ScrollBars=ssNone`（禁用滚动条，自定义滚动）
- **事件**：`FormClose`、`FormCreate`、`FormResize`
- **定位**：用户自定义的监控列表，类似自选股，可双击进入交易。

### 5.6 TYYCtrlWin — 下单控制面板

- **控件数**：~81 个
- **核心字段**：`TXchuanggao`、`TXhanggao`、`TxPriceTick`、`TxHyChaXun`、`TXUpDown`（数值微调器）
- **事件**：`TXUpDownKeyPress`、`TXUpDownContextPopup`、`TxPriceTickKeyDown`、完整 TXCntrbySprd* 事件
- **定位**：下单前的参数控制——手数、价格步进、价差阈值等。与 TYYWin 配合使用。

### 5.7 TYYWin — 交易主面板（核心）

- **控件数**：~56 个（控件少但业务重）
- **核心控件**：
  - `TStringGrid` × 1 — 价格列表（PriceList）
  - `TPanel` × 1 — 容器
- **业务方法**（来自 02-ctp-api.md RTTI 实证）：
  - `TYYWin.GoSGPriceListLoad(DepthMarketData*)` — 行情推送时刷新价格列表
  - `TYYWin.UpdateDepthVolumesThreadSafe(DepthMarketData&)` — 线程安全更新成交量
  - `TYYWin.Trade(TradeField*)` — 成交回报处理
  - `YYWinList.ShowPendingFlag` — 显示挂单标记
- **关键 RTTI 方法**：`OnRtnOrder` → `TForm1.RtnOrder(OrderField*)`
- **定位**：**软件的核心交易界面**，展示深度行情 + 实时下单 + 持仓状态。一个合约开一个 TYYWin 实例（多窗口）。

### 5.8 TPointWindow — 点位管理

- **控件数**：~137 个
- **核心字段**：`TXchuanggao`、`TXhanggao`、`TxPriceTick`、`TxHyChaXun`、完整 TXCntrbySprd* 族
- **事件**：`TxHyChaXunClick` × 2、`TxHyChaXunChange` × 2
- **定位**：管理价格点位（关键价位标记），辅助交易决策。

### 5.9 TSoundWin — 提示音配置

- **控件数**：~97 个
- **核心字段**：`TXchuanggao`、`TXhanggao`、`TxPriceTick`
- **音效文件**：`Nomoney.wav` / `cashreg.wav` / `chimes.wav` / `Cancellation.wav`
- **定位**：配置不同事件触发的提示音（资金不足/成交/撤单 等）。

### 5.10 TCloseRecordWin — 平仓记录

- **控件数**：仅 7 个（最简单窗体）
- **核心控件**：`TStringGrid` × 3（三张表：今日/历史/统计）
- **数据源**：`PnL/YYYYMMDD.csv` + CTP `QryTrade` 回报
- **定位**：展示平仓记录与每日盈亏，对应 `PnL/` 目录的 CSV 数据。

### 5.11 TJCJLWin — 价差/基础记录

- **控件数**：~151 个
- **核心字段**：完整 TXCntrbySprd* 族 + `TxProductID` + `TxPeriod` + `TxPriceTick` + `TxThreshold`
- **定位**：JCJL 推测为"价差记录"或"基础记录"，记录历史价差数据用于分析。从 `TInstrumentsWin` 引用看，与合约选择联动。

### 5.12 PositionWin — 独立持仓窗口进程

- **位置**：`qihuo-software/PositionWin`（3.22 MB PE32 程序）
- **Delphi 类**：`TForm1` / `Unit1` / `PipeClientReadThread`
- **通信**：命名管道（Named Pipe）与 0527.exe 双向通信
- **更新源**：`http://sunmengze.cc/PositionWin.txt`
- **定位**：独立持仓窗口，可拖到副屏单独显示。架构上解耦，避免主程序卡顿影响持仓查看。

## 6. 窗体间交互流

```
启动 → TForm1 (托盘) → 用户点击托盘
                          │
                          ▼
                    TConfigINI (配置主面板)
                          │
            ┌─────────────┼─────────────┬─────────────┐
            ▼             ▼             ▼             ▼
       TUserEdit    TInstrumentsWin  TSoundWin   TPointWindow
       (账号编辑)    (合约选择)       (音效配置)   (点位管理)
                          │
                          ▼
                    TMonitoredWin (监控列表)
                          │ 双击合约
                          ▼
                       TYYWin ←─→ TYYCtrlWin (下单控制)
                          │           │
                          │           └─→ OrderInsert (CTP)
                          │
                          ▼
                    PositionWin (独立进程, 命名管道)
                          │
                          ▼
                    TCloseRecordWin (平仓记录)
```

## 7. WPF 重构映射

### 7.1 窗体 → WPF View 映射

| Delphi 窗体 | WPF View | Syncfusion 控件建议 | 备注 |
|---|---|---|---|
| TForm1 + TConfigINI | `MainWindow.xaml` | `SfDockingManager` + `TaskbarIcon` | 合并为单一主窗体 |
| TUserEdit | `UserEditView.xaml` | `TextBox` + `PasswordBox` + `ComboBox` | 用 EditCard 模式 |
| TInstrumentsWin | `InstrumentsView.xaml` | `SfDataGrid` + `SfAutoComplete` + `SfTreeView` | 30493 合约需虚拟化 |
| TMonitoredWin | `MonitoredView.xaml` | `SfDataGrid`（双表联动） | 双击进入交易 |
| TYYCtrlWin | `OrderControlPanel.xaml` | `UpDown` + `ComboBox` + `Button` | 嵌入 TYYWin 侧边 |
| TYYWin | `TradingView.xaml` | `SfDataGrid`（深度行情） + 自定义 PriceList | **核心**，多实例 |
| TPointWindow | `PointManageView.xaml` | `SfDataGrid` + `SfChart` | 点位+图表 |
| TSoundWin | `SoundConfigView.xaml` | `ListBox` + `MediaElement` | 音效绑定 |
| TCloseRecordWin | `CloseRecordView.xaml` | `SfDataGrid` × 3 + `SfChart` | 盈亏图表 |
| TJCJLWin | `SpreadRecordView.xaml` | `SfDataGrid` + `SfChart` | 价差记录 |
| PositionWin | `PositionView.xaml`（嵌入主程序） | `SfDataGrid` | 取消独立进程 |

### 7.2 共享价差居中控件族 → WPF UserControl

将 7 个 TXCntrbySprd* 控件封装为统一的 `CtrBySpreadUserControl`：

```csharp
public class CtrBySpreadControl : Control {
    public static readonly DependencyProperty InstrumentIdProperty = ...;
    public static readonly DependencyProperty SpreadPointProperty = ...;
    public static readonly DependencyProperty SpreadFactorProperty = ...;
    public static readonly DependencyProperty IsExtendedProperty = ...;

    public string InstrumentId { get; set; }      // 品种 ID
    public decimal SpreadPoint { get; set; }        // 价差点位
    public decimal SpreadFactor { get; set; }       // 系数
    public bool IsExtended { get; set; }            // 扩展模式
}
```

Users.xml 的窗口布局字段（`CntrbySprdID`/`CntrbySprdPT`/`CntrbySprdFctn`/`CntrbySprdIDEX`/`CntrbySprdPTEX`）直接绑定到此控件。

### 7.3 网格控件映射（TStringGrid → SfDataGrid）

原 Delphi `TStringGrid` 大量使用，特征：
- `ScrollBars=ssNone`（自定义滚动）
- `OnDblClick`、`OnMouseDown`、`OnKeyPress` 事件
- 多列动态宽度（PriceListRatios=10,25,30,25,10）

WPF 替换为 `Syncfusion.SfDataGrid`：
- `AllowEditing="True"` + 自定义 `GridCellTemplate`
- `SelectionUnit="Cell"` + `SelectionMode="Single"`
- 自定义 `ScrollViewer` 模板以匹配原滚轮加速（`MouseWheelSpeed=3`）
- 通过 `GridColumnSizer` 实现 `PriceListRatios` 比例列宽

### 7.4 多窗口管理

TYYWin 一个合约开一个实例，是**多窗口/多标签页**场景。WPF 用：
- `Syncfusion.SfDockingManager`（多标签 + 浮动窗口）
- `Syncfusion.SfTabbedEditor`（标签页管理）
- 持久化窗口布局到 `Users.xml` 的 `<WindowHistory>` 段

### 7.5 系统托盘

- Delphi：`TTrayIcon` + `TPopupMenu` + `TrayIcon1Click`
- WPF：使用 `H.NotifyIcon.Wpf` 或 `Hardcodet.NotifyIcon.Wpf`（开源库）
- 保留右键菜单：显示主窗体 / 切换用户 / 退出

### 7.6 字体与 DPI

- 原 Delphi：`MainFont=新宋体`（GBK 编码 + 等宽中文字体）
- WPF：默认 `PerMonitorV2V2` DPI 感知（原 manifest 已设置）
- 字体建议：保留 `新宋体` 作为默认（用户习惯），同时支持 `Microsoft YaHei UI` / `Consolas`（数字等宽）
- 字号偏移：`FontSizeOffset=0` → WPF 用 `DynamicResource` 绑定全局字号

## 8. 交互习惯保留清单（必须 1:1 复刻）

以下是用户的核心操作习惯，WPF 重构时**不能改变**：

| 习惯 | 原实现 | WPF 实现要求 |
|---|---|---|
| 双击监控列表合约进入交易 | `SGMonitoredInstrumentDblClick` | `SfDataGrid` 的 `MouseDoubleClick` 事件 |
| 鼠标滚轮加速滚动 | `MouseWheelSpeed=3` | 自定义 `ScrollViewer` 的 `PreviewMouseWheel` |
| 价差居中显示买卖盘 | TXCntrbySprd* 控件族 | 自定义 `PriceListControl` |
| 数值微调（UpDown） | `TXUpDown` + 键盘输入 | `Syncfusion.UpDown` + `KeyPress` 处理 |
| 右键菜单（ContextPopup） | `TPopupMenu` | `ContextMenu` + `ContextMenuOpening` |
| 多窗口布局持久化 | Users.xml `<WindowHistory>` | 同样持久化到 `Users.xml` 或 JSON |
| 托盘图标点击显示/隐藏 | `TrayIcon1Click` | `TaskbarIcon.TrayLeftMouseUp` |
| 密码回车提交 | `TXPassKeyPress` | `PasswordBox.KeyDown` (Enter) |
| 实时刷新（定时器） | `TTimer` × 3 | `DispatcherTimer` 或 `Rx.NET` |
| 字体右键修改 | `TFontDialog` | `System.Windows.Forms.FontDialog`（互操作）或自定义 |

## 9. 局限

- **窗体 Caption 未完全解析**：DFM 的 `Caption` 属性因 TValueType 类型代码表初期错误，显示为 `<binary:2>` / `<binary:4>`。窗体实际标题需用 IDR 或 Resource Hacker 提取。
- **控件精确布局未还原**：DFM 解析得到的是控件类名与计数，完整坐标/大小/TabOrder 需进一步解析。
- **菜单项文本未提取**：`TPopupMenu` + `TMenuItem` 的菜单结构需 Resource Hacker 查看。
- **TForm1 vs TConfigINI 关系**：从组件清单高度重合推测为同一窗体，需 IDR 反编译确认。
- **TJCJLWin 业务定位**：JCJL 缩写未在 SysLog 中找到对应中文，推测为"价差记录"或"基础记录"。

## 10. 后续深度逆向建议

若需 100% 还原窗体布局，推荐：

1. **IDR (Interactive Delphi Reconstructor)** — 直接反编译 0527.exe，恢复全部 DFM 资源 + VCL 类层次 + 方法体伪代码
2. **Resource Hacker** — 提取 DFM 资源为文本，查看完整控件树与 Caption
3. **PE Explorer** — 替代 Resource Hacker，对 Delphi 资源友好
4. **DeDe** — 老牌 Delphi 反编译器（可能对老版本 Delphi 更友好）

> dnSpy / dnSpyEx **不适用**——经 01-overview.md §1 确认，本程序是 Delphi 原生代码，非 .NET。

## 11. 相关文档

- [01-overview.md](01-overview.md) — 软件总览
- [02-ctp-api.md](02-ctp-api.md) — CTP 交易/行情接口
- [03-data-formats.md](03-data-formats.md) — 配置与数据文件格式
- [04-http-cloud.md](04-http-cloud.md) — HTTP 云端接口
- [06-refactor-guide.md](06-refactor-guide.md) — WPF 重构建议
