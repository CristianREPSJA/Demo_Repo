using System;
using System.IO;
using System.Net.Http;

using Rama.Core.Accounts;
using Rama.Core.Notifications;
using Rama.Core.Secrets;

namespace SourceGit.Rama
{
    // Servicios compartidos de Rama (cuentas, secretos, notificaciones) que consumen las paginas de UI.
    public static class RamaServices
    {
        public const string AccountsFileName = "rama-accounts.json";

        public static AccountsService Accounts => _accounts.Value;
        public static InAppNotifier InApp { get; } = new();
        public static INotifier Notifier => _notifier.Value;

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        private static readonly Lazy<AccountsService> _accounts = new(() =>
        {
            var path = Path.Combine(Native.OS.BasicDirectories.ConfigDir, AccountsFileName);
            return new AccountsService(new AccountsStore(path), SecretStoreFactory.Create(), Http);
        });

        private static readonly Lazy<INotifier> _notifier = new(() => NotifierFactory.Create(InApp));
    }
}
