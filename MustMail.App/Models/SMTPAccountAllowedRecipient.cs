using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace MustMail.App.Models
{
    public class SMTPAccountAllowedRecipient
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [MaxLength(255)]
        public required string EmailAddress { get; set; }

        public int SMTPAccountId { get; set; }
        [JsonIgnore]
        public SMTPAccount SMTPAccount { get; set; } = null!;
    }
}
