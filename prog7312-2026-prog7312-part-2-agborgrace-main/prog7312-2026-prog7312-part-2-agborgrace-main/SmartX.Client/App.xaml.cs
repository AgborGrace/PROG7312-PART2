using System.Windows;
using SmartX.Client.Services;

namespace SmartX.Client;

/// <summary>
/// Application-level services shared by every screen. Keeping them here (instead of inside pages) is what lets the
/// simulator keep running and the user stay signed in while the operator switches between the module tabs.
/// </summary>
public partial class App : Application
{
    public static ClientSettings Settings { get; } = ClientSettings.Load();
    public static FirebaseAuthService Auth { get; } = new FirebaseAuthService(Settings);
    public static ApiClient Api { get; } = new ApiClient(Settings, Auth);
    public static SimulatorService Simulator { get; } = new SimulatorService(Api);
}
