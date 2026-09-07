using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;
using EcommerceAPI.Data;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class InvoicesController : ControllerBase
{
    private readonly EcommerceStoreDbContext _context;

    public InvoicesController(EcommerceStoreDbContext context) { _context = context; }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IEnumerable<object>>> GetInvoices()
    {
        return await _context.Invoices
            .AsNoTracking()
            .OrderByDescending(invoice => invoice.InvoiceDate)
            .Select(invoice => new
            {
                invoice.InvoiceId,
                invoice.OrderId,
                invoice.InvoiceNumber,
                invoice.InvoiceDate,
                invoice.PdfStoragePath,
                Order = new
                {
                    invoice.Order.OrderStatus,
                    invoice.Order.TotalAmount,
                    invoice.Order.ShippingAddress,
                    invoice.Order.CreatedAt,
                    Items = invoice.Order.OrderItems.Select(item => new
                    {
                        item.OrderItemId,
                        ProductName = item.Product.Name,
                        item.UnitPrice,
                        item.Quantity,
                        LineTotal = item.UnitPrice * item.Quantity
                    })
                }
            })
            .ToListAsync();
    }

    [HttpGet("generate/{orderId}")]
    public async Task<IActionResult> GenerateInvoice(int orderId)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var userId)) return Unauthorized();

        var invoice = await _context.Invoices
            .AsNoTracking()
            .Include(item => item.Order)
                .ThenInclude(order => order.OrderItems)
                    .ThenInclude(item => item.Product)
            .FirstOrDefaultAsync(item => item.OrderId == orderId &&
                (User.IsInRole("Admin") || item.Order.UserId == userId));

        if (invoice == null) return NotFound(new { message = "Invoice not found for this order." });

        var lines = new List<string>
        {
            "ECOMMERCE STORE",
            $"Invoice: {invoice.InvoiceNumber}",
            $"Order: #{invoice.OrderId}",
            $"Invoice date: {invoice.InvoiceDate:yyyy-MM-dd}",
            $"Order status: {invoice.Order.OrderStatus}",
            $"Shipping address: {invoice.Order.ShippingAddress}",
            "",
            "ITEMS"
        };

        foreach (var item in invoice.Order.OrderItems)
        {
            lines.Add($"{item.Product.Name} x {item.Quantity}  ${item.UnitPrice * item.Quantity:0.00}");
        }

        lines.Add("");
        lines.Add($"TOTAL: ${invoice.Order.TotalAmount:0.00}");

        return File(CreatePdf(lines), "application/pdf", $"Invoice_{orderId}.pdf");
    }

    private static byte[] CreatePdf(IEnumerable<string> lines)
    {
        var content = new StringBuilder("BT\n/F1 12 Tf\n50 760 Td\n");
        foreach (var line in lines)
        {
            var safeLine = line.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
            content.Append('(').Append(safeLine).Append(") Tj\n0 -18 Td\n");
        }

        content.Append("ET");
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content.ToString())} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        foreach (var (value, index) in objects.Select((value, index) => (value, index)))
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append($"{index + 1} 0 obj\n{value}\nendobj\n");
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        pdf.Append(string.Join("", offsets.Skip(1).Select(offset => $"{offset:0000000000} 00000 n \n")));
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}