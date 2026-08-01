using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FluentAssertions;
using FuturesTrader.UiAutomationTests.Fixtures;

namespace FuturesTrader.UiAutomationTests.Tests;

/// <summary>Mock 会话下的合约窗口实机交互回归：登录、分组布局、白格和价格梯左右键。</summary>
[Collection("Host")]
public class TradingWindowInteractionTests
{
    private readonly HostAppFixture _fixture;

    public TradingWindowInteractionTests(HostAppFixture fixture) => _fixture = fixture;

    [Fact]
    public void Contract_windows_align_and_price_cells_accept_left_and_right_clicks()
    {
        var floating = LoginToFloatingWindow();
        try
        {
            AssertFloatingDefaults(floating);
            AssertRichSearchPopupWithinScreen(floating);
            OpenThreeWindowGroup(floating);

            var windows = WaitForTradingWindows(expectedCount: 3);
            AssertAlignedWithoutOverlap(windows);

            var tradingWindow = SelectMostVisibleWindow(windows);
            SetGroupSync(floating, enabled: false);
            MoveWindowInsidePrimaryScreen(tradingWindow);
            _fixture.EnsureWindowForeground(tradingWindow);
            tradingWindow.SetForeground();
            Thread.Sleep(200);
            AssertSpreadLockInputsAreStacked(tradingWindow);
            tradingWindow.FindFirstDescendant(_fixture.Automation.ConditionFactory.ByAutomationId("PendingOrderHeader"))
                .Should().NotBeNull("价格梯必须明确显示挂单剩余手数列");

            var firstTradeCells = UiTestHelpers.WaitFor(() =>
            {
                var cells = tradingWindow.FindAllDescendants(
                    _fixture.Automation.ConditionFactory.ByName("第一交易列"));
                return cells.Length > 0 ? cells : null;
            },
                TimeSpan.FromSeconds(5));
            var secondTradeCells = UiTestHelpers.WaitFor(() =>
            {
                var cells = tradingWindow.FindAllDescendants(
                    _fixture.Automation.ConditionFactory.ByName("第二交易列"));
                return cells.Length > 0 ? cells : null;
            },
                TimeSpan.FromSeconds(5));
            firstTradeCells.Should().NotBeNull("第一交易列必须以可点击 UIA 元素暴露");
            secondTradeCells.Should().NotBeNull("第二交易列必须以可点击 UIA 元素暴露");
            var middleIndex = Math.Min(firstTradeCells!.Length, secondTradeCells!.Length) / 2;
            var firstTradeCell = firstTradeCells[middleIndex];
            var selectedRowTop = firstTradeCell.BoundingRectangle.Top;

            var leftFeedback = InvokeTradeCellAndWaitForFeedback(
                tradingWindow,
                "第一交易列",
                selectedRowTop,
                rightClick: false,
                previousMessage: string.Empty,
                expectedPendingVolume: "1");
            SaveWindowEvidence(tradingWindow, "FUTURES_UI_CLICK_EVIDENCE_PATH");
            leftFeedback.Should().NotBeNull("左键点价后必须显示提交/回报结果");
            leftFeedback!.Name.Should().NotContain("全部成交", "UI Mock 应保持 Accepted 供挂单/撤单验证");
            var leftPending = WaitForPendingVolume(tradingWindow, "1");
            var visiblePendingNames = string.Join(",", FindPendingOrderCells(tradingWindow).Select(cell => cell.Name));
            leftPending.Should().NotBeNull(
                $"左键 1 手应显示在挂单剩余手数列；反馈={leftFeedback.Name}；当前挂单格={visiblePendingNames}");

            // 左键报单回报会重建 PriceLadder，右键必须重新取得同一行的 UIA 元素。
            var rightFeedback = InvokeTradeCellAndWaitForFeedback(
                tradingWindow,
                "第二交易列",
                selectedRowTop,
                rightClick: true,
                previousMessage: leftFeedback.Name,
                expectedPendingVolume: "3");
            rightFeedback.Should().NotBeNull("右键点价后必须显示新的提交/回报结果");
            var pending = WaitForPendingVolume(tradingWindow, "3");
            var rightPendingNames = string.Join(",", FindPendingOrderCells(tradingWindow).Select(cell => cell.Name));
            pending.Should().NotBeNull(
                $"同价位左右键 1+2 手应聚合为剩余 3 手；反馈={rightFeedback!.Name}；当前挂单格={rightPendingNames}");
            SaveEvidenceScreenshot("FUTURES_UI_PENDING_EVIDENCE_PATH");

            pending!.Click();
            UiTestHelpers.WaitTrue(
                () => !FindPendingOrderCells(tradingWindow).Any(cell =>
                    int.TryParse(cell.Name, out var volume) && volume > 0),
                TimeSpan.FromSeconds(5)).Should().BeTrue("点击挂单数量应按价撤销活动委托");

            AssertWhiteGridCanHideAndRestore(floating, tradingWindow);
            SetGroupSync(floating, enabled: true);
            AssertAnchorMoveRealignsGroup(windows);
            SaveEvidenceScreenshot("FUTURES_UI_EVIDENCE_PATH");
        }
        finally
        {
            Logout(floating);
        }
    }

