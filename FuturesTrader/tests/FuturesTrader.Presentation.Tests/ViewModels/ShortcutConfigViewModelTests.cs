using System.Windows.Input;
using FluentAssertions;
using FuturesTrader.Domain.Configuration;
using FuturesTrader.Presentation.Abstractions;
using FuturesTrader.Presentation.ViewModels;

namespace FuturesTrader.Presentation.Tests.ViewModels;

public class ShortcutConfigViewModelTests
{
    [Fact]
    public void Recording_assigns_a_new_non_conflicting_gesture()
    {
        var vm = new ShortcutConfigViewModel(new ShortcutConfig());
        var binding = vm.Bindings.Single(item => item.Action == KeyboardShortcutAction.SelectiveCancelAll);

        vm.BeginRecordingCommand.Execute(binding);
        vm.TryAssign(Key.F12, ModifierKeys.Control).Should().BeTrue();

        binding.GestureText.Should().Be("Ctrl+F12");
        binding.IsRecording.Should().BeFalse();
        vm.RecordingBinding.Should().BeNull();
    }

    [Fact]
    public void Recording_rejects_a_gesture_owned_by_another_action()
    {
        var vm = new ShortcutConfigViewModel(new ShortcutConfig());
        var binding = vm.Bindings.Single(item => item.Action == KeyboardShortcutAction.SelectiveCancelAll);

        vm.BeginRecordingCommand.Execute(binding);
        vm.TryAssign(Key.W, ModifierKeys.None).Should().BeFalse();

        binding.GestureText.Should().Be("Space");
        vm.ValidationMessage.Should().Contain("强制全撤");
    }

    [Fact]
    public void Reset_one_and_reset_all_restore_domain_defaults()
    {
        var custom = new ShortcutConfig
        {
            SelectiveCancelAll = "Ctrl+Space",
            ForceCancelAll = "Ctrl+W",
            RecenterAsk = "Ctrl+A",
            RecenterBid = "Ctrl+D",
            ToggleOnlyOpen = "Ctrl+F",
            MoveSelectionUp = "Ctrl+Up",
            MoveSelectionDown = "Ctrl+Down"
        };
        var vm = new ShortcutConfigViewModel(custom);
        var selective = vm.Bindings.Single(item => item.Action == KeyboardShortcutAction.SelectiveCancelAll);

        vm.ResetOneCommand.Execute(selective);
        selective.GestureText.Should().Be("Space");

        vm.ResetAllCommand.Execute(null);
        vm.ToConfig().Should().Be(new ShortcutConfig());
    }
}
