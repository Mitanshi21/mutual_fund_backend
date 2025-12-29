using Microsoft.AspNetCore.Mvc;

namespace mutual_fund_backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FileUploadController : Controller
    {
        [HttpPost("upload-excel")]
        public IActionResult UploadExcel([FromForm] List<IFormFile> files)
        {
            // Save files to temp folder
            foreach (var file in files)
            {
                var path = Path.Combine("Uploads", Guid.NewGuid() + Path.GetExtension(file.FileName));
                using var stream = new FileStream(path, FileMode.Create);
                file.CopyTo(stream);

                // Fire-and-forget processing
                Task.Run(() =>
                {
                    var processor = new ExcelProcessingService();
                    processor.ProcessExcel(path);
                });
            }

            return Ok(new { message = "Files uploaded successfully. Processing started." });
        }
    }
}
