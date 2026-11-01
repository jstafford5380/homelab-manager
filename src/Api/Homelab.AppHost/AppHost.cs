var builder = DistributedApplication.CreateBuilder(args);
var api = builder.AddProject<Projects.Homelab_Api>("api", launchProfileName: "https")
    .WithEndpoint("http", endpoint => endpoint.Port = 5253)
    .WithEndpoint("https", endpoint => endpoint.Port = 7213);
builder.AddViteApp("web", "../../Web")
    .WithReference(api)
    .WithEnvironment("API_URL", api.GetEndpoint("http"))
    .WithEndpoint("http", endpoint => endpoint.Port = int.TryParse(builder.Configuration["Web:Port"], out var port) ? port : 26264)
    .WithExternalHttpEndpoints();
builder.Build().Run();
