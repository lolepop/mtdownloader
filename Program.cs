using System.Diagnostics;
using System.Timers;
using ShellProgressBar;

async Task Main(string[] args)
{
    var url = args[1];
    var outFile = args[2];
    var connections = int.Parse(args[3]);

    Stopwatch stopWatch = new Stopwatch();

    var downloader = new Downloader(url, connections, outFile);
    var size = await downloader.CalcSize();
    downloader.Preallocate(size);

    var outerCts = new CancellationTokenSource();
    var progress = new Progress(downloader, outerCts.Token, size);

    stopWatch.Start();

    await Task.WhenAny(new Task[] {
        Task.Run(() => downloader.Start()),
        progress.Start()
    });
    outerCts.Cancel();

    stopWatch.Stop();
    Console.WriteLine($"time taken: {stopWatch.Elapsed.TotalSeconds}s");

}

Main(Environment.GetCommandLineArgs()).GetAwaiter().GetResult();

internal class Progress
{
    public const int Ticks = 1000;

    private long totalSize;

    private Downloader instance;
    private ProgressBar rootBar = new(Ticks, "Progress", barConfig);
    private IProgress<float> rootBarProgression;
    private Dictionary<AsyncNode, (ChildProgressBar bar, IProgress<float> progression)> bars = new();
    private System.Timers.Timer timer = new(100) {
        AutoReset = true
    };

    private CancellationToken token;
    private TaskCompletionSource taskCompletionSource = new();

    private static readonly ProgressBarOptions barConfig = new() {
        // DenseProgressBar = true,
        ProgressCharacter = '─',
        CollapseWhenFinished = true,
        ProgressBarOnBottom = true
    };

    public Progress(Downloader instance, CancellationToken token, long totalSize)
    {
        this.instance = instance;
        this.token = token;
        this.rootBarProgression = rootBar.AsProgress<float>();
        timer.Elapsed += Tick;
        this.totalSize = totalSize;
    }

    private void Tick(Object? source, ElapsedEventArgs e)
    {
        if (token.IsCancellationRequested)
        {
            timer.Enabled = false;
            taskCompletionSource.SetResult();
            return;
        }

        long totalDownloaded = 0;
        Parallel.ForEach(instance.OrderedNodes, n => {
            var mem = new Dictionary<AsyncNode, long>();
            n.MemoiseDownloaded(mem);
            Interlocked.Add(ref totalDownloaded, mem[n]);
            
            var q = new Queue<(AsyncNode?, AsyncNode)>(new (AsyncNode?, AsyncNode)[] { (null, n) });
            while (q.TryDequeue(out var o))
            {
                (var parent, var self) = o;
                if (parent != null && !bars.ContainsKey(self))
                {
                    var b = bars[parent].bar.Spawn(Ticks, "", barConfig);
                    bars.Add(self, (b, b.AsProgress<float>()));
                }
                bars[self].progression.Report(mem[self] / (float)self.Size);

                if (self.Left != null) q.Enqueue((self, self.Left));
                if (self.Right != null) q.Enqueue((self, self.Right));
            }
        });
        
        rootBarProgression.Report(totalDownloaded / (float)totalSize);

    }

    public async Task Start()
    {
        foreach (var n in instance.OrderedNodes)
        {
            var b = rootBar.Spawn(Ticks, "Main Chunk", barConfig);
            bars.Add(n, (b, b.AsProgress<float>()));
        }

        timer.Enabled = true;
        await taskCompletionSource.Task;
    }

}

internal static class Util
{
    public static long MemoiseDownloaded(this AsyncNode self, Dictionary<AsyncNode, long> d)
    {
        var total = self.Downloaded + (self.Left?.MemoiseDownloaded(d) ?? 0) + (self.Right?.MemoiseDownloaded(d) ?? 0);
        d.Add(self, total);
        return total;
    }
}
