using M365Samoobsluga.Core.Models;
using M365Samoobsluga.Core.Services;
using M365Samoobsluga.Graph.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph.Models.ODataErrors;

namespace M365Samoobsluga.Tests;

public class GraphServicesTests
{
    sealed class User(string oid) : ICurrentUser { public UserIdentity Current => new(oid, "x@feer.org.pl", null); }

    [Theory]
    [InlineData(403, "uprawnień")]
    [InlineData(401, "Zaloguj")]
    [InlineData(429, "przeciążona")]
    [InlineData(503, "niedostępna")]
    public void Mapper_tlumaczy_bledy_graph_na_polski(int status, string fragment)
    {
        var ex = GraphErrorMapper.Map(new ODataError { ResponseStatusCode = status }, "autoresponder");
        Assert.IsType<UserFacingException>(ex);
        Assert.Contains(fragment, ex.Message);
    }

    [Fact]
    public void Mapper_bledne_haslo_ma_czytelny_komunikat() =>
        Assert.Contains("zasady organizacji", GraphErrorMapper.Map(new ODataError { ResponseStatusCode = 400 }, "hasło").Message);

    [Fact]
    public void Mapper_nieznany_wyjatek_przechodzi_bez_zmian()
    {
        var inner = new InvalidOperationException();
        Assert.Same(inner, GraphErrorMapper.Map(inner, "x"));
    }

    [Fact]
    public async Task Kolejka_aliasow_dopisuje_wniosek_jako_json()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jsonl");
        try
        {
            var ex = new FileQueueAliasChangeExecutor(path, NullLogger<FileQueueAliasChangeExecutor>.Instance);
            await ex.ExecuteAsync(new AliasChange("jan@feer.org.pl", AliasChangeKind.Add, "jan.k@feer.org.pl"));
            var line = Assert.Single(await File.ReadAllLinesAsync(path));
            Assert.Contains("jan.k@feer.org.pl", line);
            Assert.False(ex.AppliesImmediately);
        }
        finally { File.Delete(path); }
    }
}
