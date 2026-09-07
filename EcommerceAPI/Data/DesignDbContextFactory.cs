using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EcommerceAPI.Data
{
    public class DesignDbContextFactory : IDesignTimeDbContextFactory<EcommerceStoreDbContext>
    {
        public EcommerceStoreDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<EcommerceStoreDbContext>();
            
            optionsBuilder.UseSqlServer("Server=DESKTOP-6U29I5E;Database=EcommerceStoreDB;Trusted_Connection=True;TrustServerCertificate=True;");

            return new EcommerceStoreDbContext(optionsBuilder.Options);
        }
    }
}