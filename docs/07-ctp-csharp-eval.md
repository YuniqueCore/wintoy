# 07 - CTP C# 绑定选型评估

> 本文评估 5 个开源 CTP C# 绑定项目，给出 WPF 重构的选型建议。
> 调研日期：2026-07-30。原软件使用 CTP v6.7.10（thosttraderapi_se.dll + thostmduserapi_se.dll，32 位）。

## 1. 结论先行

- **首选方案：自研 P/Invoke 包装器**，直接对接原软件目录中已有的 `thosttraderapi_se.dll` / `thostmduserapi_se.dll`（CTP 6.7.10），不依赖第三方绑定。
- **次选方案：`ggwhsd/CTP-CSharp`**（Apache-2.0，CTP 6.7.9_P1，x86），作为参考实现和快速起步。
- **不推荐：`baok/CTPSharp`**（非商用授权，仅 X64，CTP 6.7.0），授权和版本均不匹配。
- 理由：原软件已自带 CTP 6.7.10 原生 DLL，自研 P/Invoke 包装器可精确匹配版本、无授权风险、完全控制内存/线程模型。工作量约 2-3 天（结构体映射 + SPI 回调桥接）。

## 2. 候选项目对比

| 项目 | CTP 版本 | 平台 | 授权 | 最后更新 | API 风格 | 评估 |
|---|---|---|---|---|---|---|
| **ggwhsd/CTP-CSharp** | 6.7.9_P1 | x86 | Apache-2.0 | 2025-06-17 | SWIG 生成，原生 C++ 命名 | ★★★★ |
| **baok/CTPSharp** | 6.7.0 | x64 | 非商用（需联系作者） | - | Task + event，GBK 处理 | ★★★ |
| **fastquant/ctp.net** | - | Mono/.NET | - | - | 中文文档 | ★★ |
| **slobber/CtpNetCore** | - | .NET Core | - | 2017（已停更） | 基于 hf_ctp_cs_proxy | ★ |
| **openctp/openctp** | 兼容 | 多平台 | - | 2025-06-15 | 模拟环境 + 多柜台网关 | ★★★（仅模拟环境） |

## 3. 详细评估

### 3.1 ggwhsd/CTP-CSharp（首选参考）

- **仓库**：https://github.com/ggwhsd/CTP-CSharp
- **CTP 版本**：6.7.9_P1_20250319（比原软件 6.7.10 低一个小版本）
- **平台**：x86（与原软件一致）
- **授权**：Apache-2.0（商用友好）
- **技术**：SWIG 从 C++ 头文件生成 C# 绑定 + C++ 中间 wrapper DLL
- **输出**：
  - `ctpthostmdapi.dll`（行情 wrapper，改名自 CTPWrapperForCSharp_MD.dll）
  - `ctpthosttradeapi.dll`（交易 wrapper，改名自 CTPWrapperForCSharp.dll）
  - `CTPWrapper.dll`（C# 绑定库）
- **优点**：
  - 接口名与 C++ CTP API 完全一致
  - Apache-2.0 授权，无商用限制
  - 2025-06 仍活跃维护
  - x86 与原软件匹配
- **缺点**：
  - 需要 SWIG 工具链重新生成（若要升级到 6.7.10）
  - 多一层 C++ wrapper DLL，增加部署复杂度
  - Release 中的预编译 DLL 是 6.7.9_P1，非 6.7.10

### 3.2 baok/CTPSharp（次选参考）

- **仓库**：https://github.com/baok/CTPSharp
- **CTP 版本**：6.7.0（比原软件低）
- **平台**：**仅 X64**（与原软件 x86 不匹配）
- **授权**：**非商用**（"此项目代码不得用于任何商业目的，若需要，请和我联系"）
- **技术**：代码生成器从 `.h` 文件生成 P/Invoke + C# 类
- **优点**：
  - Task + event 异步 API，现代 C# 风格
  - GBK 编码处理
  - 内存安全设计
  - 完整的 MdAPI/TdAPI 封装类
- **缺点**：
  - **非商用授权**，用户软件是商用场景，不可直接使用
  - 仅 X64，原软件是 x86
  - CTP 版本较旧（6.7.0）

### 3.3 openctp/openctp（仅模拟环境价值）

