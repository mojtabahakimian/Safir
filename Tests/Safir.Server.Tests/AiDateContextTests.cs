using System;
using System.Globalization;
using Safir.Server.Ai;
using Xunit;

namespace Safir.Server.Tests
{
    /// <summary>
    /// رگرسیون آزمون پایه‌ی دستیار (۱۴۰۵/۰۷/۰۳): «سود این ماه» با اطمینان سود
    /// مرداد را برگرداند چون مدل تاریخ امروز را نمی‌دانست.
    /// </summary>
    public class AiDateContextTests
    {
        private static DateTime Shamsi(int y, int m, int d) => new PersianCalendar().ToDateTime(y, m, d, 10, 0, 0, 0);

        [Fact]
        public void ThisMonth_IsCurrentShamsiMonthUpToToday()
        {
            var text = AiDateContext.Build(Shamsi(1405, 7, 3), 1405);

            Assert.Contains("امروز: 1405/07/03 (14050703)", text);
            Assert.Contains("«این ماه» یعنی مهر 1405: از 14050701 تا امروز 14050703", text);
            Assert.Contains("«ماه قبل» یعنی شهریور 1405: از 14050601 تا 14050631", text);
            Assert.DoesNotContain("سال مالی دیگری", text);
        }

        [Fact]
        public void PreviousMonth_InFarvardin_IsOtherFiscalYear()
        {
            var text = AiDateContext.Build(Shamsi(1405, 1, 10), 1405);

            Assert.Contains("«ماه قبل» یعنی اسفند 1404", text);
            Assert.Contains("ماه قبل (اسفند 1404) در سال مالی دیگری است", text);
        }

        [Fact]
        public void Esfand_LeapYear_Has30Days()
        {
            var pc = new PersianCalendar();
            int leap = 1403;
            Assert.True(pc.IsLeapYear(leap));

            var text = AiDateContext.Build(Shamsi(leap + 1, 1, 5), leap + 1);
            Assert.Contains($"از {leap}1201 تا {leap}1230", text);
        }

        [Fact]
        public void Today_OutsideFiscalYear_IsFlagged()
        {
            var text = AiDateContext.Build(Shamsi(1406, 2, 1), 1405);

            Assert.Contains("امروز خارج از سال مالی 1405 است", text);
            Assert.Contains("از 14050101 تا 14051229", text);
        }

        [Fact]
        public void NoFiscalYear_StillGivesDates()
        {
            var text = AiDateContext.Build(Shamsi(1405, 7, 3), null);

            Assert.Contains("امروز: 1405/07/03", text);
            Assert.DoesNotContain("سال مالی", text);
        }
    }
}
