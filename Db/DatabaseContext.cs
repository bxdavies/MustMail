namespace MustMail.Db
{
    public class DatabaseContext(DbContextOptions<DatabaseContext> options) : DbContext(options)
    {
        public DbSet<User> User { get; set; } = null!;

        public DbSet<SMTPAccount> SMTPAccount { get; set; } = null!;

        public DbSet<Message> Message { get; set; } = null!;
    }
}
