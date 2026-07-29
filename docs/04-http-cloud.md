# 04 - HTTP 云端接口

> 本文整理 0527.exe 通过 libcurl / TIdHTTP 调用的全部 HTTP 云端接口。
> 接口域名：`sunmengze.cc`（主）+ `yiyikeji.top`（第三方）。
> 证据来源：0527.exe 字符串表（`strings-ascii.txt` 第 82355–82947 行）+ 同目录本地文件 + Delphi RTTI（`TRemoteThread`）。

## 1. 结论先行

- 共发现 **13 个 HTTP 端点**（9 个静态文件 + 3 个动态 ASPX + 1 个第三方）。
- 这些端点承担 4 类职责：**热更新分发、配置下发、远程控制、止损/合约同步**。
- 客户端为 `libcurl.dll` + Delphi Indy `TIdHTTP` / `TIdTCPClient`（DFM 内实证）。
- 协议为 **明文 HTTP**（非 HTTPS），存在被中间人篡改风险，WPF 重构时必须升级为 HTTPS + 签名校验。

## 2. 端点总览

| # | URL | 类型 | 本地落盘文件 | 用途 |
|---|---|---|---|---|
| 1 | `http://sunmengze.cc/YYXX.txt` | 静态 | `YYXX.dll` | CTP 封装层 DLL 热更新 |
| 2 | `http://sunmengze.cc/sqlite3.txt` | 静态 | `sqlite3.dll` | SQLite 引擎 DLL 热更新 |
| 3 | `http://sunmengze.cc/yyVolume.txt` | 静态 | `yyVolume.dll` | 行情成交量 DLL 热更新 |
| 4 | `http://sunmengze.cc/PositionWin.txt` | 静态 | `PositionWin` | 持仓窗口程序热更新 |
| 5 | `http://sunmengze.cc/Nomoney.wav` | 静态 | `Nomoney.wav` | 资金不足提示音 |
| 6 | `http://sunmengze.cc/config.xml` | 静态 | `config.ini` | 云端主配置下发 |
| 7 | `http://sunmengze.cc/HQAddress.xml` | 静态 | `HQAddress.xml` | 行情服务器地址列表 |
| 8 | `http://sunmengze.cc/JYAddress.xml` | 静态 | `JYAddress.xml`（运行时生成） | 交易服务器地址列表 |
| 9 | `http://sunmengze.cc/Instruments.xml` | 静态 | `Instruments.xml`（9.4 MB） | 合约清单同步 |
| 10 | `http://sunmengze.cc/Remote.aspx?key=` | 动态 | - | 远程命令通道 |
| 11 | `http://sunmengze.cc/StopLoss.aspx?key=` | 动态 | - | 止损单同步 |
| 12 | `http://sunmengze.cc/UserContract.aspx` | 动态 | - | 用户合约同步 |
| 13 | `http://yiyikeji.top/zuxin/jtqy.aspx?id=` | 动态 | - | 第三方（仪一科技）组信服务 |

## 3. 静态文件分发（热更新）

### 3.1 DLL/可执行文件热更新（关键）

`.txt` 扩展名是**伪装**——服务器返回的是二进制 PE 文件，客户端下载后去掉 `.txt` 改为正确扩展名落地。

| URL | 本地文件 | 大小 | 类型 |
|---|---|---|---|
| `YYXX.txt` | `YYXX.dll` | 57 KB | CTP 交易/行情封装（VMProtect 加壳） |
| `sqlite3.txt` | `sqlite3.dll` | 2.4 MB | SQLite 数据库引擎 |
| `yyVolume.txt` | `yyVolume.dll` | 10 KB | 行情成交量扩展 |
| `PositionWin.txt` | `PositionWin` | 3.22 MB | 持仓窗口独立程序（PE32） |

**典型流程**：
```
1. 启动 / 定时器触发：HTTP GET sunmengze.cc/<file>.txt
2. 比对本地文件版本（哈希或大小）
3. 若有更新：下载到临时文件 → 校验 → 替换本地 DLL
4. 重新 LoadLibrary 加载新版本
```

