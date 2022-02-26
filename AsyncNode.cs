using System.Text.Json;
using System.Text.Json.Serialization;

public class AsyncNode : IDisposable
{
    public long Start { get; private set; }
    public long End { get; private set; }
    public long Downloaded { get; set; } = 0;
    [JsonIgnore]
    public long Size { get => End - Start + 1; }
    [JsonIgnore]
    public long Remaining { get => Size - Downloaded; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AsyncNode? Left { get; private set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AsyncNode? Right { get; private set; }
    [JsonIgnore]
    public bool HasChildren { get => Left != null || Right != null; }

    [JsonIgnore]
    public CancellationTokenSource cts { get; private set; }

    private Downloader.DownloadRange Download;

    public AsyncNode(long start, long end, CancellationToken masterCt, Downloader.DownloadRange Download)
    {
        Start = start;
        End = end;
        cts = CancellationTokenSource.CreateLinkedTokenSource(masterCt);
        this.Download = Download;
    }

    public Task DownloadChunk() => Download(Start, End, cts.Token, this);

    public (AsyncNode, AsyncNode) SplitNode(CancellationToken masterCt)
    {
        var start1 = Start + Downloaded;
        var end1 = start1 + (End - start1) / 2;
        
        Left = new AsyncNode(start1, end1, masterCt, Download);
        Right = new AsyncNode(end1 + 1, End, masterCt, Download);

        return (Left, Right);
    }

    // apparantly undisposed linked cts can cause memory leaks
    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
    }
}