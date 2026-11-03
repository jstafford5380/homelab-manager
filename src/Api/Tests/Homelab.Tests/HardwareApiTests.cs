using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Tests;
public sealed class HardwareApiTests
{
    [Fact] public async Task HardwareMutationsAreAuthenticatedScopedValidatedAndPreserveReplacementHistory()
    {
        var folder=Path.Combine(Path.GetTempPath(),"homelab-hardware-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var options=new DbContextOptionsBuilder<HomelabDb>().UseSqlite(new SqliteConnectionStringBuilder{DataSource=Path.Combine(folder,"homelab.db"),Pooling=false}.ToString()).Options;
        var a=new NasConnection{Name="A",Enabled=false};var b=new NasConnection{Name="B",Enabled=false};
        try{
            await using(var db=new HomelabDb(options)){await db.Database.MigrateAsync();db.Connections.AddRange(a,b);await db.SaveChangesAsync();}
            await using(var app=await Start(folder,true)){
                var client=app.Client;
                Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/v1/hardware/servers")).StatusCode);
                client.DefaultRequestHeaders.Add("X-Homelab-Request","1");
                (await client.PostAsJsonAsync("/api/v1/setup",new{username="owner",password="Hardware-fixture-only-123!",setupToken="hardware-fixture"})).EnsureSuccessStatusCode();
                (await client.PostAsJsonAsync("/api/v1/login",new{username="owner",password="Hardware-fixture-only-123!"})).EnsureSuccessStatusCode();
                (await client.GetAsync($"/api/v1/hardware/servers/{a.Id}?range=30d")).EnsureSuccessStatusCode(); // Deep links work before the tab list has initialized inventory.
                var list=await client.GetFromJsonAsync<JsonElement>("/api/v1/hardware/servers");Assert.Equal(2,list.GetProperty("servers").GetArrayLength());
                var body=new HardwareComponentInput(null,"power-supply","PSU","model","650 W","owner note",99,DateTime.UtcNow.AddYears(-1),DateTime.UtcNow.AddYears(-1),null,false);
                var created=await client.PostAsJsonAsync($"/api/v1/hardware/servers/{a.Id}/components",body);created.EnsureSuccessStatusCode();var cid=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
                var detail=await client.GetFromJsonAsync<HardwareDetail>($"/api/v1/hardware/servers/{a.Id}");var part=Assert.Single(detail!.Components);Assert.Equal("You entered",part.Source);
                Assert.Empty((await client.GetFromJsonAsync<HardwareDetail>($"/api/v1/hardware/servers/{b.Id}"))!.Components);
                Assert.Equal(HttpStatusCode.NotFound,(await client.PutAsJsonAsync($"/api/v1/hardware/servers/{b.Id}/components/{cid}",body with{Revision=part.Record.Revision})).StatusCode);
                Assert.Equal(HttpStatusCode.Conflict,(await client.PutAsJsonAsync($"/api/v1/hardware/servers/{a.Id}/components/{cid}",body)).StatusCode);
                var reading=new HardwareReadingInput(null,DateTime.UtcNow.AddHours(-1),"moment",null,22,null,null,36,"Spot reading");
                var path=$"/api/v1/hardware/servers/{a.Id}/readings";
                Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync(path,reading with{CpuPercent=101})).StatusCode);
                var response=await client.PostAsJsonAsync(path,reading);response.EnsureSuccessStatusCode();var row=(await response.Content.ReadFromJsonAsync<HardwareReading>())!;Assert.Equal("entered",row.Source);
                Assert.Equal(HttpStatusCode.NotFound,(await client.PutAsJsonAsync($"/api/v1/hardware/servers/{b.Id}/readings/{row.Id}",reading with{Revision=row.Revision})).StatusCode);
                var update=await client.PutAsJsonAsync(path+"/"+row.Id,reading with{Revision=row.Revision,PowerWatts=40});update.EnsureSuccessStatusCode();var updated=(await update.Content.ReadFromJsonAsync<HardwareReading>())!;
                Assert.Equal(HttpStatusCode.Conflict,(await client.DeleteAsync(path+"/"+row.Id+"?revision="+row.Revision)).StatusCode);
                (await client.DeleteAsync(path+"/"+row.Id+"?revision="+updated.Revision)).EnsureSuccessStatusCode();
                detail=await client.GetFromJsonAsync<HardwareDetail>($"/api/v1/hardware/servers/{a.Id}");Assert.Empty(detail!.Readings);
                (await client.PostAsJsonAsync($"/api/v1/hardware/servers/{a.Id}/components/{cid}/notes",new HardwareNoteInput("Kept the receipt"))).EnsureSuccessStatusCode();
                var replaced=await client.PostAsJsonAsync($"/api/v1/hardware/servers/{a.Id}/components/{cid}/replace",new HardwareReplaceInput(part.Record.Revision,body with{Name="New PSU"}));replaced.EnsureSuccessStatusCode();
                var next=(await replaced.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
                detail=await client.GetFromJsonAsync<HardwareDetail>($"/api/v1/hardware/servers/{a.Id}");Assert.Equal(next,Assert.Single(detail!.Components).Record.Id);
                var history=await client.GetFromJsonAsync<HardwareEvent[]>($"/api/v1/hardware/servers/{a.Id}/components/{cid}/history");Assert.Contains(history!,e=>e.Text=="Kept the receipt");Assert.Contains(history!,e=>e.Kind=="replaced");
                var synology=await client.PostAsJsonAsync("/api/v1/hardware/synology",new SynologyInput("DSM","192.0.2.123",null,false));synology.EnsureSuccessStatusCode();
                list=await client.GetFromJsonAsync<JsonElement>("/api/v1/hardware/servers");Assert.Equal(3,list.GetProperty("servers").GetArrayLength());
                Assert.DoesNotContain("protectedCommunity",list.GetRawText());
                client.DefaultRequestHeaders.Remove("X-Homelab-Request");Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync(path,reading)).StatusCode);
            }
            await using(var app=await Start(folder,false)){
                app.Client.DefaultRequestHeaders.Add("X-Homelab-Request","1");(await app.Client.PostAsJsonAsync("/api/v1/login",new{username="owner",password="Hardware-fixture-only-123!"})).EnsureSuccessStatusCode();
                var list=await app.Client.GetFromJsonAsync<JsonElement>("/api/v1/hardware/servers");Assert.Equal(2,list.GetProperty("servers").GetArrayLength());Assert.False(list.GetProperty("synology").GetBoolean());
                Assert.Equal(HttpStatusCode.NotFound,(await app.Client.GetAsync("/api/v1/hardware/synology/settings")).StatusCode);
            }
        }finally{SqliteConnection.ClearAllPools();Directory.Delete(folder,true);}
    }
    private sealed class Running(Process process,HttpClient client):IAsyncDisposable
    {
        public HttpClient Client=>client;
        public async ValueTask DisposeAsync(){client.Dispose();if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}process.Dispose();}
    }
    private static async Task<Running> Start(string folder,bool synology)
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null && !File.Exists(Path.Combine(root.FullName,"Homelab.sln")))root=root.Parent;
        Assert.NotNull(root);
