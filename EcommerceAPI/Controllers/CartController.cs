using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EcommerceAPI.Data;
using EcommerceAPI.Models;
using System.Text.Json.Serialization;

namespace EcommerceAPI.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class CartController : ControllerBase
    {
        private readonly EcommerceStoreDbContext _context;

        public CartController(EcommerceStoreDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetCart()
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var items = await _context.CartItems
                .AsNoTracking()
                .Where(item => item.UserId == userId.Value)
                .Include(item => item.Product)
                .OrderBy(item => item.AddedAt)
                .ThenBy(item => item.CartItemId)
                .Select(item => new
                {
                    cartItemId = item.CartItemId,
                    productId = item.ProductId,
                    quantity = item.Quantity,
                    productName = item.Product.Name,
                    price = item.Product.Price,
                    discountOnMRP = item.Product.DiscountOnMRP ?? 0,
                    imageUrl = item.Product.ImageUrl,
                    stockQuantity = item.Product.StockQuantity,
                    isEMI = item.Product.IsEMI, // Match exact casing or handle in frontend
                })
                .ToListAsync();

            return Ok(items);
        }


        // POST /api/cart — inserts ONE row per unit
        [HttpPost]
        public async Task<ActionResult> AddToCart([FromBody] AddCartItemRequest request)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            if (request == null || request.ProductId <= 0)
                return BadRequest(new { message = "Invalid product request." });
            if (request.Quantity < 1)
                return BadRequest(new { message = "Quantity must be at least 1." });

            var product = await _context.Products.FindAsync(request.ProductId);
            if (product == null) return NotFound(new { message = "Product not found." });

            int currentQty = await _context.CartItems
                .Where(item => item.UserId == userId.Value && item.ProductId == request.ProductId)
                .SumAsync(item => (int?)item.Quantity) ?? 0;

            if (currentQty + request.Quantity > product.StockQuantity)
                return BadRequest(new { message = "There is not enough stock available." });

            for (int i = 0; i < request.Quantity; i++)
            {
                _context.CartItems.Add(new CartItem
                {
                    UserId = userId.Value,
                    ProductId = request.ProductId,
                    Quantity = 1,
                    AddedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Product added to cart." });
        }

        // DELETE /api/cart/{id} — deletes ONLY the row with this CartItemId
        [HttpDelete("{id}")]
        public async Task<IActionResult> RemoveFromCart(int id)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var cartItem = await _context.CartItems
                .FirstOrDefaultAsync(item => item.CartItemId == id && item.UserId == userId.Value);
            if (cartItem == null) return NotFound();

            _context.CartItems.Remove(cartItem);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        private int? GetUserId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(claim, out var userId) ? userId : null;
        }
    }

    public class AddCartItemRequest
    {
        [JsonPropertyName("productId")]
        public int ProductId { get; set; }

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; } = 1;
    }
}
