using M365Samoobsluga.Core.Audit;
using M365Samoobsluga.Core.Services;
using M365Samoobsluga.Graph.Services;
using M365Samoobsluga.Web.Auth;
using M365Samoobsluga.Web.Components;
using M365Samoobsluga.Web.Mocks;
using M365Samoobsluga.Web.Setup;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

var builder = WebApplication.CreateBuilder(args);
var opts = builder.Configuration.GetSection("Samoobsluga");
var useMock = opts.GetValue<bool>("UseMockServices");
var dataDir = opts.GetValue<string>("DataDirectory") ?? "App_Data";

// Tenant ID i Client ID mogą pochodzić z konfiguracji albo ze strony /setup (plik App_Data/azuread.json, bez sekretów).
// Plik wczytujemy na końcu, więc ma pierwszeństwo przed appsettings.json.
builder.Configuration.AddJsonFile(Path.GetFullPath(Path.Combine(dataDir, "azuread.json")), optional: true, reloadOnChange: false);
var configured = AzureSetup.IsConfigured(builder.Configuration["AzureAd:TenantId"], builder.Configuration["AzureAd:ClientId"]);
var setupMode = !useMock && !configured;
builder.Services.AddSingleton(new SetupState(setupMode, setupMode ? SetupState.NewCode() : ""));

// Za reverse proxy (nginx) na tym samym hoście: bierzemy schemat i adres klienta z nagłówków X-Forwarded-*.
// Domyślnie ufamy tylko proxy na loopbacku. Inne adresy proxy dodaj w KnownProxies, zanim je włączysz.
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
    o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();

