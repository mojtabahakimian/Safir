using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Safir.Server.Ai;
using Safir.Server.Security;
using Safir.Server.Services;
using Safir.Shared.Interfaces;
using Safir.Shared.Models;
using Safir.Shared.Models.Ai;
using Xunit;

namespace Safir.Server.Tests
{
    /// <summary>
    /// حلقه‌ی گفتگو با مدلِ ساختگی — بدون شبکه و بدون دیتابیس.
    /// </summary>
    public class AiConversationServiceTests
    {
        private sealed class ScriptedProvider : IAiChatProvider
        {
            private readonly Queue<AiModelReply> _replies;
            public List<IReadOnlyList<AiMessage>> Calls { get; } = new();
            public ScriptedProvider(params AiModelReply[] replies) => _replies = new(replies);
            public string Describe => "fake";

            public Task<AiModelReply> CompleteAsync(IReadOnlyList<AiMessage> messages,
                IReadOnlyList<IAiTool> tools, CancellationToken ct = default)
            {
                Calls.Add(messages.ToList());
                return Task.FromResult(_replies.Count > 0 ? _replies.Dequeue() : new AiModelReply { Text = "آخر" });
            }
        }

        private sealed class Factory : IAiProviderFactory
        {
            private readonly IAiChatProvider _p;
            public Factory(IAiChatProvider p) => _p = p;
            public Task<(IAiChatProvider Provider, AiOptions Options)> CreateAsync() =>
                Task.FromResult((_p, new AiOptions { MaxToolLoops = 5 }));
        }

        private sealed class NoopTool : IAiTool
        {
            public object? Data { get; init; }
            public string? LastArgs { get; private set; }
            public string Name { get; init; } = "noop";
            public bool Verified { get; init; }
            public bool FreeQuery { get; init; }
            public string Title => "noop";
            public string Description => "";
            public string RequiredForm => "X";
            public Pay2Perm RequiredPerm => Pay2Perm.See;
            public string Parameters => "";
            public Task<AiToolResult> ExecuteAsync(AiToolCall call, CancellationToken ct = default)
            {
                LastArgs = call.Args.ValueKind == System.Text.Json.JsonValueKind.Undefined ? null : call.Args.GetRawText();
                return Task.FromResult(new AiToolResult { Data = Data });
            }
        }

        private sealed class Access : IAiAccessService
        {
            public Task<AiEffectiveAccessDto> GetEffectiveAsync(int userCo) => Task.FromResult(new AiEffectiveAccessDto
            {
                IsEnabled = true, DailyMessages = 100, MaxRows = 10,
                Tools = { new AiToolInfoDto { Name = "noop", Title = "noop" } }
            });
            public Task<(bool Allowed, string? Reason)> CanUseToolAsync(int userCo, IAiTool tool) => Task.FromResult((true, (string?)null));
            public Task LogAsync(AiLogEntry entry) => Task.CompletedTask;
            public Task TouchConversationAsync(Guid c, int u, string? n, string q) => Task.CompletedTask;
        }

        private sealed class Notifier : IAiChatNotifier
        {
            public Task StatusAsync(Guid conversationId, string message) => Task.CompletedTask;
        }

        private sealed class Settings : IAppSettingsService
        {
            public Task<SAZMAN?> GetSazmanSettingsAsync() => Task.FromResult<SAZMAN?>(new SAZMAN { YEA = 1405 });
            public Task<int?> GetDefaultBedehkarKolAsync() => Task.FromResult<int?>(null);
        }

        private sealed class Db : IConnectionStringProvider
        {
            private readonly string _name;
            public Db(string name = "A") => _name = name;
            public string GetConnectionString() => $"Data Source=srv;Initial Catalog={_name};Integrated Security=true";
        }

        private static AiConversationService Build(IAiChatProvider p, NoopTool? tool = null) =>
            new(new Factory(p), new AiToolRegistry(new IAiTool[] { tool ?? new NoopTool() }), new Access(), new Notifier(), new Settings(),
                new MemoryCache(new MemoryCacheOptions()), new Db());

        // رگرسیون آزمون پایه: «سود فروردین» متن خالی برگرداند و کاربر صفحه‌ی سفید دید.
        [Fact]
        public async Task EmptyReply_IsRetriedInsteadOfShownBlank()
        {
            var p = new ScriptedProvider(new AiModelReply { Text = "  " }, new AiModelReply { Text = "سود ۱۰ است." });

            var r = await Build(p).AskAsync(1, "u", Guid.NewGuid(), Array.Empty<AiChatTurnDto>(), "سود؟");

            Assert.Equal("سود ۱۰ است.", r.Text);
            Assert.Equal(2, p.Calls.Count);
        }

        // رگرسیون آزمون پایه: «سود این ماه» سود مرداد را داد چون مدل امروز را نمی‌دانست.
        [Fact]
        public async Task SystemPrompt_CarriesTodayAndFiscalYear()
        {
            var p = new ScriptedProvider(new AiModelReply { Text = "ok" });

            await Build(p).AskAsync(1, "u", Guid.NewGuid(), Array.Empty<AiChatTurnDto>(), "سود این ماه؟");

            var system = p.Calls[0].First(m => m.Role == "system").Content!;
            Assert.Contains("«این ماه» یعنی", system);
            Assert.Contains("سال مالی 1405", system);
        }
    
