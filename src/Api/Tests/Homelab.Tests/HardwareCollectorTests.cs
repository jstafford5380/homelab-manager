using System.Text.Json;
using Homelab.Api;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
namespace Homelab.Tests;
public sealed class HardwareCollectorTests
{
    [Fact]public async Task ReportingIsFlaggedScheduledAndDenialsPreserveInventoryAndSamples()
    {
        using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();await using var db=new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);await db.Database.MigrateAsync();
        var protection=new EphemeralDataProtectionProvider();var nas=new NasConnection{ProtectedApiKey=protection.CreateProtector("nas-credentials").Protect("fixture-only")};db.Connections.Add(nas);await db.SaveChangesAsync();
        using var services=new ServiceCollection().BuildServiceProvider();var factory=new Factory();var config=new ConfigurationBuilder().AddInMemoryCollection().Build();
        var collector=new Collector(services.GetRequiredService<IServiceScopeFactory>(),protection,NullLogger<Collector>.Instance,new WriteCoordinator(),factory,config);
        await collector.CollectAsync(nas,db,default);Assert.Equal(0,factory.Calls);Assert.Equal(3,await db.HardwareComponents.CountAsync());
        config["Homelab:HardwareMetricsEnabled"]="true";
        await collector.CollectAsync(nas,db,default);Assert.Equal(1,factory.Calls);Assert.True(factory.Backfill);var readings=await db.HardwareReadings.CountAsync();Assert.True(readings>0);
        await collector.CollectAsync(nas,db,default);Assert.Equal(1,factory.Calls);
        var s=await db.HardwareServers.SingleAsync();s.NextAttemptUtc=DateTime.UtcNow.AddMinutes(-1);await db.SaveChangesAsync();factory.Denied=true;
        await collector.CollectAsync(nas,db,default);Assert.Equal(2,factory.Calls);Assert.Equal(readings,await db.HardwareReadings.CountAsync());Assert.Equal(3,await db.HardwareComponents.CountAsync());
        await collector.CollectAsync(nas,db,default);Assert.Equal(2,factory.Calls);
        factory.Denied=false;factory.Malformed=true;s.NextAttemptUtc=DateTime.UtcNow.AddMinutes(-1);await db.SaveChangesAsync();
        await collector.CollectAsync(nas,db,default);Assert.Equal(3,factory.Calls);Assert.Equal(readings,await db.HardwareReadings.CountAsync());Assert.Equal(3,await db.HardwareComponents.CountAsync());
        config["Homelab:HardwareMetricsEnabled"]="false";var detail=await HardwareMetrics.Detail(db,s,DateTime.UtcNow,30,false);Assert.Equal("none",detail.Cpu.Source);
        Assert.All(detail.Components,c=>Assert.Equal("Not monitored",c.Status.Label));Assert.Empty(await db.Observations.ToArrayAsync());Assert.Empty(await db.Findings.ToArrayAsync());
    }
    private sealed class Factory:INasReadClientFactory
    {
        public int Calls;public bool Denied;public bool Backfill;public bool Malformed;
        public INasReadClient Create()=>new Client(this);
        private sealed class Client(Factory f):INasReadClient
        {
            public Task ConnectAsync(NasConnection profile,string key,CancellationToken ct)=>Task.CompletedTask;
            public Task<JsonElement> ReadAsync(string method,CancellationToken ct)=>Task.FromResult(method=="system.info"?JsonSerializer.SerializeToElement(new{version="TrueNAS-SCALE-24.10.2",model="Fixture CPU",physical_cores=1,cores=2,physmem=4000*1048576d,system_product="Fixture NAS",uptime_seconds=1000}):JsonSerializer.SerializeToElement(Array.Empty<object>()));
            public Task<JsonElement> ReadHardwareMetricsAsync(bool backfill,CancellationToken ct)
            {
                f.Calls++;f.Backfill=backfill;if(f.Denied)throw new NasReadException("denied");
                if(f.Malformed)return Task.FromResult(JsonSerializer.SerializeToElement(new{invalid=true}));
                var at=DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
                return Task.FromResult(JsonSerializer.SerializeToElement(new object[]{new{name="cpu",legend=new[]{"time","idle","user"},data=new[]{new double[]{at,70,30}}},new{name="memory",legend=new[]{"time","free"},data=new[]{new double[]{at,1000}}},new{name="arcsize",legend=new[]{"time","arc_size"},data=new[]{new double[]{at,2000}}},new{name="cputemp",legend=new[]{"time","cpu0"},data=new[]{new double[]{at,45}}}}));
            }
            public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
        }
    }
}