#if DEBUG
        const string mode="Debug";
#else
        const string mode="Release";
#endif
        var binary=Path.Combine(root.FullName,"Homelab.Api","bin",mode,"net10.0","Homelab.Api.dll");
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
        var start=new ProcessStartInfo("dotnet"){WorkingDirectory=Path.GetDirectoryName(binary)!,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add(binary);start.ArgumentList.Add("--urls");start.ArgumentList.Add($"http://127.0.0.1:{port}");
        foreach(var key in start.Environment.Keys.Where(k=>k.StartsWith("Homelab__",StringComparison.OrdinalIgnoreCase) || k.StartsWith("OTEL_",StringComparison.OrdinalIgnoreCase)).ToArray())start.Environment.Remove(key);
        start.Environment["ASPNETCORE_ENVIRONMENT"]="Development";start.Environment["Homelab__DataPath"]=folder;start.Environment["Homelab__SetupToken"]="hardware-fixture";start.Environment["Homelab__SynologyEnabled"]=synology.ToString();start.Environment["OTEL_SDK_DISABLED"]="true";
        var log=new System.Text.StringBuilder();var process=new Process{StartInfo=start};process.OutputDataReceived+=(_,e)=>{if(e.Data is not null)lock(log)if(log.Length<10000)log.AppendLine(e.Data);};process.ErrorDataReceived+=(_,e)=>{if(e.Data is not null)lock(log)if(log.Length<10000)log.AppendLine(e.Data);};process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
        var client=new HttpClient(new HttpClientHandler{CookieContainer=new(),AllowAutoRedirect=false,UseProxy=false}){BaseAddress=new Uri($"http://127.0.0.1:{port}"),Timeout=TimeSpan.FromSeconds(5)};
        for(var i=0;i<100 && !process.HasExited;i++){try{if((await client.GetAsync("/health/ready")).IsSuccessStatusCode)return new(process,client);}catch(HttpRequestException){}await Task.Delay(100);}
        await new Running(process,client).DisposeAsync();Assert.Fail("Hardware test API did not start: "+log);throw new InvalidOperationException();
    }
}
