using M365Samoobsluga.Core.Audit;
using M365Samoobsluga.Core.Models;
using M365Samoobsluga.Core.Services;

namespace M365Samoobsluga.Tests;

public class AuditTests
{
    sealed class MemLog : IAuditLog { public List<AuditEntry> E = []; public void Write(AuditEntry e) => E.Add(e); }
    sealed class User : ICurrentUser { public UserIdentity Current => new("oid-1", "jan@feer.org.pl", "Jan"); }
    sealed class Pwd(bool fail) : IPasswordService
    {
        public Task ChangeAsync(ChangePasswordRequest r, CancellationToken ct = default) =>
            fail ? throw new UserFacingException("Nie spełnia zasad.") : Task.CompletedTask;
    }

    [Fact]
    public async Task Zmiana_hasla_jest_audytowana_bez_hasel()
    {
        var log = new MemLog();
        await new AuditedPasswordService(new Pwd(false), log, new User()).ChangeAsync(new ChangePasswordRequest("StareTajne1!", "NoweTajne2!"));
        var e = Assert.Single(log.E);
        Assert.True(e.Success);
        Assert.Equal("jan@feer.org.pl", e.UserPrincipalName);
        var json = System.Text.Json.JsonSerializer.Serialize(e);
        Assert.DoesNotContain("StareTajne1!", json);
        Assert.DoesNotContain("NoweTajne2!", json);
    }

    [Fact]
    public async Task Blad_jest_audytowany_i_rzucany_dalej()
    {
        var log = new MemLog();
        await Assert.ThrowsAsync<UserFacingException>(() => new AuditedPasswordService(new Pwd(true), log, new User()).ChangeAsync(new ChangePasswordRequest("a", "b")));
        Assert.False(Assert.Single(log.E).Success);
    }

    [Fact]
    public void Sanitizer_usuwa_znaki_sterujace_i_ucina()
    {
        Assert.Equal("ab", AuditSanitizer.Clean("a\r\nb"));
        Assert.Equal(200, AuditSanitizer.Clean(new string('x', 500))!.Length);
        Assert.Null(AuditSanitizer.Clean(null));
    }
}
