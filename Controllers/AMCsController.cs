using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using mutual_fund_backend.Data;
using mutual_fund_backend.Models;

namespace mutual_fund_backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AMCsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AMCsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/AMCs
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Amc>>> GetAMCs()
        {
            return await _context.AMCs.ToListAsync();
        }
    }
}
