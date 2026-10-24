using System.Text.Json;
using Rama.Core.Accounts;
using Rama.Core.Http;

namespace Rama.Core.Jira;

// Carga el tablero (config, personas, issues), lo refresca y mueve tarjetas. Sin UI: la VM solo lo observa.
public sealed class BoardController(IJiraBoardApi api, JiraAccount account, TimeProvider? time = null)
{
    // Verificado 2026-10-23: solo es el respaldo; el campo real (p. ej. customfield_10024) viene de la config del board.
    public const string DefaultStoryPointsField = "customfield_10016";

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<JiraColumn>? _columns;
    private bool _scrum;
    private OptimisticMove? _move;

    // Fuerza el campo (si no es null); si no, el del board, y si no, el respaldo.
    public string? StoryPointsField { get; init; }
    private string? _detectedField;
    public string EffectiveStoryPointsField => StoryPointsField ?? _detectedField ?? DefaultStoryPointsField;
    public JiraAccount Account { get; } = account;
    public IReadOnlyList<JiraUser> Users { get; private set; } = [];
    public string? MyAccountId { get; private set; }
    public BoardModel? Board => _move?.Board;
    public DateTimeOffset? LastSync { get; private set; }
    public bool IsMovePending => _move?.State == MoveState.Pending;

    public event Action<BoardModel>? BoardChanged;

    // Primera vez: carga todo; después solo reconsulta los issues. Lanza ante fallos (el Poller aplica backoff).
    public async Task SyncAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureStaticAsync(ct).ConfigureAwait(false);
            var jql = JqlBuilder.Build(Account.ProjectKey, openSprintsOnly: _scrum);
            var issues = await api.SearchIssuesAsync(jql, EffectiveStoryPointsField, ct).ConfigureAwait(false);
            var model = new BoardModel(_columns!, issues);
            if (_move is null)
            {
                _move = new OptimisticMove(api, model);
                _move.BoardChanged += m => BoardChanged?.Invoke(m);
                BoardChanged?.Invoke(model);
            }
            else
            {
                _move.Refresh(model);
            }
            LastSync = _time.GetUtcNow();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureStaticAsync(CancellationToken ct)
    {
        if (_columns is not null) return;
        var boardId = Account.BoardId;
        if (boardId is null)
        {
            var boards = await api.GetBoardsAsync(Account.ProjectKey, ct).ConfigureAwait(false);
            var board = boards.FirstOrDefault() ?? throw new InvalidOperationException(
                $"No hay un board de Jira para el proyecto {Account.ProjectKey}.");
            boardId = board.Id;
            _scrum = string.Equals(board.Type, "scrum", StringComparison.OrdinalIgnoreCase);
        }
        var config = await api.GetBoardConfigurationAsync(boardId.Value, ct).ConfigureAwait(false);
        var columns = config.Columns;
        _detectedField = config.EstimationField;
        var me = await api.GetMyselfAsync(ct).ConfigureAwait(false);
        Users = (await api.GetAssignableUsersAsync(Account.ProjectKey, ct).ConfigureAwait(false))
            .Where(u => u.Active).ToList();
        MyAccountId = me.AccountId;
        _columns = columns;
    }

    public async Task<IReadOnlyList<ColumnDrop>> EvaluateDropsAsync(string issueKey, CancellationToken ct = default)
    {
        var board = Board ?? throw new InvalidOperationException("El tablero aún no se ha cargado.");
        var issue = board.Find(issueKey) ?? throw new InvalidOperationException($"No se encontró {issueKey}.");
        var transitions = await api.GetTransitionsAsync(issueKey, ct).ConfigureAwait(false);
        return DropEvaluator.Evaluate(issue, board.Columns, transitions);
    }

    public Task<MoveResult> MoveAsync(string issueKey, JiraColumn target,
        IReadOnlyDictionary<string, JsonElement>? fields = null, CancellationToken ct = default) =>
        _move is null
            ? Task.FromResult(new MoveResult(MoveOutcome.RolledBack, "El tablero aún no se ha cargado."))
            : _move.MoveAsync(issueKey, target, fields, ct);

    public string IssueUrl(string key) => $"{Account.BaseUrl}/browse/{Uri.EscapeDataString(key)}";

    // Mensaje corto en español para mostrar dentro del bloque.
    public static string DescribeError(Exception ex) => ex switch
    {
        ApiAuthException => "Jira rechazó tus credenciales. Revisa tu API token.",
        ApiException { StatusCode: 0 } => "No se pudo conectar con Jira. Revisa tu conexión.",
        ApiException e => $"Jira respondió con error {(int)e.StatusCode}.",
        InvalidOperationException e => e.Message,
        _ => "No se pudo cargar el tablero de Jira.",
    };
}
