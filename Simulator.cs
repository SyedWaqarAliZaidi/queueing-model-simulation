namespace QueueSim;

public class SimResult
{
    public int Customers { get; set; }
    public int Warmup { get; set; }
    public double AvgWq { get; set; }
    public double AvgW { get; set; }
    public double AvgLq { get; set; }
    public double AvgL { get; set; }
    public double Utilization { get; set; }
    public double ProbWait { get; set; }
    public double MaxWq { get; set; }
    public double LambdaHat { get; set; }
    public double WqCiHalfWidth { get; set; }
    public List<double[]> Convergence { get; set; } = new();   // [customerIndex, running average Wq]
}

/// <summary>Discrete-event simulation of a FIFO queue with c identical servers.</summary>
public static class Simulator
{
    public static SimResult Run(DistSpec arr, DistSpec svc, int c, int n, int seed)
    {
        n = Math.Clamp(n, 1000, 2_000_000);
        var rng = new Random(seed);
        var free = new PriorityQueue<double, double>();      // next-free times of the servers
        for (int i = 0; i < c; i++) free.Enqueue(0, 0);

        int warm = n / 10;
        var waits = new double[n];
        double t = 0, tWarm = 0, sumWq = 0, sumS = 0, maxWq = 0, cum = 0;
        int cnt = 0, waited = 0, step = Math.Max(1, n / 100);
        var res = new SimResult { Customers = n, Warmup = warm };

        for (int i = 0; i < n; i++)
        {
            t += arr.Sample(rng);                              // arrival time of customer i
            double f = free.Dequeue();                         // earliest-free server
            double start = Math.Max(t, f);
            double wq = start - t, s = svc.Sample(rng), dep = start + s;
            free.Enqueue(dep, dep);
            waits[i] = wq; cum += wq;
            if (i == warm) tWarm = t;
            if (i >= warm) { sumWq += wq; sumS += s; cnt++; if (wq > 1e-9) waited++; if (wq > maxWq) maxWq = wq; }
            if ((i + 1) % step == 0) res.Convergence.Add(new[] { i + 1.0, cum / (i + 1) });
        }

        double span = t - tWarm;
        double lamHat = span > 0 ? (cnt - 1) / span : 0;
        res.AvgWq = sumWq / cnt;
        res.AvgW = res.AvgWq + sumS / cnt;
        res.LambdaHat = lamHat;
        res.AvgLq = lamHat * res.AvgWq;          // Little's law
        res.AvgL = lamHat * res.AvgW;
        res.Utilization = lamHat * (sumS / cnt) / c;
        res.ProbWait = (double)waited / cnt;
        res.MaxWq = maxWq;

        // 95% CI for mean Wq by batch means (30 batches)
        const int B = 30; int size = cnt / B;
        if (size >= 1)
        {
            var means = new double[B];
            for (int b = 0; b < B; b++)
            {
                double s = 0; for (int j = 0; j < size; j++) s += waits[warm + b * size + j];
                means[b] = s / size;
            }
            double mean = means.Average();
            double sd = Math.Sqrt(means.Sum(x => (x - mean) * (x - mean)) / (B - 1));
            res.WqCiHalfWidth = 2.045 * sd / Math.Sqrt(B);
        }
        return res;
    }
}
