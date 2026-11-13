using System;
using System.Net.Mail;
using System.Windows;
using System.Windows.Controls;
using SmartX.Client.Converters;
using SmartX.Client.Services;

namespace SmartX.Client.Views;

/// <summary>Sign in / create account screen. Authentication is done by Firebase Authentication (REST API).</summary>
public partial class AuthView : UserControl
{
    private bool _registerMode;
    private bool _ready;

    public event EventHandler? Authenticated;
    public event EventHandler? BackRequested;

    public AuthView(bool startOnRegister)
    {
        InitializeComponent();

        _registerMode = startOnRegister;
        if (startOnRegister) RegisterChip.IsChecked = true;

        ConfigBanner.Visibility = App.Auth.IsConfigured ? Visibility.Collapsed : Visibility.Visible;
        _ready = true;
        ApplyMode();

        Loaded += (_, _) => EmailBox.Focus();
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;   // Checked also fires while the XAML is still loading
        _registerMode = ReferenceEquals(sender, RegisterChip);
        ApplyMode();
    }

    private void ApplyMode()
    {
        HeadingText.Text = _registerMode ? "Create your account" : "Welcome back";
        SubheadingText.Text = _registerMode ? "Register a new operator account." : "Sign in with your operator account.";
        SubmitButton.Content = _registerMode ? "Create account" : "Sign in";
        ConfirmPanel.Visibility = _registerMode ? Visibility.Visible : Visibility.Collapsed;
        ForgotButton.Visibility = _registerMode ? Visibility.Collapsed : Visibility.Visible;
        ShowMessage(string.Empty, false);
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var address = new MailAddress(email);
            return address.Address == email && email.Contains('.');
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        string email = EmailBox.Text.Trim();
        string password = PasswordInput.Password;

        // --- client-side validation before any network call ---
        if (!IsValidEmail(email)) { ShowMessage("Enter a valid email address.", true); return; }
        if (password.Length < 6) { ShowMessage("Password must be at least 6 characters.", true); return; }
        if (_registerMode && password != ConfirmInput.Password) { ShowMessage("The two passwords do not match.", true); return; }

        SetBusy(true);
        ShowMessage(_registerMode ? "Creating your account..." : "Signing in...", false);

        try
        {
            if (_registerMode) await App.Auth.SignUpAsync(email, password);
            else await App.Auth.SignInAsync(email, password);

            Authenticated?.Invoke(this, EventArgs.Empty);
        }
        catch (AuthException ex)
        {
            ShowMessage(ex.Message, true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Forgot_Click(object sender, RoutedEventArgs e)
    {
        string email = EmailBox.Text.Trim();
        if (!IsValidEmail(email)) { ShowMessage("Type your email address above first, then click \"Forgot password?\".", true); return; }

        SetBusy(true);
        try
        {
            await App.Auth.SendPasswordResetAsync(email);
            ShowMessage("A password-reset email has been sent. Check your inbox.", false);
        }
        catch (AuthException ex)
        {
            ShowMessage(ex.Message, true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void SetBusy(bool busy)
    {
        SubmitButton.IsEnabled = !busy;
        EmailBox.IsEnabled = !busy;
        PasswordInput.IsEnabled = !busy;
        ConfirmInput.IsEnabled = !busy;
    }

    private void ShowMessage(string text, bool isError)
    {
        MessageText.Text = text;
        MessageText.Foreground = isError ? Palette.Red : Palette.Grey;
    }
}
