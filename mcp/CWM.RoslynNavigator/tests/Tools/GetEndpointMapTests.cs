using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;
using Microsoft.CodeAnalysis.CSharp;

namespace CWM.RoslynNavigator.Tests.Tools;

[Collection(SampleSolutionCollection.Name)]
public class GetEndpointMapTests(TestSolutionFixture fixture)
{
    private static List<EndpointEntry> Analyze(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        return GetEndpointMapTool.AnalyzeRoot(root, "Test.cs");
    }

    [Fact]
    public void Analyze_SimpleMapGet_ReportsUnmarkedEndpoint()
    {
        var endpoints = Analyze("""app.MapGet("/health", () => "ok");""");

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("GET", endpoint.Method);
        Assert.Equal("/health", endpoint.Route);
        Assert.Equal("unmarked", endpoint.Auth);
        Assert.Equal("minimal-api", endpoint.Kind);
        Assert.Equal(1, endpoint.Line);
    }

    [Fact]
    public void Analyze_GroupVariable_ComposesPrefix()
    {
        var endpoints = Analyze("""
            var group = app.MapGroup("/api/orders");
            group.MapGet("/", ListOrders);
            group.MapPost("/{id}", CreateOrder).RequireAuthorization();
            """);

        Assert.Equal(2, endpoints.Count);
        Assert.Equal("/api/orders", endpoints[0].Route);
        Assert.Equal("unmarked", endpoints[0].Auth);
        Assert.Equal("/api/orders/{id}", endpoints[1].Route);
        Assert.Equal("authorized", endpoints[1].Auth);
    }

    [Fact]
    public void Analyze_ChainedMapGroup_ComposesPrefix()
    {
        var endpoints = Analyze("""app.MapGroup("/api/products").MapGet("/list", ListProducts);""");

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("/api/products/list", endpoint.Route);
    }

    [Fact]
    public void Analyze_GroupLevelRequireAuthorization_InheritsToEndpoints()
    {
        var endpoints = Analyze("""
            var admin = app.MapGroup("/admin").RequireAuthorization();
            admin.MapGet("/stats", GetStats);
            """);

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("/admin/stats", endpoint.Route);
        Assert.Equal("authorized", endpoint.Auth);
    }

    [Fact]
    public void Analyze_NestedGroups_ComposePrefixes()
    {
        var endpoints = Analyze("""
            var api = app.MapGroup("/api");
            var orders = api.MapGroup("/orders");
            orders.MapDelete("/{id}", DeleteOrder);
            """);

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("DELETE", endpoint.Method);
        Assert.Equal("/api/orders/{id}", endpoint.Route);
    }

    [Fact]
    public void Analyze_FluentAllowAnonymous_ReportsAnonymous()
    {
        var endpoints = Analyze("""app.MapPost("/login", Login).AllowAnonymous();""");

        Assert.Equal("anonymous", Assert.Single(endpoints).Auth);
    }

    [Fact]
    public void Analyze_NonLiteralRoute_ReportsQuestionMark()
    {
        var endpoints = Analyze("""app.MapGet(routeFromConfig, Handler);""");

        Assert.Equal("?", Assert.Single(endpoints).Route);
    }

    [Fact]
    public void Analyze_Controller_ComposesRouteAndAuth()
    {
        var endpoints = Analyze("""
            [ApiController]
            [Route("api/[controller]")]
            [Authorize]
            public class OrdersController : ControllerBase
            {
                [HttpGet]
                public IActionResult List() => Ok();

                [HttpPost("create")]
                [AllowAnonymous]
                public IActionResult Create() => Ok();
            }
            """);

        Assert.Equal(2, endpoints.Count);

        var list = endpoints.Single(e => e.Method == "GET");
        Assert.Equal("api/Orders", list.Route);
        Assert.Equal("authorized", list.Auth); // inherited from class
        Assert.Equal("controller", list.Kind);

        var create = endpoints.Single(e => e.Method == "POST");
        Assert.Equal("api/Orders/create", create.Route);
        Assert.Equal("anonymous", create.Auth); // method attribute wins
    }

