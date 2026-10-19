using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace EcommerceAPI.Models
{
    [Table("ProductImages")]
    public class ProductImage
    {
        [Key]
    public int ImageId { get; set; }

    public int ProductId { get; set; }

    [Required]
    [StringLength(500)]
    public string ImageUrl { get; set; } = string.Empty;

    [ForeignKey(nameof(ProductId))]
    [ValidateNever]
    [JsonIgnore]
    public Product? Product { get; set; }
    }
}
