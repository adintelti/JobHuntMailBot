using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using JobHunt.MailBot.Models;
using JobHunt.MailBot.Services;
using JobHunt.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Web.Tests;

public sealed class WebHttpTests : IDisposable
{
    private readonly TestDirectory _directory = new();
    private readonly Factory _factory;
    private readonly HttpClient _client;
    public WebHttpTests()
    {
        _factory = new Factory(_directory.Path);
        _client = _factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("http://localhost") });
    }
    public void Dispose() { _client.Dispose(); _factory.Dispose(); _directory.Dispose(); }

    [Theory]
    [InlineData("/")]
    [InlineData("/Batch")]
    [InlineData("/Settings")]
    [InlineData("/History")]
    public async Task PagesRenderWithoutCredentials(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("JobHunt", await response.Content.ReadAsStringAsync());
        Assert.Equal(_directory.Path, _factory.Services.GetRequiredService<WebStore>().Root);
    }

    [Fact]
    public async Task MissingAntiforgeryTokenCannotWriteFile()
    {
        var response = await _client.PostAsync("/?handler=Add", new FormUrlEncodedContent(new Dictionary<string, string> { ["FileName"] = "vagas.txt", ["Entry"] = "anything" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await _factory.Services.GetRequiredService<WebFiles>().ListAsync());
    }

    [Fact]
    public async Task ForeignHostIsForbidden()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "evil.example";
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task AddDownloadAndDuplicateValidationPreserveExactFile()
    {
        var entry = "Email : recrutamento@example.com\nAssunto a ser colocado no email : Vaga ação\n";
        var form = new Dictionary<string, string> { ["FileName"] = "vagas.txt", ["Entry"] = entry, ["__RequestVerificationToken"] = await TokenAsync("/") };
        Assert.Equal(HttpStatusCode.Redirect, (await _client.PostAsync("/?handler=Add", new FormUrlEncodedContent(form))).StatusCode);
        var expected = Encoding.UTF8.GetBytes(JobTextCodec.Serialize(new("recrutamento@example.com", "Vaga ação")));
        Assert.Equal(expected, await _client.GetByteArrayAsync("/?handler=Download&file=vagas.txt"));
        var duplicate = await _client.PostAsync("/?handler=Add", new FormUrlEncodedContent(form));
        Assert.Contains("Este email e assunto", await duplicate.Content.ReadAsStringAsync());
        Assert.Equal(expected, await _client.GetByteArrayAsync("/?handler=Download&file=vagas.txt"));
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/?handler=Download&file=..%2Fweb-state.json")).StatusCode);
    }

    [Fact]
    public async Task SettingsNeverEchoPasswordAndBatchRequiresReviewConfirmation()
    {
        const string password = "abcdefghijklmnop";
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await TokenAsync("/Settings"),
            ["Input.Email"] = "me@example.com", ["Input.Body"] = "Minha candidatura",
            ["Input.JobsDirectory"] = System.IO.Path.Combine(_directory.Path, "jobs"),
            ["Input.DelaySeconds"] = "0", ["Input.MaxRetries"] = "0", ["Input.TimeoutSeconds"] = "30",
            ["Input.RememberPassword"] = "true", ["Password"] = password
        };
        var saved = await _client.PostAsync("/Settings?handler=Save", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.DoesNotContain(password, await _client.GetStringAsync("/Settings"));
        Assert.DoesNotContain(password, await File.ReadAllTextAsync(System.IO.Path.Combine(_directory.Path, "web-state.json")));
        var files = _factory.Services.GetRequiredService<WebFiles>();
        var offer = new JobOffer("recipient@example.com", "Vaga");
        await files.AppendAsync("vagas.txt", JobTextCodec.Serialize(offer));
        var batchForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await TokenAsync("/Batch?file=vagas.txt"),
            ["file"] = "vagas.txt", ["Selected"] = offer.Key
        };
        var rejected = await _client.PostAsync("/Batch?handler=Send", new FormUrlEncodedContent(batchForm));
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Empty((await _factory.Services.GetRequiredService<WebStore>().ReadAsync()).Batches);
        batchForm["Confirm"] = "true";
        Assert.Equal(HttpStatusCode.Redirect, (await _client.PostAsync("/Batch?handler=Send", new FormUrlEncodedContent(batchForm))).StatusCode);
    }

    private async Task<string> TokenAsync(string page)
    {
        var html = await _client.GetStringAsync(page);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Antiforgery token missing");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private sealed class Factory(string root) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("JobHunt:DataDirectory", root);
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IStartupFilter, LocalTestAddress>();
                services.AddSingleton<Func<string, IEmailSender>>(_ => _ => new FakeSender());
            });
        }
    }
    private sealed class FakeSender : IEmailSender
    {
        public Task<string> SendAsync(JobOffer offer, MailContent content, CancellationToken cancellationToken) => Task.FromResult("Simulado; nenhum email enviado.");
    }
    private sealed class LocalTestAddress : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, following) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; return following(context); });
            next(app);
        };
    }
}
