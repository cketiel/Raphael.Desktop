using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Input;
using Raphael.Desktop.Views;
using Raphael.Desktop.Services.Help;
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

            // F1, everywhere, including dialogs.
            //
            // A handler on MainWindow only fires while MainWindow has the keyboard, so F1 inside
            // NotificationAlertSettingsWindow — which is exactly where somebody gets stuck — would
            // do nothing. A class handler on Window catches it in every window the application
            // opens, present and future, and Preview is used so a control that swallows KeyDown
            // cannot eat the shortcut on the way.
            EventManager.RegisterClassHandler(
                typeof(Window),
                UIElement.PreviewKeyDownEvent,
                new KeyEventHandler(OnAnyWindowPreviewKeyDown));

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

        /// <summary>
        /// Opens the contextual help for whatever has focus.
        /// </summary>
        /// <remarks>
        /// Deliberately silent when the bundle is missing or the window fails: F1 is a convenience,
        /// and a dispatcher pressing it by accident during a busy shift must never get an error
        /// dialog in front of the trip they were working.
        /// </remarks>
        private static void OnAnyWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.F1 || e.Handled)
                return;

            e.Handled = true;

            try
            {
                // Fully qualified: inside Application, a bare "MainWindow" binds to
                // Application.MainWindow, the instance property, not to our window type.
                HelpService.Instance.OpenContextual(Raphael.Desktop.MainWindow.CurrentMenu);
            }
            catch (Exception exception)
            {
                FileLogger.Log($"F1 could not open the help: {exception}");
            }
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
