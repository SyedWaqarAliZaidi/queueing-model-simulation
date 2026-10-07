using System.Globalization;
namespace QueueSim;

public class Analysis
{
    public string Kendall { get; set; } = "";     // detailed, e.g. M/D/1
    public string Family { get; set; } = "";      // M/M/1, M/G/1, G/G/1 (or c-server version)
    public string Method { get; set; } = "";
    public bool Exact { get; set; }
    public bool Stable { get; set; }
    public int Servers { get; set; }
    public double Lambda { get; set; }            // arrivals per minute
    public double Mu { get; set; }                // services per minute (per server)
    public double Rho { get; set; }
    public double Ca2 { get; set; }
    public double Cs2 { get; set; }
    public double? P0 { get; set; }
    public double? ProbWait { get; set; }
    public double? L { get; set; }
    public double? Lq { get; set; }
    public double? W { get; set; }
    public double? Wq { get; set; }
    public double? WqUpperBound { get; set; }     // Kingman upper bound (G/G/1)
    public List<double>? Pn { get; set; }         // P(n in system), M/M/1 only
    public List<string> Why { get; set; } = new();
    public List<string> Rules { get; set; } = new();
    public List<string> Steps { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public static class Analyzer
{
    static string F(double x) => x.ToString("0.####", CultureInfo.InvariantCulture);

    public static Analysis Run(DistSpec arr, DistSpec svc, int c)
    {
        var a = new Analysis { Servers = c };
        double lambda = 1 / arr.Mean, es = svc.Mean, mu = 1 / es;
        double rho = lambda / (c * mu);
        a.Lambda = lambda; a.Mu = mu; a.Rho = rho; a.Ca2 = arr.Scv; a.Cs2 = svc.Scv;
        a.Stable = rho < 1;

        bool arrM = arr.Kind == DistKind.Exponential, svcM = svc.Kind == DistKind.Exponential;
        string cs = c.ToString();
        a.Kendall = $"{arr.Letter}/{svc.Letter}/{cs}";
        a.Family = arrM && svcM ? $"M/M/{cs}" : arrM ? $"M/G/{cs}" : $"G/G/{cs}";
        bool single = c == 1;

        // ---------- why this model ----------
        a.Why.Add(arrM
            ? "Inter-arrival times are exponential (⇔ Poisson arrivals, memoryless) → arrival letter = M."
            : $"Inter-arrival times are {arr.Kind} (SCV = {F(arr.Scv)}), not exponential → arrival letter = G (general).");
        a.Why.Add(svcM
            ? "Service times are exponential (memoryless) → service letter = M."
            : $"Service times are {svc.Kind} (SCV = {F(svc.Scv)}), not exponential → service letter = G (general){(svc.Kind == DistKind.Deterministic ? " — the special case M/D/1 with Cs² = 0" : "")}.");
        a.Why.Add($"{c} server{(c > 1 ? "s" : "")} → Kendall notation {a.Kendall}  (family {a.Family}).");
        if (!single) a.Why.Add("More than one server: this is the multi-server extension of the 3 classic single-server models.");

        // ---------- rules / assumptions ----------
        a.Rules.Add("Customers are served First-In-First-Out (FIFO), one at a time per server.");
        a.Rules.Add("Infinite waiting room and infinite calling population; no balking, reneging or jockeying.");
        a.Rules.Add("Inter-arrival times are i.i.d. and independent of service times, which are also i.i.d.");
        a.Rules.Add("A server is never idle while a customer is waiting (work-conserving).");
        a.Rules.Add($"Steady state exists only if ρ = λ/(cμ) < 1.  Here ρ = {F(rho)} → {(a.Stable ? "stable ✔" : "UNSTABLE ✘")}.");
        if (a.Family.StartsWith("M/M"))
        {
            a.Rules.Add("M/M: both processes are memoryless, so the number in system is a birth–death Markov chain with exact closed-form results.");
        }
        else if (a.Family.StartsWith("M/G"))
        {
            a.Rules.Add("M/G/1: Poisson arrivals (PASTA holds); only the mean E[S] and variance Var[S] of service matter → Pollaczek–Khinchine formula (exact).");
        }
        else
        {
            a.Rules.Add("G/G/1 has no exact closed form for its mean wait. Kingman's formula (with the Krämer–Langenbach-Belz correction) is used as an approximation, accurate for high ρ; Kingman's upper bound is shown as a reference. Use the simulation as the ground truth.");
            a.Rules.Add("Exact for the only exact G/G/1 identity: P(server idle) = 1 − ρ.");
        }

        a.Steps.Add($"λ = 1 / mean inter-arrival = 1 / {F(arr.Mean)} = {F(lambda)} per min  ({F(lambda * 60)} per hour)");
        a.Steps.Add($"μ = 1 / E[S] = 1 / {F(es)} = {F(mu)} per min  ({F(mu * 60)} per hour)");
        a.Steps.Add($"ρ = λ / (c·μ) = {F(lambda)} / ({c} × {F(mu)}) = {F(rho)}");
        a.Steps.Add($"Ca² = {F(arr.Scv)},  Cs² = {F(svc.Scv)}  (squared coefficients of variation)");

        if (!a.Stable)
        {
            a.Warnings.Add("ρ ≥ 1: the queue grows without bound. No steady-state formulas exist; add servers or reduce load. The simulation below shows the waiting time drifting upward.");
            return a;
        }

        a.P0 = single ? 1 - rho : null;

        if (single && arrM && svcM)
        {
            a.Exact = true; a.Method = "Exact M/M/1 closed-form results";
            a.L = rho / (1 - rho);
            a.Lq = rho * rho / (1 - rho);
            a.W = 1 / (mu - lambda);
            a.Wq = rho / (mu - lambda);
            a.ProbWait = rho;
            a.Pn = Enumerable.Range(0, 8).Select(n => (1 - rho) * Math.Pow(rho, n)).ToList();
            a.Steps.Add($"L  = ρ/(1−ρ) = {F(a.L.Value)}");
            a.Steps.Add($"Lq = ρ²/(1−ρ) = {F(a.Lq.Value)}");
            a.Steps.Add($"W  = 1/(μ−λ) = {F(a.W.Value)} min");
            a.Steps.Add($"Wq = ρ/(μ−λ) = {F(a.Wq.Value)} min");
            a.Steps.Add($"P0 = 1−ρ = {F(1 - rho)};  P(n) = (1−ρ)ρⁿ;  P(wait > t) = ρ·e^(−(μ−λ)t)");
        }
        else if (single && arrM)
        {
            a.Exact = true; a.Method = "Pollaczek–Khinchine (exact for M/G/1)";
            double var = svc.Scv * es * es;
            a.Lq = (lambda * lambda * var + rho * rho) / (2 * (1 - rho));
            a.Wq = a.Lq.Value / lambda;
            a.W = a.Wq + es;
            a.L = lambda * a.W;
            a.ProbWait = rho;
            a.Steps.Add($"Var[S] = Cs²·E[S]² = {F(svc.Scv)} × {F(es * es)} = {F(var)}");
            a.Steps.Add($"Lq = (λ²σ² + ρ²) / (2(1−ρ)) = {F(a.Lq.Value)}");
            a.Steps.Add($"Wq = Lq/λ = {F(a.Wq.Value)} min");
            a.Steps.Add($"W = Wq + 1/μ = {F(a.W.Value)} min;  L = λW = {F(a.L.Value)}");
            a.Steps.Add($"P0 = 1−ρ = {F(a.P0!.Value)}");
        }
        else if (single)
        {
            a.Exact = false; a.Method = "Kingman's approximation with Krämer–Langenbach-Belz correction (G/G/1)";
            double kingman = rho / (1 - rho) * (arr.Scv + svc.Scv) / 2 * es;
            double ca2 = arr.Scv, cs2 = svc.Scv, g = 1;
            if (ca2 + cs2 > 0)
                g = ca2 < 1 ? Math.Exp(-2 * (1 - rho) * Math.Pow(1 - ca2, 2) / (3 * rho * (ca2 + cs2)))
                            : Math.Exp(-(1 - rho) * (ca2 - 1) / (ca2 + 4 * cs2));
            a.Wq = kingman * g;
            a.W = a.Wq + es;
            a.Lq = lambda * a.Wq.Value;
            a.L = lambda * a.W.Value;
            a.ProbWait = rho;   // approximation
            double varA = arr.Scv * arr.Mean * arr.Mean, varS = svc.Scv * es * es;
            a.WqUpperBound = lambda * (varA + varS) / (2 * (1 - rho));
            a.Steps.Add($"Kingman: Wq ≈ ρ/(1−ρ) · (Ca²+Cs²)/2 · E[S] = {F(rho / (1 - rho))} × {F((ca2 + cs2) / 2)} × {F(es)} = {F(kingman)} min");
            a.Steps.Add($"KLB correction factor g = {F(g)}  →  Wq ≈ {F(a.Wq.Value)} min  (g = 1 when Ca² = 1)");
            a.Steps.Add($"Kingman upper bound: Wq ≤ λ(σa²+σs²)/(2(1−ρ)) = {F(a.WqUpperBound.Value)} min");
            a.Steps.Add($"W = Wq + 1/μ = {F(a.W.Value)} min;  Lq = λWq = {F(a.Lq.Value)};  L = λW = {F(a.L.Value)}");
            a.Steps.Add($"P0 = 1−ρ = {F(1 - rho)} (exact);  P(wait>0) ≈ ρ");
        }
        else
        {
            // multi-server: Erlang C, scaled by Allen–Cunneen for non-Markovian inputs
            double aOff = lambda / mu, sum = 0, term = 1;
            for (int n = 0; n < c; n++) { sum += term; term *= aOff / (n + 1); }
            double last = term / (1 - rho);                 // a^c / (c!(1-ρ))
            double p0 = 1 / (sum + last);
            double pw = last * p0;                          // Erlang C
            double wqMMc = pw / (c * mu - lambda);
            bool markov = arrM && svcM;
            double factor = markov ? 1 : (arr.Scv + svc.Scv) / 2;
            a.Exact = markov;
            a.Method = markov ? $"Exact M/M/{c} (Erlang C)" : $"Allen–Cunneen approximation for {a.Family}";
            a.P0 = p0; a.ProbWait = pw;
            a.Wq = wqMMc * factor; a.W = a.Wq + es;
            a.Lq = lambda * a.Wq.Value; a.L = lambda * a.W.Value;
            a.Steps.Add($"Offered load a = λ/μ = {F(aOff)};  Erlang C = P(wait>0) = {F(pw)}");
            a.Steps.Add($"Wq(M/M/{c}) = C/(cμ−λ) = {F(wqMMc)} min" + (markov ? "" : $";  × (Ca²+Cs²)/2 = {F(factor)} → Wq ≈ {F(a.Wq.Value)} min"));
            a.Steps.Add($"W = {F(a.W.Value)} min;  Lq = {F(a.Lq.Value)};  L = {F(a.L.Value)}");
        }

        a.Steps.Add($"Little's law check: L = λW → {F(lambda)} × {F(a.W!.Value)} = {F(lambda * a.W.Value)} ✔ ;  Lq = λWq → {F(lambda * a.Wq!.Value)} ✔");
        return a;
    }
}
