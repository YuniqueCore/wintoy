using FuturesTrader.Domain.Configuration;
using FuturesTrader.Infrastructure.Persistence;

// ConfigMigrator: GBK config.ini ↔ JSON 双向迁移工具
// 用法:
//   ConfigMigrator ini2json <input.ini> <output.json>
//   ConfigMigrator json2ini <input.json> <output.ini>

if (args.Length < 3)
{
    PrintUsage();
    return 1;
}

var repo = new ConfigRepository();
var command = args[0].ToLowerInvariant();
var inputPath = args[1];
var outputPath = args[2];

try
{
    switch (command)
    {
        case "ini2json":
        {
            var config = repo.Load(inputPath);
            var json = repo.ToJson(config);
            await File.WriteAllTextAsync(outputPath, json, System.Text.Encoding.UTF8);
            Console.WriteLine($"✓ INI → JSON 迁移完成: {inputPath} → {outputPath}");
            Console.WriteLine($"  字体: {config.Window.MainFont}");
            Console.WriteLine($"  行情地址: {config.User.HqAddress}");
            Console.WriteLine($"  开盘抢单频率: {config.User.MOrderXSpeed}ms");
            Console.WriteLine($"  触发时间点: {config.User.MOrderTimes.Count} 个");
            return 0;
        }
        case "json2ini":
        {
            var json = await File.ReadAllTextAsync(inputPath, System.Text.Encoding.UTF8);
            var config = repo.FromJson(json);
            repo.Save(outputPath, config);
            Console.WriteLine($"✓ JSON → INI 迁移完成: {inputPath} → {outputPath}");
            return 0;
        }
        default:
            Console.WriteLine($"未知命令: {command}");
            PrintUsage();
            return 1;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✗ 迁移失败: {ex.Message}");
    return 2;
}

static void PrintUsage()
{
    Console.WriteLine("ConfigMigrator — GBK config.ini ↔ JSON 迁移工具");
    Console.WriteLine();
    Console.WriteLine("用法:");
    Console.WriteLine("  ConfigMigrator ini2json <input.ini>  <output.json>");
    Console.WriteLine("  ConfigMigrator json2ini <input.json> <output.ini>");
    Console.WriteLine();
    Console.WriteLine("示例:");
    Console.WriteLine("  ConfigMigrator ini2json ..\\..\\..\\..\\..\\qihuo-software\\config.ini config.json");
}
