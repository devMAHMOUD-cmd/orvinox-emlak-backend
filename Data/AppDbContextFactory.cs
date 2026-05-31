using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using DotNetEnv;

namespace CraftoraApi.Data;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        Env.Load();

        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__PostgreSQL")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var postgresDb = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "CraftoraMobile";
            var postgresUser = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "admin";
            var postgresPassword = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");

            if (string.IsNullOrWhiteSpace(postgresPassword))
            {
                throw new InvalidOperationException("PostgreSQL connection string or POSTGRES_PASSWORD is missing.");
            }

            connectionString = $"Host=localhost;Database={postgresDb};Username={postgresUser};Password={postgresPassword}";
        }

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new AppDbContext(optionsBuilder.Options);
    }
}
