using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EcommerceAPI.Data;

namespace EcommerceAPI.Controllers
{
    [Authorize(Roles = "Admin")]
    [Route("api/[controller]")]
    [ApiController]
    public class TransactionsController : ControllerBase
    {
        private readonly EcommerceStoreDbContext _context;

        public TransactionsController(EcommerceStoreDbContext context) { _context = context; }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetTransactions()
        {
            return await _context.Transactions
                .AsNoTracking()
                .OrderByDescending(transaction => transaction.ProcessedAt)
                .Select(transaction => new
                {
                    transaction.TransactionId,
                    transaction.OrderId,
                    transaction.PaymentProvider,
                    transaction.TransactionReference,
                    transaction.Amount,
                    transaction.Status,
                    transaction.ProcessedAt
                })
                .ToListAsync();
        }
    }
}
