using System;
using System.Collections.Generic;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using msfsclient.Core.App;
using msfsclient.Helpers;
using msfsclient.ViewModels;
using msfsclient.Views;

namespace msfsclient
{
    public sealed partial class MainWindow : Window
    {
        private readonly Dictionary<Screen, UIElement> _pages = [];

        public MainWindow()
        {
            Vm = App.Shell;
            this.InitializeComponent();

            Handle = WinRT.Interop.WindowNative.GetWindowHandle(this);

            // The ink header is the title bar; the window's own buttons stay on the right.
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBar);
            AppWindow.SetIcon("Assets/AppIcon.ico"); // all sizes, so the title bar and the taskbar both look sharp

            // A phone-shaped window by default: the layout is one column, and it fits beside the simulator.
            AppWindow.Resize(new Windows.Graphics.SizeInt32(520, 900));
            var area = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;
            AppWindow.Move(new Windows.Graphics.PointInt32(Math.Max(0, (area.Width - 520) / 2), Math.Max(0, (area.Height - 900) / 2)));
            if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = 360;
                presenter.PreferredMinimumHeight = 480;
            }

            Root.PointerPressed += Root_PointerPressed;
            Activated += (_, e) =>
            {
                // Announcements are fetched again whenever the window comes to the front.
                Vm.WindowActive = e.WindowActivationState != WindowActivationState.Deactivated;
                if (e.WindowActivationState != WindowActivationState.Deactivated && !Vm.State.IsOutside) _ = Vm.Controller.RefreshAnnouncementsAsync();
            };
            Vm.ScreenChanged += ShowScreen;
            Vm.BringToFront += () =>
            {
                if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } p) p.Restore();
                Activate();
            };
            Vm.PropertyChanged += (_, _) => OfferFinishedFlight();
            ShowScreen(Vm.State.Screen);
        }

        public ShellViewModel Vm { get; }

        public IntPtr Handle { get; }

        private void ShowScreen(Screen screen)
        {
            if (!_pages.TryGetValue(screen, out var page) || screen is Screen.Map or Screen.Tour or Screen.Profile or Screen.Onboarding or Screen.Login)
            {
                // Forms and the web page start fresh every time; the rest are kept so a revisit is instant.
                page = screen switch
                {
                    Screen.Loading => new LoadingView(),
                    Screen.UpdateRequired => new UpdateRequiredView(),
                    Screen.Login => new LoginView(),
                    Screen.VerifyEmail => new VerifyEmailView(),
                    Screen.Onboarding => new OnboardingView(),
                    Screen.Home => new HomeView(),
                    Screen.Flights => new FlightsView(),
                    Screen.OpenToday => new OpenTodayView(),
                    Screen.Map => new WebPageView(),
                    Screen.Timer => new TimerView(),
                    Screen.Profile => new ProfileView(),
                    Screen.Observations => new ObservationsView(),
                    Screen.Preflight => new PreflightView(),
                    Screen.History => new HistoryView(),
                    Screen.Tour => new TourView(),
                    Screen.Debug => new DebugView(),
                    _ => new LoadingView(),
                };
                _pages[screen] = page;
            }
            Page.Content = page;
            // Screen readers and keyboard users start at the top of the new page.
            DispatcherQueue.TryEnqueue(() =>
            {
                if (page is Control c) c.Focus(FocusState.Programmatic);
            });
        }

        private FinishedFlight? _offered;

        /// <summary>After tracking stops for a flight that was matched to a task, offer it once (on whatever screen the pilot is).</summary>
        private void OfferFinishedFlight()
        {
            var finished = Vm.State.FinishedFlight;
            if (finished == null || ReferenceEquals(finished, _offered) || Dialogs.IsOpen || Root.XamlRoot == null) return;
            _offered = finished;
            _ = FlightDialogs.ShowFinishedAsync(Root.XamlRoot, finished, Vm.Controller);
        }

        private void Back_Click(object sender, RoutedEventArgs e) => Vm.Back();

        private void Back_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            // Escape in a text box or a web page belongs to that control first.
            if (Vm.ShowBack || Vm.State.Screen == Screen.Tour)
            {
                Vm.Back();
                args.Handled = true;
            }
        }

        private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // The mouse's back button.
            if (e.GetCurrentPoint(Root).Properties.PointerUpdateKind == PointerUpdateKind.XButton1Pressed && Vm.ShowBack)
            {
                Vm.Back();
                e.Handled = true;
            }
        }

        private void Avatar_Click(object sender, RoutedEventArgs e) => Vm.Controller.OpenProfile();
    }
}
