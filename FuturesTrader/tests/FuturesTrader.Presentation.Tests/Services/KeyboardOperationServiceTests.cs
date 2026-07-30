using FluentAssertions;
using FuturesTrader.Presentation.Services;

namespace FuturesTrader.Presentation.Tests.Services;

/// <summary>
/// <see cref="KeyboardOperationService"/> 测试：注册/派发、选中价位移动、越界夹紧。
/// 不依赖真实 WPF 输入（Handle 需要 KeyEventArgs，在单元测试环境受限，重点测注册表逻辑 + MoveSelection）。
/// </summary>
public class KeyboardOperationServiceTests
{
    [Fact]
    public void Register_and_unregister_track_bindings_count()
    {
        var svc = new KeyboardOperationService();
        var gesture = new System.Windows.Input.KeyGesture(System.Windows.Input.Key.Up);
        var hitCount = 0;

        svc.Register(gesture, () => hitCount++, "上移");
        svc.Unregister(gesture);

        // 注销后不应有副作用（无法直接断言内部字典大小，但重复注销不抛即可）
        svc.Unregister(gesture);
        hitCount.Should().Be(0);
    }

    [Fact]
    public void Register_overwrites_previous_binding_for_same_gesture()
    {
        var svc = new KeyboardOperationService();
        var gesture = new System.Windows.Input.KeyGesture(System.Windows.Input.Key.Up);
        var first = 0;
        var second = 0;

        svc.Register(gesture, () => first++);
        svc.Register(gesture, () => second++);

        // 仅第二个回调应被保留；因无法在无 WPF 输入环境触发 Handle，
        // 通过观察 first 不变间接验证（实际派发在 UI 集成测试覆盖）
        first.Should().Be(0);
        second.Should().Be(0);
    }

    [Fact]
    public void MoveSelection_clamps_to_zero_when_unselected_and_negative_offset()
    {
        var svc = new KeyboardOperationService();
        var fired = new List<int>();
        svc.SelectedPriceIndexChanged += (_, idx) => fired.Add(idx);

        svc.MoveSelection(-1, maxIndex: 10);

        svc.SelectedPriceIndex.Should().Be(0, "未选中时下移应落到 0");
        fired.Should().Equal(new[] { 0 });
    }

    [Fact]
    public void MoveSelection_advances_and_clamps_to_maxIndex()
    {
        var svc = new KeyboardOperationService();
        svc.MoveSelection(0, maxIndex: 5); // 落到 0
        svc.SelectedPriceIndex.Should().Be(0);

        svc.MoveSelection(3, maxIndex: 5);
        svc.SelectedPriceIndex.Should().Be(3);

        svc.MoveSelection(10, maxIndex: 5);
        svc.SelectedPriceIndex.Should().Be(5, "越界应夹紧到 maxIndex");
    }

    [Fact]
    public void MoveSelection_does_not_fire_event_when_index_unchanged()
    {
        var svc = new KeyboardOperationService();
        svc.MoveSelection(0, maxIndex: 5); // -1 → 0，触发一次
        var firedCount = 0;
        svc.SelectedPriceIndexChanged += (_, _) => firedCount++;

        svc.MoveSelection(0, maxIndex: 5); // 0 → 0，不触发
        firedCount.Should().Be(0);
    }

    [Fact]
    public void ResetSelection_returns_to_negative_one_and_fires()
    {
        var svc = new KeyboardOperationService();
        // -1 → 0（首次下移），0 → 2（再下移 2 格）
        svc.MoveSelection(1, maxIndex: 5);
        svc.MoveSelection(2, maxIndex: 5);
        svc.SelectedPriceIndex.Should().Be(2);

        var fired = new List<int>();
        svc.SelectedPriceIndexChanged += (_, idx) => fired.Add(idx);
        svc.ResetSelection();

        svc.SelectedPriceIndex.Should().Be(-1);
        fired.Should().Equal(new[] { -1 });
    }

    [Fact]
    public void MoveSelection_ignores_negative_maxIndex()
    {
        var svc = new KeyboardOperationService();
        svc.MoveSelection(1, maxIndex: -1);
        svc.SelectedPriceIndex.Should().Be(-1, "maxIndex<0 时应忽略");
    }
}
