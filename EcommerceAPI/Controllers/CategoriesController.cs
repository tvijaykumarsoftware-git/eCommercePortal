using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EcommerceAPI.Data;
using EcommerceAPI.Models;

[Route("api/[controller]")]
[ApiController]
public class CategoriesController : ControllerBase
{
    private readonly EcommerceStoreDbContext _context;

    public CategoriesController(EcommerceStoreDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetCategories()
    {
        return await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsActive == true) // <--- ONLY ACTIVE
            .Select(category => new
            {
                category.CategoryId,
                category.Name,
                category.Description,
                category.IsActive
            })
            .ToListAsync();
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<Category>> CreateCategory([FromBody] Category category)
    {
        category.CategoryId = 0;
        category.Products = null!;
        category.IsActive = true; // Default to active
        _context.Categories.Add(category);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetCategories), new { id = category.CategoryId }, category);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateCategory(int id, [FromBody] Category category)
    {
        if (id != category.CategoryId) return BadRequest("ID mismatch");

        var existingCategory = await _context.Categories.FindAsync(id);
        if (existingCategory == null) return NotFound("Category not found");

        existingCategory.Name = category.Name;
        existingCategory.Description = category.Description;
        existingCategory.IsActive = category.IsActive;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw;
        }

        return NoContent();
    }

    // 2. SOFT DELETE (Update IsActive to false)
    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        var category = await _context.Categories.FindAsync(id);
        if (category == null) return NotFound(new { message = "Category not found." });

        // Instead of deleting, we just mark it inactive
        category.IsActive = false;

        
        await _context.SaveChangesAsync();
        return Ok(new { message = "Category has been marked as inactive." });
    }



}