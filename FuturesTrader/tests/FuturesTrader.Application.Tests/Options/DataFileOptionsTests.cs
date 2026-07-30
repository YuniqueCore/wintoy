using FluentAssertions;
using FuturesTrader.Application.Options;

namespace FuturesTrader.Application.Tests.Options;

/// <summary>
/// DataFileOptions 单元测试：锁定业务数据文件路径统一入口的默认值契约。
/// 确保 config.ini/HQAddress.xml/Users.xml/window-groups.json 的默认相对路径不被误改。
/// </summary>
public class DataFileOptionsTests
{
    [Fact]
    public void Defaults_point_to_expected_relative_paths()
    {
        var opts = new DataFileOptions();
        opts.ConfigIni.Should().Be("data/config.ini");
        opts.HqAddressXml.Should().Be("data/HQAddress.xml");
        opts.UsersXml.Should().Be("data/Users.xml");
        opts.GroupsJson.Should().Be("data/window-groups.json");
    }

    [Fact]
    public void Properties_are_mutable_for_postconfigure()
    {
        // PostConfigure 会把相对路径覆盖为绝对路径，属性必须可写
        var opts = new DataFileOptions
        {
            ConfigIni = "/abs/config.ini",
            HqAddressXml = "/abs/hq.xml",
            UsersXml = "/abs/users.xml",
            GroupsJson = "/abs/groups.json"
        };
        opts.ConfigIni.Should().Be("/abs/config.ini");
        opts.HqAddressXml.Should().Be("/abs/hq.xml");
        opts.UsersXml.Should().Be("/abs/users.xml");
        opts.GroupsJson.Should().Be("/abs/groups.json");
    }
}
