using Arboryn.Infrastructure.FileSystem;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Arboryn.Tests.Integration;

public class ScanResilienceTests
{
    private static readonly ScanResilienceOptions Options =
        new(MaxAttempts: 3, BaseDelay: TimeSpan.FromMilliseconds(10), MaxDelay: TimeSpan.FromMilliseconds(40));

    [Fact]
    public void Execute_TransientThenSuccess_RetriesAndReturns()
    {
        var calls = 0;
        var sleeps = new List<TimeSpan>();
        var result = ScanResilience.Execute(() =>
        {
            calls++;
            if (calls < 3)
            {
                throw new IOException("réseau momentanément indisponible");
            }

            return 42;
        }, resilient: true, Options, NullLogger.Instance, "ctx", sleeps.Add);

        result.Should().Be(42);
        calls.Should().Be(3);
        sleeps.Should().HaveCount(2);                 // 2 ré-essais avant succès
        sleeps[1].Should().BeGreaterThan(sleeps[0]);  // back-off exponentiel
    }

    [Fact]
    public void Execute_AlwaysTransient_ThrowsAfterMaxAttempts()
    {
        var calls = 0;
        var sleeps = new List<TimeSpan>();

        var act = () => ScanResilience.Execute<int>(() =>
        {
            calls++;
            throw new IOException("timeout");
        }, resilient: true, Options, NullLogger.Instance, "ctx", sleeps.Add);

        act.Should().Throw<IOException>();
        calls.Should().Be(3);     // MaxAttempts
        sleeps.Should().HaveCount(2);
    }

    [Fact]
    public void Execute_NonResilient_DoesNotRetry()
    {
        var calls = 0;
        var sleeps = new List<TimeSpan>();

        var act = () => ScanResilience.Execute<int>(() =>
        {
            calls++;
            throw new IOException("blip");
        }, resilient: false, Options, NullLogger.Instance, "ctx", sleeps.Add);

        act.Should().Throw<IOException>();
        calls.Should().Be(1);     // pas de ré-essai pour un volume local
        sleeps.Should().BeEmpty();
    }

    [Fact]
    public void Execute_DefinitiveError_NotRetried()
    {
        var calls = 0;
        var sleeps = new List<TimeSpan>();

        var act = () => ScanResilience.Execute<int>(() =>
        {
            calls++;
            throw new FileNotFoundException("disparu");
        }, resilient: true, Options, NullLogger.Instance, "ctx", sleeps.Add);

        act.Should().Throw<FileNotFoundException>();
        calls.Should().Be(1);     // fichier absent = définitif, aucun ré-essai
        sleeps.Should().BeEmpty();
    }

    [Theory]
    [InlineData(typeof(IOException), true)]
    [InlineData(typeof(FileNotFoundException), false)]
    [InlineData(typeof(DirectoryNotFoundException), false)]
    [InlineData(typeof(UnauthorizedAccessException), false)]
    public void IsTransient_ClassifiesCorrectly(Type exceptionType, bool expected)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType)!;
        ScanResilience.IsTransient(ex).Should().Be(expected);
    }
}
