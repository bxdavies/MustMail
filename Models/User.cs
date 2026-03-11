using System.ComponentModel.DataAnnotations;

namespace MustMail.Models;

public class User
{
    [MaxLength(255)]
    public required string Id { get; init; }
    [MaxLength(255)]
    public required string Name { get; init; }
    [MaxLength(254)]
    public required string Email { get; init; }
    public bool Admin { get; set; }
    public Profile Profile { get; init; } = new() { DateFormat = "dddd dd MMMM yyyy", TimeFormat = "HH:mm", TimeZone = "GMT" };
    public ICollection<Message> Messages { get; } = [];
}