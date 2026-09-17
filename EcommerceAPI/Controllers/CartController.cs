using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EcommerceAPI.Data;
using EcommerceAPI.Models;

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

            return await _context.CartItems
                .AsNoTracking()
                .Where(item => item.UserId == userId.Value)
                .OrderBy(item => item.AddedAt)
                .Select(item => new
                {
                    item.CartItemId,
                    item.ProductId,
                    item.Quantity,
                    ProductName = item.Product.Name,
                    PlatformFee = item.Product.PlatformFee ?? 0,
                    DiscountOnMRP = item.Product.DiscountOnMRP ?? 0,
                    item.Product.Price,
                    item.Product.ImageUrl,
                    item.Product.StockQuantity,
                    
                })
                .ToListAsync();
        }

        [HttpPost]
        public async Task<ActionResult> AddToCart([FromBody] AddCartItemRequest request)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();
            if (request.Quantity < 1) return BadRequest(new { message = "Quantity must be at least 1." });

            var product = await _context.Products.FindAsync(request.ProductId);
            if (product == null) return NotFound(new { message = "Product not found." });

            var cartItem = await _context.CartItems
                .FirstOrDefaultAsync(item => item.UserId == userId.Value && item.ProductId == request.ProductId);
            var requestedQuantity = (cartItem?.Quantity ?? 0) + request.Quantity;
            if (requestedQuantity > product.StockQuantity)
            {
                return BadRequest(new { message = "There is not enough stock available." });
            }

            if (cartItem == null)
            {
                _context.CartItems.Add(new CartItem
                {
                    UserId = userId.Value,
                    ProductId = request.ProductId,
                    Quantity = request.Quantity
                });
            }
            else
            {
                cartItem.Quantity = requestedQuantity;
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Product added to cart." });
        }

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
        public int ProductId { get; set; }
        public int Quantity { get; set; } = 1;
    }
}
