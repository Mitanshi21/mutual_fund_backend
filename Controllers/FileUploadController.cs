using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace mutual_fund_backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FileUploadController : ControllerBase
    {
        [HttpPost("upload-excel")]
        [RequestSizeLimit(100 * 1024 * 1024)]
        public async Task<IActionResult> UploadExcel([FromForm] List<IFormFile> files)
        {
            if(files == null || files.Count == 0)
            {
                return BadRequest(new { message = "No files uploaded." });
            }

            foreach (var file in files)
            {
                if (file.Length > 0)
                {
                    var extension = System.IO.Path.GetExtension(file.FileName).ToLower();
                    if (extension != ".xls" && extension != ".xlsx")
                    {
                        return BadRequest(new { message = "Invalid file format. Please upload an Excel file." });
                    }

                }
            }

            return Ok(new { message = "Excel file uploaded successfully." });
        }

    }
}
