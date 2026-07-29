# 02 - CTP 交易/行情接口

> 本文整理 0527.exe 调用 CTP（上期技术综合交易平台 v6.7.10）的完整接口契约。
> 接口通过自研 C++ 封装层 `YYXX.dll` / `yy.dll` / `yyVolume.dll` 暴露，非直接调用 CTP 原生 API。

## 1. 架构总览

```
0527.exe (Delphi)
  │  动态加载 (LoadLibrary + GetProcAddress)
  │  字符串内 DLL 名: sqlite3.dll / libeay32.dll / ssleay32.dll / YYXX.dll / yyVolume.dll
  ▼
YYXX.dll (C++/MSVC, VMProtect 加壳)
  │  导入: ?CreateFtdcTraderApi@CThostFtdcTraderApi@@SAPAV1@PBD_N1_N@Z
  │       ?CreateFtdcMdApi@CThostFtdcMdApi@@SAPAV1@PBD_N1_N@Z
  ▼
thosttraderapi_se.dll  +  thostmduserapi_se.dll   (CTP v6.7.10 原生)
```

**关键点**：0527.exe 不直接依赖 CTP DLL（导入表只含 21 个 Windows 系统 DLL），所有 CTP 调用经 YYXX.dll 转发。YYXX.dll 通过 CTP 的 C++ vtable 调用 `Req*` 方法，并通过 SPI 回调将 `OnRsp*`/`OnRtn*` 事件传回 0527.exe。

## 2. YYXX.dll 导出函数（核心接口，15 个）

PDB: `J:\CtpDLL6.7.10\Release\YY.pdb`

### 2.1 生命周期管理

| # | 导出函数 | 功能 | 对应 CTP 原生 API | 备注 |
|---|---|---|---|---|
| 1 | `StartCptJY` | 启动交易连接 | `RegisterFront` + `RegisterSpi` + `Init` | CtpJY = 交易 |
| 2 | `StartCptHQ` | 启动行情连接 | `RegisterFront` + `RegisterSpi` + `Init` | CtpHQ = 行情 |
| 3 | `Release` | 释放 API 实例 | `Release` | |
| 4 | `GetTradingDay` | 获取交易日 | `GetTradingDay` | 返回如 "20260724" |

### 2.2 交易接口

| # | 导出函数 | 功能 | 对应 CTP 原生 API | 输入/输出 |
|---|---|---|---|---|
| 5 | `OrderInsert` | 报单（下单） | `ReqOrderInsert` | 入: `CThostFtdcInputOrderField` |
| 6 | `OrderAction` | 撤单 | `ReqOrderAction` | 入: `CThostFtdcInputOrderActionField` |
| 7 | `NewOrder` | 新建订单（预设单？） | 封装逻辑，非 CTP 直发 | 待反编译确认 |

### 2.3 查询接口

| # | 导出函数 | 功能 | 对应 CTP 原生 API | 返回数据结构 |
|---|---|---|---|---|
| 8 | `QryInstrument` | 查询合约 | `ReqQryInstrument` | `CThostFtdcInstrumentField` |
| 9 | `QryOrder` | 查询报单 | `ReqQryOrder` | `CThostFtdcOrderField` |
| 10 | `QryTrade` | 查询成交 | `ReqQryTrade` | `CThostFtdcTradeField` |
| 11 | `QryTradingAccount` | 查询资金账户 | `ReqQryTradingAccount` | `CThostFtdcTradingAccountField` |
| 12 | `QryInvestorPosition` | 查询投资者持仓 | `ReqQryInvestorPosition` | `CThostFtdcInvestorPositionField` |
| 13 | `QryInvestorPositionDetail` | 查询持仓明细 | `ReqQryInvestorPositionDetail` | `CThostFtdcInvestorPositionDetailField` |

### 2.4 行情接口

| # | 导出函数 | 功能 | 对应 CTP 原生 API |
|---|---|---|---|
| 14 | `SubscribeMarketData` | 订阅行情 | `SubscribeMarketData` |
| 15 | `UnSubscribeMarketData` | 退订行情 | `UnSubscribeMarketData` |

> **注**：登录认证（`ReqAuthenticate` / `ReqUserLogin` / `ReqSettlementInfoConfirm`）未在 YYXX.dll 导出表中单独列出，推测封装在 `StartCptJY` 内部（从 SysLog 日志看，启动交易后自动完成"用户登陆 → 客户端认证 → 认证响应 → 交易登录请求成功"全流程）。

