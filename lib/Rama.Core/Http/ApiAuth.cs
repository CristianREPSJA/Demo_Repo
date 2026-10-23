using System.Net.Http.Headers;
using System.Text;

namespace Rama.Core.Http;

public static class ApiAuth
{
    // Basic con "usuario:token"; usuario vacío para PAT de Azure DevOps (":PAT").
    public static AuthenticationHeaderValue Basic(string? user, string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        var raw = Encoding.UTF8.GetBytes($"{user}:{token}");
        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
    }

    public static AuthenticationHeaderValue Pat(string token) => Basic(null, token);

    public static AuthenticationHeaderValue Bearer(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        return new AuthenticationHeaderValue("Bearer", token);
    }
}
