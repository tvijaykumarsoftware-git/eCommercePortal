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
    public class OrdersController : ControllerBase
    {
        private readonly EcommerceStoreDbContext _context;

        public OrdersController(EcommerceStoreDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetOrders()
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var orders = _context.Orders.AsNoTracking();
            if (!User.IsInRole("Admin"))
            {
                orders = orders.Where(order => order.UserId == userId.Value);
            }

            return await orders
                .AsNoTracking()
                .OrderByDescending(order => order.CreatedAt)
                .Select(order => new
                {
                    order.OrderId,
                    order.TotalAmount,
                    order.OrderStatus,
                    order.ShippingAddress,
                    order.CreatedAt,
                    order.Rating,
                    OrderItems = order.OrderItems.Select(item => new
                    {
                        item.OrderItemId,
                        item.ProductId,
                        ProductName = item.Product.Name,
                        item.UnitPrice,
                        item.Quantity,
                        LineTotal = item.UnitPrice * item.Quantity
                    })
                })
                .ToListAsync();
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<object>> GetOrder(int id)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var orderQuery = _context.Orders
                .AsNoTracking()
                .Include(order => order.OrderItems)
                    .ThenInclude(item => item.Product)
                .Include(order => order.Transactions)
                .Include(order => order.Invoices)
                .Where(order => order.OrderId == id);

            if (!User.IsInRole("Admin"))
            {
                orderQuery = orderQuery.Where(order => order.UserId == userId.Value);
            }

            var order = await orderQuery.SingleOrDefaultAsync();
            if (order == null) return NotFound(new { message = "Order not found." });

            return new
            {
                order.OrderId,
                order.TotalAmount,
                order.OrderStatus,
                order.ShippingAddress,
                order.CreatedAt,
                order.Rating,
                OrderItems = order.OrderItems.Select(item => new
                {
                    item.OrderItemId,
                    item.ProductId,
                    ProductName = item.Product.Name,
                    item.UnitPrice,
                    item.Quantity,
                    LineTotal = item.UnitPrice * item.Quantity
                }),
                Transactions = order.Transactions.Select(transaction => new
                {
                    transaction.TransactionId,
                    transaction.PaymentProvider,
                    transaction.TransactionReference,
                    transaction.Amount,
                    transaction.Status,
                    transaction.ProcessedAt
                }),
                Invoices = order.Invoices.Select(invoice => new
                {
                    invoice.InvoiceId,
                    invoice.InvoiceNumber,
                    invoice.InvoiceDate
                })
            };
        }

       [HttpPut("{id:int}/status")]
public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateStatusRequest request)
{
    var userId = GetUserId();
    if (userId == null) return Unauthorized(new { message = "Invalid authorization token." });

    if (string.IsNullOrWhiteSpace(request?.OrderStatus))
    {
        return BadRequest(new { message = "Order status cannot be empty." });
    }

    var order = await _context.Orders.FirstOrDefaultAsync(o => o.OrderId == id);
    if (order == null) return NotFound(new { message = $"Order #{id} not found." });

    // Allow status updates for admins or order owners
    bool isAdmin = User.IsInRole("Admin") || User.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == "Admin");
    if (!isAdmin && order.UserId != userId.Value)
    {
        return StatusCode(403, new { message = "You do not have permission to modify this order." });
    }

    order.OrderStatus = request.OrderStatus.Trim();
    
    try 
    {
        await _context.SaveChangesAsync();
        return Ok(new { message = "Order status updated successfully." });
    }
    catch (DbUpdateException ex)
    {
        return StatusCode(500, new { message = "Database save failed: " + ex.InnerException?.Message ?? ex.Message });
    }
}

