using System.Text.Json.Serialization;
using QueueSim;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapPost("/api/analyze", (AnalyzeRequest req) => Pipeline.Run(req));
app.MapPost("/api/simtable", (SimTableRequest r) => SimTable.Run(r));
app.Run();

namespace QueueSim
{
    public class AnalyzeRequest
    {
        public string? Scenario { get; set; }
        public DistSpec? Arrival { get; set; }     // optional manual override (minutes)
        public DistSpec? Service { get; set; }     // optional manual override (minutes)
        public int? Servers { get; set; }
        public int SimCustomers { get; set; } = 100000;
        public int? Seed { get; set; }
    }

    public class AnalyzeResponse
    {
        public bool Ok { get; set; }
        public List<string> Errors { get; set; } = new();
        public ParseResult Parsed { get; set; } = new();
        public Analysis? Analysis { get; set; }
        public SimResult? Sim { get; set; }
    }

    public static class Pipeline
    {
        public static AnalyzeResponse Run(AnalyzeRequest req)
        {
            var resp = new AnalyzeResponse();
            var parsed = ScenarioParser.Parse(req.Scenario ?? "");
            resp.Parsed = parsed;
            try
            {
                if (req.Arrival != null) { req.Arrival.Normalize(); parsed.Arrival = req.Arrival; }
                if (req.Service != null) { req.Service.Normalize(); parsed.Service = req.Service; }
            }
            catch (Exception e) { resp.Errors.Add(e.Message); return resp; }
            if (req.Servers is int s) parsed.Servers = Math.Clamp(s, 1, 200);

            if (parsed.Arrival == null) resp.Errors.Add("Arrival process not understood — fill in the arrival fields below and re-run.");
            if (parsed.Service == null) resp.Errors.Add("Service process not understood — fill in the service fields below and re-run.");
            if (resp.Errors.Count > 0) { resp.Errors.AddRange(parsed.Missing); return resp; }

            resp.Analysis = Analyzer.Run(parsed.Arrival!, parsed.Service!, parsed.Servers);
            resp.Analysis.Warnings.AddRange(parsed.Warnings);
            resp.Sim = Simulator.Run(parsed.Arrival!, parsed.Service!, parsed.Servers, req.SimCustomers, req.Seed ?? 12345);
            resp.Ok = true;
            return resp;
        }
    }
}
