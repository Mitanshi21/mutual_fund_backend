using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using mutual_fund_backend.Data;
using mutual_fund_backend.Models;

namespace mutual_fund_backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PortfolioDisclosureController : ControllerBase
    {
        private readonly AppDbContext _context;

        public PortfolioDisclosureController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/PortfolioDisclosure
        [HttpGet]
        public async Task<ActionResult<IEnumerable<PortfolioDisclosure>>> GetPorfolioDisclosures()
        {
            return await _context.PortfolioDisclosures.ToListAsync();
        }
    }
}
