namespace MustMail.Models;

public class Message
{
    public required string Id { get; set; }
    public DateTime Timestamp { get; set; }
    public required string SenderName { get; set; }
    public required string SenderEmail { get; set; }
    public required string Subject { get; set; }
    public int AttachmentCount { get; set; }
    public string UserId { get; set; } = default!; // Required foreign key property
    public User User { get; set; } = null!; // Required reference navigation to principal
}
