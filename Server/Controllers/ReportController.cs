using Microsoft.AspNetCore.Mvc;
using Stimulsoft.Report.Dictionary;
using Stimulsoft.Report;
using Safir.Shared.Models;
using Stimulsoft.Report.Components;
using Stimulsoft.Report.Export;

namespace Safir.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReportsController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;
        private readonly string _connString;
        private readonly ILogger<ReportsController> _logger;

        public ReportsController(IWebHostEnvironment env, IConfiguration config, ILogger<ReportsController> logger)
        {
            _env = env;
            _connString = config.GetConnectionString("DefaultConnection")!;
            _logger = logger;
        }

        [HttpPost("Generate")]
        public async Task<IActionResult> Generate([FromBody] ReportRequest req)
        {
            // 1. Locate the .mrt file
            var reportsFolder = Path.Combine(_env.ContentRootPath, "Rpts");
            var path = Path.Combine(reportsFolder, req.ReportName);
            if (!System.IO.File.Exists(path))
                return NotFound($"Report template '{req.ReportName}' not found.");

            // 2. Load & wire up DB
            var report = new StiReport();
            report.Load(path);
            var sqlDb = report.Dictionary
                    .Databases        // this is a StiDatabaseCollection
                    .OfType<StiSqlDatabase>()  // LINQ over the non-generic IEnumerable
                    .FirstOrDefault();

            if (sqlDb != null)
                sqlDb.ConnectionString = _connString;

            //foreach (var kv in req.Parameters)
            //{
            //    bool assigned = false;

            //    // 1. متغیر دیکشنری (Variables)
            //    var variable = report.Dictionary.Variables[kv.Key];
            //    if (variable != null)
            //    {
            //        variable.ValueObject = kv.Value;
            //        assigned = true;
            //    }

            //    foreach (var src in report.Dictionary.DataSources.OfType<StiSqlSource>())
            //    {
            //        var param = src.Parameters[kv.Key];
            //        if (param != null)
            //        {
            //            // 2. پارامترهای SQL و Expression و ... (این روش برای همه‌شون جواب میده!)
            //            report[kv.Key] = kv.Value?.ToString() ?? "";
            //            assigned = true;
            //        }
            //    }

            //    // 3. ست کردن کنترل‌های گزارش (مثلاً برای نمایش اطلاعات پویا)
            //    if (!assigned && report.GetComponentByName(kv.Key) is Stimulsoft.Report.Components.StiText txt)
            //    {
            //        txt.Text = kv.Value?.ToString() ?? "";
            //        assigned = true;
            //    }
            //}

            foreach (var kv in req.Parameters)
            {
                // اول سعی کن پارامتر دیتابیس/اکسپریشن/ویرایشی رو ست کنی
                report[kv.Key] = kv.Value?.ToString() ?? "";

                // بعد اگر کنترل متنی با این اسم داشتی ست کن (اختیاری)
                if (report.GetComponentByName(kv.Key) is StiText txt)
                    txt.Text = kv.Value?.ToString() ?? "";
            }



            // 4. Render & export
            return await Task.Run(() =>
            {
                var pdfSettings = new StiPdfExportSettings
                {
                    EmbeddedFonts = true,
                    UseUnicode = true,
                    Compressed = true,
                    ImageQuality = 0.75f, // Adjust image quality/compression
                    ImageResolution = 150, // Adjust image resolution //or 300f

                    ////StandardPdfFonts = false,
                    //ExportRtfTextAsImage = false,
                };

                report.Render(false);

                var exportService = new StiPdfExportService();
                using var ms = new MemoryStream();
                exportService.ExportTo(report, ms, pdfSettings);
                ms.Position = 0;

                var fileName = Path.GetFileNameWithoutExtension(req.ReportName) + ".pdf";
                return File(ms.ToArray(), "application/pdf", fileName);
            });
        }
    }
}