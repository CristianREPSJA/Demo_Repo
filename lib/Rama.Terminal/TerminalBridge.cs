using System.Reflection;
using Avalonia.VisualTree;
using Iciclecreek.Terminal;
using Porta.Pty;

namespace Rama.Terminal;

// ÚNICO punto con reflexión sobre Iciclecreek.Avalonia.Terminal 1.0.12, que no expone
// la conexión PTY ni el envío de entrada. Si una actualización cambia estos miembros,
// BindingError lo indica y la prueba de contrato falla.
public static class TerminalBridge
{
    public const string ConnectionField = "_ptyConnection";
    public const string SendMethod = "SendToPtyAsync";

    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly FieldInfo? s_connection = typeof(TerminalView).GetField(ConnectionField, Private);
    private static readonly MethodInfo? s_send = typeof(TerminalView).GetMethod(
        SendMethod, Private, [typeof(string), typeof(CancellationToken)]);

    // null si la versión instalada expone lo esperado.
    public static string? BindingError { get; } = ComputeError();

    public static TerminalView? FindView(TerminalControl control) =>
        control.GetVisualDescendants().OfType<TerminalView>().FirstOrDefault();

    public static IPtyConnection? GetConnection(TerminalView view) =>
        Required(s_connection, ConnectionField).GetValue(view) as IPtyConnection;

    public static void SetConnection(TerminalView view, IPtyConnection connection) =>
        Required(s_connection, ConnectionField).SetValue(view, connection);

    // Usa el envío interno de la vista (serializado con el teclado mediante su semáforo).
    public static Task SendAsync(TerminalView view, string data, CancellationToken ct = default)
    {
        var result = Required(s_send, SendMethod).Invoke(view, [data, ct]);
        return result as Task ?? Task.CompletedTask;
    }

    private static T Required<T>(T? member, string name) where T : class =>
        member ?? throw new InvalidOperationException(BindingError ?? $"Miembro {name} no disponible.");

    private static string? ComputeError()
    {
        var missing = new List<string>();
        if (s_connection is null || s_connection.FieldType != typeof(IPtyConnection))
            missing.Add($"{nameof(TerminalView)}.{ConnectionField} (IPtyConnection)");
        if (s_send is null || s_send.ReturnType != typeof(Task))
            missing.Add($"{nameof(TerminalView)}.{SendMethod}(string, CancellationToken): Task");
        if (missing.Count == 0)
            return null;

        var version = typeof(TerminalView).Assembly.GetName().Version;
        return $"Iciclecreek.Avalonia.Terminal {version} no expone {string.Join(" ni ", missing)}. " +
               "Revisa Rama.Terminal/TerminalBridge.cs tras actualizar la librería.";
    }
}