## 3. yy.dll 导出函数（旧版封装，14 个）

与 YYXX.dll 几乎一致，**仅缺少 `QryTrade`**。推测为兼容旧版保留：

```
GetTradingDay, NewOrder, OrderAction, OrderInsert,
QryInstrument, QryInvestorPosition, QryInvestorPositionDetail,
QryOrder, QryTradingAccount, Release,
StartCptHQ, StartCptJY, SubscribeMarketData, UnSubscribeMarketData
```

## 4. yyVolume.dll 导出函数（行情专用，3 个）

专门处理成交量行情，仅导入 `thostmduserapi_se.dll`：

| # | 导出函数 | 功能 |
|---|---|---|
| 1 | `StartCptHQ` | 启动行情连接 |
| 2 | `SubscribeMarketData` | 订阅行情 |
| 3 | `UnSubscribeMarketDataMultiple` | 批量退订行情 |

## 5. CTP 回调清单（SPI 接口）

从 0527.exe 二进制字符串表 + Delphi RTTI 提取，确认以下回调被实际处理：

### 5.1 交易回调

| 回调 | 触发场景 | 数据结构 | Delphi 处理方法（RTTI 实证） |
|---|---|---|---|
| `OnRspUserLogin` | 登录响应 | `CThostFtdcRspUserLoginField` + `CThostFtdcRspInfoField` | 启动后批量查询 |
| `OnRspError` | 错误响应 | `CThostFtdcRspInfoField` | |
| `OnRspOrderInsert` | 报单响应 | `CThostFtdcInputOrderField` + `CThostFtdcRspInfoField` + `bool(isLast)` | `GetRspOrderInsert(InputOrder*, RspInfo*, bool)` |
| `OnRspOrderAction` | 撤单响应 | `CThostFtdcInputOrderActionField` + `CThostFtdcRspInfoField` + `bool` | |
| `OnRtnOrder` | 报单通知（状态变更） | `CThostFtdcOrderField` | `TForm1.RtnOrder(OrderField*)` → `YYWinList.ShowPendingFlag` |
| `OnRtnTrade` | 成交通知 | `CThostFtdcTradeField` | `TYYWin.Trade(TradeField*)` |
| `OnErrRtnOrderInsert` | 报单错误回报 | `CThostFtdcInputOrderField` + `CThostFtdcRspInfoField` | |
| `OnErrRtnOrderAction` | 撤单错误回报 | `CThostFtdcInputOrderActionField` + `CThostFtdcRspInfoField` | |

### 5.2 查询回调

| 回调 | 数据结构 | Delphi 处理方法 |
|---|---|---|
| `OnRspQryInstrument` | `CThostFtdcInstrumentField` + `CThostFtdcRspInfoField` + `bool` | |
| `OnRspQryOrder` | `CThostFtdcOrderField` + `CThostFtdcRspInfoField` + `bool` | |
| `OnRspQryTrade` | `CThostFtdcTradeField` + `CThostFtdcRspInfoField` + `bool` | |
| `OnRspQryTradingAccount` | `CThostFtdcTradingAccountField` + `CThostFtdcRspInfoField` + `bool` | `TForm1.SendTradingAccount(TradingAccount*, RspInfo*, bool)` |
| `OnRspQryInvestorPosition` | `CThostFtdcInvestorPositionField` + `CThostFtdcRspInfoField` + `bool` | |
| `OnRspQryInvestorPositionDetail` | `CThostFtdcInvestorPositionDetailField` + `CThostFtdcRspInfoField` + `bool` | |

### 5.3 行情回调

| 回调 | 数据结构 | Delphi 处理方法（RTTI 实证） |
|---|---|---|
| `OnRtnDepthMarketData` | `CThostFtdcDepthMarketDataField` | `TYYWin.GoSGPriceListLoad(DepthMarketData*)` <br> `TYYWin.UpdateDepthVolumesThreadSafe(DepthMarketData&)` |

> `OnRtnDepthMarketData` 字符串虽未直接命中，但从 RTTI 方法签名（接收 `CThostFtdcDepthMarketDataField`）可确认被处理。

## 6. CTP 数据结构（从 Delphi RTTI 提取）