[HttpPut("{id:int}/rate")]
public async Task<IActionResult> RateOrder(int id, [FromBody] RateOrderRequest request)
{
    var userId = GetUserId();
    if (userId == null) return Unauthorized(new { message = "Invalid authorization token." });

    var order = await _context.Orders.FirstOrDefaultAsync(o => o.OrderId == id);
    if (order == null) return NotFound(new { message = $"Order #{id} not found." });

    // Allow rating if the user owns the order OR if the user is an Admin
    bool isAdmin = User.IsInRole("Admin") || User.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == "Admin");
    if (!isAdmin && order.UserId != userId.Value)
    {
        return StatusCode(403, new { message = "You can only rate your own orders." });
    }

    if (request == null || request.Rating < 1 || request.Rating > 5)
    {
        return BadRequest(new { message = "Please select a star rating between 1 and 5." });
    }

    order.Rating = request.Rating;

    try 
    {
        await _context.SaveChangesAsync();
        return Ok(new { message = "Rating submitted successfully." });
    }
    catch (DbUpdateException ex)
    {
        return StatusCode(500, new { message = "Database save failed: " + (ex.InnerException?.Message ?? ex.Message) });
    }
}

        [HttpPost]
        public async Task<ActionResult> CreateOrder([FromBody] CreateOrderRequest request)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();
            if (string.IsNullOrWhiteSpace(request.ShippingAddress))
            {
                return BadRequest(new { message = "Shipping address is required." });
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();
            var cartItems = await _context.CartItems
                .Include(item => item.Product)
                .Where(item => item.UserId == userId.Value)
                .ToListAsync();

            if (cartItems.Count == 0)
            {
                return BadRequest(new { message = "Your cart is empty." });
            }

            if (cartItems.Any(item => item.Quantity > item.Product.StockQuantity))
            {
                return BadRequest(new { message = "One or more products no longer have enough stock." });
            }

            var order = new Order
            {
                UserId = userId.Value,
                ShippingAddress = request.ShippingAddress.Trim(),
                TotalAmount = cartItems.Sum(item => item.Product.Price * item.Quantity),
                OrderStatus = "Pending",
                OrderItems = cartItems.Select(item => new OrderItem
                {
                    ProductId = item.ProductId,
                    UnitPrice = item.Product.Price,
                    Quantity = item.Quantity
                }).ToList()
            };

            foreach (var cartItem in cartItems)
            {
                cartItem.Product.StockQuantity -= cartItem.Quantity;
            }

            _context.Orders.Add(order);
            _context.CartItems.RemoveRange(cartItems);
            await _context.SaveChangesAsync();

            var paymentTransaction = new Transaction
            {
                OrderId = order.OrderId,
                PaymentProvider = "Card",
                TransactionReference = $"TXN-{Guid.NewGuid():N}",
                Amount = order.TotalAmount,
                Status = "Completed",
                ProcessedAt = DateTime.UtcNow
            };

            _context.Transactions.Add(paymentTransaction);

            var invoice = new Invoice
            {
                OrderId = order.OrderId,
                InvoiceNumber = $"INV-{order.OrderId:D8}",
                InvoiceDate = DateTime.UtcNow
            };

            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new
            {
                orderId = order.OrderId,
                invoiceId = invoice.InvoiceId,
                invoiceNumber = invoice.InvoiceNumber,
                invoiceUrl = $"/api/Invoices/generate/{order.OrderId}",
                transaction = new
                {
                    paymentTransaction.TransactionId,
                    paymentTransaction.PaymentProvider,
                    paymentTransaction.TransactionReference,
                    paymentTransaction.Amount,
                    paymentTransaction.Status,
                    paymentTransaction.ProcessedAt
                },
                message = "Order placed successfully.",
                orderItems = order.OrderItems.Select(item => new
                {
                    item.OrderItemId,
                    item.ProductId,
                    item.UnitPrice,
                    item.Quantity
                })
            });
        }

        private int? GetUserId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(claim, out var userId) ? userId : null;
        }
    }

    public class CreateOrderRequest
    {
        public string ShippingAddress { get; set; } = string.Empty;
    }

    public class UpdateStatusRequest
{
    [JsonPropertyName("orderStatus")]
    public string OrderStatus { get; set; } = string.Empty;
}

public class RateOrderRequest
{
    [JsonPropertyName("rating")]
    public int Rating { get; set; }
}
}
