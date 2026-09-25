using System.Linq;
using Safir.Server.Ai;
using Safir.Shared.Models.Ai;
using Xunit;

namespace Safir.Server.Tests
{
    /// <summary>یادداشت‌های «همیشه»ی حسابدار در پرامپت — با سقف طول، و بدون گم شدنِ بی‌صدا.</summary>
    public class AiKnowledgeTests
    {
        [Fact]
        public void Block_Empty_IsNull()
            => Assert.Null(AiKnowledgeStore.Block(Enumerable.Empty<AiKnowledgeDto>()));

        [Fact]
        public void Block_OverBudget_SaysWhatWasSkipped()
        {
            var notes = Enumerable.Range(1, 10).Select(i => new AiKnowledgeDto
                { Title = $"قاعده {i}", Body = new string('ا', 900) });

            var block = AiKnowledgeStore.Block(notes)!;

            Assert.True(block.Length <= AiKnowledgeStore.PromptBudget + 100);
            Assert.Contains("قاعده 1:", block);
            Assert.Contains("یادداشت دیگر جا نشد", block);
        }
    }
}
