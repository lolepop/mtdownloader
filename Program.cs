using System.Diagnostics;

async Task Main(string[] args)
{
    var url = args[1];
    var outFile = args[2];
    var connections = int.Parse(args[3]);

    Stopwatch stopWatch = new Stopwatch();

    var downloader = new Downloader(url, connections, outFile);
    var size = await downloader.CalcSize();
    downloader.Preallocate(size);

    stopWatch.Start();
    await downloader.Start();
    stopWatch.Stop();
    Console.WriteLine($"time taken: {stopWatch.Elapsed.TotalSeconds}s");

}

Main(Environment.GetCommandLineArgs()).GetAwaiter().GetResult();
