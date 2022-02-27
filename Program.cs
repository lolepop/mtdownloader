using System.Diagnostics;
using ShellProgressBar;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

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
    public const int Ticks = 100;

    private long totalSize;

    private Downloader instance;
    private ProgressBar rootBar = new(Ticks, "", new ProgressBarOptions {
        ProgressCharacter = '─',
        ProgressBarOnBottom = true,
        ShowEstimatedDuration = true
    });
    private IProgress<float> rootBarProgression;
    private Dictionary<AsyncNode, (ChildProgressBar bar, IProgress<float> progression)> bars = new();
    private IObservable<int> timer;

    private CancellationToken token;
    private object _obj = new();

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
        this.totalSize = totalSize;

        var b = Observable.Interval(TimeSpan.FromMilliseconds(Ticks))
            .Select(_ => Tick());

        var avg = b.EMADiff(Ticks);
        
        timer = b
            .WithLatestFrom(avg)
            .Do(a => SetAverageSpeed(a.First, a.Second))
            .SelectMany(_ => Observable.Empty<int>());
    }

    private void SetAverageSpeed(double downloaded, double avg)
    {
        var remainingTime = (totalSize - downloaded) / avg;
        rootBar.EstimatedDuration = TimeSpan.FromSeconds(double.IsInfinity(remainingTime) ? -1 : remainingTime);
        rootBar.Message = $"Average Speed: {Util.GetBytesReadable((long)avg)}/s";
    }

    private long Tick()
    {
        long totalDownloaded = 0;
        Parallel.ForEach(instance.OrderedNodes, n => {
            var mem = new Dictionary<AsyncNode, long>();
            n.MemoiseDownloaded(mem);
            Interlocked.Add(ref totalDownloaded, mem[n]);
            
            var q = new Queue<(AsyncNode?, AsyncNode)>(new (AsyncNode?, AsyncNode)[] { (null, n) });
            while (q.TryDequeue(out var o))
            {
                (var parent, var self) = o;
                lock (_obj)
                {
                    if (parent != null && !bars.ContainsKey(self))
                    {
                        var b = bars[parent].bar.Spawn(Ticks, Util.GetBytesReadable(self.Size), barConfig);
                        bars.Add(self, (b, b.AsProgress<float>()));
                    }
                }
                bars[self].progression.Report(mem[self] / (float)self.Size);

                if (self.Left != null) q.Enqueue((self, self.Left));
                if (self.Right != null) q.Enqueue((self, self.Right));
            }
        });
        
        rootBarProgression.Report((float)(totalDownloaded / (double)totalSize));
        return totalDownloaded;
    }

    public async Task Start()
    {
        foreach (var n in instance.OrderedNodes)
        {
            var b = rootBar.Spawn(Ticks, Util.GetBytesReadable(n.Size), barConfig);
            bars.Add(n, (b, b.AsProgress<float>()));
        }

        await timer.ToTask(token);
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

    public static IObservable<double> EMADiff(this IObservable<long> self, int emitSpeed, double smoothCoeff = 0.5)
    {
        var t = 1000.0 / emitSpeed;
        var smooth = smoothCoeff / t;
        return self
            .Scan<long, double?>(null, (acc, v) => acc != null ? smooth * v + (1 - smooth) * acc : v)
            .Select<double?, double>(a => a!.Value);
    }

    public static string GetBytesReadable(long i)
    {
        // Get absolute value
        long absolute_i = (i < 0 ? -i : i);
        // Determine the suffix and readable value
        string suffix;
        double readable;
        if (absolute_i >= 0x1000000000000000) // Exabyte
        {
            suffix = "EB";
            readable = (i >> 50);
        }
        else if (absolute_i >= 0x4000000000000) // Petabyte
        {
            suffix = "PB";
            readable = (i >> 40);
        }
        else if (absolute_i >= 0x10000000000) // Terabyte
        {
            suffix = "TB";
            readable = (i >> 30);
        }
        else if (absolute_i >= 0x40000000) // Gigabyte
        {
            suffix = "GB";
            readable = (i >> 20);
        }
        else if (absolute_i >= 0x100000) // Megabyte
        {
            suffix = "MB";
            readable = (i >> 10);
        }
        else if (absolute_i >= 0x400) // Kilobyte
        {
            suffix = "KB";
            readable = i;
        }
        else
        {
            return i.ToString("0 B"); // Byte
        }
        // Divide by 1024 to get fractional value
        readable = (readable / 1024);
        // Return formatted number with suffix
        return readable.ToString("0.### ") + suffix;
    }
}
