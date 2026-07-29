# 01 - 期货软件总览

> 本文档基于对 `0527.exe`（8.79 MB）及其同目录 DLL、配置文件、运行时日志的逆向分析整理。
> 分析日期：2026-07-29。目标：为 WPF 重构提供完整接口契约参考。

## 1. 结论先行

- **原软件是 Embarcadero Delphi (VCL) 编写的 32 位 Windows GUI 桌面程序**，不是 .NET 程序，`dnSpy/dnSpyEx` 不适用。
- **交易/行情通过自研 C++ 封装层 `YYXX.dll`（CTP v6.7.10）调用上期技术 CTP API**，而非直接调用 `thosttraderapi_se.dll` / `thostmduserapi_se.dll`。
- **持仓窗口是独立 Delphi 程序 `PositionWin`**，通过命名管道（Named Pipe）与主程序通信。
- 软件不仅是 CTP 直连客户端，还有 **13 个云端 HTTP 接口**（`sunmengze.cc` / `yiyikeji.top`）提供配置下发、合约同步、止损、远程命令等服务。

## 2. 技术栈

| 维度 | 内容 | 证据 |
|---|---|---|
| 主程序语言/框架 | Embarcadero Delphi + VCL | 二进制内 `Embarcadero/Borland/CodeGear/Delphi/RAD Studio` 签名 + `TPF0` DFM 标记 + VCL 控件类（TForm/TButton/TEdit/TStringGrid/TDrawGrid/TComboBox） |
| 架构 | PE32 (x86) GUI，8.79 MB | PE 头：Machine=0x014C, Subsystem=GUI(2), OptMagic=0x10B(PE32) |
| DPI 感知 | Per-Monitor V2 | Manifest 内 `dpiAwareness=PerMonitorV2` |
| CTP 封装层 | YYXX.dll（C++/MSVC，VMProtect 加壳）+ yy.dll + yyVolume.dll | PDB 路径 `J:\CtpDLL6.7.10\Release\YY.pdb`；导入 `CreateFtdcTraderApi`/`CreateFtdcMdApi` |
| CTP API 版本 | v6.7.10（上期技术综合交易平台） | `thosttraderapi_se.dll` + `thostmduserapi_se.dll` |
| 本地存储 | SQLite（FireDAC 访问） | `sqlite3.dll` + DFM 内 `TFDConnection`/`TFDPhysSQLiteDriverLink` |
| HTTP 客户端 | libcurl + Indy (TIdHTTP/TIdTCPClient) | `libcurl.dll` + DFM 内 `TIdHTTP`/`TIdTCPClient` |
| 加密 | OpenSSL 1.1 + 老 ssleay | `libcrypto-1_1.dll`/`libeay32.dll`/`ssleay32.dll` |
| 内嵌网页 | WebView2 | `WebView2Loader.dll` |
| 压缩 | zlib | `zlib.dll`/`zlibd1.dll`/`zlibwapi.dll` |
| XML 解析 | TXMLDocument（Delphi 内置） | DFM 内 `TXMLDocument` |
| Delphi 运行时 | Borland MM + RTL | `borlndmm.dll`/`cc32c290mt.dll` |

## 3. 模块清单与依赖关系

```
┌─────────────────────────────────────────────────────────────┐
│                    0527.exe  (Delphi VCL, 主程序)            │
│  TForm1 (主窗体) ─ 持有 TFDConnection/TIdHTTP/TXMLDocument  │
│  ├─ 11 个业务窗体 (TInstrumentsWin/TMonitoredWin/TYYWin...) │
│  └─ Delphi 线程:                                            │
│       TCtpJYThread   (交易线程)                              │
│       TCtpHQThread   (行情线程)                              │
│       TCtpHQExThread (行情扩展线程)                          │
│       TCtpCXThread   (查询线程)                              │
└──────────┬──────────┬──────────┬───────────┬────────────────┘
           │          │          │           │
           ▼          ▼          ▼           ▼
     ┌──────────┐ ┌──────────┐ ┌────────┐ ┌──────────────┐
     │ YYXX.dll │ │ yy.dll   │ │sqlite3 │ │ libeay32     │
     │ (15导出) │ │ (14导出) │ │ .dll   │ │ ssleay32.dll │
     │ CTP封装  │ │ 旧版封装 │ │        │ │ libcrypto    │
     └────┬─────┘ └────┬─────┘ └────────┘ └──────────────┘
          │            │
          ▼            ▼
   ┌──────────────┐ ┌──────────────┐
   │thosttraderapi│ │thostmduserapi│
   │  _se.dll     │ │  _se.dll     │
   │ (CTP 交易)   │ │ (CTP 行情)   │
   └──────────────┘ └──────────────┘

  yyVolume.dll (3导出, 行情专用封装) → thostmduserapi_se.dll

  ┌─────────────────────────────────────────────┐
  │  PositionWin (独立 Delphi 程序, 3.38 MB)     │
  │  持仓窗口专用，通过命名管道与 0527.exe 通信   │
  │  含 PipeClientReadThread 管道读取线程         │
  └─────────────────────────────────────────────┘
```