> **重要**：这意味着原软件支持**无版本号滚动发布**——服务器端上传新 `.txt`，所有客户端下次轮询即拉到新版本。WPF 重构时建议改为**带版本号的正式更新通道**（如 GitHub Releases / 自建 OSS），避免明文 HTTP 下载可执行文件。

### 3.2 音频文件

| URL | 本地文件 | 大小 | 触发场景 |
|---|---|---|---|
| `Nomoney.wav` | `Nomoney.wav` | 51 KB | 资金不足时播放（与 `cashreg.wav` / `chimes.wav` / `Cancellation.wav` 同目录，但这 3 个本地静态文件未见云端 URL） |

### 3.3 配置文件下发

#### 3.3.1 `config.xml` → `config.ini`

- 服务器返回 XML，落地为 GBK 编码的 `config.ini`。
- 推测：服务器用 XML 表达配置，客户端转换/合并到本地 ini。
- 字段对应 [03-data-formats.md](03-data-formats.md) §2 的 `[Window]` / `[Order]` / `[User]` 三段。
- 用于**云端统一下发风控参数**（如 `MaxCancelGZ=395`、`MOrderTime1-9` 抢单时间表）。

#### 3.3.2 `HQAddress.xml` / `JYAddress.xml`

行情/交易服务器地址列表，结构见 [03-data-formats.md](03-data-formats.md) §3：

```xml
<HQAddress>
  <Address Name="海通"    Port="38215">180.168.212.75</Address>
  <!-- ... 9 个地址 ... -->
</HQAddress>
```

- `HQAddress.xml` 已落地 669 B（9 个行情地址）。
- `JYAddress.xml` 同结构，但本地未持久化（运行时下载 → 内存使用）。
- WPF 重构时合并为统一 `servers.json`。

#### 3.3.3 `Instruments.xml`（合约清单）

- 9.4 MB 大文件，包含全市场 30493 个合约（期货 5559 + 股指 738 + 期权 24196）。
- 启动时下载/校验，落地到 `Instruments.xml` 作为缓存。
- CTP 登录后通过 `QryInstrument` 也会查到，云端 XML 是**离线可用 + 快速启动**的预缓存。

## 4. 动态 ASPX 接口（业务交互）

### 4.1 `Remote.aspx?key=` — 远程命令通道

```
GET http://sunmengze.cc/Remote.aspx?key=<KEY>
```

| 项 | 内容 |
|---|---|
| 方法 | GET |
| 参数 | `key` — 推测为用户 ID 或授权 token（与 Users.xml 的 `userid=338897` / `shouquan` 关联） |
| 返回 | 推测为命令字符串或简单 OK |
| 处理线程 | **`TRemoteThread`**（Delphi RTTI 实证：`@$xp$13TRemoteThread`） |
| 用途 | 远程触发操作（如强制平仓 / 修改配置 / 推送通知），具体命令集需运行时抓包 |

**特点**：使用独立线程 `TRemoteThread` 轮询，与主交易流程异步解耦。这是软件的**远程控制后门**，运营方可以在用户不知情下下发指令。

> ⚠️ 重构时必须：1) 强制 HTTPS；2) 命令签名校验；3) 用户可见的命令日志；4) 用户可关闭此通道。

### 4.2 `StopLoss.aspx?key=` — 止损单同步

```
GET http://sunmengze.cc/StopLoss.aspx?key=<KEY>
```

| 项 | 内容 |
|---|---|
| 方法 | GET |
| 参数 | `key` — 同上，用户标识 |
| 返回 | 推测为止损单列表（JSON/XML） |
| 用途 | 云端管理的止损单下发到客户端执行 |

**业务定位**：止损单在客户端本地执行（CTP 报单），但**配置在云端**——便于用户多端同步 / 运营方统一风控。配合 `config.ini` 的 `RiskOpen=0` / `CloudRiskOn=0` 开关使用。

