using System.Text.Json;

using Rama.Core.Jira;

namespace Rama.VisualTests;

// Tablero Jira simulado con la forma del tablero real (8 columnas, ~10 incidencias).
public sealed class FakeJiraApi : IJiraBoardApi
{
    private static readonly string[] ColumnNames =
        ["Por hacer", "Análisis", "En desarrollo", "Code review", "QA", "Listo para UAT", "UAT", "Hecho"];

    public IReadOnlyList<JiraTransition> Transitions { get; set; } = [];

    public Task<JiraUser> GetMyselfAsync(CancellationToken ct = default) =>
        Task.FromResult(new JiraUser("me", "Diego Muñoz", true));

    public Task<IReadOnlyList<JiraBoardInfo>> GetBoardsAsync(string p, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<JiraBoardInfo>>([new JiraBoardInfo(7, "DFE", "kanban")]);

    public Task<IReadOnlyList<JiraColumn>> GetBoardColumnsAsync(long id, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<JiraColumn>>(ColumnNames.Select((n, i) => new JiraColumn(n, [(i + 1).ToString()])).ToList());

    public async Task<JiraBoardConfig> GetBoardConfigurationAsync(long id, CancellationToken ct = default) =>
        new(await GetBoardColumnsAsync(id, ct), "customfield_10024");

    public Task<IReadOnlyList<JiraIssue>> SearchIssuesAsync(string jql, string sp = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<JiraIssue>>(
        [
            Issue("DFE-3434", "Cotizador: validar stock antes de confirmar", 1, "me", "Diego Muñoz", "Historia", "Alta", 5),
            Issue("DFE-3441", "Redondeo de IVA en notas de crédito", 2, "c", "Camila Rojas", "Error", "Crítica", 3),
            Issue("DFE-3445", "Migrar reportes de despacho a PDF", 3, "me", "Diego Muñoz", "Historia", "Media", 8),
            Issue("DFE-3448", "Pantalla de conciliación bancaria", 3, "p", "Pedro Alarcón", "Proyectos Especiales", "Alta", 13),
            Issue("DFE-3452", "Alinear permisos del rol Supervisor", 4, "c", "Camila Rojas", "Tarea", "Media", 2),
            Issue("DFE-3455", "Error al exportar libro de ventas con filtros largos", 4, "me", "Diego Muñoz", "Error", "Alta", 3),
            Issue("DFE-3460", "Optimizar consulta de stock por bodega", 5, "p", "Pedro Alarcón", "Tarea", "Media", 5),
            Issue("DFE-3463", "Texto de ayuda en formulario de despacho", 6, null, null, "Tarea", "Baja", null),
            Issue("DFE-3470", "Carga masiva de clientes desde Excel", 7, "c", "Camila Rojas", "Historia", "Alta", 8),
            Issue("DFE-3471", "Actualizar logo corporativo en facturas", 8, "me", "Diego Muñoz", "Tarea", "Baja", 1),
        ]);

    public Task<IReadOnlyList<JiraUser>> GetAssignableUsersAsync(string p, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<JiraUser>>(
            [new JiraUser("me", "Diego Muñoz", true), new JiraUser("c", "Camila Rojas", true), new JiraUser("p", "Pedro Alarcón", true)]);

    public Task<IReadOnlyList<JiraTransition>> GetTransitionsAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(Transitions);

    public Task TransitionAsync(string key, string id, IReadOnlyDictionary<string, JsonElement> f = null,
        CancellationToken ct = default) => Task.CompletedTask;

    private static JiraIssue Issue(string key, string summary, int column, string assigneeId, string assignee,
        string type, string priority, double? points) =>
        new(key, summary, column.ToString(), ColumnNames[column - 1], assigneeId, assignee, type, priority, points);
}
