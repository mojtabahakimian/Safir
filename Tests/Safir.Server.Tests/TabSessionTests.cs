using System.Text.Json;
using Blazored.LocalStorage;
using Microsoft.JSInterop;
using Safir.Client.Services;
using Safir.Shared.Models;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>
/// نشستِ هر تب (TabSession): دو تب مرورگر یک localStorage مشترک و هر کدام sessionStorage خودشان
/// را دارند. این آزمون‌ها همان را شبیه‌سازی می‌کنند: یک FakeLocal مشترک و برای هر تب یک FakeTab.
/// </summary>
public class TabSessionTests
{
    private sealed class FakeLocal : ILocalStorageService
    {
        public readonly Dictionary<string, string> Items = new();
        public event EventHandler<ChangingEventArgs>? Changing { add { } remove { } }
        public event EventHandler<ChangedEventArgs>? Changed { add { } remove { } }

        public ValueTask<T?> GetItemAsync<T>(string key, CancellationToken ct = default) =>
            ValueTask.FromResult(Items.TryGetValue(key, out var v) ? JsonSerializer.Deserialize<T>(v) : default);
        public ValueTask SetItemAsync<T>(string key, T data, CancellationToken ct = default)
        { Items[key] = JsonSerializer.Serialize(data); return ValueTask.CompletedTask; }
        public ValueTask RemoveItemAsync(string key, CancellationToken ct = default)
        { Items.Remove(key); return ValueTask.CompletedTask; }

