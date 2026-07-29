# 03 - 配置与数据文件格式

> 本文整理 0527.exe 读写的外部数据文件格式，作为 WPF 重构时的数据兼容性参考。

## 1. 文件总览

| 文件 | 编码 | 大小 | 用途 | 读/写 |
|---|---|---|---|---|
| `config.ini` | GBK | 752 B | 主配置（窗口/订单/用户三段） | 读写 |
| `HQAddress.xml` | UTF-8 | 669 B | 行情服务器地址列表（9 个） | 读 + 云端更新 |
| `Users.xml` | UTF-8 | 7.6 KB | 用户账号 + 窗口布局历史 | 读写 |
| `Instruments.xml` | UTF-8 | 9.4 MB | 合约清单缓存 | 读 + 云端更新 |
| `*.con`（5 个） | 二进制 | 6 B 各 | 状态计数器 | 读写 |
| `PnL/YYYYMMDD.csv` | GBK | 25-982 B | 每日盈亏 | 写 |
| `SysLog/YYYY年MM月DD日.txt` | GBK | 4-1500 KB | 系统日志 | 写 |
| SQLite `market_data` 表 | - | - | 行情数据缓存（内存数据库？） | 读写 |

## 2. config.ini（主配置）

**编码**：GBK（注意：非 UTF-8，WPF 重构时需处理编码转换）

```ini
[Window]
MainFont=新宋体                  # 主字体
CompactSpacing=7                 # 窗口排列紧凑度
FontSizeOffset=0                 # 字号偏移
PriceListMargin=5                # 价格列表边距
DecTitle=30                      # 标题栏减少高度
Align=1                          # 对齐方式
narrowReduceLength=40            # 窄模式减少宽度
MouseWheelSpeed=3                # 行情滚轮加速
AutoSize=0                       # 窗口大小自动调整开关
TickRowHeights=12                # Tick 行高
InstrumentWindowHeights=1000     # 合约窗口高度
PriceListRatios=10,25,30,25,10   # 价格列宽比例（5 列）
PriceListMargin=5                # 价格列表边距（重复）

[Order]
SPCK=0                           # 上海(CFFEX?) 撤单开关
GZCK=0                           # 股指撤单开关
RiskOpen=0                       # 风控开关
MaxCancelGZ=395                  # 股指最大撤单数
MaxCancelSP=10000                # 商品最大撤单数
MaxCancelQQ=10000                # 期权最大撤单数
MaxInputCount=0                  # 最大报单数限制
MaxPositionCount=0               # 最大持仓数限制

[User]
HQAddress= tcp://140.207.230.97:61213   # 默认行情地址（东证联通）
QDP=0                            # ?
RunMode=0                        # 运行模式（0=正常?）
CloudRiskOn=0                    # 云风控开关
HQFFON=0                         # 行情转发开关
HQFFIP=127.0.0.1                 # 行情转发 IP
HQFFPORT=56789                   # 行情转发端口
MOrderXSpeed=200                 # 开盘抢单频率(ms)
MOrderXStop=2200                 # 抢单持续时间(ms)
PW=                              # 密码（明文，空）
MOrderTime1=09:29:58             # 开盘触发时间 1
MOrderTime2=08:59:58             # 开盘触发时间 2
MOrderTime3=08:54:58             # 开盘触发时间 3
MOrderTime4=12:59:58             # 开盘触发时间 4
MOrderTime5=20:59:58             # 开盘触发时间 5
MOrderTime6=13:29:58             # 开盘触发时间 6
MOrderTime7=20:54:58             # 开盘触发时间 7
MOrderTime8=09:24:58             # 开盘触发时间 8
MOrderTime9=10:31:00             # 开盘触发时间 9
```

### 2.1 配置语义说明

- **SPCK / GZCK**：推测为不同交易所/品种的撤单开关（SP=商品, GZ=股指）。`0` = 关闭。
- **MaxCancelGZ/SP/QQ**：股指/商品/期权的最大撤单数限制（CTP 风控要求，防撤单过量被限制）。
- **HQFF (行情转发)**：将收到的行情转发到本地端口（127.0.0.1:56789），供其他程序使用。
- **MOrderTime1-9**：9 个开盘抢单触发时间点，覆盖各交易所开盘时段（09:25 集合竞价、09:30 开盘、13:30 下午开盘、21:00 夜盘等）。

## 3. HQAddress.xml（行情地址列表）

```xml
<?xml version="1.0"?>
<HQAddress>
  <Address Name="海通"    Port="38215">180.168.212.75</Address>
  <Address Name="东证联通" Port="61213">140.207.230.97</Address>
  <Address Name="东证电信" Port="61213">101.226.253.177</Address>
  <Address Name="创元联1" Port="41213">101.226.249.59</Address>
  <Address Name="创元联2" Port="41213">101.226.249.60</Address>
  <Address Name="天鸿联通" Port="42213">58.246.138.43</Address>
  <Address Name="天鸿电信" Port="42213">180.166.12.248</Address>
  <Address Name="中信联通" Port="28213">58.33.80.165</Address>
  <Address Name="国富电信" Port="53313">101.230.178.178</Address>
</HQAddress>
```

