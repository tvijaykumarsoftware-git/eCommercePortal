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
}
