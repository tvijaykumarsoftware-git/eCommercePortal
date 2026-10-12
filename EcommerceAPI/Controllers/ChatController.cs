using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EcommerceAPI.Data; // Ensure this matches your DbContext namespace
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EcommerceAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly EcommerceStoreDbContext _context;

    public ChatController(EcommerceStoreDbContext context)
    {
        _context = context;
    }

        public class ChatRequest
        {
            public string Question { get; set; } = string.Empty;
        }

        public class ChatResponse
        {
            public string Text { get; set; } = string.Empty;
            public List<LinkItem>? Links { get; set; }
            public List<ProductItem>? Products { get; set; }
        }

        public class LinkItem
        {
            public string To { get; set; } = string.Empty;
            public string Label { get; set; } = string.Empty;
        }

        public class ProductItem
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public decimal Price { get; set; }
            public string? ImageUrl { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> Ask([FromBody] ChatRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Question))
            {
                return BadRequest(new { message = "Question cannot be empty." });
            }

            string q = request.Question.ToLower().Trim();

            // 1. Greetings
            if (Regex.IsMatch(q, @"\b(hi|hello|hey|good morning|good evening|greetings)\b"))
            {
                return Ok(new ChatResponse
                {
                    Text = "Hello! I am your store assistant. How can I help you today with products, orders, payments, or navigation?",
                    Links = new List<LinkItem>
                    {
                        new LinkItem { To = "/catalog", Label = "Browse Catalog" },
                        new LinkItem { To = "/customer-care", Label = "Customer Care" }
                    }
                });
            }

            // 2. Returns & Refunds
            if (Regex.IsMatch(q, @"\b(return|refund|exchange|cancel|cancellation)\b"))
            {
                return Ok(new ChatResponse
                {
                    Text = "Our return policy allows items to be returned or reviewed within 1-3 business days. You can view your recent orders or reach out to support for help.",
                    Links = new List<LinkItem>
                    {
                        new LinkItem { To = "/orders", Label = "View Orders" },
                        new LinkItem { To = "/customer-care", Label = "Customer Support" }
                    }
                });
            }

            // 3. Order Tracking
            if (Regex.IsMatch(q, @"\b(track|tracking|where is|status|delivery|shipment)\b"))
            {
                return Ok(new ChatResponse
                {
                    Text = "You can track the live status of all your shipments directly from your Account Orders page.",
                    Links = new List<LinkItem>
                    {
                        new LinkItem { To = "/orders", Label = "Track Orders" }
                    }
                });
            }

            // 4. Payment & Billing
            if (Regex.IsMatch(q, @"\b(payment|pay|card|invoice|transaction|billing)\b"))
            {
                return Ok(new ChatResponse
                {
                    Text = "Manage your saved payment options, view billing histories, or check invoice downloads here:",
                    Links = new List<LinkItem>
                    {
                        new LinkItem { To = "/payment", Label = "Payment Methods" },
                        new LinkItem { To = "/admin/invoices", Label = "Invoices" },
                        new LinkItem { To = "/customer-care", Label = "Payment Support" }
                    }
                });
            }

            // 5. Cart & Purchasing
            if (Regex.IsMatch(q, @"\b(cart|checkout|buy|purchase)\b"))
            {
                return Ok(new ChatResponse
                {
                    Text = "Ready to complete your purchase? Check your shopping cart or proceed directly to checkout.",
                    Links = new List<LinkItem>
                    {
                        new LinkItem { To = "/cart", Label = "View Cart" },
                        new LinkItem { To = "/checkout", Label = "Proceed to Checkout" }
                    }
                });
            }

            // 6. Dynamic Database Product Search
            var ignoredWords = new HashSet<string> { "the", "and", "for", "with", "that", "this", "what", "which", "can", "you", "show", "find", "need", "want", "looking", "under", "below", "product", "products", "price", "cost", "available", "recommend", "something", "shop", "buy", "a", "an", "of", "in", "is", "it", "me", "my", "i" };
            
            var terms = Regex.Matches(q, @"[a-z0-9]+")
                .Cast<Match>()
                .Select(m => m.Value)
                .Where(w => !ignoredWords.Contains(w))
                .ToList();

            // Extract budget if present (e.g., "under 50", "below $100")
            decimal? maxBudget = null;
            var budgetMatch = Regex.Match(q, @"(?:under|below|less than|within|budget of)\s*[$₹]?\s*(\d+(?:\.\d+)?)");
            if (budgetMatch.Success && decimal.TryParse(budgetMatch.Groups[1].Value, out decimal parsedBudget))
            {
                maxBudget = parsedBudget;
            }

            var allProducts = await _context.Products.ToListAsync();

            var matches = allProducts
                .Select(p => {
                    string searchable = $"{p.Name} {p.Description}".ToLower();
                    int score = terms.Count > 0 ? terms.Count(term => searchable.Contains(term)) : 0;
                    decimal effectivePrice = (p.DiscountOnMRP.HasValue && p.DiscountOnMRP > 0 && p.DiscountOnMRP < p.Price)
                        ? p.DiscountOnMRP.Value
                        : p.Price;

                    return new { Product = p, Score = score, EffectivePrice = effectivePrice };
                })
                .Where(x => (terms.Count == 0 || x.Score > 0) 
                         && x.Product.StockQuantity > 0 
                         && (!maxBudget.HasValue || x.EffectivePrice <= maxBudget.Value))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.EffectivePrice)
                .Take(3)
                .ToList();

            if (matches.Any())
            {
                return Ok(new ChatResponse
                {
                    Text = maxBudget.HasValue 
                        ? $"Here are available products under ${maxBudget}:" 
                        : "Here are products matching your request:",
                    Products = matches.Select(m => new ProductItem
                    {
                        Id = m.Product.ProductId,
                        Name = m.Product.Name,
                        Price = m.EffectivePrice,
                        ImageUrl = m.Product.ImageUrl
                    }).ToList()
                });
            }

            // 7. General Fallback
            return Ok(new ChatResponse
            {
                Text = "I couldn't find exact product matches for that, but I can help you navigate account settings, check orders, or explore categories.",
                Links = new List<LinkItem>
                {
                    new LinkItem { To = "/catalog", Label = "Browse Catalog" },
                    new LinkItem { To = "/customer-care", Label = "Customer Care" }
                }
            });
        }
}
