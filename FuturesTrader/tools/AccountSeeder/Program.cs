using FuturesTrader.Domain.Connections;
using FuturesTrader.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccountSeeder;

// AccountSeeder: 把测试账号 (000102) 写入 Users.xml（用 IAccountRepository.Add 落盘，UTF-8 无 BOM + Tab 缩进）。
// 用法:
//   AccountSeeder add <UsersXmlPath> <userid> <brokerid> <password> <frontAddr> <appid> <authcode> [title]
//   AccountSeeder list <UsersXmlPath>
//   AccountSeeder ensure <UsersXmlPath> <userid> <brokerid> <password> <frontAddr> <appid> <authcode> [title]
//     （ensure = 已存在则跳过，不存在则新增；幂等）

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var repo = new UsersXmlAccountRepository(NullLogger<UsersXmlAccountRepository>.Instance);
        var cmd = args[0].ToLowerInvariant();

        try
        {
            switch (cmd)
            {
                case "add":
                    return Add(repo, args);
                case "list":
                    return List(repo, args);
                case "ensure":
                    return Ensure(repo, args);
                default:
                    Console.Error.WriteLine($"未知命令: {cmd}");
                    PrintUsage();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗ 失败：{ex.Message}");
            return 2;
        }
    }

    private static int Add(UsersXmlAccountRepository repo, string[] args)
    {
        if (args.Length < 8)
        {
            Console.Error.WriteLine("参数不足：add <xml> <userid> <brokerid> <password> <frontAddr> <appid> <authcode> [title]");
            return 1;
        }
        var xml = args[1];
        var account = Build(args, 2);
        repo.Add(xml, account);
        Console.WriteLine($"✓ 已新增账号 {account.UserId} → {xml}");
        return 0;
    }

    private static int List(UsersXmlAccountRepository repo, string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("参数不足：list <xml>");
            return 1;
        }
        var xml = args[1];
        var accounts = repo.Load(xml);
        Console.WriteLine($"Users.xml {xml} 共有 {accounts.Count} 个账号：");
        foreach (var a in accounts)
        {
            Console.WriteLine($"  [{a.UserId}] broker={a.BrokerId} title={a.Title} addr={a.TradingAddress}");
            Console.WriteLine($"             appid={a.AppId} authcode={a.AuthCode}");
        }
        return 0;
    }

    private static int Ensure(UsersXmlAccountRepository repo, string[] args)
    {
        if (args.Length < 8)
        {
            Console.Error.WriteLine("参数不足：ensure <xml> <userid> <brokerid> <password> <frontAddr> <appid> <authcode> [title]");
            return 1;
        }
        var xml = args[1];
        var userId = args[2];
        var existing = repo.Load(xml);
        if (existing.Any(a => a.UserId == userId))
        {
            Console.WriteLine($"= 账号 {userId} 已存在，跳过");
            return 0;
        }
        var account = Build(args, 2);
        repo.Add(xml, account);
        Console.WriteLine($"+ 已新增账号 {userId} → {xml}");
        return 0;
    }

    /// <summary>
    /// 构造 AccountEntry，参数顺序（从 <paramref name="start"/> 起）：
    /// userid, brokerid, password, frontAddr, appid, authcode, [title]。
    /// </summary>
    private static AccountEntry Build(string[] args, int start)
    {
        var userId = args[start];
        var brokerId = args[start + 1];
        var frontAddr = args[start + 3];
        var appId = args[start + 4];
        var authCode = args[start + 5];
        var title = args.Length > start + 6 ? args[start + 6] : userId;
        return new AccountEntry
        {
            Title = title,
            TradingAddress = frontAddr,
            BrokerId = brokerId,
            UserId = userId,
            AppId = appId,
            AuthCode = authCode,
        };
    }

    private static void PrintUsage()
    {
        Console.WriteLine("AccountSeeder — 通过 IAccountRepository 写入 Users.xml");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  AccountSeeder add    <xml> <userid> <brokerid> <password> <frontAddr> <appid> <authcode> [title]");
        Console.WriteLine("  AccountSeeder list   <xml>");
        Console.WriteLine("  AccountSeeder ensure <xml> <userid> <brokerid> <password> <frontAddr> <appid> <authcode> [title]");
        Console.WriteLine();
        Console.WriteLine("示例:");
        Console.WriteLine("  AccountSeeder add data/Users.xml 000102 8080 258147 tcp://60.12.233.58:18105 client_qihuo159_1.0 AC2F6ESEXEEYSIGU 测试账号");
    }
}
