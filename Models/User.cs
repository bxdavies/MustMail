namespace MustMail.Models;

public class User
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public bool Admin { get; set; } = false;
    public Profile Profile { get; set; } = new Profile { DateFormat = "dddd dd MMMM yyyy", TimeFormat = "HH:mm", TimeZone = "GMT" };
    public ICollection<Message> Messages { get; } = [];
}