using System.Net;
using System.Net.Http;
using System.Text;
using Tuinet;

namespace Tuinet.App.Tests;

public class AzureDevOpsProjectsTests
{
    [Fact]
    public void ListProjects_sorts_names_from_json()
    {
        var azure = new AzureDevOpsProjects(Client("""{"value":[{"name":"Beta"},{"name":"Alpha"}]}"""));
        string[] names = azure.ListProjects("acme", "token");
        Assert.Equal(["Alpha", "Beta"], names);
    }

    [Fact]
    public void ListProjects_throws_on_http_error()
    {
        var azure = new AzureDevOpsProjects(Client("{}", HttpStatusCode.Unauthorized));
        Assert.Throws<InvalidOperationException>(() => azure.ListProjects("acme", "token"));
    }

    private static HttpClient Client(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return new HttpClient(new StubHandler(response));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StubHandler(HttpResponseMessage response) => _response = response;

        protected override HttpResponseMessage Send(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            _response;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }
}
