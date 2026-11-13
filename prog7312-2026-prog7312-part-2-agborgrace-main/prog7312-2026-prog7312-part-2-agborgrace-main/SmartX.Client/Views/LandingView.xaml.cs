using System;
using System.Windows;
using System.Windows.Controls;

namespace SmartX.Client.Views;

/// <summary>The first screen: a product-style landing page with an animated mesh network.</summary>
public partial class LandingView : UserControl
{
    public event EventHandler? SignInRequested;
    public event EventHandler? RegisterRequested;

    public LandingView()
    {
        InitializeComponent();
    }

    private void SignIn_Click(object sender, RoutedEventArgs e) => SignInRequested?.Invoke(this, EventArgs.Empty);

    private void Register_Click(object sender, RoutedEventArgs e) => RegisterRequested?.Invoke(this, EventArgs.Empty);
}
