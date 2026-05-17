using System.Globalization;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var specPath = Environment.GetEnvironmentVariable("KPI_SPEC_PATH") ?? "kpi_spec.json";
var spec = await KpiSpec.LoadAsync(specPath);
var engine = new KpiEngine(spec);

app.MapGet("/api/kpis", () =>
{
    var result = engine.Compute();
    return Results.Json(result);
});

app.MapGet("/", () =>
{
    var result = engine.Compute();
    var html = DashboardRenderer.Render(result);
    return Results.Content(html, "text/html", Encoding.UTF8);
});

app.Run("http://127.0.0.1:8080");

public sealed class KpiEngine
{
    private readonly KpiSpec _spec;

    public KpiEngine(KpiSpec spec) => _spec = spec;

    public ComputeResult Compute()
    {
        var computed = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var kpi in _spec.Kpis)
        {
            var value = ExpressionEvaluator.Eval(kpi.Formula, _spec.Data, computed);
            computed[kpi.Name] = value;
        }

        return new ComputeResult
        {
            Title = _spec.Dashboard.Title,
            Chart = _spec.Dashboard.Chart,
            Kpis = computed
        };
    }
}

public static class DashboardRenderer
{
    public static string Render(ComputeResult result)
    {
        var labelsJson = JsonSerializer.Serialize(result.Kpis.Keys);
        var valuesJson = JsonSerializer.Serialize(result.Kpis.Values);

        var cards = string.Join("\n", result.Kpis.Select(kvp =>
            $"<div class='card'><h3>{kvp.Key}</h3><p>{kvp.Value.ToString("N2", CultureInfo.InvariantCulture)}</p></div>"));

        return $$"""
<!doctype html>
<html>
<head>
  <meta charset="utf-8" />
  <title>{{result.Title}}</title>
  <script src="https://cdn.jsdelivr.net/npm/chart.js"></script>
  <style>
    body { font-family: sans-serif; margin: 24px; background: #f7f9fb; color: #1a202c; }
    .grid { display:grid; grid-template-columns: repeat(auto-fit, minmax(170px,1fr)); gap: 12px; margin-bottom:20px; }
    .card { background: white; border-radius: 12px; padding: 14px; box-shadow:0 1px 3px rgba(0,0,0,.1); }
    .card h3 { margin:0; font-size:15px; color:#4a5568; }
    .card p { margin:8px 0 0; font-size:26px; font-weight:700; }
    .panel { background:white; border-radius:12px; padding:20px; box-shadow:0 1px 3px rgba(0,0,0,.1); }
  </style>
</head>
<body>
  <h1>{{result.Title}}</h1>
  <div class="grid">{{cards}}</div>
  <div class="panel"><canvas id="kpiChart" height="100"></canvas></div>
  <script>
    const labels = {{labelsJson}};
    const values = {{valuesJson}};
    new Chart(document.getElementById('kpiChart'), {
      type: {{JsonSerializer.Serialize(result.Chart)}},
      data: { labels, datasets: [{ label: 'KPI', data: values }] },
      options: { responsive: true, plugins: { legend: { display: false } } }
    });
  </script>
</body>
</html>
""";
    }
}

public static class ExpressionEvaluator
{
    public static double Eval(string expression, Dictionary<string, List<double>> data, Dictionary<string, double> computed)
    {
        var expr = expression.Trim();

        if (expr.StartsWith("sum(") && expr.EndsWith(")"))
        {
            var name = expr[4..^1].Trim();
            return data.TryGetValue(name, out var arr)
                ? arr.Sum()
                : throw new InvalidOperationException($"Data array '{name}' not found.");
        }

        if (expr.StartsWith("avg(") && expr.EndsWith(")"))
        {
            var name = expr[4..^1].Trim();
            if (!data.TryGetValue(name, out var arr) || arr.Count == 0)
                throw new InvalidOperationException($"Data array '{name}' not found or empty.");
            return arr.Average();
        }

        if (double.TryParse(expr, NumberStyles.Float, CultureInfo.InvariantCulture, out var constant))
            return constant;

        var tokens = expr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 1)
        {
            if (computed.TryGetValue(tokens[0], out var v)) return v;
            throw new InvalidOperationException($"Unknown token '{tokens[0]}'.");
        }

        if (tokens.Length == 3)
        {
            var left = Resolve(tokens[0], data, computed);
            var right = Resolve(tokens[2], data, computed);
            return tokens[1] switch
            {
                "+" => left + right,
                "-" => left - right,
                "*" => left * right,
                "/" => right == 0 ? throw new DivideByZeroException("Division by zero in formula.") : left / right,
                _ => throw new InvalidOperationException($"Operator '{tokens[1]}' not supported.")
            };
        }

        throw new InvalidOperationException($"Formula '{expression}' is invalid. Use patterns like sum(revenue), profit / count, or avg(cost).");
    }

    private static double Resolve(string token, Dictionary<string, List<double>> data, Dictionary<string, double> computed)
    {
        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return n;
        if (computed.TryGetValue(token, out var c)) return c;

        if (token.StartsWith("sum(") && token.EndsWith(")"))
        {
            var name = token[4..^1].Trim();
            return data.TryGetValue(name, out var arr)
                ? arr.Sum()
                : throw new InvalidOperationException($"Data array '{name}' not found.");
        }

        if (token.StartsWith("avg(") && token.EndsWith(")"))
        {
            var name = token[4..^1].Trim();
            if (!data.TryGetValue(name, out var arr) || arr.Count == 0)
                throw new InvalidOperationException($"Data array '{name}' not found or empty.");
            return arr.Average();
        }

        throw new InvalidOperationException($"Cannot resolve token '{token}'.");
    }
}

public sealed class KpiSpec
{
    public Dictionary<string, List<double>> Data { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<KpiDefinition> Kpis { get; init; } = [];
    public DashboardConfig Dashboard { get; init; } = new();

    public static async Task<KpiSpec> LoadAsync(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"KPI spec not found: {path}");

        var json = await File.ReadAllTextAsync(path);
        var spec = JsonSerializer.Deserialize<KpiSpec>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return spec ?? throw new InvalidOperationException("Cannot deserialize KPI spec JSON.");
    }
}

public sealed class KpiDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Formula { get; init; } = string.Empty;
}

public sealed class DashboardConfig
{
    public string Title { get; init; } = "KPI Dashboard";
    public string Chart { get; init; } = "bar";
}

public sealed class ComputeResult
{
    public string Title { get; init; } = string.Empty;
    public string Chart { get; init; } = "bar";
    public Dictionary<string, double> Kpis { get; init; } = new();
}
