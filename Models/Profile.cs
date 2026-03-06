using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MustMail.Models;

public class Profile
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; init; }
    [MaxLength(100)] public required string TimeZone { get; set; }
    [MaxLength(50)] public required string DateFormat { get; set; }
    [MaxLength(50)] public required string TimeFormat { get; set; }
    public string UserId { get; set; } = default!; // Required foreign key property
    public User User { get; set; } = null!; // Required reference navigation to principal

}