**云端更新源**：`http://sunmengze.cc/HQAddress.xml`

## 4. Users.xml（用户账号 + 窗口布局）

```xml
<?xml version="1.0" encoding="UTF-8"?>
<Users>
  <User>
    <WindowHistory>
      <Instrument Top="33" Left="881" Height="1306" Width="271"
                  ValLeft="1" ValRight="2" RowHeight="10"
                  RBOA="false" RBOB="true" CBNearby="false" CBOnlyOpen="false"
                  Group="1" GroupEX="0"
                  CntrbySprdID="ag" CntrbySprdPT="0"
                  CntrbySprdIDEX="ag" CntrbySprdPTEX="0" CntrbySprdFctn="1"
                  isNarrowMode="false"
                  CBCntrbySprd="false" CBCntrbySprdEX="false"
                  CBCDLock="false" CBBGDS="true" CBZDTlock="true">ag2608</Instrument>
      <!-- ... 更多 Instrument 窗口 ... -->
    </WindowHistory>
    <title>338897</title>                          <!-- 用户ID -->
    <address>tcp://122.224.130.77:42205</address>   <!-- 交易服务器地址 -->
    <brokerid>88888</brokerid>                      <!-- 经纪商ID (SimNow) -->
    <userid>338897</userid>                         <!-- 用户ID -->
    <appid>Weg_yiyisy_V1.0</appid>                  <!-- 认证 AppID -->
    <shouquan>VLH1QX4FHIJ976UC</shouquan>           <!-- 授权码 -->
  </User>
</Users>
```

### 4.1 Instrument 窗口布局字段

| 字段 | 含义 |
|---|---|
| `Top` / `Left` / `Height` / `Width` | 窗口位置和尺寸 |
| `ValLeft` / `ValRight` | 显示的买卖盘档位（如 1 档、2 档） |
| `RowHeight` | 行高 |
| `RBOA` / `RBOB` | 排序方式（A/B 两种） |
| `CBNearby` | 仅显示近月 |
| `CBOnlyOpen` | 仅显示有持仓 |
| `Group` / `GroupEX` | 分组编号（主/扩展） |
| `CntrbySprdID` / `CntrbySprdIDEX` | 按价差筛选的品种 ID（如 "ag"） |
| `CntrbySprdPT` / `CntrbySprdPTEX` | 按价差筛选的价位 |
| `CntrbySprdFctn` | 价差筛选系数 |
| `isNarrowMode` | 窄模式 |
| `CBCntrbySprd` / `CBCntrbySprdEX` | 启用价差筛选（主/扩展） |
| `CBCDLock` | CD 锁定？ |
| `CBBGDS` | 背景式？ |
| `CBZDTlock` | 涨跌停锁定 |

**元素内容**：合约代码（如 `ag2608`、`jd2609-P-3200` 期权）

## 5. Instruments.xml（合约清单缓存）

9.4 MB，存储全部合约。结构（推测，从大小和用途判断）：

```xml
<?xml version="1.0" encoding="UTF-8"?>
<Instruments>
  <Instrument>
    <InstrumentID>ag2608</InstrumentID>
    <ExchangeID>SHFE</ExchangeID>
    <ProductID>ag</ProductID>
    <ProductClass>Futures</ProductClass>
    <PriceTick>1</PriceTick>
    <VolumeMultiple>15</VolumeMultiple>
    <!-- ... 更多字段 ... -->
  </Instrument>
  <!-- ... 30493 个合约 ... -->
</Instruments>
```

**云端更新源**：`http://sunmengze.cc/Instruments.xml`

**统计**（来自 SysLog）：期货 5559 + 股指 738 + 期权 24196 = 30493 个合约

## 6. *.con 状态文件（5 个，各 6 字节）

| 文件 | Hex 内容 | 推测含义 |
|---|---|---|
| `DialogRsp.con` | `00 00 00 00 00 01` | 对话框响应计数器 |
| `Private.con` | `42 72 00 00 00 34` | 私有流计数器（"Br" + 0x34=52） |
| `Public.con` | `42 72 00 00 01 FC` | 公有流计数器（"Br" + 0x01FC=508） |
| `QueryRsp.con` | `00 00 00 00 35 CF` | 查询响应计数器（0x35CF=13775） |
| `TradingDay.con` | `42 74 00 00 00 00` | 交易日标记（"Bt" + 0） |

