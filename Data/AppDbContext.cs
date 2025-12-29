using Microsoft.EntityFrameworkCore;
using mutual_fund_backend.Models;
using System;

namespace mutual_fund_backend.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }
        public DbSet<AMC> AMCs { get; set; }

        public DbSet<PortfolioDisclosure> PortfolioDisclosures { get; set; }
    }
}
