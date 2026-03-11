using System.ComponentModel.DataAnnotations;

namespace MustMail.Models;

public class Message
{
    [MaxLength(255)]
    public required string Id { get; init; }
    public DateTime Timestamp { get; init; }
    [MaxLength(255)]
    public required string SenderName { get; init; }
    [MaxLength(254)]
    public required string SenderEmail { get; init; }
    [MaxLength(255)]
    public required string Subject { get; init; }
    public int AttachmentCount { get; init; }
    [MaxLength(255)]
    public string UserId { get; init; } = null!; // Required foreign key property
    public User User { get; init; } = null!; // Required reference navigation to principal
}
