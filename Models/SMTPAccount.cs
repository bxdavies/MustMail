using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MustMail.Models;

public class SMTPAccount
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string Password { get; set; }
    public required string Description { get; set; }

}