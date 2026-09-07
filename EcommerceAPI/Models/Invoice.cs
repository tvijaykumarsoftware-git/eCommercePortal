using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EcommerceAPI.Models
{
    [Table("Invoices")]
    public class Invoice
    {
        [Key]
        public int InvoiceId { get; set; }

        public int OrderId { get; set; }

        [Required]
        [StringLength(100)]
        public string InvoiceNumber { get; set; } = string.Empty;

        public DateTime InvoiceDate { get; set; } = DateTime.UtcNow;

        [StringLength(500)]
        public string? PdfStoragePath { get; set; }

        // Foreign Key Navigation
        [ForeignKey(nameof(OrderId))]
        public Order Order { get; set; } = null!;
    }
}