- **仓库**：https://github.com/openctp/openctp
- **定位**：CTP 兼容的模拟环境 + 多柜台网关（ctp2TTS 模拟、ctp2XTP 中泰、ctp2STP 华鑫等）
- **对本项目的价值**：
  - 开发期可用 openctp 模拟环境替代 SimNow（更稳定）
  - 不直接提供 C# 绑定
- **建议**：开发期用 openctp 模拟环境测试，生产用真实 CTP

## 4. 自研 P/Invoke 方案（推荐）

### 4.1 理由

1. **版本精确匹配**：原软件已自带 CTP 6.7.10 的 `thosttraderapi_se.dll` + `thostmduserapi_se.dll`，直接 P/Invoke 调用，无需版本妥协。
2. **无授权风险**：CTP API 头文件是上期技术公开发布的，P/Invoke 包装不涉及第三方授权。
3. **完全控制**：内存管理、线程模型、回调路由完全可控，适配 WPF 的 Dispatcher 线程模型。
4. **部署简单**：只需 2 个原生 DLL + 1 个 C# 程序集，无中间 C++ wrapper。
5. **工作量可控**：CTP API 约 100+ 函数，但本项目实际使用 15 个（见 [02-ctp-api.md](02-ctp-api.md)），结构体约 10 个。

### 4.2 实现策略

```
thosttraderapi_se.dll (C++, CTP 6.7.10 原生)
    │ C++ vtable (CreateFtdcTraderApi → CThostFtdcTraderApi*)
    │
    ▼ P/Invoke (ThisCall)
FuturesTrader.Infrastructure.Ctp.Native/
    ├── ThostTraderApiNative.cs    # P/Invoke 声明
    ├── ThostMdApiNative.cs        # P/Invoke 声明
    └── Structs/                   # CTP 结构体 C# 映射
        ├── CThostFtdcInputOrderField.cs
        ├── CThostFtdcOrderField.cs
        └── ...
    │
    ▼ C# 适配层
FuturesTrader.Infrastructure.Ctp/
    ├── CtpTradingService.cs       # ITradingService 实现
    ├── CtpMarketDataService.cs    # IMarketDataService 实现
    └── CtpCallbackRouter.cs       # SPI 回调 → IObservable
```

### 4.3 关键技术点

#### 4.3.1 C++ vtable 的 P/Invoke

CTP API 使用 C++ 虚函数表，不能直接 P/Invoke。需要用 C++/CLI 或委托桥接：

**方案 A：C++/CLI 桥接（推荐）**
```cpp
// CtpBridge.h (C++/CLI)
#pragma once
using namespace System;
using namespace System::Runtime::InteropServices;

namespace CtpBridge {
    public ref class TraderApiWrapper {
    public:
        void Create(String^ flowPath);
        void RegisterFront(String^ frontAddr);
        void Init();
        // ... 将每个 CTP 方法包装为 .NET 方法
    };
}
```

**方案 B：函数指针委托（纯 C#，复杂但无 C++ 依赖）**
```csharp
// 通过 Marshal.GetDelegateForFunctionPointer 调用 vtable 中的方法
// 需要手动计算 vtable 偏移
```

**推荐方案 A**：C++/CLI 桥接层更可靠，且可编译为单一 DLL。工作量约 1-2 天。

#### 4.3.2 SPI 回调路由

CTP 的 SPI 是 C++ 抽象类，需要 C++/CLI 继承并转发到 C#：

```cpp
// CtpCallbackBridge.h (C++/CLI)
class CtpTraderSpiBridge : public CThostFtdcTraderSpi {
private:
    gcroot<CallbackRouter^> _router;
public:
    CtpTraderSpiBridge(CallbackRouter^ router) : _router(router) {}

    void OnRtnOrder(CThostFtdcOrderField *pOrder) override {
        _router->OnRtnOrder(IntPtr(pOrder));
    }
    // ... 其他回调
};
```

```csharp
// C# 侧
public sealed class CtpCallbackRouter {
    private readonly Subject<Order> _orderSubject = new();
    public IObservable<Order> OrderStream => _orderSubject;

    internal void OnRtnOrder(IntPtr orderPtr) {
        var field = Marshal.PtrToStructure<CThostFtdcOrderField>(orderPtr);
        var order = MapToDomain(field);
        _orderSubject.OnNext(order);
    }
}
```

