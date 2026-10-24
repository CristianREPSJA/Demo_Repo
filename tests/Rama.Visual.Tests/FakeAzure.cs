using System.Net;
using System.Text;

using Rama.Core.Azure;
using Rama.Core.Http;
using Rama.Core.Reviews;

namespace Rama.VisualTests;

// Azure DevOps simulado: un HttpMessageHandler en memoria (sin red) con datos ficticios.
public static class FakeAzure
{
    public const string Me = "me-id";
    public const int RunId = 4242;

    private sealed class RouteHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var (code, json) = Route(r.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    public static ReviewsService CreateService()
    {
        var client = new AzureDevOpsClient(new HttpClient(new RouteHandler()), new AzureConnection("contoso", "Ventas"),
            "pat-ficticio", new ApiClientOptions { MaxRetries = 0 });
        return new ReviewsService(client, new PendingWorkService(client));
    }

    private static string Pr(int id, string title, string creator, string creatorName, string src, string dst, int myVote,
        string desc = "") =>
        $$$"""
        {"pullRequestId":{{{id}}},"title":"{{{title}}}","status":"active","isDraft":false,"mergeStatus":"succeeded",
         "createdBy":{"id":"{{{creator}}}","displayName":"{{{creatorName}}}"},"creationDate":"2026-10-22T14:00:00Z",
         "sourceRefName":"refs/heads/{{{src}}}","targetRefName":"refs/heads/{{{dst}}}","description":"{{{desc}}}",
         "repository":{"id":"r1","name":"erp-ventas","project":{"id":"p1"}},"lastMergeSourceCommit":{"commitId":"c{{{id}}}"},
         "reviewers":[{"id":"me-id","displayName":"Diego Muñoz","vote":{{{myVote}}},"isRequired":true},
                      {"id":"o1","displayName":"Camila Rojas","vote":10},
                      {"id":"o2","displayName":"Equipo Calidad","vote":0,"isContainer":true}]}
        """;

    private static (HttpStatusCode, string) Route(string u)
    {
        const string desc = "Agrega validación de stock al cotizador y corrige el redondeo de IVA en notas de crédito.";
        if (u.Contains("connectionData"))
            return (HttpStatusCode.OK, """{"authenticatedUser":{"id":"me-id","displayName":"Diego Muñoz"}}""");
        if (u.Contains("searchCriteria.reviewerId"))
            return (HttpStatusCode.OK, $$"""
                {"value":[
                {{Pr(65, "Validación de stock en cotizador", "cami", "Camila Rojas", "feat/stock-cotizador", "develop", 0, desc)}},
                {{Pr(71, "Corrige redondeo de IVA", "pedro", "Pedro Alarcón", "fix/iva-redondeo", "develop", 0)}},
                {{Pr(73, "Migra reportes a QuestPDF", "vale", "Valentina Soto", "feat/reportes-pdf", "main", 0)}}]}
                """);
        if (u.Contains("searchCriteria.creatorId"))
            return (HttpStatusCode.OK, $$"""
                {"value":[
                {{Pr(64, "Refactor de módulo de clientes", "me-id", "Diego Muñoz", "refactor/clientes", "develop", 0)}},
                {{Pr(68, "Actualiza dependencias de seguridad", "me-id", "Diego Muñoz", "chore/deps", "develop", 0)}}]}
                """);
        if (u.Contains("pipelines/approvals"))
            return (HttpStatusCode.OK, $$"""
                {"value":[{"id":"a1","status":"pending","createdOn":"2026-10-23T09:30:00Z",
                "instructions":"Verifica con el área de ventas antes de aprobar el paso a producción.",
                "pipeline":{"name":"erp-ventas-release","owner":{"id":{{RunId}} } },
                "steps":[{"assignedApprover":{"id":"me-id","displayName":"Diego Muñoz"},"status":"pending"}]}]}
                """);
        if (u.Contains($"builds/{RunId}/timeline"))
            return (HttpStatusCode.OK, Timeline);
        if (u.Contains("policy/evaluations"))
            return (HttpStatusCode.OK, """
                {"value":[
                {"status":"approved","configuration":{"isBlocking":true,"type":{"displayName":"Revisores mínimos"}}},
                {"status":"running","configuration":{"isBlocking":true,"type":{"displayName":"Build"}}},
                {"status":"rejected","configuration":{"isBlocking":true,"type":{"displayName":"Comentarios resueltos"}}}]}
                """);
        if (u.Contains("/commits?"))
            return (HttpStatusCode.OK, """{"value":[{"commitId":"1"},{"commitId":"2"},{"commitId":"3"}]}""");
        if (u.Contains("/pullRequests/65?"))
            return (HttpStatusCode.OK, Pr(65, "Validación de stock en cotizador", "cami", "Camila Rojas", "feat/stock-cotizador", "develop", 0, desc));
        return (HttpStatusCode.OK, "{}");
    }

    private const string Timeline = """
        {"records":[
          {"id":"s1","type":"Stage","name":"Build","state":"completed","result":"succeeded","order":1},
          {"id":"s2","type":"Stage","name":"Pruebas","state":"completed","result":"succeeded","order":2},
          {"id":"s3","type":"Stage","name":"Despliegue QA","state":"completed","result":"succeeded","order":3},
          {"id":"s4","type":"Stage","name":"Despliegue Producción","state":"inProgress","order":4},
          {"id":"c4","parentId":"s4","type":"Checkpoint.Approval","name":"Approval","state":"inProgress","order":1},
          {"id":"s5","type":"Stage","name":"Verificación posterior","state":"pending","order":5}]}
        """;
}
