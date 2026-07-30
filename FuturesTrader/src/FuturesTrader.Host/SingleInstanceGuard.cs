using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace FuturesTrader.Host;

/// <summary>
/// 单例守卫：用 <see cref="Mutex"/> 保证进程唯一；第二个实例启动时通过命名管道通知首个实例激活前台后退出。
/// <para>
/// 对齐 0527.exe「不可多开」约束：每次点开都需登录，但同一时刻只允许一个进程实例运行。
/// </para>
/// <para>
/// 协议：首个实例持锁并启动命名管道服务端 <c>FuturesTrader0527</c>；
/// 第二个实例 <see cref="TryAcquire"/> 返回 false 后调用 <see cref="SignalActivateExisting"/> 向管道写入 <c>ACTIVATE</c>，
/// 首个实例 <see cref="ActivateRequested"/> 事件触发 → 主窗口 <c>Activate()</c> 置顶。
/// </para>
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\FuturesTrader0527_SingleInstance";
    private const string PipeName = "FuturesTrader0527_Activate";

    private Mutex? _mutex;
    private CancellationTokenSource? _pipeCts;
    private Task? _pipeServerTask;

    /// <summary>首个实例收到激活请求时触发（UI 线程订阅后调用窗口 Activate/Topmost）。</summary>
    public event EventHandler? ActivateRequested;

    /// <summary>
    /// 尝试获取单例锁。成功返回 true 且自动启动管道服务端监听激活请求；
    /// 失败（已有实例运行）返回 false，调用方应退出进程。
    /// </summary>
    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, name: MutexName, out var createdNew);
        if (!createdNew)
        {
            // 已有实例：释放本进程持有的引用，返回 false 让调用方退出
            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        StartPipeServer();
        return true;
    }

    /// <summary>第二个实例调用：向已运行实例的管道发送 ACTIVATE 消息（fire-and-forget，发送后即退出）。</summary>
    public static void SignalActivateExisting()
    {
        try
        {
            using var client = new NamedPipeClientStream(
                serverName: ".", PipeName,
                PipeDirection.Out, PipeOptions.None);
            client.Connect(timeout: 1500);
            using var writer = new StreamWriter(client);
            writer.Write("ACTIVATE");
            writer.Flush();
        }
        catch
        {
            // 首个实例可能正在关闭或管道未就绪，忽略：本进程即将退出
        }
    }

    /// <summary>启动命名管道服务端，后台等待激活请求。</summary>
    private void StartPipeServer()
    {
        _pipeCts = new CancellationTokenSource();
        var token = _pipeCts.Token;

        _pipeServerTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                NamedPipeServerStream? server = null;
                try
                {
                    server = new NamedPipeServerStream(
                        PipeName, PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(server);
                    var message = await reader.ReadToEndAsync(token);
                    if (message.Contains("ACTIVATE", StringComparison.Ordinal))
                    {
                        ActivateRequested?.Invoke(this, EventArgs.Empty);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // 单次连接异常不应终止服务端循环；客户端会重试
                }
                finally
                {
                    server?.Dispose();
                }
            }
        }, token);
    }

    public void Dispose()
    {
        try { _pipeCts?.Cancel(); } catch { }
        try { _pipeServerTask?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _pipeCts?.Dispose();
        _pipeServerTask?.Dispose();

        if (_mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch { }
            _mutex.Dispose();
            _mutex = null;
        }
    }
}
