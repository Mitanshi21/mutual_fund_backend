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
        private readonly ExcelProcessingService _excelService;

        public FileUploadController(AppDbContext context, ExcelProcessingService excelService)
        {
            _context = context;
            _excelService = excelService;
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
                    //var processor = new ExcelProcessingService(_context);

                    // 2. Capture the warnings returned by the service
                    //List<string> fileWarnings = await processor.ProcessExcel(
                    //    path,
                    //    row.Amc_Id,
                    //    row.Portfolio_Type_Id
                    //);

                    await _excelService.ProcessExcel(
                     path,
                     row.Amc_Id,
                     row.Portfolio_Type_Id
                 );

                               // 3. Generate the URL dynamically
                // This creates a full URL like: https://localhost:7000/api/FileUpload/logs/today
                    string logUrl = $"{Request.Scheme}://{Request.Host}/api/FileUpload/logs/today";

                    // 4. Return response with the link
                    return Ok(new
                    {
                        message = "Files processed successfully.",
                        logDetailsUrl = logUrl,
                        note = "Click the link above to view/download the execution logs."
                    });
                }
            }

            // 4. Return the list in the response
            return Ok(new
            {
                message = "Files uploaded and processing started. Check server logs for details."
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

        [HttpGet("logs/today")]
        public IActionResult GetTodayLog()
        {
            // 1. Determine the file path based on today's date
            // Serilog rolling file format usually appends the date like: app-log-20250101.txt
            // Adjust "yyyyMMdd" to match your Serilog config if it differs.
            string datePart = DateTime.Now.ToString("yyyyMMdd");
            string fileName = $"app-log-{datePart}.txt";
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "logs", fileName);

            if (!System.IO.File.Exists(filePath))
            {
                return NotFound(new { message = "No log file generated for today yet." });
            }

            // 2. Open the file with FileShare.ReadWrite
            // Important: Serilog keeps the file open for writing. Using FileShare.ReadWrite
            // allows us to read it without crashing or locking the logger.
            var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            return File(fileStream, "text/plain", fileName);
        }
    }
}