    private Window LoginToFloatingWindow()
    {
        var login = _fixture.RefreshLoginWindow() ?? throw new Xunit.Sdk.XunitException("登录窗口不存在");
        _fixture.EnsureLoginWindowForeground();
        var password = login.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByAutomationId("PasswordBox"));
        var button = login.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByName("登录"))?.AsButton();
        password.Should().NotBeNull();
        button.Should().NotBeNull();

        password!.AsTextBox().Text = "mock-password";
        UiTestHelpers.WaitTrue(() => button!.IsEnabled, TimeSpan.FromSeconds(5))
            .Should().BeTrue("Mock 登录资料已选择且密码非空");
        button!.Invoke();

        return _fixture.FindWindowByAutomationId("FloatingMainWindow", TimeSpan.FromSeconds(15))
            ?? throw new Xunit.Sdk.XunitException("Mock 登录后未出现浮动工具栏");
    }

    private void AssertFloatingDefaults(Window floating)
    {
        var whiteGrid = floating.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByAutomationId("ShowWhiteGridToggle"))?.AsCheckBox();
        whiteGrid.Should().NotBeNull();
        whiteGrid!.IsChecked.Should().BeTrue("白格默认必须勾选");

        AssertSelectedSegment(floating, "DisplayModeSwitcher", "单");
        AssertSelectedSegment(floating, "OrderModeSwitcher", "仓");
        AssertSelectedSegment(floating, "AbModeSwitcher", "A");
    }

    private void AssertSelectedSegment(Window floating, string switcherId, string selectedName)
    {
        var switcher = floating.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByAutomationId(switcherId));
        switcher.Should().NotBeNull($"应找到 {switcherId}");
        var selected = switcher!.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByName(selectedName))?.AsToggleButton();
        selected.Should().NotBeNull($"{switcherId} 应包含 {selectedName}");
        selected!.ToggleState.Should().Be(ToggleState.On, $"{selectedName} 应是默认选中项");
    }

    private void AssertRichSearchPopupWithinScreen(Window floating)
    {
        var searchBox = floating.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByAutomationId("InstrumentSearchBox"))?.AsTextBox();
        searchBox.Should().NotBeNull("浮动栏应暴露合约 autocomplete 输入框");
        searchBox!.Text = "au26";

        var results = UiTestHelpers.WaitFor(() =>
            _fixture.Automation.GetDesktop().FindFirstDescendant(
                _fixture.Automation.ConditionFactory.ByAutomationId("InstrumentSearchResults")),
            TimeSpan.FromSeconds(5));
        results.Should().NotBeNull("输入合约代码前缀后应显示候选表");

        var names = results!.FindAllDescendants(
            _fixture.Automation.ConditionFactory.ByAutomationId("InstrumentResultName"));
        var codes = results.FindAllDescendants(
            _fixture.Automation.ConditionFactory.ByAutomationId("InstrumentResultCode"));
        var details = results.FindAllDescendants(
            _fixture.Automation.ConditionFactory.ByAutomationId("InstrumentResultDetails"));
        var limits = results.FindAllDescendants(
            _fixture.Automation.ConditionFactory.ByAutomationId("InstrumentResultLimits"));
        names.Select(element => element.Name).Should().Contain("黄金2610");
        codes.Select(element => element.Name).Should().Contain("au2610");
        details.Select(element => element.Name).Should().Contain(detail =>
            detail.Contains("SHFE", StringComparison.Ordinal)
            && detail.Contains("0.02", StringComparison.Ordinal)
            && detail.Contains("1000", StringComparison.Ordinal));
        limits.Select(element => element.Name).Should().Contain(limit =>
            limit.Contains("1", StringComparison.Ordinal)
            && limit.Contains("1000", StringComparison.Ordinal));

        using var capture = Capture.MainScreen();
        var screen = capture.OriginalBounds;
        var popupBounds = results.BoundingRectangle;
        popupBounds.Left.Should().BeGreaterThanOrEqualTo(screen.Left - 2);
        popupBounds.Top.Should().BeGreaterThanOrEqualTo(screen.Top - 2);
        popupBounds.Right.Should().BeLessThanOrEqualTo(screen.Right + 2);
        popupBounds.Bottom.Should().BeLessThanOrEqualTo(screen.Bottom + 2);
        SaveEvidenceScreenshot("FUTURES_UI_SEARCH_EVIDENCE_PATH");
        SaveElementEvidence(results, "FUTURES_UI_SEARCH_RESULTS_EVIDENCE_PATH");

        searchBox.Text = string.Empty;
    }

    private void OpenThreeWindowGroup(Window floating)
    {
        _fixture.EnsureWindowForeground(floating);
        var groupButton = UiTestHelpers.WaitFor(() =>
            floating.FindFirstDescendant(
                _fixture.Automation.ConditionFactory.ByName("2")
                    .And(_fixture.Automation.ConditionFactory.ByControlType(ControlType.Button)))?.AsButton(),
            TimeSpan.FromSeconds(5));
        groupButton.Should().NotBeNull("测试配置的第 2 组应包含三个合约窗口");
        groupButton!.IsEnabled.Should().BeTrue();
        groupButton.Invoke();
    }

    private Window[] WaitForTradingWindows(int expectedCount) =>
        UiTestHelpers.WaitFor(() =>
        {
            try
            {
                var condition = _fixture.Automation.ConditionFactory
                    .ByAutomationId("TradingWindow")
                    .And(_fixture.Automation.ConditionFactory.ByControlType(ControlType.Window));
                var windows = _fixture.Automation.GetDesktop()
                    .FindAllChildren(condition)
                    .Select(element => element.AsWindow())
                    .Where(window => window.IsAvailable)
                    .OrderBy(window => window.BoundingRectangle.Left)
                    .ToArray();
                return windows.Length == expectedCount ? windows : null;
            }
            catch
            {
                return null;
            }
        }, TimeSpan.FromSeconds(15))
        ?? throw new Xunit.Sdk.XunitException($"未出现 {expectedCount} 个合约窗口");

    private Window SelectMostVisibleWindow(IReadOnlyList<Window> windows)
    {
        using var capture = Capture.MainScreen();
        var screen = capture.OriginalBounds;
        var screenCenter = screen.Left + screen.Width / 2d;
        return windows.MinBy(window =>
            Math.Abs(window.BoundingRectangle.Left + window.BoundingRectangle.Width / 2d - screenCenter))!;
    }

    private void AssertSpreadLockInputsAreStacked(Window tradingWindow)
    {
        var instrument = tradingWindow.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByAutomationId("CounterpartySpreadInstrument"));
        var point = tradingWindow.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByAutomationId("CounterpartySpreadPoint"));
        var factor = tradingWindow.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByAutomationId("CounterpartySpreadFactor"));
        instrument.Should().NotBeNull("价差锁定应显示对手合约输入框");
        point.Should().NotBeNull("价差锁定应显示 Pt 输入框");
        factor.Should().NotBeNull("价差锁定应显示 Fctr 输入框");

        var bounds = new[] { instrument!.BoundingRectangle, point!.BoundingRectangle, factor!.BoundingRectangle };
        bounds[0].Top.Should().BeLessThan(bounds[1].Top);
        bounds[1].Top.Should().BeLessThan(bounds[2].Top);
        bounds.Should().OnlyContain(rectangle => rectangle.Width >= 80,
            "三项输入应占满窄栏剩余宽度，而不是挤成不可读的小方块");
        (bounds.Max(rectangle => rectangle.Left) - bounds.Min(rectangle => rectangle.Left))
            .Should().BeLessThanOrEqualTo(2, "合约、Pt、Fctr 三行输入应左侧对齐");
    }

    private void MoveWindowInsidePrimaryScreen(Window window)
    {
        using var capture = Capture.MainScreen();
        var screen = capture.OriginalBounds;
        window.Move(screen.Left + 80, screen.Top + 80);
        Thread.Sleep(300);
    }

    private void SetGroupSync(Window floating, bool enabled)
    {
        _fixture.EnsureWindowForeground(floating);
        var root = floating.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByAutomationId("SyncLinkToggle"));
        var toggle = root?.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByAutomationId("InnerToggle"))?.AsToggleButton();
        toggle.Should().NotBeNull("浮动栏应暴露同步状态开关");
        var expected = enabled ? ToggleState.On : ToggleState.Off;
        if (toggle!.ToggleState != expected)
            toggle.Toggle();
        UiTestHelpers.WaitTrue(() => toggle.ToggleState == expected, TimeSpan.FromSeconds(3))
            .Should().BeTrue("同步 switcher 点击后不应被重复 Command 翻转回原状态");
    }

    private static void AssertAlignedWithoutOverlap(IReadOnlyList<Window> windows)
    {
        var top = windows[0].BoundingRectangle.Top;
        windows.Should().OnlyContain(
            window => Math.Abs(window.BoundingRectangle.Top - top) <= 2,
            "同组窗口 Top 应在同一水平线上");
        for (var index = 1; index < windows.Count; index++)
        {
            windows[index].BoundingRectangle.Left.Should().BeGreaterThanOrEqualTo(
                windows[index - 1].BoundingRectangle.Right - 1,
                "同组窗口不允许水平重叠");
        }
    }

    private AutomationElement? WaitForVisibleOrderFeedback(Window window) =>
        UiTestHelpers.WaitFor(() =>
        {
            var status = window.FindFirstDescendant(
                _fixture.Automation.ConditionFactory.ByAutomationId("PriceInteractionStatus"));
            return status is not null && !string.IsNullOrWhiteSpace(status.Name) ? status : null;
        }, TimeSpan.FromSeconds(5));

    private AutomationElement? WaitForChangedOrderFeedback(Window window, string previousMessage) =>
        WaitForChangedOrderFeedback(window, previousMessage, TimeSpan.FromSeconds(5));

    private AutomationElement? WaitForChangedOrderFeedback(
        Window window,
        string previousMessage,
        TimeSpan timeout) =>
        UiTestHelpers.WaitFor(() =>
        {
            var status = window.FindFirstDescendant(
                _fixture.Automation.ConditionFactory.ByAutomationId("PriceInteractionStatus"));
            return status is not null
                   && !string.IsNullOrWhiteSpace(status.Name)
                   && status.Name != previousMessage
                ? status
                : null;
        }, timeout);

    private AutomationElement? InvokeTradeCellAndWaitForFeedback(
        Window window,
        string columnName,
        double rowTop,
        bool rightClick,
        string previousMessage,
        string expectedPendingVolume)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var cell = WaitForTradeCellAtRow(window, columnName, rowTop);
            if (cell is null) continue;
            if (rightClick) cell.RightClick();
            else cell.AsButton().Invoke();

            var feedback = WaitForChangedOrderFeedback(window, previousMessage, TimeSpan.FromSeconds(2));
            if (feedback is not null) return feedback;
            if (WaitForPendingVolume(window, expectedPendingVolume, TimeSpan.FromSeconds(1)) is not null)
                return WaitForVisibleOrderFeedback(window);
        }
        return null;
    }

    private AutomationElement? WaitForTradeCellAtRow(Window window, string columnName, double rowTop) =>
        UiTestHelpers.WaitFor(() =>
        {
            var nearest = window.FindAllDescendants(
                    _fixture.Automation.ConditionFactory.ByName(columnName))
                .MinBy(cell => Math.Abs(cell.BoundingRectangle.Top - rowTop));
            return nearest is not null && Math.Abs(nearest.BoundingRectangle.Top - rowTop) <= 2
                ? nearest
                : null;
        }, TimeSpan.FromSeconds(5));

    private AutomationElement? WaitForPendingVolume(Window window, string expectedVolume) =>
        WaitForPendingVolume(window, expectedVolume, TimeSpan.FromSeconds(5));

    private AutomationElement? WaitForPendingVolume(
        Window window,
        string expectedVolume,
        TimeSpan timeout) =>
        UiTestHelpers.WaitFor(() =>
        {
            return FindPendingOrderCells(window)
                .FirstOrDefault(pending => pending.Name == expectedVolume);
        }, timeout);

    private AutomationElement[] FindPendingOrderCells(Window window) =>
        window.FindAllDescendants(
            _fixture.Automation.ConditionFactory.ByAutomationId("PendingOrderCell"));

    private void AssertWhiteGridCanHideAndRestore(Window floating, Window tradingWindow)
    {
        var condition = _fixture.Automation.ConditionFactory.ByName("第一交易列");
        var visibleCount = tradingWindow.FindAllDescendants(condition).Length;
        visibleCount.Should().BeGreaterThan(10, "白格开启时应包含买卖报价区之间的可点击无人报价行");

        _fixture.EnsureWindowForeground(floating);
        var toggle = floating.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByAutomationId("ShowWhiteGridToggle"))?.AsCheckBox();
        toggle.Should().NotBeNull();
        toggle!.Toggle();
        UiTestHelpers.WaitTrue(
            () => tradingWindow.FindAllDescendants(condition).Length < visibleCount,
            TimeSpan.FromSeconds(5)).Should().BeTrue("取消白格后无人报价行应隐藏");

        toggle.Toggle();
        UiTestHelpers.WaitTrue(
            () => tradingWindow.FindAllDescendants(condition).Length == visibleCount,
            TimeSpan.FromSeconds(5)).Should().BeTrue("重新勾选白格后无人报价行应恢复");
    }

    private void AssertAnchorMoveRealignsGroup(IReadOnlyList<Window> originalWindows)
    {
        var anchor = originalWindows[1];
        var before = anchor.BoundingRectangle;
        anchor.Move((int)before.Left + 30, (int)before.Top + 50);

        UiTestHelpers.WaitTrue(() =>
        {
            var windows = WaitForTradingWindows(originalWindows.Count);
            var top = windows[0].BoundingRectangle.Top;
            return windows.All(window => Math.Abs(window.BoundingRectangle.Top - top) <= 2);
        }, TimeSpan.FromSeconds(5)).Should().BeTrue("拖动锚点后整组 Top 应重新对齐");

        AssertAlignedWithoutOverlap(WaitForTradingWindows(originalWindows.Count));
    }

    private static void SaveEvidenceScreenshot(string environmentVariable)
    {
        var path = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var capture = Capture.MainScreen();
        capture.ToFile(path);
    }

    private static void SaveWindowEvidence(Window window, string environmentVariable)
    {
        var path = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        window.CaptureToFile(path);
    }

    private static void SaveElementEvidence(AutomationElement element, string environmentVariable)
    {
        var path = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        element.CaptureToFile(path);
    }

    private void Logout(Window floating)
    {
        try
        {
            _fixture.EnsureWindowForeground(floating);
            floating.FindFirstDescendant(
                _fixture.Automation.ConditionFactory.ByAutomationId("LogoutButton"))?.AsButton().Invoke();
            UiTestHelpers.WaitFor(_fixture.RefreshLoginWindow, TimeSpan.FromSeconds(10));
        }
        catch
        {
            // Fixture 最终会终止 Host；清理失败不覆盖主断言。
        }
    }
}
