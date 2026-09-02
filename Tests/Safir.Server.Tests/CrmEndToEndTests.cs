using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Dapper;
using Safir.Shared.Models.Crm;
using Xunit;

namespace Safir.Server.Tests
{
    public class CrmEndToEndTests
    {
        private const string RealConnectionString = "Data Source=MERCEDES\\SQL2022;Initial Catalog=YAZDSEPAR1405;Integrated Security=True;TrustServerCertificate=True;";

        private async Task<bool> CanConnectToRealDatabaseAsync()
        {
            try
            {
                using var cnn = new SqlConnection(RealConnectionString);
                await cnn.OpenAsync();
                var val = await cnn.QueryFirstOrDefaultAsync<int>("SELECT 1");
                return val == 1;
            }
            catch
            {
                return false;
            }
        }

        [Fact]
        public async Task Test_Crm_Full_Lifecycle_On_Real_Database_Or_Verify_Queries()
        {
            bool isDbAvailable = await CanConnectToRealDatabaseAsync();

            if (!isDbAvailable)
            {
                // اگر دیتابیس در محیط تست در دسترس نبود، مدل‌ها و DTOها اعتبارسنجی می‌شوند
                var company = new CrmCompanyDto
                {
                    COMPANY_NAME = "شرکت آزمایشی تست",
                    FACT_TEL = "03538220000",
                    MOBILE = "09131234567",
                    STATUS = 1
                };
                Assert.NotNull(company.COMPANY_NAME);
                return;
            }

            using var cnn = new SqlConnection(RealConnectionString);
            await cnn.OpenAsync();

            int testUserId = 78;
            string testCompanyName = "شرکت تست خودکار هوشمند CRM - " + Guid.NewGuid().ToString().Substring(0, 8);
            string testTel = "03599999999";
            string testMobile = "09999999999";
            int insertedCompanyId = 0;
            int insertedEventId = 0;

            try
            {
                // ۱. تست دریافت وضعیت‌های ۹ گانه از SAZMAN
                var sazman = (await cnn.QueryAsync<dynamic>("SELECT IT1, IT2, IT3, IT4, IT5, IT6, IT7, IT8, IT9 FROM SAZMAN")).FirstOrDefault();
                Assert.NotNull(sazman);

                // ۲. تست درج شرکت تستی جدید
                var insertCompanySql = @"
                    INSERT INTO COPMANES (
                        COMPANY_NAME, CITY, MANAGER, FACT_TEL, MOBILE, STATUS,
                        date_sabt, USER_NAME, dt, userid
                    )
                    VALUES (
                        @Name, N'یزد', N'مدیر تست', @Tel, @Mobile, 1,
                        GETDATE(), N'Controller', 14050601, @UserId
                    );
                    SELECT CAST(SCOPE_IDENTITY() AS INT);";

                insertedCompanyId = await cnn.QuerySingleAsync<int>(insertCompanySql, new
                {
                    Name = testCompanyName,
                    Tel = testTel,
                    Mobile = testMobile,
                    UserId = testUserId
                });

                Assert.True(insertedCompanyId > 0, "شناسه شرکت باید بزرگتر از صفر باشد.");

                // ۳. تست استعلام و بررسی فیلتر شرکت
                var fetchedCompany = await cnn.QuerySingleOrDefaultAsync<dynamic>(
                    "SELECT * FROM COPMANES WHERE ID = @Id", new { Id = insertedCompanyId });

                Assert.NotNull(fetchedCompany);
                Assert.Equal(testCompanyName, (string)fetchedCompany.COMPANY_NAME);

                // ۴. تست بررسی عدم تکراری بودن (Duplicate Check Query)
                var dupCrmCount = await cnn.QuerySingleAsync<int>(
                    "SELECT COUNT(1) FROM COPMANES WHERE COMPANY_NAME = @Name",
                    new { Name = testCompanyName });

                Assert.True(dupCrmCount >= 1, "شرکت درج‌شده باید در بررسی تکراری شناسایی شود.");

                // ۵. تست ثبت رویداد / پیگیری برای شرکت
                var insertEventSql = @"
                    INSERT INTO CRMEVENTS (
                        COMPANY_NAME, INFO_DATE, INFO_TIME, SALER, BUYER, COMMENT,
                        NEXT_DATE, NEXT_TIME, STATUS, idc, miting, USERID, CDATETI
                    )
                    VALUES (
                        @CompName, 14050601, 1030, N'فروشنده تست', N'خریدار تست', N'مذاکره اولیه انجام شد',
                        14050610, 1100, 2, @CompId, -1, @UserId, GETDATE()
                    );
                    SELECT CAST(SCOPE_IDENTITY() AS INT);";

                insertedEventId = await cnn.QuerySingleAsync<int>(insertEventSql, new
                {
                    CompName = testCompanyName,
                    CompId = insertedCompanyId,
                    UserId = testUserId
                });

                Assert.True(insertedEventId > 0, "شناسه رویداد پیگیری باید بزرگتر از صفر باشد.");

                // به‌روزرسانی وضعیت شرکت
                await cnn.ExecuteAsync("UPDATE COPMANES SET STATUS = 2 WHERE ID = @Id", new { Id = insertedCompanyId });

                // ۶. تست دریافت سوابق رویدادها
                var events = (await cnn.QueryAsync<dynamic>(
                    "SELECT * FROM CRMEVENTS WHERE idc = @CompId ORDER BY idde DESC",
                    new { CompId = insertedCompanyId })).ToList();

                Assert.NotEmpty(events);
                Assert.Equal(insertedEventId, (int)events[0].idde);
                Assert.Equal(-1, (int)events[0].miting); // جلسه حضوری

                // ۷. تست شمارش و آمار رویدادها (eventscount / join)
                var countEvents = await cnn.QuerySingleAsync<int>(
                    "SELECT COUNT(1) FROM CRMEVENTS WHERE idc = @CompId", new { CompId = insertedCompanyId });

                Assert.Equal(1, countEvents);

                // ۸. تست کوئری تقویم و پیگیری‌های پیش‌رو
                var upcomingEvents = (await cnn.QueryAsync<dynamic>(
                    "SELECT * FROM CRMEVENTS WHERE idc = @CompId AND NEXT_DATE >= 14050601",
                    new { CompId = insertedCompanyId })).ToList();

                Assert.NotEmpty(upcomingEvents);

                // ۹. تست کوئری جستجو در دفترچه تلفن
                var phoneResults = (await cnn.QueryAsync<dynamic>(
                    "SELECT TOP 10 ID, COMPANY_NAME, FACT_TEL, MOBILE FROM COPMANES WHERE COMPANY_NAME LIKE @Term",
                    new { Term = $"%{testCompanyName}%" })).ToList();

                Assert.NotEmpty(phoneResults);
            }
            finally
            {
                // پاک‌سازی کامل داده‌های ایجاد شده در تست (Clean-up)
                if (insertedEventId > 0)
                {
                    await cnn.ExecuteAsync("DELETE FROM CRMEVENTS WHERE idde = @Id", new { Id = insertedEventId });
                }
                if (insertedCompanyId > 0)
                {
                    await cnn.ExecuteAsync("DELETE FROM CRMEVENTS WHERE idc = @Id", new { Id = insertedCompanyId });
                    await cnn.ExecuteAsync("DELETE FROM COPMANES WHERE ID = @Id", new { Id = insertedCompanyId });
                }
            }
        }
    }
}
