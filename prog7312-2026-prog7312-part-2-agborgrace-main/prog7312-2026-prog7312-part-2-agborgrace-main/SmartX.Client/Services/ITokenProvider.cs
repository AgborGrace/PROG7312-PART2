using System.Threading.Tasks;

namespace SmartX.Client.Services;

/// <summary>Anything that can hand the API client a valid Firebase ID token (the real one is FirebaseAuthService).</summary>
public interface ITokenProvider
{
    Task<string> GetValidIdTokenAsync();
}
