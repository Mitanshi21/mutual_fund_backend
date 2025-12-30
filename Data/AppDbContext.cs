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
        public DbSet<Amc> AMCs { get; set; }
        public DbSet<PortfolioDisclosure> PortfolioDisclosures { get; set; }
        public DbSet<Fund> Funds { get; set; }
        public DbSet<PortfolioSnapshot> PortfolioSnapshots { get; set; }
        public DbSet<InstrumentHeader> InstrumentHeaders { get; set; }
        public DbSet<Industry> Industries { get; set; }
        public DbSet<InstrumentMaster> Instruments { get; set; }
        public DbSet<PortfolioHolding> PortfolioHoldings { get; set; }
        public DbSet<UploadEntry> Uploads { get; set; }
    }
}
