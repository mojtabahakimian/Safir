using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Safir.Server.Ai;
using Xunit;

namespace Safir.Server.Tests
{
    /// <summary>
    /// مدل جایگزین و پروکسیِ دستیار. در آزمون روی 9router، دو حساب از سه حساب
    /// 403 می‌دادند؛ بدون جایگزین، همان خطا مستقیم جلوی مدیر می‌آمد.
    /// </summary>
    public class AiFallbackProviderTests
    {
        private sealed class Fake : IAiChatProvider
        {
            private readonly AiModelReply _reply;
            public int Calls;
            public Fake(AiModelReply reply) => _reply = reply;
            public string Describe => "fake";
            public Task<AiModelReply> CompleteAsync(IReadOnlyList<AiMessage> m, IReadOnlyList<IAiTool> t, CancellationToken ct = default)
            {
                Calls++;
                return Task.FromResult(_reply);
            }
        }

        private static readonly AiMessage[] Msgs = { new() { Role = "user", Content = "سلام" } };

        [Fact]
        public async Task PrimaryOk_FallbackNotCalled()
        {
            var a = new Fake(new AiModelReply { Text = "A" });
            var b = new Fake(new AiModelReply { Text = "B" });

            var r = await new FallbackChatProvider(a, b).CompleteAsync(Msgs, Array.Empty<IAiTool>());

            Assert.Equal("A", r.Text);
            Assert.Equal(0, b.Calls);
        }

        [Fact]
        public async Task PrimaryFails_FallbackAnswers()
        {
            var a = new Fake(new AiModelReply { Error = "403" });
            var b = new Fake(new AiModelReply { Text = "B" });

            var r = await new FallbackChatProvider(a, b).CompleteAsync(Msgs, Array.Empty<IAiTool>());

            Assert.True(r.Ok);
            Assert.Equal("B", r.Text);
        }

        [Fact]
        public async Task BothFail_BothReasonsShown()
        {
            var a = new Fake(new AiModelReply { Error = "403" });
            var b = new Fake(new AiModelReply { Error = "timeout" });

            var r = await new FallbackChatProvider(a, b).CompleteAsync(Msgs, Array.Empty<IAiTool>());

            Assert.False(r.Ok);
            Assert.Contains("403", r.Error);
            Assert.Contains("timeout", r.Error);
        }

        private sealed class NamedFactory : IHttpClientFactory
        {
            public int Created;
            public HttpClient CreateClient(string name) { Created++; return new HttpClient(); }
        }

        [Fact]
        public void NoProxy_UsesNamedClient()
        {
            var f = new NamedFactory();
            AiHttp.Create(f, null);
            AiHttp.Create(f, "  ");
            Assert.Equal(2, f.Created);
        }

        [Fact]
        public void Proxy_BypassesNamedClient()
        {
            var f = new NamedFactory();
            using var http = AiHttp.Create(f, "http://127.0.0.1:10809");
            Assert.Equal(0, f.Created);
        }

        // درگاه محلی از پروکسی رد نشود؛ در آزمون زنده v2rayN برایش ۵۰۳ داد.
        [Fact]
        public void Proxy_BypassesLocalGateway()
        {
            var p = AiHttp.BuildProxy("http://127.0.0.1:10809");
            Assert.True(p.IsBypassed(new Uri("http://localhost:20128/v1/models")));
            Assert.True(p.IsBypassed(new Uri("http://127.0.0.1:20128/v1/models")));
            Assert.False(p.IsBypassed(new Uri("https://api.anthropic.com/v1/messages")));
        }

        [Fact]
        public void Clone_DoesNotTouchOriginal()
        {
            var o = new AiOptions { Model = "a", ProxyUrl = "http://p:1" };
            var c = o.Clone();
            c.Model = "b"; c.ProxyUrl = null;
            Assert.Equal("a", o.Model);
            Assert.Equal("http://p:1", o.ProxyUrl);
        }
    }
}
