using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Rama.Core.Azure.Entra;

namespace SourceGit.Rama
{
    // Credenciales de Entra para git: antes de un comando de red se renueva el token en silencio (PrepareAsync) y al
    // crear el proceso se inyecta como extraheader por entorno (Apply). Nunca falla el comando git por culpa del login.
    public static class GitAuth
    {
        public static async Task PrepareAsync(string gitArgs)
        {
            if (!GitAuthEnvironment.NeedsNetwork(gitArgs))
                return;

            try
            {
                var svc = RamaServices.Accounts;
                await svc.EnsureLoadedAsync().ConfigureAwait(false);
                if (svc.Current.Azure.Any(a => a.IsEntra))
                    await svc.TryGetAzureGitTokenAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Native.OS.LogException(ex);
            }
        }

        public static void Apply(IDictionary<string, string> env, string gitArgs)
        {
            if (env == null || !GitAuthEnvironment.NeedsNetwork(gitArgs))
                return;

            try
            {
                var token = RamaServices.Accounts.TryGetCachedAzureGitToken();
                if (!string.IsNullOrEmpty(token))
                    GitAuthEnvironment.Apply(env, token);
            }
            catch (Exception ex)
            {
                Native.OS.LogException(ex);
            }
        }
    }
}