### 4.3 `UserContract.aspx` — 用户合约同步

```
GET http://sunmengze.cc/UserContract.aspx
```

| 项 | 内容 |
|---|---|
| 方法 | GET |
| 参数 | 二进制字符串中出现 **2 次**（`0x006AC146` + `0x006ADC26`），推测一次拉取、一次回写 |
| 返回 | 用户自定义合约列表 / 分组 |
| 用途 | 同步用户订阅的合约集合（区别于全市场 `Instruments.xml`） |

> **注**：字符串表中 URL 后未跟 `?key=`，但实际调用大概率仍带身份参数（可能在 HTTP Header 或 POST body，静态分析无法确认）。

### 4.4 `yiyikeji.top/zuxin/jtqy.aspx?id=` — 第三方组信服务

```
GET http://yiyikeji.top/zuxin/jtqy.aspx?id=<ID>
```

| 项 | 内容 |
|---|---|
| 域名 | `yiyikeji.top`（仪一科技，独立于 `sunmengze.cc`） |
| 路径 | `/zuxin/`（组信？）`/jtqy.aspx`（JTQY?） |
| 参数 | `id` — 用户 ID 或组 ID |
| 用途 | **未知**，推测为社群/组消息推送或群组持仓查询 |
| 安全风险 | 第三方域名，明文 HTTP，数据会泄露给仪一科技 |

## 5. 调用方式与认证

### 5.1 HTTP 客户端栈

```
0527.exe
  ├─ libcurl.dll         (360 KB)   ← 云端文件下载（DLL/大文件）
  ├─ TIdHTTP             (DFM 内)   ← ASPX 接口调用（动态业务）
  ├─ TIdTCPClient        (DFM 内)   ← 长连接（推测远程命令推送）
  └─ TXMLDocument        (DFM 内)   ← 解析返回的 XML
```

### 5.2 认证机制（推测）

字符串表中**未见明显 token/auth header**，认证方式推测为：

1. **URL 参数携带身份**：`?key=<userid>` 或 `?key=<shouquan>`
2. **User-Agent 标识**：可能用 UA 携带客户端版本
3. **无认证**：可能服务端按 IP 白名单 / 简单 Key 限制

WPF 重构时必须改为：
- HTTPS + Bearer Token / 签名
- 服务端鉴权（JWT/OAuth2）
- 客户端证书绑定（可选）

## 6. 接口调用时机（来自 SysLog 实证 + 推测）

```
启动阶段:
  1. GET config.xml         ← 拉取云端配置
  2. GET HQAddress.xml      ← 刷新行情地址
  3. GET JYAddress.xml      ← 刷新交易地址
  4. GET Instruments.xml    ← 刷新合约清单（9.4 MB，可能跳过若本地最新）
  5. GET YYXX.txt/sqlite3.txt/yyVolume.txt  ← DLL 版本检查
  6. GET PositionWin.txt    ← 持仓窗口程序版本检查

登录后:
  7. GET UserContract.aspx  ← 拉取用户合约分组
  8. GET StopLoss.aspx?key= ← 拉取止损单
  9. GET jtqy.aspx?id=      ← 拉取组信数据

运行时（持续）:
  10. GET Remote.aspx?key=  ← TRemoteThread 周期轮询远程命令
```

## 7. WPF 重构建议

### 7.1 接口契约定义（C# 示例）

