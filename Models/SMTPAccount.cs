using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MustMail.Models;

public class SMTPAccount
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    [MaxLength(255)]
    public required string Username { get; set; }
    [MaxLength(512)]
    public required string Password { get; set; }
    [MaxLength(255)]
    public required string Description { get; set; }

}