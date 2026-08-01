using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FluentAssertions;
using FuturesTrader.UiAutomationTests.Fixtures;

namespace FuturesTrader.UiAutomationTests.Tests;

/// <summary>Mock 会话下的合约窗口实机交互回归：登录、分组布局、白格和价格梯左右键。</summary>
[Collection("Host")]
public class TradingWindowInteractionTests
{
    private readonly HostAppFixture _fixture;
    private bool _physicalRightClickAvailable = true;

    public TradingWindowInteractionTests(HostAppFixture fixture) => _fixture = fixture;

    [Fact]
    public void Contract_windows_align_and_price_cells_accept_left_and_right_clicks()
    {
        var floating = LoginToFloatingWindow();
        try
        {
            AssertFloatingDefaults(floating);
            AssertRichSearchPopupWithinScreen(floating);
            AssertGroupLifecycle(floating);

            var windows = WaitForTradingWindows(minimumCount: 3);
            AssertAlignedWithoutOverlap(windows);
            AssertContractTitles(windows);
            AssertAbModeBroadcast(floating, windows);

            var tradingWindow = SelectMostVisibleWindow(windows);
            SetGroupSync(floating, enabled: false);
            MoveWindowInsidePrimaryScreen(tradingWindow);
            _fixture.EnsureWindowForeground(tradingWindow);
            tradingWindow.SetForeground();
            Thread.Sleep(200);
            AssertSpreadLockInputsAreStacked(tradingWindow);
            AssertNarrowEditorValuesAreVisible(tradingWindow);
            AssertQuoteRowCountSteppers(tradingWindow);
            AssertPriceRowHeightStepper(tradingWindow);
            AssertFooterShowsHintWithoutStatusOverlap(tradingWindow);
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
            if (_physicalRightClickAvailable)
            {
                rightFeedback.Should().NotBeNull("右键点价后必须显示新的提交/回报结果");
                var pending = WaitForPendingTotal(tradingWindow, expectedTotal: 3);
                var rightPendingNames = string.Join(",", FindPendingOrderCells(tradingWindow).Select(cell => cell.Name));
                pending.Should().NotBeNull(
                    $"左右键 1+2 手应在挂单列显示合计 3 手；反馈={rightFeedback!.Name}；当前挂单格={rightPendingNames}");
            }
            else
            {
                // 当前 Windows 自动化桌面由 UIPI 禁止 SendInput；右键业务分支由无桌面单元测试覆盖。
                leftPending.Should().NotBeNull();
            }
            SaveEvidenceScreenshot("FUTURES_UI_PENDING_EVIDENCE_PATH");

            _fixture.EnsureWindowForeground(tradingWindow);
            PressWindowKey(tradingWindow, VirtualKeyShort.SPACE);
            UiTestHelpers.WaitTrue(
                () => !FindPendingOrderCells(tradingWindow).Any(cell =>
                    int.TryParse(cell.Name, out var volume) && volume > 0),
                TimeSpan.FromSeconds(5)).Should().BeTrue("Space 应向所有可见合约窗口提交选择性全撤请求");

            AssertForceCancelShortcut(tradingWindow, selectedRowTop);
            AssertOnlyOpenShortcut(tradingWindow);

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
        AssertSelectedSegment(floating, "AbModeSwitcher", "B");
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

    private void AssertGroupLifecycle(Window floating)
    {
        _fixture.EnsureWindowForeground(floating);
        FindGroupButton(floating, "3").Invoke();
        _ = WaitForTradingWindows(1);
        Thread.Sleep(600);
        var group3 = FindTradingWindows();
        group3.Should().NotBeEmpty("第 3 组至少应恢复一个已配置窗口");
        var group3Handles = group3.Select(window => window.FrameworkAutomationElement.NativeWindowHandle.Value).ToHashSet();

        FindGroupButton(floating, "2").Invoke();
        _ = WaitForTradingWindows(1);
        Thread.Sleep(600);
        var group2 = FindTradingWindows();
        group2.Should().NotBeEmpty("第 2 组至少应恢复一个已配置窗口");
        var group2Handles = group2.Select(window => window.FrameworkAutomationElement.NativeWindowHandle.Value).ToHashSet();
        group2Handles.Overlaps(group3Handles).Should().BeFalse("第 2 组和第 3 组是不同的三个合约窗口实例");

        floating.FindFirstDescendant(
                _fixture.Automation.ConditionFactory.ByAutomationId("WithdrawCurrentGroupButton"))
            ?.AsButton().Invoke();
        UiTestHelpers.WaitTrue(() => FindTradingWindows().Length == 0, TimeSpan.FromSeconds(5))
            .Should().BeTrue("撤组应隐藏当前显示组的全部合约窗口");

        floating.FindFirstDescendant(
                _fixture.Automation.ConditionFactory.ByAutomationId("SwitchPreviousGroupButton"))
            ?.AsButton().Invoke();
        UiTestHelpers.WaitFor(() =>
        {
            var windows = FindTradingWindows();
            return windows.Length == group3Handles.Count
                   && windows.Select(window => window.FrameworkAutomationElement.NativeWindowHandle.Value)
                       .ToHashSet().SetEquals(group3Handles)
                ? windows
                : null;
        }, TimeSpan.FromSeconds(5)).Should().NotBeNull(
            "切组应恢复前组的原窗口实例，而不是关闭后重建");

        floating.FindFirstDescendant(
                _fixture.Automation.ConditionFactory.ByAutomationId("SwitchPreviousGroupButton"))
            ?.AsButton().Invoke();
        UiTestHelpers.WaitFor(() =>
        {
            var windows = FindTradingWindows();
            return windows.Length == group2Handles.Count
                   && windows.Select(window => window.FrameworkAutomationElement.NativeWindowHandle.Value)
                       .ToHashSet().SetEquals(group2Handles)
                ? windows
                : null;
        }, TimeSpan.FromSeconds(5)).Should().NotBeNull("连续切组应在当前组和前组之间往返");
    }

    private Button FindGroupButton(Window floating, string groupId)
    {
        var button = UiTestHelpers.WaitFor(() =>
            floating.FindFirstDescendant(
                _fixture.Automation.ConditionFactory.ByName(groupId)
                    .And(_fixture.Automation.ConditionFactory.ByControlType(ControlType.Button)))?.AsButton(),
            TimeSpan.FromSeconds(5));
        button.Should().NotBeNull($"测试配置的第 {groupId} 组应包含合约窗口");
        button!.IsEnabled.Should().BeTrue();
        return button;
    }

    private Window[] FindTradingWindows()
    {
        try
        {
            var condition = _fixture.Automation.ConditionFactory
                .ByAutomationId("TradingWindow")
                .And(_fixture.Automation.ConditionFactory.ByControlType(ControlType.Window));
            return _fixture.Automation.GetDesktop()
                .FindAllChildren(condition)
                .Select(element => element.AsWindow())
                .Where(window => window.IsAvailable)
                .OrderBy(window => window.BoundingRectangle.Left)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private Window[] WaitForTradingWindows(int minimumCount) =>
        UiTestHelpers.WaitFor(() =>
        {
            var windows = FindTradingWindows();
            return windows.Length >= minimumCount ? windows : null;
        }, TimeSpan.FromSeconds(15))
        ?? throw new Xunit.Sdk.XunitException($"未出现至少 {minimumCount} 个合约窗口");

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

    private void AssertContractTitles(IReadOnlyList<Window> windows)
    {
        windows.Should().OnlyContain(window => window.Title.Contains(" - ", StringComparison.Ordinal),
            "合约窗口标题应为“名称 - 代码”，不能再显示组号占位标题");
        windows.Where(window => window.Title.Contains("-P-", StringComparison.OrdinalIgnoreCase))
            .Should().OnlyContain(window => window.Title.Contains('[', StringComparison.Ordinal)
                                          && window.Title.Contains("天 ", StringComparison.Ordinal),
                "期权标题应追加剩余天数和到期月日");
        foreach (var window in windows)
        {
            var internalTitle = window.FindFirstDescendant(
                _fixture.Automation.ConditionFactory.ByAutomationId("ContractTitleText"));
            internalTitle.Should().NotBeNull("合约窗口内部顶部 TitleBar 必须显式呈现动态合约名称");
            internalTitle!.Name.Should().Be(window.Title,
                "内部顶部 TitleBar 与 Windows 任务栏/系统窗口名必须来自同一个合约标题");
        }
    }

    private void AssertNarrowEditorValuesAreVisible(Window tradingWindow)
    {
        var numberBoxIds = new[]
        {
            "LeftOrderQuantity",
            "RightOrderQuantity",
            "AskQuoteRowCountStep",
            "BidQuoteRowCountStep",
            "PriceRowHeightStep",
            "CounterpartySpreadPoint",
            "CounterpartySpreadFactor"
        };
        foreach (var automationId in numberBoxIds)
        {
            var numberBox = tradingWindow.FindFirstDescendant(
                _fixture.Automation.ConditionFactory.ByAutomationId(automationId));
            numberBox.Should().NotBeNull($"数值控件 {automationId} 必须存在");
            numberBox!.BoundingRectangle.Width.Should().BeGreaterThanOrEqualTo(80,
                $"数值控件 {automationId} 必须给输入值保留足够宽度");
            var editor = numberBox.ControlType == ControlType.Edit
                ? numberBox
                : numberBox.FindFirstDescendant(
                    _fixture.Automation.ConditionFactory.ByControlType(ControlType.Edit));
            editor.Should().NotBeNull($"数值控件 {automationId} 必须暴露可编辑文本区");
            editor!.BoundingRectangle.Width.Should().BeGreaterThanOrEqualTo(36,
                $"数值控件 {automationId} 的 spinner 不能挤掉文本区");
            editor.AsTextBox().Text.Should().NotBeNullOrWhiteSpace(
                $"数值控件 {automationId} 必须实际显示当前值");
        }
    }

    private void AssertQuoteRowCountSteppers(Window tradingWindow)
    {
        var askStepper = tradingWindow.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByAutomationId("AskQuoteRowCountStep"));
        var bidStepper = tradingWindow.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByAutomationId("BidQuoteRowCountStep"));
        askStepper.Should().NotBeNull();
        bidStepper.Should().NotBeNull();
        var askEditor = (askStepper!.ControlType == ControlType.Edit
            ? askStepper
            : askStepper.FindFirstDescendant(_fixture.Automation.ConditionFactory.ByControlType(ControlType.Edit)))!;
        var bidEditor = (bidStepper!.ControlType == ControlType.Edit
            ? bidStepper
            : bidStepper.FindFirstDescendant(_fixture.Automation.ConditionFactory.ByControlType(ControlType.Edit)))!;
        askEditor.AsTextBox().Text.Should().Be("30");
        bidEditor.AsTextBox().Text.Should().Be("30");

        var automatic = tradingWindow.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByAutomationId("AutomaticWhiteGridCount"));
        automatic.Should().NotBeNull();
        automatic!.Name.Should().MatchRegex("自动 [1-9][0-9]* 格 · 共 [0-9]+ 格",
            "白格数量和价格梯实际总行数应直接展示，不能依赖虚拟化容器猜测");
        var beforeTotal = ParseTotalRows(automatic.Name);
        beforeTotal.Should().BeGreaterThan(60, "默认应包含空 30、多 30 以及自动计算的白格");

        askEditor.Focus();
        askEditor.AsTextBox().Text = "35";
        bidEditor.Focus(); // NumberBox 在失焦时提交文本到 Value 绑定
        UiTestHelpers.WaitTrue(
            () => ParseTotalRows(automatic.Name) == beforeTotal + 5,
            TimeSpan.FromSeconds(5)).Should().BeTrue("空区改为 35 后应立即多显示 5 个可点击格子");
        askEditor.Focus();
        askEditor.AsTextBox().Text = "30";
        bidEditor.Focus();
        UiTestHelpers.WaitTrue(
            () => ParseTotalRows(automatic.Name) == beforeTotal,
            TimeSpan.FromSeconds(5)).Should().BeTrue("测试结束恢复空区默认值，避免污染窗口配置");
    }

    private static int ParseTotalRows(string summary)
    {
        var match = System.Text.RegularExpressions.Regex.Match(summary, @"共\s+(\d+)\s+格");
        return match.Success && int.TryParse(match.Groups[1].Value, out var count) ? count : -1;
    }

    private void AssertAbModeBroadcast(Window floating, IReadOnlyList<Window> windows)
    {
        windows.All(window =>
            window.FindFirstDescendant(_fixture.Automation.ConditionFactory.ByAutomationId("ContractAbModeB"))
                ?.AsRadioButton().IsChecked == true).Should().BeTrue(
            "Users.xml、合约窗口和浮动栏默认都应是 B 模式");

        var switcher = floating.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByAutomationId("AbModeSwitcher"));
        var a = switcher!.FindFirstDescendant(_fixture.Automation.ConditionFactory.ByName("A"))!.AsToggleButton();
        _fixture.EnsureWindowForeground(floating);
        a.Focus();
        PressWindowKey(floating, VirtualKeyShort.SPACE);
        UiTestHelpers.WaitTrue(() => windows.All(window =>
                window.FindFirstDescendant(_fixture.Automation.ConditionFactory.ByAutomationId("ContractAbModeA"))
                    ?.AsRadioButton().IsChecked == true),
            TimeSpan.FromSeconds(5)).Should().BeTrue("浮动栏切到 A 应广播给全部已创建合约窗口");

        var b = switcher.FindFirstDescendant(_fixture.Automation.ConditionFactory.ByName("B"))!.AsToggleButton();
        b.Focus();
        PressWindowKey(floating, VirtualKeyShort.SPACE);
        UiTestHelpers.WaitTrue(() => windows.All(window =>
                window.FindFirstDescendant(_fixture.Automation.ConditionFactory.ByAutomationId("ContractAbModeB"))
                    ?.AsRadioButton().IsChecked == true),
            TimeSpan.FromSeconds(5)).Should().BeTrue("切回 B 后全部合约窗口应恢复 B");
    }

