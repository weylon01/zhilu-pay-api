using Microsoft.EntityFrameworkCore;

namespace Zhilu.PayApi.Data;

/// <summary>
/// 订单实体
/// </summary>
public class Order
{
    public string OrderNo { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string ProductType { get; set; } = string.Empty; // 例如：HollandTest, EnneagramTest, MBTI
    public int UserId { get; set; }
    public string? OpenId { get; set; }
    public string? PrepayId { get; set; }
    public string? TransactionId { get; set; } // 微信支付单号
    public OrderStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? PayNotifyData { get; set; }
}

/// <summary>
/// 订单状态
/// </summary>
public enum OrderStatus
{
    Pending = 0,      // 待支付
    Paid = 1,         // 已支付
    Failed = 2,       // 支付失败
    Refunded = 3,     // 已退款
    Expired = 4       // 已过期
}

/// <summary>
/// 数据库上下文
/// </summary>
public class ZhiluDbContext : DbContext
{
    public ZhiluDbContext(DbContextOptions<ZhiluDbContext> options) : base(options) { }
    
    public DbSet<Order> Orders => Set<Order>();
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.OrderNo);
            entity.Property(e => e.Amount).HasPrecision(10, 2);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.Status);
        });
    }
}
