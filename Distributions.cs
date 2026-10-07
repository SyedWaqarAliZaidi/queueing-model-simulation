using System.Globalization;
namespace QueueSim;

public enum DistKind { Exponential, Deterministic, Uniform, Normal, Gamma, Erlang, General }

/// <summary>A time distribution (inter-arrival or service). All times are in MINUTES.</summary>
public class DistSpec
{
    public DistKind Kind { get; set; } = DistKind.Exponential;
    public double Mean { get; set; }
    public double Sd { get; set; }
    public double Scv { get; set; }          // squared coefficient of variation = Var / Mean^2
    public double A { get; set; }            // uniform lower bound
    public double B { get; set; }            // uniform upper bound
    public int K { get; set; } = 2;          // Erlang phases

    public void Normalize()
    {
        switch (Kind)
        {
            case DistKind.Exponential: Scv = 1; break;
            case DistKind.Deterministic: Scv = 0; break;
            case DistKind.Uniform:
                if (B < A) throw new ArgumentException("Uniform: upper bound must be >= lower bound.");
                Mean = (A + B) / 2;
                if (Mean <= 0) throw new ArgumentException("Uniform bounds must give a positive mean.");
                Scv = ((B - A) * (B - A) / 12.0) / (Mean * Mean); break;
            case DistKind.Normal:
                if (Mean <= 0) throw new ArgumentException("Mean must be positive.");
                Scv = Sd * Sd / (Mean * Mean); break;
            case DistKind.Gamma:
                if (Mean <= 0 || Sd <= 0) throw new ArgumentException("Gamma needs a positive mean and standard deviation.");
                Scv = Sd * Sd / (Mean * Mean); break;
            case DistKind.Erlang:
                if (K < 1) K = 1;
                Scv = 1.0 / K; break;
            case DistKind.General:
                if (Mean <= 0) throw new ArgumentException("Mean must be positive.");
                if (Scv <= 0 && Sd > 0) Scv = Sd * Sd / (Mean * Mean);
                if (Scv < 0) Scv = 0; break;
        }
        if (Mean <= 0) throw new ArgumentException("Mean must be positive.");
        Sd = Math.Sqrt(Scv) * Mean;
    }

    /// <summary>Kendall letter: M (exponential), D (deterministic), Ek (Erlang), G (anything else).</summary>
    public string Letter => Kind switch
    {
        DistKind.Exponential => "M",
        DistKind.Deterministic => "D",
        DistKind.Erlang => "E" + K,
        _ => "G"
    };

    public string Describe()
    {
        string F(double x) => x.ToString("0.###", CultureInfo.InvariantCulture);
        return Kind switch
        {
            DistKind.Exponential => $"Exponential, mean {F(Mean)} min (rate {F(60 / Mean)}/hr)",
            DistKind.Deterministic => $"Deterministic, constant {F(Mean)} min",
            DistKind.Uniform => $"Uniform({F(A)}, {F(B)}) min, mean {F(Mean)}, SCV {F(Scv)}",
            DistKind.Normal => $"Normal, mean {F(Mean)} min, sd {F(Sd)} (truncated at 0), SCV {F(Scv)}",
            DistKind.Gamma => $"Gamma, mean {F(Mean)} min, sd {F(Sd)}, SCV {F(Scv)}",
            DistKind.Erlang => $"Erlang-{K}, mean {F(Mean)} min, SCV {F(Scv)}",
            _ => $"General, mean {F(Mean)} min, SCV {F(Scv)} (sd {F(Sd)})"
        };
    }

    public double Sample(Random r)
    {
        switch (Kind)
        {
            case DistKind.Exponential: return -Mean * Math.Log(1 - r.NextDouble());
            case DistKind.Deterministic: return Mean;
            case DistKind.Uniform: return A + (B - A) * r.NextDouble();
            case DistKind.Normal:
                double x; do { x = Mean + Sd * Normal01(r); } while (x <= 0); return x;
            case DistKind.Erlang:
            {
                double p = 1; for (int i = 0; i < K; i++) p *= (1 - r.NextDouble());
                return -(Mean / K) * Math.Log(p);
            }
            case DistKind.Gamma:
            default:
                if (Scv < 1e-9) return Mean;
                return Gamma(r, 1.0 / Scv) * (Mean * Scv);   // gamma matched to mean & variance
        }
    }

    static double Normal01(Random r) =>
        Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());

    static double Gamma(Random r, double shape)   // Marsaglia–Tsang
    {
        if (shape < 1) return Gamma(r, shape + 1) * Math.Pow(r.NextDouble(), 1.0 / shape);
        double d = shape - 1.0 / 3, c = 1 / Math.Sqrt(9 * d);
        while (true)
        {
            double x, v;
            do { x = Normal01(r); v = 1 + c * x; } while (v <= 0);
            v = v * v * v; double u = r.NextDouble();
            if (u < 1 - 0.0331 * x * x * x * x) return d * v;
            if (Math.Log(u) < 0.5 * x * x + d * (1 - v + Math.Log(v))) return d * v;
        }
    }
}
