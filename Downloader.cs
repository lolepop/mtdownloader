using System.Net;
using System.Net.Http.Headers;

// TODO: dump tree to fs to resume downloading
class Downloader : IDisposable
{
    public const int BufferSize = 4096;
    public const int SplitThreshold = 1 * 1024 * 1024;

    public string Url { get; private set; }
    public int Parallelism { get; private set; }
    public long Size { get; private set; }
    public string OutFile { get; set; }

    private List<AsyncNode> orderedNodes = new List<AsyncNode>();
    private HashSet<AsyncNode> nodes = new HashSet<AsyncNode>();
    private CancellationTokenSource cts = new CancellationTokenSource();
    // private SemaphoreSlim semaphore;

    private static HttpClient client = new HttpClient();

    public delegate Task DownloadRange(long start, long end, CancellationToken ct, AsyncNode parent);

    private object _obj = new Object();

    public Downloader(string url, int parallelism, string outFile)
    {
        Url = url;
        Parallelism = parallelism;
        OutFile = outFile;
        // semaphore = new SemaphoreSlim(parallelism, parallelism);

        ServicePointManager.DefaultConnectionLimit = 1000;
    }

    private async Task OnDownloadComplete(AsyncNode parent)
    {
        Task[]? tasks = null;

        lock (_obj) // for hashset
        {
            nodes.Remove(parent);
            if (!parent.HasChildren)
            {
                // sort by descending size to prevent subdividing small sections unnecessarily
                var active = nodes
                    .Where(n => n.Remaining > SplitThreshold)
                    // .MaxBy(a => a.Remaining); // why is this slower
                    .MaxBy(a => a.Size);
                active?.cts.Cancel();
            
                var t = active?.SplitNode(cts.Token);
                if (t.HasValue)
                {
                    (AsyncNode a, AsyncNode b) = t.Value;
                    nodes.Add(a);
                    nodes.Add(b);
                    tasks = new Task[] {
                        Task.Run(() => a.DownloadChunk()),
                        Task.Run(() => b.DownloadChunk())
                    };
                }
            }
        }

        if (tasks != null) // prevent mutex deadlock
            await Task.WhenAll(tasks);

    }

    public async Task DownloadChunk(long start, long end, CancellationToken ct, AsyncNode parent)
    {
        // await semaphore.WaitAsync();

        try
        {
            var reqMsg = new HttpRequestMessage(HttpMethod.Get, Url);
            reqMsg.Headers.Range = new RangeHeaderValue(start, end);
            
            var res = await client.SendAsync(reqMsg, HttpCompletionOption.ResponseHeadersRead, ct);
            if (res == null) return;
            res.EnsureSuccessStatusCode();

            using (var stream = await res.Content.ReadAsStreamAsync())
            {
                using (var fs = new FileStream(OutFile, FileMode.Open, FileAccess.Write, FileShare.Write, BufferSize, true))
                {
                    fs.Seek(start, SeekOrigin.Begin);
                    var buffer = new byte[BufferSize];
                    int size = 0;
                    do
                    {
                        if (ct.IsCancellationRequested) break;
                        size = await stream.ReadAsync(buffer, 0, BufferSize, ct);
                        await fs.WriteAsync(buffer, 0, size);
                        await fs.FlushAsync();
                        parent.Downloaded += size;
                    } while (size > 0);
                }
            }
        }
        catch(TaskCanceledException) {}
        catch(Exception) { throw; }
        finally
        {
            // semaphore.Release();
            await OnDownloadComplete(parent);
        }

    }

    public void Preallocate(long size)
    {
        using (var fs = new FileStream(OutFile, FileMode.Create))
            fs.SetLength(size);
    }

    public async Task Start()
    {
        await Task.WhenAll(
            nodes.Select(node => Task.Run(() => node.DownloadChunk()))
        );
    }

    private void createNodesFromLength(long length)
    {
        var avgChunkSize = length / Parallelism;
        var excessChunk = avgChunkSize + length % Parallelism;
        for (int i = 0; i < Parallelism; i++)
        {
            var start = i * avgChunkSize;
            var node = new AsyncNode(start, start + (i != Parallelism - 1 ? avgChunkSize : excessChunk) - 1, cts.Token, DownloadChunk);
            nodes.Add(node);
            orderedNodes.Add(node);
        }
    }

    public async Task<long> CalcSize()
    {
        var res = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, Url), HttpCompletionOption.ResponseHeadersRead);
        res.EnsureSuccessStatusCode();
        if (res.Headers.AcceptRanges.Contains("none"))
            throw new Exception("Url does not support range requests");

        var contentLength =  res.Content.Headers.ContentLength;
        if (!contentLength.HasValue)
            throw new Exception("Server did not return content length");
        
        createNodesFromLength(contentLength.Value);
        return contentLength.Value;
    }

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
    }
}