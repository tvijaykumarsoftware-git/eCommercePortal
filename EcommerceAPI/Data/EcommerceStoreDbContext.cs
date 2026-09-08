using Microsoft.EntityFrameworkCore;
using EcommerceAPI.Models;

namespace EcommerceAPI.Data
{
    public class EcommerceStoreDbContext : DbContext
    {
        public EcommerceStoreDbContext(DbContextOptions<EcommerceStoreDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<CartItem> CartItems { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<Transaction> Transactions { get; set; }
        public DbSet<Invoice> Invoices { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ==========================================
            // 1. Roles & Users
            // ==========================================
            modelBuilder.Entity<Role>(entity =>
            {
                entity.HasKey(r => r.RoleId);
                entity.HasIndex(r => r.RoleName).IsUnique();
                entity.Property(r => r.RoleName).IsRequired().HasMaxLength(50).IsUnicode(false);
            });

            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(u => u.UserId);
                entity.HasIndex(u => u.Email).IsUnique();
                
                entity.Property(u => u.FirstName).IsRequired().HasMaxLength(100).IsUnicode(false);
                entity.Property(u => u.LastName).IsRequired().HasMaxLength(100).IsUnicode(false);
                entity.Property(u => u.Email).IsRequired().HasMaxLength(150).IsUnicode(false);
                entity.Property(u => u.ImageUrl).HasMaxLength(500).IsUnicode(false);
                entity.Property(u => u.Location).HasMaxLength(200).IsUnicode(false);
                entity.Property(u => u.PhoneNumber).HasMaxLength(30).IsUnicode(false);
                entity.Property(u => u.PasswordHash).IsRequired().IsUnicode(false);
                entity.Property(u => u.CreatedAt).HasDefaultValueSql("GETDATE()");
                entity.Property(u => u.IsActive).HasDefaultValue(true);

                // User -> Role (Many-to-One)
                entity.HasOne(u => u.Role)
                      .WithMany(r => r.Users)
                      .HasForeignKey(u => u.RoleId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ==========================================
            // 2. Categories & Products
            // ==========================================
            modelBuilder.Entity<Category>(entity =>
            {
                entity.HasKey(c => c.CategoryId);
                entity.Property(c => c.Name).IsRequired().HasMaxLength(100).IsUnicode(false);
                entity.Property(c => c.Description).HasMaxLength(250).IsUnicode(false);
                entity.Property(c => c.IsActive).HasDefaultValue(true);
            });

            modelBuilder.Entity<Product>(entity =>
            {
                entity.HasKey(p => p.ProductId);
                entity.Property(p => p.ProductId).ValueGeneratedOnAdd();
                entity.Property(p => p.Name).IsRequired().HasMaxLength(150).IsUnicode(false);
                entity.Property(p => p.Price).HasPrecision(18, 2);
                entity.Property(p => p.StockQuantity).HasDefaultValue(0);
                entity.Property(p => p.ImageUrl).HasMaxLength(500).IsUnicode(false);
                entity.Property(p => p.CreatedAt)
                      .ValueGeneratedOnAdd()
                      .HasDefaultValueSql("GETDATE()");

                // Product -> Category (Many-to-One)
                entity.HasOne(p => p.Category)
                      .WithMany(c => c.Products)
                      .HasForeignKey(p => p.CategoryId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ==========================================
            // 3. Cart Items
            // ==========================================
            modelBuilder.Entity<CartItem>(entity =>
            {
                entity.HasKey(ci => ci.CartItemId);
                entity.Property(ci => ci.Quantity).HasDefaultValue(1);
                entity.Property(ci => ci.AddedAt).HasDefaultValueSql("GETDATE()");

                // CartItem -> User (Cascade delete cart if user is removed)
                entity.HasOne(ci => ci.User)
                      .WithMany(u => u.CartItems)
                      .HasForeignKey(ci => ci.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                // CartItem -> Product
                entity.HasOne(ci => ci.Product)
                      .WithMany(p => p.CartItems)
                      .HasForeignKey(ci => ci.ProductId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ==========================================
            // 4. Orders & Order Items
            // ==========================================
            modelBuilder.Entity<Order>(entity =>
            {
                entity.HasKey(o => o.OrderId);
                entity.Property(o => o.TotalAmount).HasPrecision(18, 2);
                entity.Property(o => o.OrderStatus).IsRequired().HasMaxLength(50).HasDefaultValue("Pending").IsUnicode(false);
                entity.Property(o => o.ShippingAddress).IsRequired().IsUnicode(false);
                entity.Property(o => o.CreatedAt).HasDefaultValueSql("GETDATE()");

                // Order -> User (Restrict delete to preserve historical order logs)
                entity.HasOne(o => o.User)
                      .WithMany(u => u.Orders)
                      .HasForeignKey(o => o.UserId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<OrderItem>(entity =>
            {
                entity.HasKey(oi => oi.OrderItemId);
                entity.Property(oi => oi.UnitPrice).HasPrecision(18, 2);

                // OrderItem -> Order (Cascade delete line items if order is removed)
                entity.HasOne(oi => oi.Order)
                      .WithMany(o => o.OrderItems)
                      .HasForeignKey(oi => oi.OrderId)
                      .OnDelete(DeleteBehavior.Cascade);

                // OrderItem -> Product
                entity.HasOne(oi => oi.Product)
                      .WithMany(p => p.OrderItems)
                      .HasForeignKey(oi => oi.ProductId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ==========================================
            // 5. Transactions & Invoices
            // ==========================================
            modelBuilder.Entity<Transaction>(entity =>
            {
                entity.HasKey(t => t.TransactionId);
                entity.Property(t => t.PaymentProvider).IsRequired().HasMaxLength(50).IsUnicode(false);
                entity.Property(t => t.TransactionReference).IsRequired().HasMaxLength(100).IsUnicode(false);
                entity.Property(t => t.Amount).HasPrecision(18, 2);
                entity.Property(t => t.Status).IsRequired().HasMaxLength(50).IsUnicode(false);
                entity.Property(t => t.ProcessedAt).HasDefaultValueSql("GETDATE()");

                // Transaction -> Order
                entity.HasOne(t => t.Order)
                      .WithMany(o => o.Transactions)
                      .HasForeignKey(t => t.OrderId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Invoice>(entity =>
            {
                entity.HasKey(i => i.InvoiceId);
                entity.HasIndex(i => i.InvoiceNumber).IsUnique();
                
                entity.Property(i => i.InvoiceNumber).IsRequired().HasMaxLength(100).IsUnicode(false);
                entity.Property(i => i.InvoiceDate).HasDefaultValueSql("GETDATE()");
                entity.Property(i => i.PdfStoragePath).HasMaxLength(500).IsUnicode(false);

                // Invoice -> Order
                entity.HasOne(i => i.Order)
                      .WithMany(o => o.Invoices)
                      .HasForeignKey(i => i.OrderId)
                      .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}