if (!useMock && configured)
{
    // Sekrety (ClientSecret / certyfikat) wyłącznie z User Secrets lub zmiennych środowiskowych, nigdy z repozytorium.
    var scopes = builder.Configuration.GetSection("DownstreamApis:MicrosoftGraph:Scopes").Get<string[]>() ?? [];
    builder.Services.AddMicrosoftIdentityWebAppAuthentication(builder.Configuration, "AzureAd")
        .EnableTokenAcquisitionToCallDownstreamApi(scopes)
        .AddMicrosoftGraph(builder.Configuration.GetSection("DownstreamApis:MicrosoftGraph"))
        .AddInMemoryTokenCaches();
    builder.Services.AddControllersWithViews().AddMicrosoftIdentityUI();
    // Niezalogowani trafiają na stronę powitalną /logowanie (przycisk prowadzi do Microsoftu), a nie od razu do logowania Microsoft.
    builder.Services.Configure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(o => o.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
    builder.Services.PostConfigure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme, o =>
    { o.LoginPath = "/logowanie"; o.AccessDeniedPath = "/logowanie"; });
    // Błąd w powrocie z logowania: zapisujemy przyczynę w logu i pokazujemy czytelną stronę zamiast ogólnego ekranu błędu.
    builder.Services.PostConfigure<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions>(
        Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectDefaults.AuthenticationScheme, o =>
        {
            o.Events.OnRemoteFailure = ctx =>
            {
                var message = ctx.Failure?.Message ?? "";
                var info = LoginErrors.Describe(message);
                ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Logowanie")
                    .LogError("Logowanie nie powiodło się ({Code}): {Message}", info.Code, message.Length > 400 ? message[..400] : message);
                ctx.HandleResponse();
                ctx.Response.Redirect("/logowanie-blad?kod=" + Uri.EscapeDataString(info.Code));
                return Task.CompletedTask;
            };
        });
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
else if (useMock)
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
// Wzór stopki: domyślne wartości z konfiguracji (Samoobsluga:Signature), zmiany administratora w pliku w katalogu danych.
var defaultTemplate = new SignatureTemplate(
    opts.GetSection("Signature:Org").Get<M365Samoobsluga.Core.Models.SignatureOrg>() ?? new(),
    opts.GetSection("Signature:Design").Get<M365Samoobsluga.Core.Models.SignatureDesign>() ?? new());
builder.Services.AddScoped<TemplateStoreAccessor>(sp => new TemplateStoreAccessor(
    useMock ? sp.GetRequiredService<MockTemplateStore>() : new FileSignatureTemplateStore(Path.Combine(dataDir, "signature-template.json"), defaultTemplate)));
builder.Services.AddScoped<ISignatureTemplateStore>(sp =>
    new AuditedSignatureTemplateStore(sp.GetRequiredService<TemplateStoreAccessor>().Raw, sp.GetRequiredService<IAuditLog>(), sp.GetRequiredService<ICurrentUser>()));

// Powiadomienia o stopce: wysyłka ze skrzynki zalogowanego administratora (Mail.Send, zakres dobierany dopiero przy wysyłce).
builder.Services.AddScoped<INoticeMailer>(sp => new AuditedNoticeMailer(
    useMock ? new MockNoticeMailer() : new GraphNoticeMailer(sp.GetRequiredService<Microsoft.Graph.GraphServiceClient>()),
    sp.GetRequiredService<IAuditLog>(), sp.GetRequiredService<ICurrentUser>()));

builder.Services.AddScoped<IDirectoryUsers>(sp => useMock ? new MockDirectoryUsers() : new GraphDirectoryUsers(sp.GetRequiredService<Microsoft.Graph.GraphServiceClient>()));

// Reguła stopki w Exchange: wdrożenie, usunięcie i czyszczenie podpisów z poziomu aplikacji przez PowerShell (certyfikat aplikacji).
var exchange = opts.GetSection("Exchange").Get<ExchangeSettings>() ?? new ExchangeSettings();
builder.Services.AddHttpClient();
builder.Services.AddSingleton(exchange);
var exchangeProvider = new ExchangeSettingsProvider(exchange, dataDir);
builder.Services.AddSingleton(exchangeProvider);
builder.Services.AddSingleton<IExchangeAutomationStore>(new FileExchangeAutomationStore(Path.Combine(dataDir, "exchange-automation.json")));
builder.Services.AddSingleton<PowerShellDeployerAccessor>(sp => new PowerShellDeployerAccessor(
    useMock ? new MockExchangeDeployer()
    : string.Equals(exchange.Mode, "PowerShell", StringComparison.OrdinalIgnoreCase)
        ? new PowerShellExchangeRuleDeployer(exchangeProvider, Path.Combine(AppContext.BaseDirectory, "scripts"), dataDir, sp.GetRequiredService<ILogger<PowerShellExchangeRuleDeployer>>())
        : new ExchangeRestRuleDeployer(exchangeProvider, dataDir, sp.GetRequiredService<IHttpClientFactory>())));
builder.Services.AddScoped<IExchangeRuleDeployer>(sp =>
    new AuditedExchangeRuleDeployer(sp.GetRequiredService<PowerShellDeployerAccessor>().Raw, sp.GetRequiredService<IAuditLog>(), sp.GetRequiredService<ICurrentUser>()));
if (!useMock && configured) builder.Services.AddHostedService<ExchangeRuleSync>();
if (useMock) builder.Services.AddSingleton(new MockTemplateStore(defaultTemplate));

// Administratorzy ustawień: Samoobsluga:Admins (UPN lub identyfikatory obiektu). W trybie demo administratorem jest konto demo.
var admins = new AdminList(useMock ? ["demo@feer.org.pl"] : opts.GetSection("Admins").Get<string[]>() ?? []);
builder.Services.AddSingleton(admins);
builder.Services.AddAuthorizationBuilder().AddPolicy("Admin", p => p.RequireAssertion(ctx =>
    admins.IsAdmin(new M365Samoobsluga.Core.Models.UserIdentity(
        ctx.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value ?? ctx.User.FindFirst("oid")?.Value ?? "",
        ctx.User.FindFirst("preferred_username")?.Value ?? "", null))));

var app = builder.Build();
app.UseForwardedHeaders();

if (setupMode)
{
    var state = app.Services.GetRequiredService<SetupState>();
    app.Logger.LogWarning("Aplikacja nie ma Tenant ID i Client ID. Otwórz /setup i wpisz je. Kod konfiguracji: {Code}", state.Code);
}
if (!useMock && configured)
{
    var types = builder.Configuration.GetSection("AzureAd:ClientCredentials").GetChildren().Select(c => c["SourceType"] ?? "").Where(t => t != "").ToList();
    foreach (var warning in CredentialPolicy.Evaluate(types, app.Environment.IsProduction()))
        app.Logger.LogWarning("{Warning}", warning);
    if (types.Count == 0)
    {
        // Bez sekretu lub certyfikatu wymiana kodu logowania na tokeny zawsze się nie uda, a użytkownik krążyłby w pętli logowania.
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/_framework") || ctx.Request.Path.Value!.Contains('.')) { await next(); return; }
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync("<!DOCTYPE html><html lang=\"pl\"><head><meta charset=\"utf-8\"><title>Brak poświadczeń aplikacji</title></head><body style=\"font-family:sans-serif;max-width:40rem;margin:2rem auto;padding:0 1rem;color:#1d1d1a\"><h1>Aplikacja nie jest jeszcze gotowa</h1><p>Brakuje sekretu klienta albo certyfikatu aplikacji. Administrator ustawia go w pliku <code>/etc/m365-samoobsluga.env</code> (zmienne <code>AzureAd__ClientCredentials__0__...</code>) i restartuje usługę. Szczegóły są w README, w sekcji o rejestracji aplikacji.</p></body></html>");
        });
    }
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
if (setupMode)
{
    // Do czasu konfiguracji działa tylko /setup (oraz zasoby techniczne).
    app.Use(async (ctx, next) =>
    {
        var path = ctx.Request.Path.Value ?? "/";
        if (path != "/setup" && !path.Contains('.') && !path.StartsWith("/_")) { ctx.Response.Redirect("/setup"); return; }
        await next();
    });
}
if (!setupMode) app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();
if (!useMock && configured) app.MapControllers();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();

public partial class Program;