    [Fact]
    public void Analyze_ControllerWithoutAuthAttributes_ReportsUnmarked()
    {
        var endpoints = Analyze("""
            public class HealthController : ControllerBase
            {
                [HttpGet("/health")]
                public IActionResult Get() => Ok();
            }
            """);

        Assert.Equal("unmarked", Assert.Single(endpoints).Auth);
    }

    [Fact]
    public void Analyze_BaseControllerInAnotherFile_InheritsControllerRouteAndAuth()
    {
        // Jellyfin's shape: [ApiController] and [Route("[controller]")] live on a base class in
        // another file, and the derived controllers name neither ControllerBase nor ApiController.
        var baseRoot = CSharpSyntaxTree.ParseText("""
            [ApiController]
            [Route("[controller]")]
            [Authorize]
            public class BaseApiController : ControllerBase { }
            """).GetRoot(TestContext.Current.CancellationToken);
        var root = CSharpSyntaxTree.ParseText("""
            public class ItemsController : BaseApiController
            {
                [HttpGet("{id}")]
                public IActionResult Get(int id) => Ok();
            }
            """).GetRoot(TestContext.Current.CancellationToken);
        var index = new ControllerIndex();
        index.Add(ControllerIndex.Collect(baseRoot));
        index.Add(ControllerIndex.Collect(root));

        var endpoint = Assert.Single(GetEndpointMapTool.AnalyzeRoot(root, "Items.cs", index));

        Assert.Equal("Items/{id}", endpoint.Route);
        Assert.Equal("authorized", endpoint.Auth);
        Assert.Equal("controller", endpoint.Kind);
    }

    [Fact]
    public void Analyze_DerivedRouteOverridesBaseRoute_UsesNearest()
    {
        var endpoints = Analyze("""
            [ApiController]
            [Route("[controller]")]
            public class BaseApiController : ControllerBase { }

            [Route("")]
            public class ImageController : BaseApiController
            {
                [HttpPost("Users/{userId}/Images")]
                public IActionResult Post() => Ok();
            }
            """);

        Assert.Equal("Users/{userId}/Images", Assert.Single(endpoints).Route);
    }

    [Fact]
    public void Analyze_ClassWithoutControllerAncestry_IsIgnored()
    {
        var endpoints = Analyze("""
            public class Base { }
            public class NotAController : Base
            {
                [HttpGet("x")]
                public void Get() { }
            }
            """);

        Assert.Empty(endpoints);
    }

    [Fact]
    public void Analyze_StackedHttpAttributes_ReportsOneEndpointEach()
    {
        var endpoints = Analyze("""
            [Route("videos")]
            public class VideosController : ControllerBase
            {
                [HttpGet("{id}/stream")]
                [HttpHead("{id}/stream")]
                public IActionResult Stream(int id) => Ok();
            }
            """);

        Assert.Equal(["GET", "HEAD"], endpoints.Select(e => e.Method));
        Assert.All(endpoints, e => Assert.Equal("videos/{id}/stream", e.Route));
    }

    [Fact]
    public void Analyze_ActionTokenAndAbsoluteTemplate_ResolveLikeAspNetCore()
    {
        var endpoints = Analyze("""
            [Route("api/[controller]")]
            public class OrdersController : ControllerBase
            {
                [HttpGet("[action]")]
                public IActionResult Recent() => Ok();

                [HttpGet("~/status")]
                public IActionResult Status() => Ok();
            }
            """);

        Assert.Equal(["api/Orders/Recent", "/status"], endpoints.Select(e => e.Route));
    }

    [Fact]
    public async Task GetEndpointMap_SampleSolutionHasNoEndpoints_ReturnsEmpty()
    {
        var json = await GetEndpointMapTool.ExecuteAsync(
            fixture.WorkspaceManager, ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<EndpointMapResult>(json)!;

        Assert.Equal(0, result.Count);
        Assert.Equal(0, result.TotalFound);
    }
}
