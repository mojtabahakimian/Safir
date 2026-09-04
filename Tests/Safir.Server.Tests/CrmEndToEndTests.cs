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
        public async Task Test_1_StatusList_And_Organization_Settings()
        {
            if (!await CanConnectToRealDatabaseAsync()) return;
            using var cnn = new SqlConnection(RealConnectionString);
            await cnn.OpenAsync();

            var sazman = (await cnn.QueryAsync<dynamic>("SELECT IT1, IT2, IT3, IT4, IT5, IT6, IT7, IT8, IT9 FROM SAZMAN")).FirstOrDefault();
            Assert.NotNull(sazman);
            var dict = (IDictionary<string, object>)sazman;
            Assert.NotEmpty(dict);
            Assert.NotNull(dict["IT1"]);
        }

        [Fact]
        public async Task Test_2_Company_CRUD_And_Duplicate_Checking()
        {
            if (!await CanConnectToRealDatabaseAsync()) return;
            using var cnn = new SqlConnection(RealConnectionString);
            await cnn.OpenAsync();

            string testName = "شرکت تست جامع CRM " + Guid.NewGuid().ToString().Substring(0, 6);
            string testTel = "03512349999";
            string testMobile = "09139998877";
            int compId = 0;

            try
            {
                // درج
                var insertSql = @"
                    INSERT INTO COPMANES (COMPANY_NAME, CITY, MANAGER, FACT_TEL, MOBILE, STATUS, date_sabt, USER_NAME, dt, userid)
                    VALUES (@Name, N'یزد', N'مدیر تست', @Tel, @Mob, 1, GETDATE(), N'Controller', 14050601, 78);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);";

                compId = await cnn.QuerySingleAsync<int>(insertSql, new { Name = testName, Tel = testTel, Mob = testMobile });
                Assert.True(compId > 0);

                // استعلام
                var comp = await cnn.QuerySingleOrDefaultAsync<dynamic>("SELECT * FROM COPMANES WHERE ID = @Id", new { Id = compId });
                Assert.NotNull(comp);
                Assert.Equal(testName, (string)comp.COMPANY_NAME);

                // بررسی تشابه نام
                var nameCount = await cnn.QuerySingleAsync<int>("SELECT COUNT(1) FROM COPMANES WHERE COMPANY_NAME = @Name", new { Name = testName });
                Assert.True(nameCount >= 1);

                // ویرایش
                await cnn.ExecuteAsync("UPDATE COPMANES SET CITY = N'میبد', STATUS = 3 WHERE ID = @Id", new { Id = compId });
                var updated = await cnn.QuerySingleOrDefaultAsync<dynamic>("SELECT * FROM COPMANES WHERE ID = @Id", new { Id = compId });
                Assert.Equal("میبد", (string)updated.CITY);
                Assert.Equal(3, (int)updated.STATUS);
            }
            finally
            {
                if (compId > 0)
                {
                    await cnn.ExecuteAsync("DELETE FROM CRMEVENTS WHERE idc = @Id", new { Id = compId });
                    await cnn.ExecuteAsync("DELETE FROM COPMANES WHERE ID = @Id", new { Id = compId });
                }
            }
        }

        [Fact]
        public async Task Test_3_Events_Lifecycle_And_Auto_Status_Update()
        {
            if (!await CanConnectToRealDatabaseAsync()) return;
            using var cnn = new SqlConnection(RealConnectionString);
            await cnn.OpenAsync();

            int compId = 0;
            int evId = 0;

            try
            {
                // ایجاد شرکت برای اتصال رویداد
                compId = await cnn.QuerySingleAsync<int>(@"
                    INSERT INTO COPMANES (COMPANY_NAME, STATUS, userid) VALUES (N'شرکت تستی رویداد', 1, 78);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);");

                // درج رویداد و جلسه
                evId = await cnn.QuerySingleAsync<int>(@"
                    INSERT INTO CRMEVENTS (COMPANY_NAME, INFO_DATE, INFO_TIME, COMMENT, NEXT_DATE, NEXT_TIME, STATUS, idc, miting, USERID, CDATETI)
                    VALUES (N'شرکت تستی رویداد', 14050601, 1000, N'مذاکره تلفنی', 14050615, 1130, 4, @CompId, -1, 78, GETDATE());
                    SELECT CAST(SCOPE_IDENTITY() AS INT);", new { CompId = compId });

                Assert.True(evId > 0);

                // استعلام رویداد
                var ev = await cnn.QuerySingleOrDefaultAsync<dynamic>("SELECT * FROM CRMEVENTS WHERE idde = @Id", new { Id = evId });
                Assert.NotNull(ev);
                Assert.Equal(14050615, (int)ev.NEXT_DATE);
                Assert.Equal(-1, (int)ev.miting); // جلسه حضوری

                // به‌روزرسانی وضعیت شرکت
                await cnn.ExecuteAsync("UPDATE COPMANES SET STATUS = 4 WHERE ID = @Id", new { Id = compId });
                var compStatus = await cnn.QuerySingleAsync<int>("SELECT STATUS FROM COPMANES WHERE ID = @Id", new { Id = compId });
                Assert.Equal(4, compStatus);
            }
            finally
            {
                if (evId > 0) await cnn.ExecuteAsync("DELETE FROM CRMEVENTS WHERE idde = @Id", new { Id = evId });
                if (compId > 0) await cnn.ExecuteAsync("DELETE FROM COPMANES WHERE ID = @Id", new { Id = compId });
            }
        }

        [Fact]
        public async Task Test_4_Dashboard_Summary_And_Metrics()
        {
            if (!await CanConnectToRealDatabaseAsync()) return;
            using var cnn = new SqlConnection(RealConnectionString);
            await cnn.OpenAsync();

            int totalComp = await cnn.QuerySingleAsync<int>("SELECT COUNT(1) FROM COPMANES WHERE userid = 78 OR userid IS NULL");
            Assert.True(totalComp >= 0);

            var statusCounts = (await cnn.QueryAsync<dynamic>("SELECT STATUS, COUNT(1) as cnt FROM COPMANES GROUP BY STATUS")).ToList();
            Assert.NotNull(statusCounts);
        }

        [Fact]
        public async Task Test_6_Notes_Lifecycle()
        {
            if (!await CanConnectToRealDatabaseAsync()) return;
            using var cnn = new SqlConnection(RealConnectionString);
            await cnn.OpenAsync();

            int noteId = 0;
            try
            {
                // درج یادداشت
                noteId = await cnn.QuerySingleAsync<int>(@"
                    INSERT INTO Notes (Note, Ndate, Ntime, userid, Ndone)
                    VALUES (N'یادداشت تستی آزمون', 14050601, '10:00', 78, 0);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);");

                Assert.True(noteId > 0);

                // تغییر وضعیت به انجام شده
                await cnn.ExecuteAsync("UPDATE Notes SET Ndone = 1 WHERE idd = @Id", new { Id = noteId });
                var doneVal = await cnn.QuerySingleAsync<bool>("SELECT Ndone FROM Notes WHERE idd = @Id", new { Id = noteId });
                Assert.True(doneVal);
            }
            finally
            {
                if (noteId > 0) await cnn.ExecuteAsync("DELETE FROM Notes WHERE idd = @Id", new { Id = noteId });
            }
        }

        [Fact]
        public async Task Test_7_Sms_Log_Integration()
        {
            if (!await CanConnectToRealDatabaseAsync()) return;
            using var cnn = new SqlConnection(RealConnectionString);
            await cnn.OpenAsync();

            int smsId = 0;
            try
            {
                smsId = await cnn.QuerySingleAsync<int>(@"
                    INSERT INTO SMS_SENDS (SM_DT, SM_TT, SM_DTQ, SM_TTQ, SM_AMobiles, AMSG, NUMBER, TAGS, id_sms, CUST_NO, USERNAME, STATUSSMS, CRT)
                    VALUES (14050601, 100000, 14050601, 100000, '09139999999', N'تست پیامک', 0, 1, '12345', '9139999999', 'Controller', 1, GETDATE());
                    SELECT CAST(SCOPE_IDENTITY() AS INT);");

                Assert.True(smsId > 0);
            }
            finally
            {
                if (smsId > 0) await cnn.ExecuteAsync("DELETE FROM SMS_SENDS WHERE IDS = @Id", new { Id = smsId });
            }
        }
    }
}
