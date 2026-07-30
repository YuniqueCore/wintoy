using Xunit;

namespace FuturesTrader.UiAutomationTests.Fixtures;

/// <summary>
/// UI 测试共享集合：所有测试类共享单个 <see cref="HostAppFixture"/>（即一个 Host 进程）。
/// <para>
/// 为什么不用 IClassFixture：xunit 默认并行运行不同测试类，每个类各启一个 Host，
/// 单例守卫会 kill 掉后启动的实例 → <c>Application.Attach</c> 时进程已退出 →
/// <c>GetMainModuleFilepath</c> 返回 null 抛 NullReferenceException。
/// ICollectionFixture 保证全 assembly 只启一次 Host，从根本上消除冲突。
/// </para>
/// <para>
/// <b>副作用约定</b>：测试类之间共享 Host 状态（如已打开的设置窗口）。
/// 每个测试需自行清理前置状态（如关闭设置窗口、恢复主题），避免污染后续测试。
/// </para>
/// </summary>
[CollectionDefinition("Host")]
public sealed class UiTestCollection : ICollectionFixture<HostAppFixture> { }
