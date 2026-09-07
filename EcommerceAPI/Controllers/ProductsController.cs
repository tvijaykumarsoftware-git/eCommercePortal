using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EcommerceAPI.Data;
using EcommerceAPI.Models;

[Route("api/[controller]")]
[ApiController]
public class ProductsController : ControllerBase
{
    private readonly EcommerceStoreDbContext _context;

    public ProductsController(EcommerceStoreDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetProducts([FromQuery] string? search, [FromQuery] int? categoryId)
    {
        var products = _context.Products
            .AsNoTracking()
            .Where(product => string.IsNullOrWhiteSpace(search)
                || product.Name.Contains(search!)
                || (product.Description != null && product.Description.Contains(search!)))
            .Where(product => !categoryId.HasValue || product.CategoryId == categoryId.Value)
            .Select(product => new
            {
                product.ProductId,
                product.CategoryId,
                CategoryName = product.Category.Name,
                product.Name,
                product.Description,
                product.Price,
                product.StockQuantity,
                product.ImageUrl
            });

        return await products.ToListAsync();
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<Product>> CreateProduct([FromBody] Product product)
    {
        _context.Products.Add(product);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetProducts), new { id = product.ProductId }, product);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateProduct(int id, [FromBody] Product product)
    {
        if (id != product.ProductId) return BadRequest();
        _context.Entry(product).State = EntityState.Modified;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProduct(int id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null) return NotFound();
        _context.Products.Remove(product);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}