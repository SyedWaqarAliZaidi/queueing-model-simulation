namespace QueueSim;

public class SimTableRequest
{
    public DistSpec? Arrival { get; set; }
    public DistSpec? Service { get; set; }
    public int N { get; set; } = 10;
    public int? Seed { get; set; }
    public List<double>? ManualInterarrival { get; set; }   // minutes
    public List<double>? ManualService { get; set; }        // minutes
}

public class SimRow
{
    public int Customer { get; set; }
    public double Interarrival { get; set; }
    public double Arrival { get; set; }
    public double Service { get; set; }
    public double Start { get; set; }
    public double End { get; set; }
    public double Waiting { get; set; }
    public double SystemTime { get; set; }
    public double Idle { get; set; }
}

public class SimTableResponse
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public List<SimRow> Rows { get; set; } = new();
    public int N { get; set; }
    public double AvgWaiting { get; set; }
    public double AvgService { get; set; }
    public double AvgSystem { get; set; }
    public double TotalIdle { get; set; }
    public double MaxWaiting { get; set; }
    public int Waited { get; set; }
    public double ProbWait { get; set; }
    public bool Consistent { get; set; }
}

/// <summary>Customer-by-customer single-server simulation table (classic textbook method).</summary>
public static class SimTable
{
    public static SimTableResponse Run(SimTableRequest q)
    {
        var res = new SimTableResponse();
        try
        {
            bool manual = (q.ManualInterarrival?.Count ?? 0) > 0 || (q.ManualService?.Count ?? 0) > 0;
            int n;
            if (manual)
            {
                if (q.ManualInterarrival == null || q.ManualService == null || q.ManualInterarrival.Count == 0 || q.ManualService.Count == 0)
                    throw new ArgumentException("Manual mode needs both an interarrival list and a service list.");
                if (q.ManualInterarrival.Count != q.ManualService.Count)
                    throw new ArgumentException("The interarrival and service lists must have the same length.");
                n = q.ManualInterarrival.Count;
            }
            else
            {
                n = q.N;
                if (q.Arrival == null || q.Service == null) throw new ArgumentException("Arrival and service distributions are required.");
                q.Arrival.Normalize(); q.Service.Normalize();
            }
            if (n < 1 || n > 2000) throw new ArgumentException("Number of customers must be between 1 and 2000.");

            var rng = new Random(q.Seed ?? 12345);
            double prevEnd = 0, prevArr = 0, sumW = 0, sumS = 0, sumSys = 0, idle = 0, maxW = 0; bool ok = true;
            for (int i = 0; i < n; i++)
            {
                double ia = i == 0 ? 0 : (manual ? q.ManualInterarrival![i] : q.Arrival!.Sample(rng));
                double s = manual ? q.ManualService![i] : q.Service!.Sample(rng);
                double arr = i == 0 ? 0 : prevArr + ia;
                double start = Math.Max(arr, prevEnd);
                double end = start + s, wait = start - arr, sys = end - arr, id = Math.Max(0, arr - prevEnd);
                if (Math.Abs(sys - (wait + s)) > 1e-9) ok = false;
                res.Rows.Add(new SimRow { Customer = i + 1, Interarrival = ia, Arrival = arr, Service = s, Start = start, End = end, Waiting = wait, SystemTime = sys, Idle = id });
                sumW += wait; sumS += s; sumSys += sys; idle += id; maxW = Math.Max(maxW, wait);
                if (wait > 1e-9) res.Waited++;
                prevEnd = end; prevArr = arr;
            }
            res.N = n; res.AvgWaiting = sumW / n; res.AvgService = sumS / n; res.AvgSystem = sumSys / n;
            res.TotalIdle = idle; res.MaxWaiting = maxW; res.ProbWait = (double)res.Waited / n; res.Consistent = ok; res.Ok = true;
        }
        catch (Exception e) { res.Error = e.Message; }
        return res;
    }
}
