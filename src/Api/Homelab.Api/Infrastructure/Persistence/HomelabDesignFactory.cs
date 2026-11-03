using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Homelab.Api;
// EF tooling must never start the runtime, collectors, or production database.
public sealed class HomelabDesignFactory : IDesignTimeDbContextFactory<HomelabDb>
{
    public HomelabDb CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<HomelabDb>().UseSqlite("Data Source=:memory:").Options);
}
