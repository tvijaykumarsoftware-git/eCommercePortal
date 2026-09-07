using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EcommerceAPI.Models
{
    [Table("Transactions")]
    public class Transaction
    {
        [Key]
        public int TransactionId { get; set; }

        public int OrderId { get; set; }

        [Required]
        [StringLength(50)]
        public string PaymentProvider { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string TransactionReference { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [Required]
        [StringLength(50)]
        public string Status { get; set; } = string.Empty;

        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

        // Foreign Key Navigation
        [ForeignKey(nameof(OrderId))]
        public Order Order { get; set; } = null!;
    }
}