using System;
using System.IO;
using System.Net.Http;

using Rama.Core.Accounts;
using Rama.Core.Azure.Entra;
using Rama.Core.Notifications;
using Rama.Core.Secrets;

namespace SourceGit.Rama
{
    // Servicios compartidos de Rama (cuentas, secretos, notificaciones) que consumen las paginas de UI.
    public static class RamaServices
    {
        public const string AccountsFileName = "rama-accounts.json";
        public const string EntraCacheDirName = "entra";

        public static AccountsService Accounts => _accounts.Value;
        public static InAppNotifier InApp { get; } = new();
        public static INotifier Notifier => _notifier.Value;
        public static SourceGit.ViewModels.PendingReviewsViewModel Reviews => _reviews.Value;

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        private static readonly Lazy<AccountsService> _accounts = new(() =>
        {
            var configDir = Native.OS.BasicDirectories.ConfigDir;
            var path = Path.Combine(configDir, AccountsFileName);
            // El caché de tokens de MSAL va cifrado (DPAPI/Keychain/libsecret) dentro del directorio de datos de Rama.
            return new AccountsService(new AccountsStore(path), SecretStoreFactory.Create(), Http,
                entraFactory: o => new MsalEntraAuthenticator(o))
            {
                EntraCacheDirectory = Path.Combine(configDir, EntraCacheDirName),
            };
        });

        private static readonly Lazy<SourceGit.ViewModels.PendingReviewsViewModel> _reviews = new(() => new SourceGit.ViewModels.PendingReviewsViewModel());

        private static readonly Lazy<INotifier> _notifier = new(() => NotifierFactory.Create(InApp));
    }
}
