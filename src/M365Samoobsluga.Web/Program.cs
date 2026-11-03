using M365Samoobsluga.Core.Audit;
using M365Samoobsluga.Core.Services;
using M365Samoobsluga.Graph.Services;
using M365Samoobsluga.Web.Auth;
using M365Samoobsluga.Web.Components;
using M365Samoobsluga.Web.Mocks;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

var builder = WebApplication.CreateBuilder(args);
var opts = builder.Configuration.GetSection("Samoobsluga");
var useMock = opts.GetValue<bool>("UseMockServices");
var dataDir = opts.GetValue<string>("DataDirectory") ?? "App_Data";

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();

if (!useMock)
{
    // Sekrety (ClientSecret / certyfikat) wyłącznie z User Secrets lub zmiennych środowiskowych, nigdy z repozytorium.
    var scopes = builder.Configuration.GetSection("DownstreamApis:MicrosoftGraph:Scopes").Get<string[]>() ?? [];
    builder.Services.AddMicrosoftIdentityWebAppAuthentication(builder.Configuration, "AzureAd")
        .EnableTokenAcquisitionToCallDownstreamApi(scopes)
        .AddMicrosoftGraph(builder.Configuration.GetSection("DownstreamApis:MicrosoftGraph"))
        .AddInMemoryTokenCaches();
    builder.Services.AddControllersWithViews().AddMicrosoftIdentityUI();
    builder.Services.AddScoped<MicrosoftIdentityConsentAndConditionalAccessHandler>();
    builder.Services.AddAuthorization(o => o.FallbackPolicy = o.DefaultPolicy);
    builder.Services.AddScoped<ICurrentUser, CurrentUser>();

    builder.Services.AddSingleton<IAliasChangeExecutor>(sp =>
        new FileQueueAliasChangeExecutor(opts.GetValue<string>("AliasRequestQueuePath") ?? "App_Data/alias-requests.jsonl", sp.GetRequiredService<ILogger<FileQueueAliasChangeExecutor>>()));
    builder.Services.AddScoped<GraphAliasService>();
    builder.Services.AddScoped<GraphAutoReplyService>();
    builder.Services.AddScoped<GraphForwardingService>(sp =>
        new GraphForwardingService(sp.GetRequiredService<Microsoft.Graph.GraphServiceClient>(), sp.GetRequiredService<GraphAliasService>()));
    builder.Services.AddScoped<GraphPasswordService>();
    builder.Services.AddScoped<GraphTwoFactorService>();
    builder.Services.AddScoped<ITwoFactorService>(sp => new AuditedTwoFactorService(sp.GetRequiredService<GraphTwoFactorService>(), sp.GetRequiredService<IAuditLog>(), sp.GetRequiredService<ICurrentUser>()));
    builder.Services.AddScoped<IAuditLog, LoggerAuditLog>();
    builder.Services.AddScoped<IAliasService>(sp => new AuditedAliasService(sp.GetRequiredService<GraphAliasService>(), sp.GetRequiredService<IAuditLog>(), sp.GetRequiredService<ICurrentUser>()));
    builder.Services.AddScoped<IAutoReplyService>(sp => new AuditedAutoReplyService(sp.GetRequiredService<GraphAutoReplyService>(), sp.GetRequiredService<IAuditLog>(), sp.GetRequiredService<ICurrentUser>()));
    builder.Services.AddScoped<IForwardingService>(sp => new AuditedForwardingService(sp.GetRequiredService<GraphForwardingService>(), sp.GetRequiredService<IAuditLog>(), sp.GetRequiredService<ICurrentUser>()));
    builder.Services.AddScoped<IPasswordService>(sp => new AuditedPasswordService(sp.GetRequiredService<GraphPasswordService>(), sp.GetRequiredService<IAuditLog>(), sp.GetRequiredService<ICurrentUser>()));
    builder.Services.AddScoped<GraphSignatureService>(sp => new GraphSignatureService(sp.GetRequiredService<Microsoft.Graph.GraphServiceClient>(), Path.Combine(dataDir, "signatures"), sp.GetRequiredService<ICurrentUser>()));
    builder.Services.AddScoped<ISignatureService>(sp => new AuditedSignatureService(sp.GetRequiredService<GraphSignatureService>(), sp.GetRequiredService<IAuditLog>(), sp.GetRequiredService<ICurrentUser>()));
}
else
{
    // Tryb lokalny bez tenanta: dane w pamięci, bez logowania. Nie używać na produkcji.
    builder.Services.AddAuthentication("Mock").AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, MockAuthHandler>("Mock", _ => { });
    builder.Services.AddAuthorization();
    builder.Services.AddScoped<ICurrentUser, CurrentUser>();
    builder.Services.AddSingleton<MockMailbox>();
    builder.Services.AddScoped<IAuditLog, LoggerAuditLog>();
    builder.Services.AddScoped<IAliasService>(sp => new AuditedAliasService(new MockAliasService(sp.GetRequiredService<MockMailbox>()), sp.GetRequiredService<IAuditLog>(), sp.GetRequiredService<ICurrentUser>()));
    builder.Services.AddScoped<IAutoReplyService>(sp => new AuditedAutoReplyService(new MockAutoReplyService(sp.GetRequiredService<MockMailbox>()), sp.GetRequiredService<IAuditLog>(), sp.GetRequiredService<ICurrentUser>()));
    builder.Services.AddScoped<IForwardingService>(sp => new AuditedForwardingService(new MockForwardingService(sp.GetRequiredService<MockMailbox>(), sp.GetRequiredService<IAliasService>()), sp.GetRequiredService<IAuditLog>(), sp.GetRequiredService<ICurrentUser>()));
    builder.Services.AddSingleton<MockTwoFactorService>();
    builder.Services.AddScoped<ITwoFactorService>(sp => new AuditedTwoFactorService(sp.GetRequiredService<MockTwoFactorService>(), sp.GetRequiredService<IAuditLog>(), sp.GetRequiredService<ICurrentUser>()));
    builder.Services.AddScoped<IPasswordService>(sp => new AuditedPasswordService(new MockPasswordService(), sp.GetRequiredService<IAuditLog>(), sp.GetRequiredService<ICurrentUser>()));
    builder.Services.AddScoped<ISignatureService>(sp => new AuditedSignatureService(new MockSignatureService(sp.GetRequiredService<MockMailbox>()), sp.GetRequiredService<IAuditLog>(), sp.GetRequiredService<ICurrentUser>()));
}
builder.Services.AddScoped<UiGuard>();
// Szablon stopki organizacji (dane i wygląd) ustawia administrator w konfiguracji: Samoobsluga:Signature:Org i :Design.
builder.Services.AddSingleton(new SignatureTemplate(
    opts.GetSection("Signature:Org").Get<M365Samoobsluga.Core.Models.SignatureOrg>() ?? new(),
    opts.GetSection("Signature:Design").Get<M365Samoobsluga.Core.Models.SignatureDesign>() ?? new()));

var app = builder.Build();

if (!useMock)
{
    var types = builder.Configuration.GetSection("AzureAd:ClientCredentials").GetChildren().Select(c => c["SourceType"] ?? "").Where(t => t != "").ToList();
    foreach (var warning in CredentialPolicy.Evaluate(types, app.Environment.IsProduction()))
        app.Logger.LogWarning("{Warning}", warning);
    CertificateInspector.LogExpiry(builder.Configuration, app.Logger, DateTimeOffset.UtcNow);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
if (!useMock) app.MapControllers();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();

public partial class Program;
