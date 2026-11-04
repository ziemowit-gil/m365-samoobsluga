using M365Samoobsluga.Core.Services;
using M365Samoobsluga.Graph.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace M365Samoobsluga.Tests;

public class AliasQueueTests
{
    sealed class Exec(Func<AliasChange, Task> act) : IAliasChangeExecutor
    {
        public bool AppliesImmediately => true;
        public List<AliasChange> Seen = [];
        public async Task<AliasOutcome> ExecuteAsync(AliasChange c, CancellationToken ct = default) { Seen.Add(c); await act(c); return AliasOutcome.Applied; }
    }

    static string Line(string upn, string kind, string addr) => $"{{\"at\":\"2026-11-04T10:00:00Z\",\"UserPrincipalName\":\"{upn}\",\"kind\":\"{kind}\",\"Address\":\"{addr}\"}}";
    static string Tmp() => Path.Combine(Path.GetTempPath(), "aq-" + Guid.NewGuid().ToString("N") + ".jsonl");

    [Fact]
    public async Task Wykonane_wnioski_znikaja_z_kolejki()
    {
        var p = Tmp(); File.WriteAllLines(p, [Line("a@feer.org.pl", "Add", "x@feer.org.pl"), Line("b@feer.org.pl", "Remove", "y@feer.org.pl")]);
        var e = new Exec(_ => Task.CompletedTask);
        var r = await AliasQueueProcessor.ProcessAsync(p, e, NullLogger.Instance);
        Assert.Equal(new AliasQueueResult(2, 0, 0), r);
        Assert.Empty(File.ReadAllLines(p));
        Assert.Equal(AliasChangeKind.Remove, e.Seen[1].Kind);
    }

    [Fact]
    public async Task Brak_Exchange_zostawia_kolejke_i_przerywa()
    {
        var p = Tmp(); File.WriteAllLines(p, [Line("a@feer.org.pl", "Add", "x@feer.org.pl"), Line("b@feer.org.pl", "Add", "y@feer.org.pl")]);
        var e = new Exec(_ => throw new InvalidOperationException("brak"));
        var r = await AliasQueueProcessor.ProcessAsync(p, e, NullLogger.Instance);
        Assert.Equal(new AliasQueueResult(0, 0, 2), r);
        Assert.Equal(2, File.ReadAllLines(p).Length);
        Assert.Single(e.Seen);
    }

    [Fact]
    public async Task Odrzucony_adres_zajety_jest_usuwany_a_reszta_wykonana()
    {
        var p = Tmp(); File.WriteAllLines(p, [Line("a@feer.org.pl", "Add", "zajety@feer.org.pl"), Line("b@feer.org.pl", "Add", "wolny@feer.org.pl")]);
        var e = new Exec(c => c.Address.StartsWith("zajety") ? throw new UserFacingException("zajęty") : Task.CompletedTask);
        var r = await AliasQueueProcessor.ProcessAsync(p, e, NullLogger.Instance);
        Assert.Equal(new AliasQueueResult(1, 1, 0), r);
        Assert.Empty(File.ReadAllLines(p));
    }

    [Fact]
    public async Task Nieczytelny_wpis_jest_pomijany_i_usuwany()
    {
        var p = Tmp(); File.WriteAllLines(p, ["to nie json", Line("a@feer.org.pl", "Add", "x@feer.org.pl")]);
        var r = await AliasQueueProcessor.ProcessAsync(p, new Exec(_ => Task.CompletedTask), NullLogger.Instance);
        Assert.Equal(new AliasQueueResult(1, 1, 0), r);
    }

    [Fact]
    public async Task Brak_pliku_to_pusta_kolejka() =>
        Assert.Equal(new AliasQueueResult(0, 0, 0), await AliasQueueProcessor.ProcessAsync(Tmp(), new Exec(_ => Task.CompletedTask), NullLogger.Instance));
}