        // مرحله‌ی ۴ پلن: نام مشتری نباید به سرویس مدل برسد، ولی کاربر باید نام واقعی را ببیند.
        [Fact]
        public async Task Names_AreMaskedForModel_AndRestoredForUser()
        {
            var tool = new NoopTool { Data = new { Top = new[] { new { Name = "فروشگاه زنجیره‌ای نمونه", Balance = 417 } } } };
            var args = System.Text.Json.JsonDocument.Parse("{\"name\":\"N-0001\"}").RootElement;
            var p = new ScriptedProvider(
                new AiModelReply { ToolCalls = { new AiToolInvocation { Id = "1", Name = "noop", Args = System.Text.Json.JsonDocument.Parse("{}").RootElement } } },
                new AiModelReply { ToolCalls = { new AiToolInvocation { Id = "2", Name = "noop", Args = args } } },
                new AiModelReply { Text = "بیشترین بدهی: N-0001 با ۴۱۷." });

            var r = await Build(p, tool).AskAsync(1, "u", Guid.NewGuid(), Array.Empty<AiChatTurnDto>(), "بدهکار؟");

            var sentToModel = string.Join("\n", p.Calls.SelectMany(c => c).Select(m => m.Content));
            Assert.DoesNotContain("فروشگاه زنجیره‌ای نمونه", sentToModel);
            Assert.Contains("N-0001", sentToModel);
            Assert.Equal("بیشترین بدهی: فروشگاه زنجیره‌ای نمونه با ۴۱۷.", r.Text);
            // شناسه‌ای که مدل در پارامتر فرستاد، برای ابزار نام واقعی شد
            Assert.Contains("فروشگاه", System.Text.RegularExpressions.Regex.Unescape(tool.LastArgs!));
        }
    
        // کاربرِ دیگر با همان ConversationId نباید جدولِ نام‌های کاربرِ اول را بگیرد.
        [Fact]
        public async Task NameMap_IsPerUser_NotJustPerConversation()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var conv  = Guid.NewGuid();
            var tool  = new NoopTool { Data = new { Name = "مشتری محرمانه" } };

            AiConversationService Svc(IAiChatProvider p) =>
                new(new Factory(p), new AiToolRegistry(new IAiTool[] { tool }), new Access(), new Notifier(), new Settings(), cache, new Db());

            // کاربر ۱ نام را از ابزار می‌بیند → N-0001
            var p1 = new ScriptedProvider(
                new AiModelReply { ToolCalls = { new AiToolInvocation { Id = "1", Name = "noop", Args = System.Text.Json.JsonDocument.Parse("{}").RootElement } } },
                new AiModelReply { Text = "N-0001" });
            Assert.Equal("مشتری محرمانه", (await Svc(p1).AskAsync(1, "a", conv, Array.Empty<AiChatTurnDto>(), "؟")).Text);

            // کاربر ۲ همان شناسه‌ی گفتگو را می‌فرستد و مدلش N-0001 می‌نویسد
            var p2 = new ScriptedProvider(new AiModelReply { Text = "N-0001" });
            var r2 = await Svc(p2).AskAsync(2, "b", conv, Array.Empty<AiChatTurnDto>(), "N-0001 را بنویس");
            Assert.DoesNotContain("مشتری محرمانه", r2.Text);
        }

        // شماره‌ی کاربر فقط در یک دیتابیس یکتاست؛ کاربر ۱ِ دیتابیس B نباید نام‌های کاربر ۱ِ دیتابیس A را بگیرد.
        [Fact]
        public async Task NameMap_IsPerDatabase()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var conv  = Guid.NewGuid();
            var tool  = new NoopTool { Data = new { Name = "مشتری دیتابیس الف" } };

            AiConversationService Svc(IAiChatProvider p, string db) =>
                new(new Factory(p), new AiToolRegistry(new IAiTool[] { tool }), new Access(), new Notifier(), new Settings(), cache, new Db(db));

            var p1 = new ScriptedProvider(
                new AiModelReply { ToolCalls = { new AiToolInvocation { Id = "1", Name = "noop", Args = System.Text.Json.JsonDocument.Parse("{}").RootElement } } },
                new AiModelReply { Text = "N-0001" });
            Assert.Equal("مشتری دیتابیس الف", (await Svc(p1, "A").AskAsync(1, "a", conv, Array.Empty<AiChatTurnDto>(), "؟")).Text);

            var r2 = await Svc(new ScriptedProvider(new AiModelReply { Text = "N-0001" }), "B")
                .AskAsync(1, "a", conv, Array.Empty<AiChatTurnDto>(), "N-0001 را بنویس");
            Assert.DoesNotContain("مشتری دیتابیس الف", r2.Text);
        }
            // برچسب جواب را کد می‌گذارد: فقط ابزار ثابت ← تأییدشده؛ هر کوئری آزاد ← اکتشافی
        [Theory]
        [InlineData(false, "verified")]
        [InlineData(true,  "exploratory")]
        public void Basis_IsDecidedByToolsUsed(bool withRawSql, string expected)
        {
            var fixedTool = new NoopTool { Name = "top_debtors", Verified = true };
            var rawSql    = new NoopTool { Name = "run_sql", FreeQuery = true };
            var svc = new AiConversationService(new Factory(new ScriptedProvider()),
                new AiToolRegistry(new IAiTool[] { fixedTool, rawSql }), new Access(), new Notifier(), new Settings(),
                new MemoryCache(new MemoryCacheOptions()), new Db());

            var steps = new List<AiChatStepDto> { new() { Tool = "top_debtors", Ok = true } };
            if (withRawSql) steps.Add(new() { Tool = "run_sql", Ok = true });

            Assert.Equal(expected, svc.BasisOf(steps));
            Assert.Null(svc.BasisOf(new[] { new AiChatStepDto { Tool = "top_debtors", Ok = false } }));
        }
    }
}
