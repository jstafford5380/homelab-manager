var builder = DistributedApplication.CreateBuilder(args);
var api = builder.AddProject<Projects.Homelab_Api>("api");
builder.AddViteApp("web", "../homelab-web")
    .WithReference(api)
    .WithEnvironment("API_URL", api.GetEndpoint("http"))
    .WithExternalHttpEndpoints();
builder.Build().Run();
