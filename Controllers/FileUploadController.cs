using Microsoft.AspNetCore.Mvc;
using mutual_fund_backend.Data;
using mutual_fund_backend.Services; // Ensure you have this using for ExcelProcessingService

namespace mutual_fund_backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FileUploadController : Controller
    {
        private readonly AppDbContext _context;

        public FileUploadController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost("upload-excel")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadExcel([FromForm] MultiUploadRequest request)
        {
            if (request.Rows == null || request.Rows.Count == 0)
                return BadRequest("No upload rows provided");

            // 1. Initialize a master list to hold warnings from ALL files
            var masterWarningList = new List<string>();

            foreach (var row in request.Rows)
            {
                if (row.Amc_Id <= 0 || row.Portfolio_Type_Id <= 0)
                    return BadRequest("Invalid AMC or Portfolio in one of the rows");

                if (row.Files == null || row.Files.Count == 0)
                    return BadRequest("One of the rows has no files");

                string amcName = _context.AMCs
                    .Where(a => a.id == row.Amc_Id)
                    .Select(a => a.amcname)
                    .FirstOrDefault();

                string portfolioTypeName = _context.PortfolioDisclosures
                    .Where(p => p.id == row.Portfolio_Type_Id)
                    .Select(p => p.portfolio_type)
                    .FirstOrDefault();

                foreach (var file in row.Files)
                {
                    var extension = Path.GetExtension(file.FileName);
                    string safeAmc = SanitizeFileName(amcName);
                    string safePortfolio = SanitizeFileName(portfolioTypeName);
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string fileName = $"{safeAmc}_{safePortfolio}_{timestamp}{extension}";

                    var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads");
                    if (!Directory.Exists(uploadsDir))
                        Directory.CreateDirectory(uploadsDir);

                    var path = Path.Combine(uploadsDir, fileName);

                    using (var stream = new FileStream(path, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    // Instantiate the service (Or better: Inject it via Constructor)
                    var processor = new ExcelProcessingService(_context);

                    // 2. Capture the warnings returned by the service
                    List<string> fileWarnings = await processor.ProcessExcel(
                        path,
                        row.Amc_Id,
                        row.Portfolio_Type_Id
                    );

                    // 3. Add them to the master list
                    if (fileWarnings != null && fileWarnings.Count > 0)
                    {
                        masterWarningList.AddRange(fileWarnings);
                    }
                }
            }

            // 4. Return the list in the response
            return Ok(new
            {
                message = masterWarningList.Count > 0 
                          ? "Processed with warnings" 
                          : "All rows and files processed successfully",
                warnings = masterWarningList
            });
        }

        public static string SanitizeFileName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "Unknown"; // Safety check
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(c, '_');
            }
            return value.Replace(" ", "_");
        }

        public class UploadRow
        {
            public int Amc_Id { get; set; }
            public int Portfolio_Type_Id { get; set; }
            public List<IFormFile> Files { get; set; }
        }

        public class MultiUploadRequest
        {
            public List<UploadRow> Rows { get; set; }
        }
    }
}