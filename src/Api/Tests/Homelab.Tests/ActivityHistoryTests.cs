using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Tests;
public class ActivityHistoryTests {
 [Fact]public async Task AllRetainedEventsAreReachableDespiteNewInsertionsAndTimestampReordering(){
  await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();await using var db=new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);await db.Database.EnsureCreatedAsync();
  var empty=await ActivityHistory.ReadAsync(db);Assert.Empty(empty.Items);Assert.Null(empty.NextBefore);Assert.Equal(0,empty.SnapshotId);
  for(var i=0;i<151;i++)db.Activity.Add(new(){Action="event "+i,Actor="owner",Detail="retained evidence",AtUtc=DateTime.UtcNow.AddDays(-(i%3))});await db.SaveChangesAsync();
  var expected=await db.Activity.OrderByDescending(x=>x.Id).Select(x=>x.Id).ToArrayAsync();var first=await ActivityHistory.ReadAsync(db);Assert.Equal(50,first.Items.Length);
  db.Activity.Add(new(){Action="later backdated event",AtUtc=DateTime.UtcNow.AddYears(-1)});await db.SaveChangesAsync();
  var ids=first.Items.Select(x=>x.Id).ToList();var cursor=first.NextBefore;while(cursor is not null){var page=await ActivityHistory.ReadAsync(db,first.SnapshotId,cursor);Assert.InRange(page.Items.Length,1,50);ids.AddRange(page.Items.Select(x=>x.Id));cursor=page.NextBefore;Assert.True(ids.Count<=151);}
  Assert.Equal(expected,ids);Assert.All(first.Items,x=>{Assert.Equal("owner",x.Actor);Assert.Equal("retained evidence",x.Detail);});Assert.Equal("later backdated event",(await ActivityHistory.ReadAsync(db)).Items[0].Action);
 }
}
