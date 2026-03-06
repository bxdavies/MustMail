namespace MustMail.Db
{
    public class DatabaseContext(DbContextOptions<DatabaseContext> options) : DbContext(options)
    {
        public DbSet<User> User { get; set; } = default!;

        public DbSet<SMTPAccount> SMTPAccount { get; set; } = default!;

        public DbSet<Message> Message { get; set; } = default!;
    }
}