```csharp
public interface ICloudService {
    // 静态文件分发
    Task<VersionedFile> FetchCtpWrapperAsync(CancellationToken ct);      // YYXX.txt
    Task<VersionedFile> FetchSqliteAsync(CancellationToken ct);          // sqlite3.txt
    Task<VersionedFile> FetchVolumeExtAsync(CancellationToken ct);       // yyVolume.txt
    Task<VersionedFile> FetchPositionWindowAsync(CancellationToken ct);  // PositionWin.txt
    Task<Stream> FetchAlertSoundAsync(string name, CancellationToken ct);// Nomoney.wav 等

    // 配置下发
    Task<CloudConfig> FetchConfigAsync(CancellationToken ct);
    Task<IReadOnlyList<ServerEndpoint>> FetchMarketServersAsync(CancellationToken ct);
    Task<IReadOnlyList<ServerEndpoint>> FetchTradeServersAsync(CancellationToken ct);
    Task<InstrumentCatalog> FetchInstrumentsAsync(DateTimeOffset? since, CancellationToken ct);

    // 业务交互
    Task<UserContractSync> FetchUserContractsAsync(string userId, CancellationToken ct);
    Task<IReadOnlyList<StopLossOrder>> FetchStopLossAsync(string userId, CancellationToken ct);
    Task<RemoteCommand?> PollRemoteCommandAsync(string userId, CancellationToken ct);

    // 第三方
    Task<GroupMessage> FetchGroupMessageAsync(string userId, CancellationToken ct);
}

public record VersionedFile(string FileName, byte[] Content, string Sha256, Version Version);
public record CloudConfig(WindowCfg Window, OrderCfg Order, UserCfg User);
public record ServerEndpoint(string Name, string Host, int Port, string Protocol);
```

### 7.2 重构必做项

| 项 | 原状 | 目标 |
|---|---|---|
| 传输协议 | 明文 HTTP | **强制 HTTPS + TLS 1.2+** |
| 身份认证 | URL 参数 `?key=` | JWT Bearer Token + 刷新机制 |
| 文件校验 | 无（直接覆盖） | SHA-256 哈希 + Ed25519 签名 |
| 版本管理 | 无版本号 | SemVer + 服务端版本协商 |
| 远程命令 | 隐式后门 | 用户可见、可关闭、命令日志 |
| 第三方调用 | 明文 HTTP to yiyikeji.top | 评估必要性，能去则去 |
| 重试/超时 | 未知 | Polly 策略（指数退避 + 熔断） |
| 日志 | SysLog 文本 | 结构化日志（Serilog + JSON） |

### 7.3 服务端迁移建议

原服务器 `sunmengze.cc` 推测为简单 ASP.NET WebForms + 静态文件服务。重构时建议：

1. **后端**：ASP.NET Core 8+ Minimal API 或 FastEndpoints
2. **文件分发**：迁移到 OSS（阿里云 OSS / 腾讯云 COS）+ CDN
3. **配置中心**：用 Apollo / Consul / etcd 替代 `config.xml`
4. **远程命令**：改为 SignalR / WebSocket 推送（替代 TRemoteThread 轮询）
5. **合约同步**：增量同步（按 `last_modified` 增量拉取，避免 9.4 MB 全量下载）

## 8. 局限与待确认项

- **请求/响应具体格式**：静态分析只能确认 URL，无法看到 body/Header/响应体结构，需**抓包确认**（Fiddler / Wireshark + 模拟环境）。
- **`key` 参数构成**：未在字符串中直接命中拼接逻辑，推测为 `userid` 或 `shouquan`，需运行时验证。
- **`UserContract.aspx` 调用方式**：可能是 GET 也可能 POST，URL 出现 2 次暗示可能有读写两个动作。
- **`yiyikeji.top/jtqy.aspx` 业务语义**：未在 SysLog 找到对应日志，可能是新版未启用功能或调试遗留。
- **认证机制**：未发现 token 管理代码，可能完全靠 IP 限制或简单 key。

## 9. 相关文档

- [01-overview.md](01-overview.md) — 软件总览
- [02-ctp-api.md](02-ctp-api.md) — CTP 交易/行情接口
- [03-data-formats.md](03-data-formats.md) — 配置与数据文件格式
- [05-ui-windows.md](05-ui-windows.md) — UI 窗体与功能映射
- [06-refactor-guide.md](06-refactor-guide.md) — WPF 重构建议
