using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Tests;
public class RecoveryHistoryTests {
 [Theory][InlineData("runs")][InlineData("exercises")]
 public async Task PagingRetainsTiesOldRevisionsAndSubmissionBoundary(string kind) {
  await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
  await using var db=new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);await db.Database.EnsureCreatedAsync();
  var policy=new RecoveryPolicy();var other=new RecoveryPolicy();db.Recovery.AddRange(policy,other);await db.SaveChangesAsync();
  var anchor=DateTime.UtcNow;var revision=Guid.NewGuid();var expected=new List<(Guid Id,DateTime At)>();
  void Add(Guid parent,DateTime occurred,DateTime recorded,bool include) {
   var id=Guid.NewGuid();if(include)expected.Add((id,occurred));
   if(kind=="runs")db.BackupEvidence.Add(new(){Id=id,PolicyId=parent,PolicyRevision=revision,AttemptedUtc=occurred,RecordedUtc=recorded,DestinationConfirmed=true,Evidence="manifest",FailureReason="prior failure"});
   else db.RestoreExercises.Add(new(){Id=id,PolicyId=parent,PolicyRevision=revision,TestedUtc=occurred,RecordedUtc=recorded,Scope="photos",Checks="opened files",DurationMinutes=15,Evidence="restore log"});
  }
  for(var i=0;i<151;i++)Add(policy.Id,anchor.AddDays(-1).AddMinutes(-(i/17)),anchor.AddMinutes(-1),true);
  Add(other.Id,anchor.AddDays(-1),anchor.AddMinutes(-1),false);
  Add(policy.Id,anchor.AddDays(-2),anchor.AddMinutes(1),false);await db.SaveChangesAsync();
  var actual=new List<RecoveryHistoryItem>();RecoveryHistoryCursor? cursor=null;var pages=0;
  do {var page=await RecoveryHistory.ReadAsync(db,policy.Id,kind,anchor,cursor);Assert.InRange(page.Items.Length,1,50);actual.AddRange(page.Items);cursor=page.Next;Assert.True(++pages<=4);}while(cursor is not null);
  Assert.Equal(4,pages);Assert.Equal(expected.OrderByDescending(x=>x.At).ThenByDescending(x=>x.Id.ToString().ToUpperInvariant(),StringComparer.Ordinal).Select(x=>x.Id),actual.Select(x=>x.Id));
  Assert.All(actual,row=>{Assert.Equal(revision,row.PolicyRevision);if(kind=="runs"){Assert.True(row.DestinationConfirmed);Assert.Equal("prior failure",row.FailureReason);}else{Assert.Equal("photos",row.Scope);Assert.Equal("opened files",row.Checks);Assert.Equal(15,row.DurationMinutes);}});
  var refreshed=new List<RecoveryHistoryItem>();cursor=null;do{var page=await RecoveryHistory.ReadAsync(db,policy.Id,kind,anchor.AddMinutes(2),cursor);refreshed.AddRange(page.Items);cursor=page.Next;}while(cursor is not null);Assert.Equal(152,refreshed.Count);
 }
}
