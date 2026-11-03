using Homelab.Api.Infrastructure.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddHomelabApi();
var app = builder.Build();
app.UseHomelabApi();
app.Run();
public partial class Program;
