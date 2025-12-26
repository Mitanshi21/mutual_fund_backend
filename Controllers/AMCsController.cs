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
        public async Task<ActionResult<IEnumerable<AMC>>> GetAMCs()
        {
            return await _context.AMCs.ToListAsync();
        }

        // GET: api/AMCs/5
        [HttpGet("{id}")]
        public async Task<ActionResult<AMC>> GetAMC(int id)
        {
            var aMC = await _context.AMCs.FindAsync(id);

            if (aMC == null)
            {
                return NotFound();
            }

            return aMC;
        }

        // PUT: api/AMCs/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutAMC(int id, AMC aMC)
        {
            if (id != aMC.id)
            {
                return BadRequest();
            }

            _context.Entry(aMC).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!AMCExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // POST: api/AMCs
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<AMC>> PostAMC(AMC aMC)
        {
            _context.AMCs.Add(aMC);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetAMC", new { id = aMC.id }, aMC);
        }

        // DELETE: api/AMCs/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAMC(int id)
        {
            var aMC = await _context.AMCs.FindAsync(id);
            if (aMC == null)
            {
                return NotFound();
            }

            _context.AMCs.Remove(aMC);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool AMCExists(int id)
        {
            return _context.AMCs.Any(e => e.id == id);
        }
    }
}
