class AsyncNode
{
    public long Start { get; private set; }
    public long End { get; private set; }
    public long Downloaded { get; set; } = 0;
    public long Size { get => End - Start; }

    public AsyncNode? Left { get; private set; }
    public AsyncNode? Right { get; private set; }
    public bool HasChildren { get => Left != null || Right != null; }

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
}