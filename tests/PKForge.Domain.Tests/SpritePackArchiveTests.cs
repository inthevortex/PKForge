using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using PKForge.Infrastructure;
using Xunit;

namespace PKForge.Domain.Tests;

public sealed class SpritePackArchiveTests : IDisposable
{
    private const string Url = "https://example.test/sprites-1/pack.zip";
    private static readonly TimeSpan NoStall = TimeSpan.FromSeconds(30);

    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "pkforge-pack-" + Guid.NewGuid().ToString("N"));
    private readonly string _root;
    private readonly byte[] _render = RandomBytes(300_000, seed: 1);
    private readonly byte[] _zip;
    private readonly SpritePackArchive _pack;

    public SpritePackArchiveTests()
    {
        _root = Path.Combine(_sandbox, "data");
        Directory.CreateDirectory(_root);
        _zip = BuildZip(new()
        {
            ["home/6.png"] = _render,
            ["showdown/6.gif"] = [1, 2, 3],
            ["items/poke-ball.png"] = [4, 5, 6],
            ["home/kept.png"] = [7, 7, 7],
            ["../escape.png"] = [9],
            ["other/6.png"] = [9],
            ["home/run.exe"] = [9],
        });
        _pack = new SpritePackArchive("Test pack", Url, _zip.Length, Convert.ToHexStringLower(SHA256.HashData(_zip)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_sandbox)) Directory.Delete(_sandbox, recursive: true);
    }

    private string Folder => Path.Combine(_root, "spritepack");
    private string Partial => Path.Combine(Folder, "pack.zip.part");
    private string Archive => Path.Combine(Folder, "pack.zip");

    private Task<ArchiveResult> InstallAsync(FakeServer server, SpritePackArchive? pack = null, long free = long.MaxValue,
        TimeSpan? stall = null, CancellationToken cancellationToken = default, Action<string, double>? onProgress = null,
        Action<string>? onUnpacked = null) =>
        new SpritePackArchiveInstaller(new HttpClient(server), _ => free, stall ?? NoStall, onUnpacked)
            .InstallAsync(_root, pack ?? _pack, onProgress ?? ((_, _) => { }), cancellationToken);

    [Fact]
    public async Task InstallsVerifiesAndUnpacksOnlyPlainCacheFiles()
    {
        Directory.CreateDirectory(Path.Combine(_root, "home"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "home", "kept.png"), [1]);

        var result = await InstallAsync(new FakeServer(_zip));

        Assert.Equal(ArchiveOutcome.Installed, result.Outcome);
        Assert.Equal(_render, await File.ReadAllBytesAsync(Path.Combine(_root, "home", "6.png")));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(_root, "showdown", "6.gif")));
        Assert.Equal(new byte[] { 4, 5, 6 }, await File.ReadAllBytesAsync(Path.Combine(_root, "items", "poke-ball.png")));
        Assert.Equal(new byte[] { 1 }, await File.ReadAllBytesAsync(Path.Combine(_root, "home", "kept.png")));
        Assert.False(File.Exists(Path.Combine(_sandbox, "escape.png")));
        Assert.False(Directory.Exists(Path.Combine(_root, "other")));
        Assert.False(File.Exists(Path.Combine(_root, "home", "run.exe")));
        Assert.False(File.Exists(Archive));
        Assert.False(File.Exists(Partial));
        Assert.True(File.Exists(_pack.DoneMarker(_root)));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ReportsEachFileItUnpacksIntoPlace()
    {
        Directory.CreateDirectory(Path.Combine(_root, "home"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "home", "kept.png"), [1]);
        var unpacked = new List<string>();

        var result = await InstallAsync(new FakeServer(_zip), onUnpacked: unpacked.Add);

        Assert.Equal(ArchiveOutcome.Installed, result.Outcome);
        Assert.Equal(
            [Path.Combine(_root, "home/6.png"), Path.Combine(_root, "showdown/6.gif"), Path.Combine(_root, "items/poke-ball.png")],
            unpacked);
        Assert.All(unpacked, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public async Task ResumesAPartialDownloadWhereItStopped()
    {
        Directory.CreateDirectory(Folder);
        await File.WriteAllBytesAsync(Partial, _zip[..100_000]);
        var server = new FakeServer(_zip);

        var result = await InstallAsync(server);

        Assert.Equal(ArchiveOutcome.Installed, result.Outcome);
        Assert.Equal(100_000, Assert.Single(server.RangeStarts));
        Assert.Equal(_render, await File.ReadAllBytesAsync(Path.Combine(_root, "home", "6.png")));
    }

    [Fact]
    public async Task StartsOverWhenTheServerIgnoresTheRange()
    {
        Directory.CreateDirectory(Folder);
        await File.WriteAllBytesAsync(Partial, RandomBytes(100_000, seed: 2));

        var result = await InstallAsync(new FakeServer(_zip) { SupportsRange = false });

        Assert.Equal(ArchiveOutcome.Installed, result.Outcome);
        Assert.Equal(_render, await File.ReadAllBytesAsync(Path.Combine(_root, "home", "6.png")));
    }

    [Fact]
    public async Task UnpacksAnArchiveAlreadyOnDiskWithoutDownloadingIt()
    {
        Directory.CreateDirectory(Folder);
        await File.WriteAllBytesAsync(Archive, _zip);
        var server = new FakeServer(_zip);

        var result = await InstallAsync(server);

        Assert.Equal(ArchiveOutcome.Installed, result.Outcome);
        Assert.Equal(0, server.Requests);
        Assert.True(File.Exists(Path.Combine(_root, "home", "6.png")));
    }

    [Fact]
    public async Task DiscardsADownloadThatFailsTheChecksum()
    {
        var result = await InstallAsync(new FakeServer(_zip), _pack with { Sha256 = new string('0', 64) });

        Assert.Equal(ArchiveOutcome.Damaged, result.Outcome);
        Assert.False(File.Exists(Partial));
        Assert.False(Directory.Exists(Path.Combine(_root, "home")));
        Assert.False(File.Exists(_pack.DoneMarker(_root)));
    }

    [Fact]
    public async Task DiscardsADownloadLargerThanTheArchive()
    {
        var result = await InstallAsync(new FakeServer(_zip), _pack with { Bytes = _zip.Length - 10 });

        Assert.Equal(ArchiveOutcome.Damaged, result.Outcome);
        Assert.False(File.Exists(Partial));
    }

    [Fact]
    public async Task RefusesWithoutEnoughSpaceBeforeDownloadingAnything()
    {
        var server = new FakeServer(_zip);

        var result = await InstallAsync(server, free: _zip.Length);

        Assert.Equal(ArchiveOutcome.NoSpace, result.Outcome);
        Assert.Equal(_zip.Length * 2L + (64L << 20), result.SpaceNeeded);
        Assert.Equal(_zip.Length, result.SpaceFree);
        Assert.Equal(0, server.Requests);
    }

    [Fact]
    public async Task ThePartAlreadyDownloadedCountsTowardTheSpace()
    {
        Directory.CreateDirectory(Folder);
        await File.WriteAllBytesAsync(Partial, _zip[..100_000]);

        var result = await InstallAsync(new FakeServer(_zip), free: 0);

        Assert.Equal(_pack.SpaceNeeded(0) - 100_000, result.SpaceNeeded);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AnArchiveTheServerWillNotServeIsUnavailable(HttpStatusCode status)
    {
        var result = await InstallAsync(new FakeServer(_zip) { Status = status });

        Assert.Equal(ArchiveOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task ADroppedConnectionKeepsWhatArrivedAndTheNextAttemptResumes()
    {
        var first = await InstallAsync(new FakeServer(_zip) { CutAfter = 150_000 });

        Assert.Equal(ArchiveOutcome.Interrupted, first.Outcome);
        Assert.Equal(150_000, new FileInfo(Partial).Length);

        var server = new FakeServer(_zip);
        var second = await InstallAsync(server);

        Assert.Equal(ArchiveOutcome.Installed, second.Outcome);
        Assert.Equal(150_000, Assert.Single(server.RangeStarts));
        Assert.Equal(_render, await File.ReadAllBytesAsync(Path.Combine(_root, "home", "6.png")));
    }

    [Fact]
    public async Task AFailedConnectionIsInterrupted()
    {
        var result = await InstallAsync(new FakeServer(_zip) { Refuse = true });

        Assert.Equal(ArchiveOutcome.Interrupted, result.Outcome);
    }

    [Fact]
    public async Task AStalledDownloadIsInterrupted()
    {
        var result = await InstallAsync(new FakeServer(_zip) { StallAfter = 70_000 }, stall: TimeSpan.FromMilliseconds(200));

        Assert.Equal(ArchiveOutcome.Interrupted, result.Outcome);
        Assert.True(File.Exists(Partial));
    }

    [Fact]
    public async Task CancellingThrowsAndKeepsThePartialDownload()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            InstallAsync(new FakeServer(_zip) { StallAfter = 70_000 }, cancellationToken: cancel.Token));

        Assert.True(File.Exists(Partial));
        Assert.False(File.Exists(_pack.DoneMarker(_root)));
    }

    [Fact]
    public async Task ReportsProgressUpToTheWholeArchive()
    {
        var fractions = new List<double>();

        await InstallAsync(new FakeServer(_zip), onProgress: (_, fraction) => fractions.Add(fraction));

        Assert.NotEmpty(fractions);
        Assert.All(fractions, f => Assert.InRange(f, 0, 1));
        Assert.Equal(1, fractions[^1]);
    }

    private static byte[] RandomBytes(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static byte[] BuildZip(Dictionary<string, byte[]> entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var stream = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
                stream.Write(content);
            }
        }
        return buffer.ToArray();
    }

    /// <summary>Serves one file over fake HTTP, with byte ranges, refusals, cuts and stalls on demand.</summary>
    private sealed class FakeServer(byte[] body) : HttpMessageHandler
    {
        public bool SupportsRange { get; init; } = true;
        public HttpStatusCode? Status { get; init; }
        public bool Refuse { get; init; }
        public int? CutAfter { get; init; }
        public int? StallAfter { get; init; }
        public int Requests { get; private set; }
        public List<long> RangeStarts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (Refuse) throw new HttpRequestException("Connection refused");
            if (Status is { } status) return Task.FromResult(new HttpResponseMessage(status));

            var from = 0L;
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            if (request.Headers.Range?.Ranges.FirstOrDefault()?.From is { } start)
            {
                RangeStarts.Add(start);
                if (SupportsRange)
                {
                    from = start;
                    response.StatusCode = HttpStatusCode.PartialContent;
                }
            }
            response.Content = new StreamContent(new ScriptedStream(body[(int)from..], CutAfter, StallAfter));
            if (response.StatusCode == HttpStatusCode.PartialContent)
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, body.Length - 1, body.Length);
            return Task.FromResult(response);
        }
    }

    /// <summary>A body that throws after <c>cutAfter</c> bytes, or stops sending after <c>stallAfter</c>.</summary>
    private sealed class ScriptedStream(byte[] data, int? cutAfter, int? stallAfter) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var limit = Math.Min(data.Length, cutAfter ?? stallAfter ?? data.Length);
            if (_position >= limit && cutAfter is not null) throw new IOException("Connection reset");
            var count = Math.Min(buffer.Length, limit - _position);
            data.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (stallAfter is { } stall && _position >= stall)
                await Task.Delay(Timeout.Infinite, cancellationToken);
            return Read(buffer.Span);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
