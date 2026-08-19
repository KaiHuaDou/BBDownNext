using System.Threading.Channels;

using BBDown.Core.Download;

namespace BBDown.Serve.Tasks;

/// <summary>
/// 已受理任务的入队通道：解析完成的任务经 Writer 进入，TaskWorker 逐条消费。
/// 有界（100）提供背压，写满时入队失败由端点返回 429。
/// </summary>
internal sealed class TaskQueue
{
    private readonly Channel<TaskEnvelope> channel = Channel.CreateBounded<TaskEnvelope>(new BoundedChannelOptions(100)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false,
    });

    public ChannelWriter<TaskEnvelope> Writer => channel.Writer;

    public ChannelReader<TaskEnvelope> Reader => channel.Reader;
}

/// <summary>
/// 排队中的任务执行单元：Request 供下载管线消费，CallBackWebHook 为任务完成回调地址。
/// </summary>
internal sealed record TaskEnvelope(DownloadTask Task, DownloadRequest Request, string? CallBackWebHook);
