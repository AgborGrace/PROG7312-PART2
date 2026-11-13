using System;
using System.Windows;
using SmartX.Client.Views;

namespace SmartX.Client;

/// <summary>
/// Flow: LandingView -> AuthView (Firebase sign-in / sign-up) -> ShellView (the three-module menu).
/// The shell is created ONCE per sign-in, so its module pages keep their state while the operator switches tabs.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ShowLanding();
    }

    private void ShowLanding()
    {
        var landing = new LandingView();
        landing.SignInRequested += (_, _) => ShowAuth(false);
        landing.RegisterRequested += (_, _) => ShowAuth(true);
        RootHost.Content = landing;
    }

    private void ShowAuth(bool startOnRegister)
    {
        var auth = new AuthView(startOnRegister);
        auth.BackRequested += (_, _) => ShowLanding();
        auth.Authenticated += (_, _) => ShowShell();
        RootHost.Content = auth;
    }

    private void ShowShell()
    {
        var shell = new ShellView();
        shell.SignedOut += (_, _) => ShowLanding();
        RootHost.Content = shell;
    }
}