    private void AssertPriceRowHeightStepper(Window tradingWindow)
    {
        var condition = _fixture.Automation.ConditionFactory.ByName("第一交易列");
        var before = tradingWindow.FindAllDescendants(condition).First().BoundingRectangle.Height;
        var stepper = tradingWindow.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByAutomationId("PriceRowHeightStep"));
        stepper.Should().NotBeNull("合约窗口左栏必须提供价格梯格高 stepper");
        var editor = stepper!.ControlType == ControlType.Edit
            ? stepper
            : stepper.FindFirstDescendant(
                _fixture.Automation.ConditionFactory.ByControlType(ControlType.Edit));
        editor.Should().NotBeNull("格高 stepper 必须暴露可聚焦的数值编辑区");
        var textBox = editor!.AsTextBox();
        var commitRoot = tradingWindow.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByAutomationId("AskQuoteRowCountStep"));
        var commitTarget = commitRoot?.ControlType == ControlType.Edit
            ? commitRoot
            : commitRoot?.FindFirstDescendant(
                _fixture.Automation.ConditionFactory.ByControlType(ControlType.Edit));
        var originalValue = textBox.Text;
        editor.Focus();
        textBox.Text = "18";
        commitTarget?.Focus();
        UiTestHelpers.WaitTrue(
            () => tradingWindow.FindAllDescendants(condition).First().BoundingRectangle.Height >= 17,
            TimeSpan.FromSeconds(5)).Should().BeTrue("把格高编辑为 18 后，每个价格格子高度应同步变化");
        editor.Focus();
        textBox.Text = originalValue;
        commitTarget?.Focus();
        UiTestHelpers.WaitTrue(
            () => tradingWindow.FindAllDescendants(condition).First().BoundingRectangle.Height == before,
            TimeSpan.FromSeconds(5)).Should().BeTrue("测试结束应恢复原格高，避免污染持久化窗口配置");
    }

    private void AssertFooterShowsHintWithoutStatusOverlap(Window tradingWindow)
    {
        var hint = tradingWindow.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByAutomationId("PriceInteractionHint"));
        hint.Should().NotBeNull("无下单状态时底部应显示操作 hint");
        var status = tradingWindow.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByAutomationId("PriceInteractionStatus"));
        if (status is not null)
            status.BoundingRectangle.Height.Should().Be(0, "状态为空时状态文字必须折叠，不能与 hint 重叠");
    }

    private void AssertForceCancelShortcut(Window tradingWindow, double selectedRowTop)
    {
        var previous = tradingWindow.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByAutomationId("PriceInteractionStatus"))?.Name ?? string.Empty;
        _ = InvokeTradeCellAndWaitForFeedback(
            tradingWindow, "第一交易列", selectedRowTop, rightClick: false,
            previousMessage: previous, expectedPendingVolume: "1");
        WaitForPendingVolume(tradingWindow, "1").Should().NotBeNull("测试 W 前应存在一笔活动挂单");

        _fixture.EnsureWindowForeground(tradingWindow);
        PressWindowKey(tradingWindow, VirtualKeyShort.KEY_W);
        UiTestHelpers.WaitTrue(
            () => !FindPendingOrderCells(tradingWindow).Any(cell =>
                int.TryParse(cell.Name, out var volume) && volume > 0),
            TimeSpan.FromSeconds(5)).Should().BeTrue("W 应执行强制全撤");
    }

    private void AssertOnlyOpenShortcut(Window tradingWindow)
    {
        var onlyOpen = tradingWindow.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByName("OnlyOpen（开仓）"))?.AsCheckBox();
        onlyOpen.Should().NotBeNull("合约窗口应暴露 OnlyOpen 开关");
        var before = onlyOpen!.IsChecked;
        _fixture.EnsureWindowForeground(tradingWindow);
        PressWindowKey(tradingWindow, VirtualKeyShort.KEY_F);
        UiTestHelpers.WaitTrue(() => onlyOpen.IsChecked != before, TimeSpan.FromSeconds(3))
            .Should().BeTrue("F 应切换当前合约窗口 OnlyOpen");
        PressWindowKey(tradingWindow, VirtualKeyShort.KEY_F);
        UiTestHelpers.WaitTrue(() => onlyOpen.IsChecked == before, TimeSpan.FromSeconds(3))
            .Should().BeTrue("再次按 F 应恢复原状态，避免污染窗口配置");
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
            if (rightClick)
            {
                if (!TryRightClickWindowElement(cell)) return null;
            }
            else
            {
                cell.AsButton().Invoke();
            }

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

    private AutomationElement[]? WaitForPendingTotal(Window window, int expectedTotal) =>
        UiTestHelpers.WaitFor(() =>
        {
            var cells = FindPendingOrderCells(window);
            var total = cells.Sum(cell => int.TryParse(cell.Name, out var volume) ? volume : 0);
            return total == expectedTotal ? cells : null;
        }, TimeSpan.FromSeconds(5));

    private void AssertWhiteGridCanHideAndRestore(Window floating, Window tradingWindow)
    {
        var condition = _fixture.Automation.ConditionFactory.ByName("第一交易列");
        var visibleCount = tradingWindow.FindAllDescendants(condition).Length;
        visibleCount.Should().BeGreaterThan(60, "默认空/多各 30 格时应暴露 60 格加自动白格");

        _fixture.EnsureWindowForeground(floating);
        var toggle = floating.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByAutomationId("ShowWhiteGridToggle"))?.AsCheckBox();
        toggle.Should().NotBeNull();
        toggle!.Toggle();
        UiTestHelpers.WaitTrue(
            () => tradingWindow.FindAllDescendants(condition).Length < visibleCount,
            TimeSpan.FromSeconds(5)).Should().BeTrue("取消白格后无人报价行应隐藏");
        var quotedOnlyCount = tradingWindow.FindAllDescendants(condition).Length;
        (visibleCount - quotedOnlyCount).Should().BeGreaterThan(1,
            "Mock 买一/卖一价差应稳定产生多行白格，不能再只有一行");

        toggle.Toggle();
        UiTestHelpers.WaitTrue(
            () => tradingWindow.FindAllDescendants(condition).Length == visibleCount,
            TimeSpan.FromSeconds(5)).Should().BeTrue("重新勾选白格后无人报价行应恢复");
    }

    private void AssertAnchorMoveRealignsGroup(IReadOnlyList<Window> originalWindows)
    {
        using var capture = Capture.MainScreen();
        var screen = capture.OriginalBounds;
        var anchor = originalWindows[^1];
        var before = anchor.BoundingRectangle;
        anchor.Move((int)screen.Left + 20, (int)before.Top + 50);

        UiTestHelpers.WaitTrue(() =>
        {
            var windows = WaitForTradingWindows(originalWindows.Count);
            var top = windows[0].BoundingRectangle.Top;
            return windows.All(window => Math.Abs(window.BoundingRectangle.Top - top) <= 2)
                   && windows.Take(windows.Length - 1)
                       .All(window => window.BoundingRectangle.Left < screen.Left);
        }, TimeSpan.FromSeconds(5)).Should().BeTrue(
            "拖动最右侧锚点到屏幕左缘后，前面的组窗口应允许移出屏幕且整组 Top 对齐");

        var movedWindows = WaitForTradingWindows(originalWindows.Count);
        Math.Abs(movedWindows[^1].BoundingRectangle.Left - (screen.Left + 20)).Should().BeLessThanOrEqualTo(2,
            "锚点最终横坐标不能被整组工作区钳制改写");
        AssertAlignedWithoutOverlap(movedWindows);
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

    private static void PressWindowKey(Window window, VirtualKeyShort key)
    {
        try
        {
            Keyboard.Press(key);
            return;
        }
        catch (Win32Exception)
        {
            // 某些自动化桌面禁止 SendInput；PostMessage 仍走目标窗口真实 KeyDown 路由。
        }

        var handle = new IntPtr(window.FrameworkAutomationElement.NativeWindowHandle.Value);
        PostMessage(handle, WmKeyDown, new IntPtr((int)key), IntPtr.Zero).Should().BeTrue();
        PostMessage(handle, WmKeyUp, new IntPtr((int)key), IntPtr.Zero).Should().BeTrue();
        Thread.Sleep(80);
    }

    private bool TryRightClickWindowElement(AutomationElement element)
    {
        try
        {
            element.RightClick();
            return true;
        }
        catch (Win32Exception)
        {
            _physicalRightClickAvailable = false;
            return false;
        }
    }

    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