> **注**：前两字节 "Br"/"Bt" 可能是 CTP 流文件标记（CTP 的 flow 文件用类似标记）。这些文件对应 CTP API 的 flow 文件（订阅/查询的序列化状态）。WPF 重构时若用新 CTP wrapper，可不再需要这些文件。

## 7. PnL/YYYYMMDD.csv（每日盈亏）

**编码**：GBK。文件名按交易日期命名（如 `20260107.csv`）。

```csv
合约,平仓盈亏
lc2606,30700
ni2602C152000,6644
ps2605,4650
pt2606-C-600,3700
au2602C1208,3300
...
ao2602C2850,-60
lc2602-C-136000,-150
```

**格式**：两列 CSV
- 第 1 列：合约代码（如 `ag2602C23200` = ag 2602 合约 Call 期权 行权价 23200）
- 第 2 列：平仓盈亏金额（元，负数为亏损）

## 8. SysLog/YYYY年MM月DD日.txt（系统日志）

**编码**：GBK。文件名按日期命名（中文格式）。

### 8.1 日志头部（每次启动）

```
版本 2026年 5月27日 10:06:52 日志更新时间: 2026年07月24日 12:44:07
====================================
时间服务 ntp1.aliyun.com
网络时间 2026/7/24 12:12:24
时间差 2.92 秒
-------------12:12:24-------------
默认行情服务: tcp://140.207.230.97:61213
开盘抢单频率: 200ms
持续时间: 2200ms
股指最大撤单数: 395
商品最大撤单数: 10000
期权最大撤单数: 10000
交易窗口字体: 新宋体
列宽比例: 10 25 30 25 10
开盘触发时间表
       09:29:58
       ...
窄模式减少宽度: 40
标题栏减少高度: 30
价格列表边距: 5
窗口排列紧凑度: 7
行情滚轮加速: 3
启用切组自动调平
开启窗口大小自动调整
```

### 8.2 运行时事件格式

```
-------------HH:mm:ss-------------
事件描述
```

典型事件：
- `期货 5559 股指 738 期权 24196` / `正在订阅: 30493个合约`
- `正在读取成交量.....` / `读取完毕 未推送: 11694 0成交: 14306`
- `选择了用户: 338897`
- `选择交易服务: tcp://122.224.130.77:42205`
- `[*]0 用户登陆` / `[*]1 客户端认证成功` / `[*]4 交易登录请求成功`
- `登入认证成功 交易日: 20260724` / `会话编号: -367078502`
- `查询持仓...` / `查询持仓成功 [jd2609-P-3300] 持仓 今:3 多空:2`
- `触发5秒 重新订阅`（行情断线重连）
- `取消订阅完成，共计: 13 个合约`

## 9. SQLite market_data 表

从 0527.exe 字符串提取的 SQL：

```sql
CREATE TABLE IF NOT EXISTS market_data (
    id            INTEGER PRIMARY KEY,
    instrument_id TEXT,
    ts            INTEGER,
    tp            INTEGER,
    data          TEXT
);

INSERT INTO market_data (instrument_id, ts, tp, data)
VALUES (:instrument_id, :ts, :tp, :data);
```

### 9.1 字段语义推测

| 字段 | 类型 | 语义 |
|---|---|---|
| `id` | INTEGER PK | 自增主键 |
| `instrument_id` | TEXT | 合约代码（如 "ag2608"） |
| `ts` | INTEGER | 时间戳（Unix epoch 秒/毫秒） |
| `tp` | INTEGER | 数据类型（type？tick type？） |
| `data` | TEXT | 序列化的行情数据（JSON？） |

**数据库文件位置**：未在软件目录找到 `.db`/`.sqlite` 文件，推测为**内存数据库**（`:memory:`）用于行情缓存，或运行时临时文件。

## 10. WPF 重构数据兼容性建议

1. **config.ini**：WPF 用 `System.Text.Encoding.GetEncoding(936)` 读写 GBK；或迁移到 JSON 配置（提供迁移工具转换旧 ini）。
2. **HQAddress.xml / Users.xml / Instruments.xml**：可保持 XML 格式，或迁移到 SQLite/JSON。注意保留 Users.xml 的窗口布局字段（用户习惯）。
3. **PnL CSV**：保持 GBK CSV 格式（Excel 兼容），或迁移到 SQLite。
4. **SysLog**：保持文本日志，建议改为结构化日志（serilog + JSON）。
5. **.con 文件**：CTP flow 文件，若换用新 CTP wrapper 可废弃。
6. **SQLite market_data**：重构时改用正规的 SQLite 文件数据库（如 `market.db`），而非内存数据库。

## 11. 相关文档

- [01-overview.md](01-overview.md) — 软件总览
- [04-http-cloud.md](04-http-cloud.md) — HTTP 云端接口（含配置下发）
- [06-refactor-guide.md](06-refactor-guide.md) — WPF 重构建议
