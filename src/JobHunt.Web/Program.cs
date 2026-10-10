using System.Net;
using JobHunt.MailBot.Services;
using JobHunt.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);
// This first version is a personal application. Network access requires a separate authenticated deployment.
var port = builder.Configuration.GetValue("JobHunt:Port", 5080);
if (port is < 1024 or > 65535) throw new InvalidOperationException("JobHunt:Port deve estar entre 1024 e 65535.");
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
var root = builder.Configuration["JobHunt:DataDirectory"] ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JobHunt.Web");
var store = new WebStore(root);
using var certificate = WebCredentials.LoadCertificate(store.Root);
var keys = Path.Combine(store.Root, "keys");
WebStore.PrivateDirectory(keys);
builder.Services.AddDataProtection().SetApplicationName("JobHunt.Web")
    .PersistKeysToFileSystem(new DirectoryInfo(keys)).ProtectKeysWithCertificate(certificate);
builder.Services.AddSingleton(store);
builder.Services.AddSingleton<WebFiles>();
builder.Services.AddSingleton<WebCredentials>();
builder.Services.AddSingleton<Func<string, IEmailSender>>(_ => password => new GmailSender(new AppPasswordAuthentication(password)));
builder.Services.AddSingleton<WebBatchWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<WebBatchWorker>());
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(60));
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 12 * 1024 * 1024);
builder.Services.AddRazorPages().AddMvcOptions(o => o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);
builder.Services.AddAntiforgery(o => { o.Cookie.Name = "JobHunt.Antiforgery"; o.Cookie.SameSite = SameSiteMode.Strict; });
var app = builder.Build();
app.Use(async (context, next) =>
{
    var address = context.Connection.RemoteIpAddress;
    var host = context.Request.Host.Host;
    if (address is null || !IPAddress.IsLoopback(address) ||
        !(host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(host, out var parsed) && IPAddress.IsLoopback(parsed)))
    {
        context.Response.StatusCode = 403;
        return;
    }
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; style-src 'self'; script-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
app.UseExceptionHandler("/Error");
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.Run();

public partial class Program;