### 3.1 YYXX.dll（CTP 封装层，核心）

- **导出 15 个函数**（即 0527.exe 调用 CTP 的全部接口）
- C++/MSVC 编译，**VMProtect 加壳**（`.vmp0`/`.vmp1` 节区）
- PDB 路径：`J:\CtpDLL6.7.10\Release\YY.pdb`
- 导入 `thosttraderapi_se.dll` 的 `CreateFtdcTraderApi` + `thostmduserapi_se.dll` 的 `CreateFtdcMdApi`
- 详见 [02-ctp-api.md](02-ctp-api.md)

### 3.2 yy.dll（旧版 CTP 封装）

- 导出 14 个函数（比 YYXX.dll 少 `QryTrade`）
- 与 YYXX.dll 接口几乎一致，可能是老版本保留兼容

### 3.3 yyVolume.dll（行情专用封装）

- 导出 3 个函数：`StartCptHQ` / `SubscribeMarketData` / `UnSubscribeMarketDataMultiple`
- 仅导入 `thostmduserapi_se.dll`，专门处理成交量行情

### 3.4 PositionWin（独立持仓窗口程序）

- Delphi 编写，PE32 x86 GUI，3.38 MB
- 含 `TForm1` / `Unit1` / `PipeClientReadThread`
- 通过**命名管道（Named Pipe）**与 0527.exe 通信
- 从云端 `http://sunmengze.cc/PositionWin.txt` 下发更新

## 4. 目录结构

```
qihuo-software/
├── 0527.exe                  # 主程序（最新版, 8.79 MB）
├── 0106A.exe ~ 0416.exe      # 历史版本（1月-4月迭代）
├── YYXX.dll                  # CTP 封装层（15 导出, VMP 加壳）
├── yy.dll                    # CTP 封装旧版（14 导出）
├── yyVolume.dll              # 行情封装（3 导出）
├── PositionWin               # 独立持仓窗口程序（3.38 MB PE）
├── thosttraderapi_se.dll     # CTP 交易 API (v6.7.10)
├── thostmduserapi_se.dll     # CTP 行情 API (v6.7.10)
├── sqlite3.dll               # SQLite 本地存储
├── libcurl.dll               # HTTP 客户端
├── libcrypto-1_1.dll         # OpenSSL 1.1
├── libeay32.dll / ssleay32.dll  # OpenSSL (老版)
├── WebView2Loader.dll        # WebView2
├── zlib.dll / zlibd1.dll / zlibwapi.dll  # 压缩
├── borlndmm.dll / cc32c290mt.dll  # Delphi 运行时
├── msvcr120.dll / msvcp120.dll / MSVCP140.dll / VCRUNTIME140.dll  # VC++ 运行时
├── config.ini                # 主配置（GBK 编码）
├── HQAddress.xml             # 行情服务器地址列表（9 个）
├── Users.xml                 # 用户账号 + 窗口布局历史
├── Instruments.xml           # 合约清单缓存（9.4 MB）
├── DialogRsp.con / Private.con / Public.con / QueryRsp.con / TradingDay.con  # 6字节状态文件
├── Cancellation.wav / cashreg.wav / chimes.wav / Nomoney.wav  # 提示音效
├── PnL/                      # 每日盈亏 CSV（20260107.csv ~ 20260727.csv, ~100 个）
└── SysLog/                   # 系统日志 TXT（2025-12 ~ 2026-07, ~130 个）
```

## 5. 版本演化

同目录保留了 9 个版本，从 1 月到 5 月持续迭代：

