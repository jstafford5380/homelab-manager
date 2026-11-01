using Homelab.Api;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Tests;
public class ReferenceCoverageTests {
 [Fact]public void EntityIdReferencesHaveDatabaseConstraintsExceptExplicitRequestTokens(){
  using var db=new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite("Data Source=:memory:").Options);
  var missing=new List<string>();
  foreach(var entity in db.Model.GetEntityTypes())foreach(var property in entity.GetProperties()){
   var type=Nullable.GetUnderlyingType(property.ClrType)??property.ClrType;
   if(!property.Name.EndsWith("Id") || (type!=typeof(Guid)&&type!=typeof(long)) || entity.FindPrimaryKey()!.Properties.Contains(property))continue;
   // Idempotency request tokens are client operation identities, not entity references.
   if(property.Name=="RequestId")continue;
   if(!entity.GetForeignKeys().Any(key=>key.Properties.Contains(property)))missing.Add(entity.ClrType.Name+"."+property.Name);
  }
  Assert.Empty(missing);
 }
}
