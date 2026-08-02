using FluentAssertions;
using FuturesTrader.Application;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.MarketData;
using FuturesTrader.Domain.WindowGroups;
using FuturesTrader.Presentation.Services;
using FuturesTrader.Presentation.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FuturesTrader.Presentation.Tests.ViewModels;

/// <summary><see cref="WindowGroupBarViewModel"/> 的合约身份展示测试。</summary>
public sealed class WindowGroupBarViewModelTests
{
    [Fact]
    public async Task Group_item_uses_the_same_name_and_code_title_as_contract_window()
    {
        var catalog = new InstrumentCatalogCache();
        catalog.Upsert(new Instrument
        {
            InstrumentId = "ag2608",
            Name = "白银2608",
            ProductClass = (byte)'1',
        });
        var vm = CreateViewModel(catalog);
        await WaitForLoadedAsync(vm);

        vm.Groups[0].Windows.Single().DisplayTitle.Should().Be("白银2608 - ag2608");
    }

    [Fact]
    public async Task Group_item_title_refreshes_when_instrument_metadata_arrives_later()
    {
        var catalog = new InstrumentCatalogCache();
        var vm = CreateViewModel(catalog);
        await WaitForLoadedAsync(vm);
        var item = vm.Groups[0].Windows.Single();
        item.DisplayTitle.Should().Be("ag2608", "元数据尚未到达时以合约代码安全兜底");

        catalog.Upsert(new Instrument
        {
            InstrumentId = "ag2608",
            Name = "白银2608",
            ProductClass = (byte)'1',
        });

        item.DisplayTitle.Should().Be("白银2608 - ag2608");
    }

    private static WindowGroupBarViewModel CreateViewModel(InstrumentCatalogCache catalog)
    {
        var service = new WindowGroupService(
            new StubWindowGroupRepository(new WindowLayout
            {
                Windows = [new InstrumentWindow { InstrumentCode = "ag2608", GroupId = 1 }],
            }),
            new NullWindowHost(),
            Options.Create(new WindowLayoutOptions()),
            NullLogger<WindowGroupService>.Instance);
        return new WindowGroupBarViewModel(
            service,
            NullLogger<WindowGroupBarViewModel>.Instance,
            catalog);
    }

    private static async Task WaitForLoadedAsync(WindowGroupBarViewModel vm)
    {
        for (var attempt = 0; attempt < 100 && vm.State is WindowGroupEditorState.Loading; attempt++)
            await Task.Delay(10);

        vm.State.Should().BeOfType<WindowGroupEditorState.Loaded>();
    }

    private sealed class StubWindowGroupRepository : IWindowGroupRepository
    {
        private readonly WindowLayout _layout;

        public StubWindowGroupRepository(WindowLayout layout) => _layout = layout;

        public WindowLayout Load(WindowLayoutOptions options) => _layout;

        public void Save(WindowLayoutOptions options, WindowLayout layout) { }
    }
}