以下结构在 0527.exe 的 `@$xp$...` 类型信息中被引用，确认实际使用：

| CTP 结构 | 用途 | 关键字段（CTP 标准） |
|---|---|---|
| `CThostFtdcInputOrderField` | 下单请求 | BrokerID, InvestorID, InstrumentID, OrderRef, UserID, OrderPriceType, Direction, CombOffsetFlag, CombHedgeFlag, LimitPrice, VolumeTotalOriginal, TimeCondition, GTDDate, VolumeCondition, MinVolume, ContingentCondition, StopPrice, ForceCloseReason, IsAutoSuspend, BusinessUnit, RequestID |
| `CThostFtdcOrderField` | 报单回报 | 上述字段 + FrontID, SessionID, ExchangeID, OrderSysID, OrderStatus, OrderSubmitStatus, VolumeTraded, VolumeTotal, InsertDate, InsertTime, CancelTime, ... |
| `CThostFtdcTradeField` | 成交回报 | BrokerID, InvestorID, InstrumentID, OrderRef, UserID, ExchangeID, TradeID, Direction, OrderSysID, ParticipantID, ClientID, TradingRole, OffsetFlag, HedgeFlag, Price, Volume, TradeDate, TradeTime, TradeType, PriceSource, ... |
| `CThostFtdcDepthMarketDataField` | 深度行情 | TradingDay, InstrumentID, ExchangeID, ExchangeInstID, LastPrice, PreSettlementPrice, PreClosePrice, PreOpenInterest, OpenPrice, HighestPrice, LowestPrice, Volume(成交量), Turnover, OpenInterest, ClosePrice, SettlementPrice, UpperLimitPrice, LowerLimitPrice, PreDelta, CurrDelta, UpdateTime, UpdateMillisec, BidPrice1-5, BidVolume1-5, AskPrice1-5, AskVolume1-5, AveragePrice, ... |
| `CThostFtdcTradingAccountField` | 资金账户 | BrokerID, AccountID, PreMortgage, PreCredit, PreDeposit, PreBalance, PreMargin, Interest, Deposit, Withdraw, FrozenMargin, FrozenCash, FrozenCommission, CurrMargin, CashIn, Commission, CloseProfit, PositionProfit, Balance, Available, WithdrawQuota, Reserve, TradingDay, SettlementID, Credit, Mortgage, ExchangeMargin, ... |
| `CThostFtdcInvestorPositionField` | 投资者持仓 | InstrumentID, BrokerID, InvestorID, PosiDirection, HedgeFlag, Position, TodayPosition, HistoryPosition, ... |
| `CThostFtdcInvestorPositionDetailField` | 持仓明细 | InstrumentID, BrokerID, InvestorID, Direction, HedgeFlag, OpenDate, TradeID, Volume, OpenPrice, ... |
| `CThostFtdcRspInfoField` | 响应信息 | ErrorID, ErrorMsg |
| `CThostFtdcRspUserLoginField` | 登录响应 | TradingDay, LoginTime, BrokerID, UserID, FrontID, SessionID, ... |

### 6.1 0527.exe 中确认使用的 CTP 字段（从字符串提取）

```
BrokerID, FrontID, SessionID, MaxOrderRef, OrderRef,
InvestorID, InstrumentID, CombInstrumentID, AppID, Broker, AppID
```

## 7. 登录认证流程（来自 SysLog 实证）

```
[*]0 用户登陆           ← StartCptJY 调用，CTP ReqUserLogin
[*]1 客户端认证成功      ← CTP ReqAuthenticate 响应 (AppID + 授权码认证)
[*]3 认证响应           ← OnRspAuthenticate
[*]4 交易登录请求成功    ← OnRspUserLogin (TraderApi)
   登入认证成功 交易日: 20260724
   会话编号: -367078502  ← SessionID
   本机时间 vs 交易所时间，计算时间差
[*]6 行情登录请求成功    ← OnRspUserLogin (MdApi)
```

**认证所需参数**（来自 Users.xml）：
- `brokerid` = 88888（SimNow 模拟环境）
- `userid` = 338897
- `appid` = `Weg_yiyisy_V1.0`
- `shouquan`（授权码）= `VLH1QX4FHIJ976UC`
- 交易地址 `address` = `tcp://122.224.130.77:42205`（SimNow 7×24）

## 8. 登录后查询流程（来自 SysLog 实证）

