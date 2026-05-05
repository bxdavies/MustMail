using Microsoft.EntityFrameworkCore.Design;

namespace MustMail.Db;

    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DatabaseContext>
    {
        public DatabaseContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<DatabaseContext>();

            var provider = Environment.GetEnvironmentVariable("DB_PROVIDER");

            if (provider == "Postgres")
            {
                optionsBuilder.UseNpgsql();
            }
            else
            {
                optionsBuilder.UseSqlite();
            }

            return new DatabaseContext(optionsBuilder.Options);
        }
    }