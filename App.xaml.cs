using System.Configuration;
using System.Data;
using System.Windows;
using Raphael.Desktop.Views;
using Microsoft.Extensions.Configuration;
using MaterialDesignThemes.Wpf;
using MaterialDesignColors;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;


namespace Raphael.Desktop
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static IConfiguration Configuration { get; private set; }
        protected void Application_Startup(object sender, StartupEventArgs e)
        {
            // First thing, before anything can fail: an unhandled exception used to close
            // the application without a word, and a dispatcher could only report that it
            // "cerró sola". Now it is written to a log and, when the UI thread can carry
            // on, it does.
            CrashReporter.Install(this);

            var login = new LoginWindow();
            login.ResizeMode = ResizeMode.NoResize;
            login.WindowState = WindowState.Normal;
            login.Topmost = true;
            login.Show();

            // Load saved language in Settings
            var language = Raphael.Desktop.Properties.Settings.Default.Language ?? "en";
            //language = "en"; 
            LocalizationService.Instance.LoadLanguage(language);

            // Load configuration from appsettings.json
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            Configuration = builder.Build();

            //SetThemeBasedOnTime();


            //base.OnStartup(e);
        }

        private void SetThemeBasedOnTime()
        {
            var paletteHelper = new PaletteHelper();
            Theme theme = paletteHelper.GetTheme();

            var now = DateTime.Now.TimeOfDay;
            var isNight = now >= new TimeSpan(18, 0, 0) || now < new TimeSpan(6, 0, 0);

            theme.SetBaseTheme(isNight ? BaseTheme.Dark : BaseTheme.Light);
            theme.SetBaseTheme(BaseTheme.Dark);

            paletteHelper.SetTheme(theme);
                       
        }


        /*protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var login = new LoginWindow();
            login.Show();
        }*/

    }

}