        public ValueTask ClearAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<string?> GetItemAsStringAsync(string key, CancellationToken ct = default) =>
            ValueTask.FromResult(Items.TryGetValue(key, out var v) ? v : null);
        public ValueTask<string?> KeyAsync(int index, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<IEnumerable<string>> KeysAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<bool> ContainKeyAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<int> LengthAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask RemoveItemsAsync(IEnumerable<string> keys, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask SetItemAsStringAsync(string key, string data, CancellationToken ct = default)
        { Items[key] = data; return ValueTask.CompletedTask; }
    }

    /// <summary>sessionStorage یک تب</summary>
    private sealed class FakeTab : IJSRuntime
    {
        public readonly Dictionary<string, string> Session = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            var key = (string)args![0]!;
            object? result = null;
            switch (identifier)
            {
                case "sessionStorage.getItem": result = Session.TryGetValue(key, out var v) ? v : null; break;
                case "sessionStorage.setItem": Session[key] = (string)args[1]!; break;
                case "sessionStorage.removeItem": Session.Remove(key); break;
                default: throw new NotSupportedException(identifier);
            }
            return ValueTask.FromResult((TValue)result!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    private static DbConnectionSettings Db(string name) => new() { Server = "DB2", Database = name, IsWindowsAuthentication = true };

    // همان کاری که AuthService بعد از ورود موفق می‌کند
    private static async Task LoginAsync(TabSession tab, string token)
    {
        await tab.SetAsync(TabSession.AuthTokenKey, token);
        await tab.SaveAsLastLoginAsync();
    }

    [Fact]
    public async Task Last_login_keeps_token_and_database_of_the_same_tab()
    {
        var local = new FakeLocal();
        var yazd = new TabSession(new FakeTab(), local);
        var poodr = new TabSession(new FakeTab(), local);

        await yazd.SetAsync(TabSession.DbSettingsKey, Db("YAZDSEPAR1405"));
        await LoginAsync(yazd, "token-yazd");

        await poodr.SetAsync(TabSession.DbSettingsKey, Db("NEWPOODR1405"));   // فقط ذخیره‌ی تنظیم، بدون ورود

        await LoginAsync(yazd, "token-yazd-2");                              // ورود دوباره در تب یزدسپار

        // مرورگر بسته و دوباره باز شد: تب تازه از «آخرین ورود» شروع می‌کند
        var reopened = new TabSession(new FakeTab(), local);
        Assert.Equal("token-yazd-2", await reopened.GetAsync<string>(TabSession.AuthTokenKey));
        Assert.Equal("YAZDSEPAR1405", (await reopened.GetAsync<DbConnectionSettings>(TabSession.DbSettingsKey))!.Database);
    }

    [Fact]
    public async Task Tab_without_own_database_clears_last_database_on_login()
    {
        var local = new FakeLocal();
        var poodr = new TabSession(new FakeTab(), local);
        await poodr.SetAsync(TabSession.DbSettingsKey, Db("NEWPOODR1405"));
        await LoginAsync(poodr, "token-poodr");

        // تبی که تنظیمش را پاک کرده و با دیتابیس پیش‌فرض سرور وارد می‌شود (ClearSettingsAsync)
        var tab = new TabSession(new FakeTab(), local);
        await tab.RemoveAsync<DbConnectionSettings>(TabSession.DbSettingsKey);
        await LoginAsync(tab, "token-default");

        Assert.False(local.Items.ContainsKey(TabSession.DbSettingsKey));
        Assert.Equal("token-default", await local.GetItemAsync<string>(TabSession.AuthTokenKey));
    }

    [Fact]
    public async Task Each_tab_keeps_its_own_values_and_refresh_does_not_switch()
    {
        var local = new FakeLocal();
        var yazdJs = new FakeTab();
        var yazd = new TabSession(yazdJs, local);
        await yazd.SetAsync(TabSession.DbSettingsKey, Db("YAZDSEPAR1405"));
        await LoginAsync(yazd, "token-yazd");

        var poodr = new TabSession(new FakeTab(), local);
        await poodr.SetAsync(TabSession.DbSettingsKey, Db("NEWPOODR1405"));
        await LoginAsync(poodr, "token-poodr");

        var yazdAfterRefresh = new TabSession(yazdJs, local);   // رفرش: همان sessionStorage، حافظه‌ی جدید
        Assert.Equal("token-yazd", await yazdAfterRefresh.GetAsync<string>(TabSession.AuthTokenKey));
        Assert.Equal("YAZDSEPAR1405", (await yazdAfterRefresh.GetAsync<DbConnectionSettings>(TabSession.DbSettingsKey))!.Database);
    }

    [Fact]
    public async Task New_company_tab_does_not_take_the_last_token()
    {
        var local = new FakeLocal();
        await LoginAsync(new TabSession(new FakeTab(), local), "token-yazd");

        var js = new FakeTab();
        js.Session["safir.freshTab"] = "1";
        Assert.Null(await new TabSession(js, local).GetAsync<string>(TabSession.AuthTokenKey));
    }

    [Fact]
    public async Task Logout_keeps_last_login_of_another_tab()
    {
        var local = new FakeLocal();
        var yazd = new TabSession(new FakeTab(), local);
        await LoginAsync(yazd, "token-yazd");
        var poodr = new TabSession(new FakeTab(), local);
        await LoginAsync(poodr, "token-poodr");

        await yazd.RemoveAsync<string>(TabSession.AuthTokenKey);   // خروج از یزدسپار

        Assert.Equal("token-poodr", await local.GetItemAsync<string>(TabSession.AuthTokenKey));
    }

    // خروج از یک تب و رفرش نباید توکنِ تبی را که بعداً وارد شده جایگزین کند
    [Fact]
    public async Task Logout_then_refresh_stays_logged_out_even_if_another_tab_logged_in_later()
    {
        var local = new FakeLocal();
        var yazdJs = new FakeTab();
        var yazd = new TabSession(yazdJs, local);
        await yazd.SetAsync(TabSession.DbSettingsKey, Db("YAZDSEPAR1405"));
        await LoginAsync(yazd, "token-yazd");

        var poodr = new TabSession(new FakeTab(), local);
        await poodr.SetAsync(TabSession.DbSettingsKey, Db("YAZDSEPAR1405"));
        await LoginAsync(poodr, "token-other-user");

        await yazd.RemoveAsync<string>(TabSession.AuthTokenKey);   // خروج
        var afterReload = new TabSession(yazdJs, local);           // LogoutUser با forceLoad رفرش می‌کند

        Assert.Null(await afterReload.GetAsync<string>(TabSession.AuthTokenKey));
        Assert.Equal("token-other-user", await local.GetItemAsync<string>(TabSession.AuthTokenKey));
    }

    // ذخیره‌ی تنظیم دیتابیس بدون ورود، «آخرین ورود» را خراب نمی‌کند
    [Fact]
    public async Task Saving_settings_without_login_keeps_last_login_pair()
    {
        var local = new FakeLocal();
        var yazd = new TabSession(new FakeTab(), local);
        await yazd.SetAsync(TabSession.DbSettingsKey, Db("YAZDSEPAR1405"));
        await LoginAsync(yazd, "token-yazd");

        await new TabSession(new FakeTab(), local).SetAsync(TabSession.DbSettingsKey, Db("NEWPOODR1405"));

        var reopened = new TabSession(new FakeTab(), local);
        Assert.Equal("token-yazd", await reopened.GetAsync<string>(TabSession.AuthTokenKey));
        Assert.Equal("YAZDSEPAR1405", (await reopened.GetAsync<DbConnectionSettings>(TabSession.DbSettingsKey))!.Database);
    }

    // تب جدید توکن و دیتابیس را با هم برمی‌دارد، حتی اگر بین دو خواندن تب دیگری وارد شود
    [Fact]
    public async Task New_tab_takes_token_and_database_of_the_same_login()
    {
        var local = new FakeLocal();
        var yazd = new TabSession(new FakeTab(), local);
        await yazd.SetAsync(TabSession.DbSettingsKey, Db("YAZDSEPAR1405"));
        await LoginAsync(yazd, "token-yazd");

        var newTab = new TabSession(new FakeTab(), local);
        var db = await newTab.GetAsync<DbConnectionSettings>(TabSession.DbSettingsKey);   // Program.cs: اول تنظیم

        var poodr = new TabSession(new FakeTab(), local);                                // هم‌زمان تب دیگری وارد پودر می‌شود
        await poodr.SetAsync(TabSession.DbSettingsKey, Db("NEWPOODR1405"));
        await LoginAsync(poodr, "token-poodr");

        Assert.Equal("YAZDSEPAR1405", db!.Database);
        Assert.Equal("token-yazd", await newTab.GetAsync<string>(TabSession.AuthTokenKey)); // بعد توکن: همان ورود
    }
}