#### 4.3.3 GBK 字符串处理

CTP 使用 GBK 编码的 `char[]`，C# 侧需要转换：

```csharp
public static class CtpStringExtensions {
    private static readonly Encoding Gbk = Encoding.GetEncoding(936);

    public static string ToCString(this byte[] bytes) {
        var nullIdx = Array.IndexOf(bytes, (byte)0);
        var span = nullIdx >= 0 ? bytes.AsSpan(0, nullIdx) : bytes.AsSpan();
        return Gbk.GetString(span);
    }

    public static byte[] ToCBytes(this string str, int length) {
        var bytes = new byte[length];
        var encoded = Gbk.GetBytes(str);
        Array.Copy(encoded, bytes, Math.Min(encoded.Length, length - 1));
        return bytes;
    }
}
```

### 4.4 参考实现

自研时可参考以下项目的结构体定义和 P/Invoke 声明（**仅作参考，不直接复制代码**）：

- `ggwhsd/CTP-CSharp` 的 `CSharpLibraryCS` 目录 — 结构体定义
- `baok/CTPSharp` 的 `generated` 目录 — P/Invoke 声明 + 异步封装模式
- CTP 官方 `ThostFtdcUserApiStruct.h` — 权威结构体定义

## 5. 选型决策矩阵

| 维度 | 自研 P/Invoke | ggwhsd/CTP-CSharp | baok/CTPSharp |
|---|---|---|---|
| CTP 版本匹配 | ★★★★★ (6.7.10) | ★★★★ (6.7.9_P1) | ★★★ (6.7.0) |
| 授权 | ★★★★★ (无限制) | ★★★★★ (Apache-2.0) | ★ (非商用) |
| 平台匹配 | ★★★★★ (x86/x64 可选) | ★★★★★ (x86) | ★★ (仅 x64) |
| 开发工作量 | ★★ (2-3 天) | ★★★★ (1 天集成) | ★★★★ (1 天集成) |
| 维护成本 | ★★★★ (自己控制) | ★★★ (依赖上游) | ★★★ (依赖上游) |
| 代码质量 | ★★★★ (自己控制) | ★★★ (SWIG 生成) | ★★★★ (手写优化) |
| **综合** | **★★★★** | **★★★★** | **★★★** |

## 6. 最终建议

### 6.1 短期（M2-M3 里程碑）

- **采用自研 P/Invoke 方案**
- 从 `ggwhsd/CTP-CSharp` 的结构体定义参考（Apache-2.0 兼容）
- 从 `baok/CTPSharp` 的异步 API 设计模式参考（设计模式不受授权限制）
- 直接使用原软件目录中的 `thosttraderapi_se.dll` + `thostmduserapi_se.dll`

### 6.2 中期（M4+ 里程碑）

- 评估 openctp 模拟环境用于自动化测试
- 关注 CTP API 版本升级（如 6.7.11+）
- 考虑发布自研 P/Invoke 包装器为开源项目（Apache-2.0）

### 6.3 开发期模拟环境

| 环境 | 用途 | 优势 |
|---|---|---|
| **SimNow** (上期技术官方模拟) | 标准测试 | 官方环境，与生产一致 |
| **openctp ctp2TTS** | 开发期高频测试 | 本地部署，无流控，7×24 |
| **单元测试 mock** | CI/CD | 完全离线，可重复 |

## 7. 相关文档

- [01-overview.md](01-overview.md) — 软件总览
- [02-ctp-api.md](02-ctp-api.md) — CTP 交易/行情接口
- [06-refactor-guide.md](06-refactor-guide.md) — WPF 重构建议

## 8. 来源

- [ggwhsd/CTP-CSharp](https://github.com/ggwhsd/CTP-CSharp) — Apache-2.0, CTP 6.7.9_P1, x86
- [baok/CTPSharp](https://github.com/baok/CTPSharp) — 非商用, CTP 6.7.0, x64
- [fastquant/ctp.net](https://github.com/fastquant/ctp.net) — Mono/.NET C# binding
- [slobber/CtpNetCore](https://github.com/slobber/CtpNetCore) — .NET Core (已停更)
- [openctp/openctp](https://github.com/openctp/openctp) — CTP 兼容模拟环境 + 多柜台网关
