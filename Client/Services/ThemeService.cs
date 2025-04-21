using Microsoft.JSInterop;
using MudBlazor;

namespace Safir.Client.Services
{
    public class ThemeService
    {
        private readonly IJSRuntime _jsRuntime;
        private bool _isDarkMode = false;
        private readonly MudTheme _lightTheme;
        private readonly MudTheme _darkTheme;

        public event Action ThemeChanged;

        public bool IsDarkMode
        {
            get => _isDarkMode;
            set
            {
                if (_isDarkMode != value)
                {
                    _isDarkMode = value;
                    SaveThemePreference();
                    ThemeChanged?.Invoke();
                }
            }
        }

        #region Gemini

        public event Func<Task>? OnThemeChanged;

        // Define your Green color (adjust hex code as needed)
        //     Primary = "#2e7d32", // Green
        private static readonly string GreenColor = "#1DB954"; // Example Spotify Green

        private static readonly MudTheme LightTheme = new MudTheme()
        {
            Palette = new PaletteLight()
            {
                Primary = GreenColor,
                AppbarBackground = GreenColor,
                Background = Colors.Shades.White,
                TextPrimary = Colors.Grey.Darken3,
                // Add other palette color overrides if needed
            },
            Typography = new Typography()
            {
                Default = new Default()
                {
                    FontFamily = new[] { "IRANYekanFN", "Helvetica", "Arial", "sans-serif" }
                }
                // Define other typography settings if needed
            },
            LayoutProperties = new LayoutProperties()
            {
                DrawerWidthLeft = "260px",
                DrawerWidthRight = "300px"
            }
        };

        private static readonly MudTheme DarkTheme = new MudTheme()
        {
            Palette = new PaletteDark()
            {
                Primary = GreenColor,
                Surface = Colors.Grey.Darken4, // Dark background elements
                Background = Colors.Grey.Darken3, // Main background
                BackgroundGrey = Colors.Grey.Darken2,
                AppbarBackground = Colors.Grey.Darken4, // Dark app bar
                DrawerBackground = Colors.Grey.Darken4,
                TextPrimary = Colors.Shades.White,
                TextSecondary = Colors.Grey.Lighten1,
                // Add other palette color overrides if needed
            },
            Typography = new Typography()
            {
                Default = new Default()
                {
                    FontFamily = new[] { "IRANYekanFN", "Helvetica", "Arial", "sans-serif" }
                }
                // Define other typography settings if needed
            },
            LayoutProperties = new LayoutProperties()
            {
                DrawerWidthLeft = "260px",
                DrawerWidthRight = "300px"
            }
        };

        public async Task ToggleTheme()
        {
            IsDarkMode = !IsDarkMode;
            CurrentTheme = IsDarkMode ? DarkTheme : LightTheme;
            if (OnThemeChanged != null)
            {
                await OnThemeChanged.Invoke();
            }
        }
        public MudTheme CurrentTheme { get; private set; } = LightTheme;
        #endregion

       // public MudTheme CurrentTheme => IsDarkMode ? _darkTheme : _lightTheme;
        public ThemeService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;

            _lightTheme = new MudTheme
            {
                Palette = new PaletteLight
                {
                    Primary = GreenColor, // Green
                    PrimaryDarken = "#1b5e20",
                    PrimaryLighten = "#4caf50",
                    Secondary = "#ffffff", // White
                    Background = "#ffffff",
                    AppbarBackground = "#2e7d32",
                    DrawerBackground = "#fafafa",
                    TextPrimary = "#333333",
                    DrawerText = "#424242",
                    Surface = "#ffffff",
                },
                Typography = new Typography
                {
                    Default = new Default
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    H1 = new H1
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    H2 = new H2
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    H3 = new H3
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    H4 = new H4
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    H5 = new H5
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    H6 = new H6
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Body1 = new Body1
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Body2 = new Body2
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Button = new Button
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Caption = new Caption
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Overline = new Overline
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Subtitle1 = new Subtitle1
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Subtitle2 = new Subtitle2
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    }
                }
            };

            _darkTheme = new MudTheme
            {
                Palette = new PaletteDark
                {
                    Primary = GreenColor, // Green
                    PrimaryDarken = "#2e7d32",
                    PrimaryLighten = "#81c784",
                    Secondary = "#f5f5f5", // Off-white
                    Background = "#121212",
                    AppbarBackground = "#1b5e20",
                    DrawerBackground = "#1f1f1f",
                    TextPrimary = "#ffffff",
                    DrawerText = "#e0e0e0",
                    Surface = "#1e1e1e",
                    DrawerIcon = "#ffffff"
                },
                Typography = new Typography
                {
                    Default = new Default
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    H1 = new H1
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    H2 = new H2
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    H3 = new H3
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    H4 = new H4
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    H5 = new H5
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    H6 = new H6
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Body1 = new Body1
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Body2 = new Body2
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Button = new Button
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Caption = new Caption
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Overline = new Overline
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Subtitle1 = new Subtitle1
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Subtitle2 = new Subtitle2
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    }
                }
            };

            // Load theme preference on service initialization
            LoadThemePreference();
        }

        private async void LoadThemePreference()
        {
            try
            {
                _isDarkMode = await _jsRuntime.InvokeAsync<bool>("themeStorage.getTheme");
                ThemeChanged?.Invoke();
            }
            catch
            {
                // Default to light theme if there's an error
                _isDarkMode = false;
            }
        }

        private async void SaveThemePreference()
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("themeStorage.setTheme", _isDarkMode);
            }
            catch
            {
                // Handle error if needed
            }
        }
    }
}
