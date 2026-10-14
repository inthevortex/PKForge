using System.Net;
using PKForge.App.Services;
using Xunit;

namespace PKForge.App.Tests;

public sealed class CachedFileFetcherTests : IDisposable
{
    private const string Url = "https://example.test/home/6.png";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pkforge-fetch-" + Guid.NewGuid().ToString("N"));
    private readonly string _file;

    public CachedFileFetcherTests() => _file = Path.Combine(_root, "home", "6.png");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task CallersAskingForOneFileShareOneDownload()
    {
        var handler = new StubHandler();
        var release = handler.Hold();
        var fetcher = new CachedFileFetcher(new HttpClient(handler), TimeSpan.FromSeconds(30));

        var first = fetcher.EnsureAsync(Url, _file, CancellationToken.None);
        var second = fetcher.EnsureAsync(Url, _file, CancellationToken.None);
        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, handler.Requests);
        Assert.Equal(StubHandler.Body, await File.ReadAllBytesAsync(_file));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(_file)!, "*.part"));
    }

    [Fact]
    public async Task ADownloadNeverWritesThroughAnotherWritersTemporaryFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var stale = _file + ".part";
        await File.WriteAllTextAsync(stale, "someone else's bytes");
        var fetcher = new CachedFileFetcher(new HttpClient(new StubHandler()), TimeSpan.FromSeconds(30));

        await fetcher.EnsureAsync(Url, _file, CancellationToken.None);

        Assert.Equal(StubHandler.Body, await File.ReadAllBytesAsync(_file));
        Assert.Equal("someone else's bytes", await File.ReadAllTextAsync(stale));
    }

    [Fact]
    public async Task AFailedDownloadLeavesNothingAndTheNextCallTriesAgain()
    {
        var handler = new StubHandler { Status = HttpStatusCode.InternalServerError };
        var fetcher = new CachedFileFetcher(new HttpClient(handler), TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<HttpRequestException>(() => fetcher.EnsureAsync(Url, _file, CancellationToken.None));
        Assert.False(File.Exists(_file));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(_file)!));

        handler.Status = HttpStatusCode.OK;
        await fetcher.EnsureAsync(Url, _file, CancellationToken.None);

        Assert.Equal(2, handler.Requests);
        Assert.True(File.Exists(_file));
    }

    [Fact]
    public async Task OneCallerGivingUpDoesNotStopTheSharedDownload()
    {
        var handler = new StubHandler();
        var release = handler.Hold();
        var fetcher = new CachedFileFetcher(new HttpClient(handler), TimeSpan.FromSeconds(30));
        using var cancel = new CancellationTokenSource();

        var leaving = fetcher.EnsureAsync(Url, _file, cancel.Token);
        var staying = fetcher.EnsureAsync(Url, _file, CancellationToken.None);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => leaving);
        release.SetResult();
        await staying;

        Assert.Equal(1, handler.Requests);
        Assert.True(File.Exists(_file));
    }

    [Fact]
    public async Task AFileDeclaredLargerThanTheCapIsRefused()
    {
        var handler = new StubHandler { Payload = new byte[16], DeclaredLength = CachedFileFetcher.MaxFileBytes + 1 };
        var fetcher = new CachedFileFetcher(new HttpClient(handler), TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<InvalidDataException>(() => fetcher.EnsureAsync(Url, _file, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(_file)!));
    }

    [Fact]
    public async Task AFileThatTurnsOutLargerThanTheCapIsRefused()
    {
        var handler = new StubHandler { Payload = new byte[CachedFileFetcher.MaxFileBytes + 1], Chunked = true };
        var fetcher = new CachedFileFetcher(new HttpClient(handler), TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<InvalidDataException>(() => fetcher.EnsureAsync(Url, _file, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(_file)!));
    }

    [Fact]
    public async Task ACachedFileThatWillNotDecodeIsFetchedAgain()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        await File.WriteAllTextAsync(_file, "corrupt");
        var handler = new StubHandler();
        var fetcher = new CachedFileFetcher(new HttpClient(handler), TimeSpan.FromSeconds(30));

        var decoded = await fetcher.LoadAsync(Url, _file, Decode, downloadGate: null);

        Assert.Equal(StubHandler.Body, decoded);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task AFreshDownloadThatWillNotDecodeGivesNoArtAndIsDeleted()
    {
        var handler = new StubHandler { Payload = "corrupt"u8.ToArray() };
        var fetcher = new CachedFileFetcher(new HttpClient(handler), TimeSpan.FromSeconds(30));

        var decoded = await fetcher.LoadAsync(Url, _file, Decode, downloadGate: null);

        Assert.Null(decoded);
        Assert.False(File.Exists(_file));
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task ACachedFileOverTheCapIsNotDecoded()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        await File.WriteAllBytesAsync(_file, new byte[CachedFileFetcher.MaxFileBytes + 1]);
        var decodes = 0;
        var fetcher = new CachedFileFetcher(new HttpClient(new StubHandler()), TimeSpan.FromSeconds(30));

        var decoded = await fetcher.LoadAsync(Url, _file, path =>
        {
            decodes++;
            return Decode(path);
        }, downloadGate: null);

        Assert.Equal(StubHandler.Body, decoded);
        Assert.Equal(1, decodes);
    }

    [Fact]
    public async Task ADownloadThatFailsIsReportedNotTakenForNoArt()
    {
        var fetcher = new CachedFileFetcher(new HttpClient(new StubHandler { Status = HttpStatusCode.NotFound }), TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<HttpRequestException>(() => fetcher.LoadAsync(Url, _file, Decode, downloadGate: null));
    }

    [Fact]
    public void TheSweepDeletesStaleTemporaryFilesAndKeepsTheRest()
    {
        var home = Path.GetDirectoryName(_file)!;
        var showdown = Path.Combine(_root, "showdown");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(showdown);
        var stale = DateTime.UtcNow - TimeSpan.FromMinutes(10);
        var orphan = Touch(Path.Combine(home, $"6.png.{Guid.NewGuid():N}.part"), stale);
        var legacy = Touch(Path.Combine(showdown, "25.gif.part"), stale);
        var running = Touch(Path.Combine(home, $"7.png.{Guid.NewGuid():N}.part"), DateTime.UtcNow);
        var cached = Touch(_file, stale);
        var fetcher = new CachedFileFetcher(new HttpClient(new StubHandler()), TimeSpan.FromMinutes(2));

        var deleted = fetcher.SweepStaleParts([home, showdown, Path.Combine(_root, "items")]);

        Assert.Equal(2, deleted);
        Assert.False(File.Exists(orphan));
        Assert.False(File.Exists(legacy));
        Assert.True(File.Exists(running));
        Assert.True(File.Exists(cached));
    }

    [Fact]
    public void TheSweepLeavesFoldersItWasNotGiven()
    {
        var spritepack = Path.Combine(_root, "spritepack");
        Directory.CreateDirectory(spritepack);
        var archive = Touch(Path.Combine(spritepack, "pkforge-sprites.zip.part"), DateTime.UtcNow - TimeSpan.FromDays(1));
        var fetcher = new CachedFileFetcher(new HttpClient(new StubHandler()), TimeSpan.FromMinutes(2));

        Assert.Equal(0, fetcher.SweepStaleParts([Path.Combine(_root, "home")]));
        Assert.True(File.Exists(archive));
    }

    [Fact]
    public void TheSweepDoesNotCountATemporaryFileThatVanishedBeforeItWasLookedAt()
    {
        var home = Path.GetDirectoryName(_file)!;
        Directory.CreateDirectory(home);
        var part = Touch(Path.Combine(home, $"6.png.{Guid.NewGuid():N}.part"), DateTime.UtcNow - TimeSpan.FromMinutes(10));
        var enumerated = new FileInfo(part);
        File.Delete(part); // a download finished and renamed it, after the folder was listed

        Assert.False(CachedFileFetcher.TryDeleteStale(enumerated, DateTime.UtcNow));
    }

    private static string Touch(string path, DateTime writtenUtc)
    {
        File.WriteAllText(path, "bytes");
        File.SetLastWriteTimeUtc(path, writtenUtc);
        return path;
    }

    /// <summary>"Decodes" a file holding exactly the stub's body; anything else will not decode.</summary>
    private static async Task<byte[]?> Decode(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        return bytes.AsSpan().SequenceEqual(StubHandler.Body) ? bytes : null;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public static readonly byte[] Body = "a HOME render"u8.ToArray();

        private TaskCompletionSource? _hold;
        private int _requests;

        public int Requests => Volatile.Read(ref _requests);
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public byte[] Payload { get; init; } = Body;
        public long? DeclaredLength { get; init; }
        public bool Chunked { get; init; }

        /// <summary>Holds every response until the returned source completes.</summary>
        public TaskCompletionSource Hold() => _hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            if (_hold is { } hold) await hold.Task.WaitAsync(cancellationToken);
            var response = new HttpResponseMessage(Status);
            if (Status != HttpStatusCode.OK) return response;
            HttpContent content = Chunked ? new StreamContent(new MemoryStream(Payload)) : new ByteArrayContent(Payload);
            content.Headers.ContentLength = Chunked ? null : DeclaredLength ?? Payload.Length;
            response.Content = content;
            return response;
        }
    }
}
