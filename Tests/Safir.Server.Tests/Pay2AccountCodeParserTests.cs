using Safir.Server.Services;
using Xunit;

namespace Safir.Server.Tests
{
    public class Pay2AccountCodeParserTests
    {
        [Fact]
        public void Parse_Valid2Level_ReturnsCorrectData()
        {
            var res = Pay2AccountCodeParser.Parse("112-1", "Test");
            Assert.True(res.IsValid);
            Assert.Equal(112, res.HesK);
            Assert.Equal(1, res.HesM);
            Assert.Equal(0, res.HesT); // Contract default
            Assert.Null(res.HesT2);
        }

        [Fact]
        public void Parse_Valid3Level_ReturnsCorrectData()
        {
            var res = Pay2AccountCodeParser.Parse("213-1-150", "Test");
            Assert.True(res.IsValid);
            Assert.Equal(213, res.HesK);
            Assert.Equal(1, res.HesM);
            Assert.Equal(150, res.HesT);
            Assert.Null(res.HesT2);
        }

        [Fact]
        public void Parse_Valid6Level_ReturnsCorrectData()
        {
            var res = Pay2AccountCodeParser.Parse("112-1-2-3-4-5", "Test");
            Assert.True(res.IsValid);
            Assert.Equal(112, res.HesK);
            Assert.Equal(1, res.HesM);
            Assert.Equal(2, res.HesT);
            Assert.Equal(3, res.HesT2);
            Assert.Equal(4, res.HesT3);
            Assert.Equal(5, res.HesT4);
        }

        [Fact]
        public void Parse_EmptyOrNull_ReturnsInvalid()
        {
            Assert.False(Pay2AccountCodeParser.Parse("", "Test").IsValid);
            Assert.False(Pay2AccountCodeParser.Parse(null, "Test").IsValid);
            Assert.False(Pay2AccountCodeParser.Parse("   ", "Test").IsValid);
        }

        [Fact]
        public void Parse_OneLevel_ReturnsInvalid()
        {
            Assert.False(Pay2AccountCodeParser.Parse("112", "Test").IsValid);
        }

        [Fact]
        public void Parse_SevenLevel_ReturnsInvalid()
        {
            Assert.False(Pay2AccountCodeParser.Parse("1-2-3-4-5-6-7", "Test").IsValid);
        }

        [Fact]
        public void Parse_NonNumeric_ReturnsInvalid()
        {
            Assert.False(Pay2AccountCodeParser.Parse("112-A-150", "Test").IsValid);
        }

        [Fact]
        public void Parse_DuplicateDashOrEmptySegment_ReturnsInvalid()
        {
            Assert.False(Pay2AccountCodeParser.Parse("112--1", "Test").IsValid);
            Assert.False(Pay2AccountCodeParser.Parse("-112-1", "Test").IsValid);
            Assert.False(Pay2AccountCodeParser.Parse("112-1-", "Test").IsValid);
        }

        [Fact]
        public void Parse_Overflow_ReturnsInvalid()
        {
            Assert.False(Pay2AccountCodeParser.Parse("9999999999999-1", "Test").IsValid);
        }
    }
}
