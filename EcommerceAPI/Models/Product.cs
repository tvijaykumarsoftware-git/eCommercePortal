using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace EcommerceAPI.Models
{
    [Table("Products")]
    public class Product
{
    [Key]
    public int ProductId { get; set; }

    public int CategoryId { get; set; }

    [Required]
    [StringLength(150)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; }

    public int StockQuantity { get; set; } = 0;

    [StringLength(500)]
    public string? ImageUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Foreign Key Navigation
    [ForeignKey(nameof(CategoryId))]
    [ValidateNever]
    [JsonIgnore]
    public Category? Category { get; set; }

    // Navigation Properties
    [ValidateNever]
    [JsonIgnore]
    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();

    [ValidateNever]
    [JsonIgnore]
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}
}