```
查询持仓...        → QryInvestorPosition  → OnRspQryInvestorPosition
查询持仓成功 [jd2609-P-3300] 持仓 今:3 多空:2
查询明细...        → QryInvestorPositionDetail → OnRspQryInvestorPositionDetail
查询明细成功
查询报单...        → QryOrder → OnRspQryOrder
查询报单成功
查询成交...        → QryTrade → OnRspQryTrade
查询成交记录成功
```

持仓显示格式：`[合约] 持仓 今:数量 多空:方向`

## 9. 行情断线重连机制

SysLog 显示行情断线时每 5 秒触发重新订阅：
```
-------------13:02:27-------------
触发5秒 重新订阅
-------------13:02:32-------------
触发5秒 重新订阅
...（持续直到行情恢复）
```

## 10. 开盘抢单机制（来自 config.ini + SysLog）

| 配置项 | 值 | 含义 |
|---|---|---|
| `MOrderXSpeed` | 200 | 开盘抢单频率 200ms |
| `MOrderXStop` | 2200 | 持续时间 2200ms |
| `MaxCancelGZ` | 395 | 股指最大撤单数 |
| `MaxCancelSP` | 10000 | 商品最大撤单数 |
| `MaxCancelQQ` | 10000 | 期权最大撤单数 |
| `MOrderTime1-9` | 09:29:58 等 | 9 个开盘触发时间点 |

## 11. Delphi 线程模型

| 线程类 | 职责 |
|---|---|
| `TCtpJYThread` | 交易线程（CTP TraderApi 回调处理） |
| `TCtpHQThread` | 行情线程（CTP MdApi 回调处理） |
| `TCtpHQExThread` | 行情扩展线程（成交量等扩展处理） |
| `TCtpCXThread` | 查询线程（CX = 查询，串行化查询请求避免流控） |

回调通过 `TThread.Queue` / `TThread.Synchronize` 跨线程传递到主线程更新 UI（从 RTTI 的 `System.Classes.TThreadProcedure` + `InvokeWrapper` 可见）。

## 12. WPF 重构时的接口契约建议

### 12.1 C# 接口定义（P/Invoke YYXX.dll 或重写）

```csharp
// 方案 A: 直接 P/Invoke 现有 YYXX.dll（过渡期）
public static class YyxxNative {
    [DllImport("YYXX.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int StartCptJY(string flowPath, string frontAddr, ...);

    [DllImport("YYXX.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int OrderInsert(ref CThostFtdcInputOrderField order);

    [DllImport("YYXX.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int OrderAction(ref CThostFtdcInputOrderActionField action);

    // ... 其余 12 个导出函数
}
```

### 12.2 重写为 C# 原生 CTP 接口（推荐，长期方案）

直接对接 CTP 官方 C# wrapper（如 `thosttraderapi_se.dll` 的 C# 绑定），跳过 YYXX.dll 封装层：

```csharp
public interface ITradingService {
    Task<LoginResult> LoginAsync(LoginRequest req);
    Task<OrderResult> OrderInsertAsync(OrderRequest req);
    Task<OrderResult> OrderActionAsync(ActionRequest req);
    IObservable<Trade> TradeStream { get; }      // OnRtnTrade
    IObservable<Order> OrderStream { get; }      // OnRtnOrder
    IObservable<MarketData> MarketDataStream { get; } // OnRtnDepthMarketData
}

public interface IQueryService {
    Task<Instrument[]> QueryInstrumentsAsync();
    Task<Order[]> QueryOrdersAsync();
    Task<Trade[]> QueryTradesAsync();
    Task<TradingAccount> QueryTradingAccountAsync();
    Task<Position[]> QueryPositionsAsync();
    Task<PositionDetail[]> QueryPositionDetailsAsync();
}
```

详见 [06-refactor-guide.md](06-refactor-guide.md)。

## 13. 局限

- **YYXX.dll 被 VMProtect 加壳**，导出函数的精确参数签名无法从静态分析 100% 确认。上表参数基于 CTP 6.7.10 标准 API + Delphi RTTI 推断。
- **`NewOrder` 函数**的语义（预设单？条件单？）需运行时验证或反编译确认。
- **认证流程**的内部调用顺序（`ReqAuthenticate` vs `ReqUserLogin`）从日志推断，需结合 CTP 文档确认。
