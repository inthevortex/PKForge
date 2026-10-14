using System.Globalization;
using System.Net;
using PKForge.App.Services;
using Xunit;

namespace PKForge.App.Tests;

public sealed class ItemIconMissesTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "pkforge-items-" + Guid.NewGuid().ToString("N"));
    private readonly string _icon;

    public ItemIconMissesTests()
    {
        Directory.CreateDirectory(_root);
        _icon = Path.Combine(_root, "master-ball.png");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void ANotFoundAnswerMeansTheIconIsAbsent() =>
        Assert.Equal(ItemMissKind.Absent, ItemIconMisses.KindOf(new HttpRequestException("gone", null, HttpStatusCode.NotFound)));

    [Fact]
    public void AnyOtherErrorIsAFailure()
    {
        Exception[] failures =
        [
            new HttpRequestException("server error", null, HttpStatusCode.InternalServerError),
            new HttpRequestException("no connection"),
            new TaskCanceledException("timed out"),
            new IOException("disk full"),
        ];

        Assert.All(failures, error => Assert.Equal(ItemMissKind.Failed, ItemIconMisses.KindOf(error)));
    }

    [Theory]
    [InlineData(nameof(ItemMissKind.Absent), false, true)]
    [InlineData(nameof(ItemMissKind.Absent), true, true)]
    [InlineData(nameof(ItemMissKind.Failed), false, false)]
    [InlineData(nameof(ItemMissKind.Failed), true, true)]
    public void ThePackKeepsOnlyAbsencesWhileScreensKeepFailuresToo(string kind, bool rememberFailures, bool holds) =>
        Assert.Equal(holds, ItemIconMisses.Holds(Enum.Parse<ItemMissKind>(kind), rememberFailures));

    [Theory]
    [InlineData(nameof(ItemMissKind.Absent))]
    [InlineData(nameof(ItemMissKind.Failed))]
    public async Task AWrittenMissReadsBackWithItsKind(string kindName)
    {
        var kind = Enum.Parse<ItemMissKind>(kindName);
        await ItemIconMisses.WriteAsync(ItemIconMisses.MissPath(_icon), kind, Now);

        Assert.Equal(kind, ItemIconMisses.ReadFresh(ItemIconMisses.MissPath(_icon), Now.AddHours(1)));
    }

    [Fact]
    public async Task AnAbsenceSettlesTheIconForThePack()
    {
        await ItemIconMisses.WriteAsync(ItemIconMisses.MissPath(_icon), ItemMissKind.Absent, Now);

        Assert.True(ItemIconMisses.IsSettled(_icon, rememberFailures: false, Now));
    }

    [Fact]
    public async Task AFailureSettlesTheIconForScreensButNotForThePack()
    {
        await ItemIconMisses.WriteAsync(ItemIconMisses.MissPath(_icon), ItemMissKind.Failed, Now);

        Assert.False(ItemIconMisses.IsSettled(_icon, rememberFailures: false, Now));
        Assert.True(ItemIconMisses.IsSettled(_icon, rememberFailures: true, Now));
    }

    [Fact]
    public async Task AMissThatRecordsOnlyItsTimeCountsAsAFailure()
    {
        var miss = ItemIconMisses.MissPath(_icon);
        await File.WriteAllTextAsync(miss, Now.ToString("O", CultureInfo.InvariantCulture));

        Assert.Equal(ItemMissKind.Failed, ItemIconMisses.ReadFresh(miss, Now));
        Assert.False(ItemIconMisses.IsSettled(_icon, rememberFailures: false, Now));
    }

    [Fact]
    public async Task AnExpiredMissIsDeletedSoTheIconIsAskedForAgain()
    {
        var miss = ItemIconMisses.MissPath(_icon);
        await ItemIconMisses.WriteAsync(miss, ItemMissKind.Absent, Now);

        Assert.Null(ItemIconMisses.ReadFresh(miss, Now + ItemIconMisses.Lifetime));
        Assert.False(File.Exists(miss));
        Assert.False(ItemIconMisses.IsSettled(_icon, rememberFailures: true, Now + ItemIconMisses.Lifetime));
    }

    [Fact]
    public async Task AnUnreadableMissIsIgnored()
    {
        var miss = ItemIconMisses.MissPath(_icon);
        await File.WriteAllTextAsync(miss, "Absent\nnot a time");

        Assert.Null(ItemIconMisses.ReadFresh(miss, Now));
        Assert.False(ItemIconMisses.IsSettled(_icon, rememberFailures: true, Now));
    }

    [Fact]
    public async Task AnIconOnDiskIsSettledWhateverItsMissSays()
    {
        await File.WriteAllBytesAsync(_icon, ItemIconFileTests.Png());
        await ItemIconMisses.WriteAsync(ItemIconMisses.MissPath(_icon), ItemMissKind.Failed, Now);

        Assert.True(ItemIconMisses.IsSettled(_icon, rememberFailures: false, Now));
    }

    [Fact]
    public async Task ATruncatedIconIsNotSettledSoThePackFetchesItAgain()
    {
        await File.WriteAllBytesAsync(_icon, ItemIconFileTests.Png()[..^4]);

        Assert.False(ItemIconMisses.IsSettled(_icon, rememberFailures: false, Now));
        Assert.False(File.Exists(_icon));
    }

    [Fact]
    public async Task ATruncatedIconWithAFreshAbsenceStaysSettled()
    {
        await File.WriteAllTextAsync(_icon, "<html>rate limited</html>");
        await ItemIconMisses.WriteAsync(ItemIconMisses.MissPath(_icon), ItemMissKind.Absent, Now);

        Assert.True(ItemIconMisses.IsSettled(_icon, rememberFailures: false, Now));
    }

    [Fact]
    public void AnIconWithNoFileAndNoMissIsNotSettled() =>
        Assert.False(ItemIconMisses.IsSettled(_icon, rememberFailures: true, Now));

    [Theory]
    [InlineData(0, true, true)]
    [InlineData(1, true, false)]
    [InlineData(0, false, false)]
    [InlineData(3, false, false)]
    public void ThePackIsMarkedCompleteOnlyWhenNoItemIconFailed(int itemFailures, bool spritesInPlace, bool complete) =>
        Assert.Equal(complete, ItemIconMisses.PackCanBeMarkedComplete(itemFailures, spritesInPlace));
}
