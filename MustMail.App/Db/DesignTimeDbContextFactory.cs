using Microsoft.EntityFrameworkCore.Design;

namespace MustMail.App.Db;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DatabaseContext>
{
    public DatabaseContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<DatabaseContext> optionsBuilder = new DbContextOptionsBuilder<DatabaseContext>();

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("Db_Provider")))
            throw new InvalidOperationException(
                "The environment variable 'Db_Provider' must be set.");

        switch (Environment.GetEnvironmentVariable("Db_Provider"))
        {
            case "Postgres":
                optionsBuilder.UseNpgsql(x => x.MigrationsAssembly("MustMail.Migrations.Postgres"));
                break;
            case "Sqlite":
                optionsBuilder.UseSqlite(x => x.MigrationsAssembly("MustMail.Migrations.Sqlite"));
                break;
            case "MySQL":
                optionsBuilder.UseMySQL(x => x.MigrationsAssembly("MustMail.Migrations.MySQL"));
                break;
            case "SqlServer":
                optionsBuilder.UseSqlServer(x => x.MigrationsAssembly("MustMail.Migrations.SqlServer"));
                break;
            case "AzureSql":
                optionsBuilder.UseAzureSql(x => x.MigrationsAssembly("MustMail.Migrations.AzureSql"));
                break;
            default:
                break;
        }
      
        return new DatabaseContext(optionsBuilder.Options);
    }
}