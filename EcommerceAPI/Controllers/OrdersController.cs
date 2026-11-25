using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EcommerceAPI.Data;
using EcommerceAPI.Models;
using System.Text.Json.Serialization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EcommerceAPI.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class OrdersController : ControllerBase
    {
        private readonly EcommerceStoreDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<OrdersController> _logger;

        public OrdersController(
            EcommerceStoreDbContext context,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<OrdersController> logger)
        {
            _context = context;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
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

            if (!string.Equals(request.PaymentProvider, "Cash on Delivery", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Online payments must be completed through Razorpay." });
            }

            return await CreateOrderFromCartAsync(userId.Value, request);
        }

        [HttpPost("razorpay/order")]
        public async Task<ActionResult> CreateRazorpayOrder([FromBody] CreateRazorpayOrderRequest request)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();
            if (request.CartItemIds == null || request.CartItemIds.Count == 0)
            {
                return BadRequest(new { message = "Select at least one cart item." });
            }

            if (!TryGetRazorpayCredentials(out var keyId, out var keySecret))
            {
                return StatusCode(503, new { message = "Razorpay is not configured. Set Razorpay:KeyId and Razorpay:KeySecret in backend user-secrets or environment variables." });
            }

            var cartItems = await GetCartItemsAsync(userId.Value, request.CartItemIds);
            if (!HasExactCartSelection(request.CartItemIds, cartItems))
            {
                return BadRequest(new { message = "Your cart changed. Refresh checkout and try again." });
            }
            if (cartItems.Count == 0 || !HasAvailableStock(cartItems))
            {
                return BadRequest(new { message = "Your cart is empty or one or more products are out of stock." });
            }

            var amountInPaise = decimal.ToInt64(decimal.Round(GetCheckoutTotal(cartItems) * 100m, 0, MidpointRounding.AwayFromZero));
            if (amountInPaise <= 0)
            {
                return BadRequest(new { message = "The order amount must be greater than zero." });
            }

            try
            {
                using var response = await SendRazorpayRequestAsync(
                    HttpMethod.Post,
                    "orders",
                    keyId,
                    keySecret,
                    new
                    {
                        amount = amountInPaise,
                        currency = "INR",
                        receipt = $"rcpt_{Guid.NewGuid():N}",
                        notes = new { userId = userId.Value.ToString() }
                    });

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Razorpay order creation failed with status {StatusCode}.", response.StatusCode);
                    return StatusCode(StatusCodes.Status502BadGateway, new { message = "Razorpay could not create the payment order." });
                }

                using var responseJson = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                var root = responseJson.RootElement;
                return Ok(new
                {
                    keyId,
                    orderId = root.GetProperty("id").GetString(),
                    amount = root.GetProperty("amount").GetInt64(),
                    currency = root.GetProperty("currency").GetString()
                });
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException)
            {
                _logger.LogError(exception, "Unable to create a Razorpay order.");
                return StatusCode(StatusCodes.Status502BadGateway, new { message = "Razorpay could not create the payment order." });
            }
        }

        [HttpPost("razorpay/complete")]
        public async Task<ActionResult> CompleteRazorpayOrder([FromBody] CompleteRazorpayOrderRequest request)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();
            if (string.IsNullOrWhiteSpace(request.ShippingAddress) ||
                string.IsNullOrWhiteSpace(request.RazorpayOrderId) ||
                string.IsNullOrWhiteSpace(request.RazorpayPaymentId) ||
                string.IsNullOrWhiteSpace(request.RazorpaySignature) ||
                request.CartItemIds == null || request.CartItemIds.Count == 0)
            {
                return BadRequest(new { message = "Shipping and verified Razorpay payment details are required." });
            }

            if (!TryGetRazorpayCredentials(out var keyId, out var keySecret))
            {
                return StatusCode(503, new { message = "Razorpay is not configured. Set Razorpay:KeyId and Razorpay:KeySecret in backend user-secrets or environment variables." });
            }

            var cartItems = await GetCartItemsAsync(userId.Value, request.CartItemIds);
            if (!HasExactCartSelection(request.CartItemIds, cartItems))
            {
                return BadRequest(new { message = "Your cart changed. Refresh checkout and try again." });
            }
            if (cartItems.Count == 0 || !HasAvailableStock(cartItems))
            {
                return BadRequest(new { message = "Your cart is empty or one or more products are out of stock." });
            }

            if (await _context.Transactions.AnyAsync(transaction => transaction.TransactionReference == request.RazorpayPaymentId))
            {
                return Conflict(new { message = "This payment has already been used for an order." });
            }

            try
            {
                if (!IsValidRazorpaySignature(request.RazorpayOrderId, request.RazorpayPaymentId, request.RazorpaySignature, keySecret))
                {
                    return BadRequest(new { message = "Razorpay payment verification failed." });
                }

                using var orderResponse = await SendRazorpayRequestAsync(
                    HttpMethod.Get, $"orders/{Uri.EscapeDataString(request.RazorpayOrderId)}", keyId, keySecret);
                using var paymentResponse = await SendRazorpayRequestAsync(
                    HttpMethod.Get, $"payments/{Uri.EscapeDataString(request.RazorpayPaymentId)}", keyId, keySecret);

                if (!orderResponse.IsSuccessStatusCode || !paymentResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Razorpay payment lookup failed. Order status: {OrderStatus}; payment status: {PaymentStatus}.",
                        orderResponse.StatusCode, paymentResponse.StatusCode);
                    return StatusCode(StatusCodes.Status502BadGateway, new { message = "Unable to verify the payment with Razorpay." });
                }

                using var orderJson = await JsonDocument.ParseAsync(await orderResponse.Content.ReadAsStreamAsync());
                using var paymentJson = await JsonDocument.ParseAsync(await paymentResponse.Content.ReadAsStreamAsync());
                var razorpayOrder = orderJson.RootElement;
                var razorpayPayment = paymentJson.RootElement;
                var expectedAmount = decimal.ToInt64(decimal.Round(GetCheckoutTotal(cartItems) * 100m, 0, MidpointRounding.AwayFromZero));
                var noteUserId = razorpayOrder.GetProperty("notes").GetProperty("userId").GetString();
                var paymentMatches = string.Equals(razorpayOrder.GetProperty("id").GetString(), request.RazorpayOrderId, StringComparison.Ordinal) &&
                    string.Equals(noteUserId, userId.Value.ToString(), StringComparison.Ordinal) &&
                    razorpayOrder.GetProperty("amount").GetInt64() == expectedAmount &&
                    string.Equals(razorpayOrder.GetProperty("currency").GetString(), "INR", StringComparison.Ordinal) &&
                    string.Equals(razorpayPayment.GetProperty("order_id").GetString(), request.RazorpayOrderId, StringComparison.Ordinal) &&
                    razorpayPayment.GetProperty("amount").GetInt64() == expectedAmount &&
                    string.Equals(razorpayPayment.GetProperty("currency").GetString(), "INR", StringComparison.Ordinal) &&
                    string.Equals(razorpayPayment.GetProperty("status").GetString(), "captured", StringComparison.Ordinal);

                if (!paymentMatches)
                {
                    return BadRequest(new { message = "Razorpay payment is not captured for this cart and account." });
                }

                var createOrderRequest = new CreateOrderRequest
                {
                    ShippingAddress = request.ShippingAddress,
                    PaymentProvider = "Razorpay",
                    CartItemIds = request.CartItemIds
                };
                return await CreateOrderFromCartAsync(userId.Value, createOrderRequest, request.RazorpayPaymentId);
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException or FormatException)
            {
                _logger.LogError(exception, "Unable to verify Razorpay payment {PaymentId}.", request.RazorpayPaymentId);
                return StatusCode(StatusCodes.Status502BadGateway, new { message = "Unable to verify the payment with Razorpay." });
            }
        }

        private async Task<ActionResult> CreateOrderFromCartAsync(int userId, CreateOrderRequest request, string? paymentReference = null)
        {
            if (string.IsNullOrWhiteSpace(request.ShippingAddress))
            {
                return BadRequest(new { message = "Shipping address is required." });
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var cartItems = await GetCartItemsAsync(userId, request.CartItemIds);

                if (cartItems.Count == 0)
                {
                    return BadRequest(new { message = "Your cart is empty or contains no valid available items." });
                }

                if (!HasExactCartSelection(request.CartItemIds, cartItems) || !HasAvailableStock(cartItems))
                {
                    return BadRequest(new { message = "Your cart changed or one or more products no longer have enough stock." });
                }

                // 1. Create the Order
                var order = new Order
                {
                    UserId = userId,
                    ShippingAddress = request.ShippingAddress.Trim(),
                    TotalAmount = GetCheckoutTotal(cartItems),
                    OrderStatus = "Pending",
                    OrderItems = cartItems.Select(item => new OrderItem
                    {
                        ProductId = item.ProductId,
                        UnitPrice = GetSellingPrice(item.Product) + (item.Product.PlatformFee ?? 0m),
                        Quantity = item.Quantity
                    }).ToList()
                };

                // 2. Deduct Stock Quantities for checked-out items only
                foreach (var cartItem in cartItems)
                {
                    cartItem.Product.StockQuantity -= cartItem.Quantity;
                }

                _context.Orders.Add(order);

                // Remove ONLY the processed cart items, leaving sold-out items in the cart
                _context.CartItems.RemoveRange(cartItems);
                await _context.SaveChangesAsync();

                // 3. Create Transaction with payment details & EMI fields
                string provider = string.IsNullOrWhiteSpace(request.PaymentProvider)
                    ? "Credit / Debit Card"
                    : request.PaymentProvider;

                string randomTenDigits = Random.Shared.NextInt64(1000000000L, 10000000000L).ToString();

                var paymentTransaction = new Transaction
                {
                    OrderId = order.OrderId,
                    PaymentProvider = provider,
                    TransactionReference = paymentReference ?? $"TXN{randomTenDigits}",
                    Amount = order.TotalAmount,
                    Status = provider.Equals("Cash on Delivery", StringComparison.OrdinalIgnoreCase) ? "Pending" : "Completed",
                    ProcessedAt = DateTime.UtcNow,
                    VendorName = request.VendorName,
                    EmiTenure = request.EmiTenure,
                    EmiAmount = request.EmiAmount,
                    EmiDateOn = request.EmiDateOn
                };

                _context.Transactions.Add(paymentTransaction);

                // 4. Create Invoice
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
                        paymentTransaction.ProcessedAt,
                        paymentTransaction.VendorName,
                        paymentTransaction.EmiTenure,
                        paymentTransaction.EmiAmount,
                        paymentTransaction.EmiDateOn
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
            catch (Exception)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { message = "An error occurred while processing your order." });
            }
        }

        private async Task<List<CartItem>> GetCartItemsAsync(int userId, List<int>? cartItemIds)
        {
            var cartQuery = _context.CartItems
                .Include(item => item.Product)
                .Where(item => item.UserId == userId);

            if (cartItemIds != null)
            {
                cartQuery = cartQuery.Where(item => cartItemIds.Contains(item.CartItemId));
            }

            return await cartQuery.ToListAsync();
        }

        private static bool HasExactCartSelection(List<int>? requestedIds, List<CartItem> cartItems)
        {
            if (requestedIds == null) return true;
            return requestedIds.Count == requestedIds.Distinct().Count() &&
                requestedIds.Count == cartItems.Count &&
                requestedIds.All(id => cartItems.Any(item => item.CartItemId == id));
        }

        private static bool HasAvailableStock(List<CartItem> cartItems)
        {
            return cartItems.All(item => item.Quantity <= item.Product.StockQuantity);
        }

        private static decimal GetSellingPrice(Product product)
        {
            return product.DiscountOnMRP is > 0 && product.DiscountOnMRP < product.Price
                ? product.DiscountOnMRP.Value
                : product.Price;
        }

        private static decimal GetCheckoutTotal(List<CartItem> cartItems)
        {
            return cartItems.Sum(item => (GetSellingPrice(item.Product) + (item.Product.PlatformFee ?? 0m)) * item.Quantity);
        }

        private bool TryGetRazorpayCredentials(out string keyId, out string keySecret)
        {
            keyId = _configuration["Razorpay:KeyId"] ?? string.Empty;
            keySecret = _configuration["Razorpay:KeySecret"] ?? string.Empty;
            return !string.IsNullOrWhiteSpace(keyId) && !string.IsNullOrWhiteSpace(keySecret);
        }

        private async Task<HttpResponseMessage> SendRazorpayRequestAsync(
            HttpMethod method,
            string path,
            string keyId,
            string keySecret,
            object? body = null)
        {
            var request = new HttpRequestMessage(method, $"https://api.razorpay.com/v1/{path}");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{keyId}:{keySecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            if (body != null)
            {
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            }

            return await _httpClientFactory.CreateClient().SendAsync(request);
        }

        private static bool IsValidRazorpaySignature(string orderId, string paymentId, string signature, string keySecret)
        {
            var message = Encoding.UTF8.GetBytes($"{orderId}|{paymentId}");
            var secret = Encoding.UTF8.GetBytes(keySecret);
            var expectedSignature = HMACSHA256.HashData(secret, message);
            var providedSignature = Convert.FromHexString(signature);
            return expectedSignature.Length == providedSignature.Length &&
                CryptographicOperations.FixedTimeEquals(expectedSignature, providedSignature);
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
       public string PaymentProvider { get; set; } = "Cash on Delivery";
    public string? VendorName { get; set; }
    public int? EmiTenure { get; set; }
    public decimal? EmiAmount { get; set; }
    public DateTime? EmiDateOn { get; set; }
    public List<int>? CartItemIds { get; set; }
    }

public class CreateRazorpayOrderRequest
{
    public List<int>? CartItemIds { get; set; }
}

public class CompleteRazorpayOrderRequest : CreateRazorpayOrderRequest
{
    public string ShippingAddress { get; set; } = string.Empty;
    public string RazorpayOrderId { get; set; } = string.Empty;
    public string RazorpayPaymentId { get; set; } = string.Empty;
    public string RazorpaySignature { get; set; } = string.Empty;
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
