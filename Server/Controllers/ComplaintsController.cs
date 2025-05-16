using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Complaints;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization; // برای AllowAnonymous

namespace Safir.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [AllowAnonymous] // این کنترلر برای همه قابل دسترس است
    public class ComplaintsController : ControllerBase
    {
        private readonly IDatabaseService _dbService;
        private readonly ILogger<ComplaintsController> _logger;

        public ComplaintsController(IDatabaseService dbService, ILogger<ComplaintsController> logger)
        {
            _dbService = dbService;
            _logger = logger;
        }

        [HttpPost("submit")]
        public async Task<IActionResult> SubmitComplaint([FromBody] ComplaintFormDto complaintDto)
        {
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Invalid complaint submission received.");
                return BadRequest(ModelState);
            }

            try
            {
                // شما می‌توانید قبل از ذخیره، منطق بیشتری مانند ارسال ایمیل اطلاع‌رسانی و ... اضافه کنید
                string sql = @"INSERT INTO [dbo].[CustomerComplaints]
                                   ([CustomerFirstName],[CustomerLastName],[CustomerMobile],[CustomerEmail],[CustomerAddress]
                                   ,[ProductTypeComplaint],[PizzaType],[ProductWeight],[ProductionDate],[ExpiryDate],[ProductCode]
                                   ,[OtherDairyProductName],[PurchaseLocation],[PurchaseDate],[BatchNumber],[ComplaintRegisteredDate]
                                   ,[IsComplaintType_TasteSmell],[IsComplaintType_Packaging],[IsComplaintType_WrongExpiryDate]
                                   ,[IsComplaintType_NonConformity],[IsComplaintType_ForeignObject],[IsComplaintType_AbnormalTexture]
                                   ,[IsComplaintType_Mold],[IsComplaintType_Other],[ComplaintType_OtherDescription]
                                   ,[ComplaintDescription],[CustomerActionTaken],[CustomerActionDescription]
                                   ,[RequestedResolution_Refund],[RequestedResolution_Replacement],[RequestedResolution_FurtherInvestigation]
                                   ,[RequestedResolution_Explanation],[InformationConfirmed])
                             VALUES
                                   (@CustomerFirstName, @CustomerLastName, @CustomerMobile, @CustomerEmail, @CustomerAddress,
                                    @ProductTypeComplaint, @PizzaType, @ProductWeight, @ProductionDate, @ExpiryDate, @ProductCode,
                                    @OtherDairyProductName, @PurchaseLocation, @PurchaseDate, @BatchNumber, @ComplaintRegisteredDate,
                                    @IsComplaintType_TasteSmell, @IsComplaintType_Packaging, @IsComplaintType_WrongExpiryDate,
                                    @IsComplaintType_NonConformity, @IsComplaintType_ForeignObject, @IsComplaintType_AbnormalTexture,
                                    @IsComplaintType_Mold, @IsComplaintType_Other, @ComplaintType_OtherDescription,
                                    @ComplaintDescription, @CustomerActionTaken, @CustomerActionDescription,
                                    @RequestedResolution_Refund, @RequestedResolution_Replacement, @RequestedResolution_FurtherInvestigation,
                                    @RequestedResolution_Explanation, @InformationConfirmed)";

                int result = await _dbService.DoExecuteSQLAsync(sql, complaintDto);

                if (result > 0)
                {
                    _logger.LogInformation("New complaint submitted successfully from {Mobile}", complaintDto.CustomerMobile);
                    return Ok(new { Message = "شکایت شما با موفقیت ثبت شد. از اطلاع‌رسانی شما سپاسگزاریم." });
                }
                else
                {
                    _logger.LogError("Failed to save complaint for {Mobile}", complaintDto.CustomerMobile);
                    return StatusCode(500, "خطایی در ثبت شکایت رخ داد. لطفاً بعداً تلاش کنید.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting complaint for {Mobile}", complaintDto.CustomerMobile);
                return StatusCode(500, "خطای داخلی سرور. لطفاً با پشتیبانی تماس بگیرید.");
            }
        }
    }
}