| 版本 | 大小 | 备注 |
|---|---|---|
| 0106A.exe | 6.31 MB | 1月6日 A 版 |
| 0114E.exe | 6.35 MB | 1月14日 E 版 |
| 0122.exe | 6.35 MB | |
| 0126.exe | 6.36 MB | |
| 0202B.exe | 6.36 MB | 2月2日 B 版 |
| 0303B.exe | 6.46 MB | |
| 0401.exe | 6.51 MB | |
| 0416.exe | 6.52 MB | |
| **0527.exe** | **9.22 MB** | **最新版**，体积明显增大（+2.7MB，疑新增 WebView2/CEF 模块） |

## 6. 运行时启动流程（来自 SysLog 实证）

```
1. NTP 时间同步（ntp1.aliyun.com），计算时间差
2. 加载 config.ini 配置（行情地址/抢单频率/撤单限制/字体/列宽/触发时间表）
3. 统计合约：期货 5559 + 股指 738 + 期权 24196 = 30493 个合约
4. 订阅 30493 个合约行情
5. 读取成交量（yyVolume.dll）
6. 选择用户（如 338897）→ 选择交易服务（如 tcp://122.224.130.77:42205）
7. 登录认证流程：
   [*]0 用户登陆
   [*]1 客户端认证成功
   [*]3 认证响应
   [*]4 交易登录请求成功 → 登入认证成功，获取交易日 + 会话编号
   [*]6 行情登录请求成功
8. 登录后批量查询：查询持仓 → 查询明细 → 查询报单 → 查询成交
9. 行情断线重连：每 5 秒触发"重新订阅"
10. 退出：取消订阅（如 13 个合约）
```

## 7. 逆向分析方法与工具

| 工具/方法 | 用途 | 产出文件 |
|---|---|---|
| PE 头解析（自研 `Get-PeInfo.ps1`） | 解析导入表/导出表/节区 | `pe-imports-0527.txt`, `exports-*.txt` |
| ASCII 字符串提取（ISO-8859-1） | 提取代码内常量、API 名、字段名 | `strings-ascii.txt` (116138 条) |
| UTF-16LE 字符串提取 | 提取 VCL 控件 Caption（有假阳性） | `strings-utf16.txt` (88163 条) |
| DFM 窗体解析（自研 `Get-DfmForms.ps1`） | 提取窗体类名/控件树/属性 | `dfm-forms-strict.txt` (11 窗体) |
| DFM ShortString 启发式提取 | 提取控件类名/实例名/属性名 | `dfm-shortstrings.txt` |
| Delphi RTTI 解析（`@$xp$` 类型信息） | 提取方法签名 + CTP 数据结构 | 见 02-ctp-api.md |
| SysLog 日志分析 | 实证运行时业务流程 | `SysLog/*.txt` |

### 后续深度逆向建议

若需更深入反编译（如恢复完整业务逻辑），推荐：
- **IDR (Interactive Delphi Reconstructor)** — 专门反编译 Delphi，恢复 VCL 类层次/方法/事件
- **Resource Hacker** — 查看 DFM 资源、菜单、图标
- **Ghidra / IDA Pro** — 通用反汇编（对 YYXX.dll 受 VMProtect 限制）

## 8. 局限与假设

- **YYXX.dll 被 VMProtect 加壳**，内部 CTP 调用细节无法通过静态分析完全还原。接口契约依赖导出函数名 + CTP 6.7.10 官方 API 文档 + Delphi RTTI 方法签名三方印证。
- **DFM 完整属性解析未完成**（TValueType 类型代码表初期错误，后改用 ShortString 启发式）。窗体 Caption 等细节需 IDR 工具补充。
- **SQLite 数据库文件未在软件目录找到**，`market_data` 表可能为内存数据库或运行时临时文件。
- **HTTP 云端接口的请求/响应格式**只能从 URL 和上下文推测，实际需抓包确认。

## 9. 相关文档

- [02-ctp-api.md](02-ctp-api.md) — CTP 交易/行情接口详解
- [03-data-formats.md](03-data-formats.md) — 配置与数据文件格式
- [04-http-cloud.md](04-http-cloud.md) — HTTP 云端接口
- [05-ui-windows.md](05-ui-windows.md) — UI 窗体与功能映射
- [06-refactor-guide.md](06-refactor-guide.md) — WPF 重构建议
