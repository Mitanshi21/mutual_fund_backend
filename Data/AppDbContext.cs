using Microsoft.EntityFrameworkCore;
using mutual_fund_backend.Models;
namespace mutual_fund_backend.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }
        public DbSet<AMC> AMCs { get; set; }
    }
}
