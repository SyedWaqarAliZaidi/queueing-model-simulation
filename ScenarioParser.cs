using System.Globalization;
using System.Text.RegularExpressions;
namespace QueueSim;

public class ParseResult
{
    public DistSpec? Arrival { get; set; }
    public DistSpec? Service { get; set; }
    public int Servers { get; set; } = 1;
    public List<string> Notes { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Missing { get; set; } = new();
}

/// <summary>Rule-based reader that turns a plain-English scenario into queue parameters.</summary>
public static class ScenarioParser
{
    const string U = @"seconds?|secs?|minutes?|mins?|hours?|hrs?|days?";
    const string Nm = @"\d+(?:\.\d+)?";
    static readonly RegexOptions I = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    static readonly Regex ArrKw = new(@"arriv|inter-?arrival|\bcomes?\b|\bcoming\b|\benters?\b|\bcalls?\b|\brequests?\b|\bvisits?\b|\bshows? up\b|\breach(?:es)?\b", I);
    static readonly Regex SvcKw = new(@"serv(?:e|ice|ed|es|ing)|\btakes?\b|\btaking\b|\bprocessing\b|\bprocessed\b|\bhandl|\brepair|\btreat|check-?out|\binspect", I);

    static double UnitMin(string u)
    {
        u = u.ToLowerInvariant();
        if (u.StartsWith("s")) return 1.0 / 60;
        if (u.StartsWith("h")) return 60;
        if (u.StartsWith("d")) return 1440;
        return 1;
    }
    static double D(Match m, string g = "n") => double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
    static string F(double x) => x.ToString("0.###", CultureInfo.InvariantCulture);

    static readonly Dictionary<string, int> Words = new()
    { ["one"]=1,["single"]=1,["two"]=2,["three"]=3,["four"]=4,["five"]=5,["six"]=6,["seven"]=7,["eight"]=8,["nine"]=9,["ten"]=10 };

    public static ParseResult Parse(string text)
    {
        var r = new ParseResult();
        text = (text ?? "").Replace('\u2019', '\'');
        if (string.IsNullOrWhiteSpace(text)) { r.Missing.Add("Scenario text is empty."); return r; }

        // ---- number of servers ----
        var sv = new Regex(@"\b(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten|single)[\s-]+(?:parallel[\s-]+|identical[\s-]+)?(?:server|clerk|teller|cashier|counter|machine|repairm[ae]n|doctor|operator|agent|window|checkout|lane|barber|mechanic|nurse|technician|bay|pump|station|dock|runway)s?\b", I);
        var sm = sv.Match(text);
        if (sm.Success)
        {
            var g = sm.Groups["n"].Value.ToLowerInvariant();
            r.Servers = Words.TryGetValue(g, out var w) ? w : int.Parse(g);
        }
        else
        {
            var s2 = Regex.Match(text, @"\b(?:servers?|channels?)\s*(?:=|:|is|are)\s*(\d+)|\bc\s*=\s*(\d+)", I);
            if (s2.Success) r.Servers = int.Parse(s2.Groups[1].Success ? s2.Groups[1].Value : s2.Groups[2].Value);
        }
        text = sv.Replace(text, " ");
        r.Notes.Add($"Number of servers: {r.Servers}");

        // ---- model-breaking features (the 3 models assume none of these) ----
        if (Regex.IsMatch(text, @"capacity\s*(?:of|is|=)?\s*\d+|finite (?:queue|buffer|capacity|waiting)|waiting (?:room|area) (?:for|of|holds?)|balk|reneg|\bleaves? if|turned away|blocked", I))
            r.Warnings.Add("Finite capacity / balking / reneging detected. M/M/1, M/G/1 and G/G/1 assume an infinite waiting room and patient customers; results are only approximate here (consider M/M/1/K).");
        if (Regex.IsMatch(text, @"finite (?:population|source)|calling population", I))
            r.Warnings.Add("Finite calling population detected. The classic models assume an infinite population (consider the machine-repair model).");
        if (Regex.IsMatch(text, @"priorit|\blifo\b|last[- ]in|random order|shortest job", I))
            r.Warnings.Add("Non-FIFO discipline / priorities detected. Formulas for mean W and L assume FIFO (mean values are unchanged for many disciplines, but waiting-time distributions are not).");
        if (Regex.IsMatch(text, @"\bbatch|breakdown|vacation|set-?up time", I))
            r.Warnings.Add("Batch arrivals, breakdowns, vacations or set-up times detected. These are outside the three standard models.");

        // ---- split into arrival / service parts ----
        string arr = "", svc = "";
        bool? last = null;
        foreach (var s in Regex.Split(text, @"(?<=[.!?;])\s+|\n+").Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var hits = new List<(int idx, bool arr)>();
            foreach (Match m in ArrKw.Matches(s)) hits.Add((m.Index, true));
            foreach (Match m in SvcKw.Matches(s)) hits.Add((m.Index, false));
            hits = hits.OrderBy(h => h.idx).ToList();
            if (hits.Count == 0)
            {
                if (last == true) arr += " " + s; else if (last == false) svc += " " + s;
                continue;
            }
            bool cur = hits[0].arr; int start = 0;
            foreach (var h in hits)
            {
                if (h.arr != cur)
                {
                    var piece = s[start..h.idx];
                    if (cur) arr += " " + piece; else svc += " " + piece;
                    start = h.idx; cur = h.arr;
                }
            }
            var tail = s[start..];
            if (cur) arr += " " + tail; else svc += " " + tail;
            last = cur;
        }

        r.Arrival = ParseDist(arr, "arrival", r);
        r.Service = ParseDist(svc, "service", r);
        if (r.Arrival != null) r.Notes.Add("Arrival process → inter-arrival time: " + r.Arrival.Describe());
        if (r.Service != null) r.Notes.Add("Service process → service time: " + r.Service.Describe());
        return r;
    }

    static DistSpec? ParseDist(string raw, string who, ParseResult r)
    {
        if (string.IsNullOrWhiteSpace(raw)) { r.Missing.Add($"Could not find any {who} information."); return null; }
        var t = raw.ToLowerInvariant();
        double unitMin = 1;
        var um = Regex.Match(t, $@"\b(?<u>{U})\b");
        if (um.Success) unitMin = UnitMin(um.Groups["u"].Value);

        double? sd = null, var = null, scv = null; int? k = null;
        var m = Regex.Match(t, $@"(?:standard deviation|std\.?(?:\s*dev\w*)?|\bsd\b|sigma)\D{{0,15}}?(?<n>{Nm})\s*(?:(?<u>{U})\b)?");
        if (m.Success) { sd = D(m) * (m.Groups["u"].Success ? UnitMin(m.Groups["u"].Value) : unitMin); t = t.Remove(m.Index, m.Length); }
        m = Regex.Match(t, $@"variance\D{{0,15}}?(?<n>{Nm})\s*(?:(?<u>{U})\b)?");
        if (m.Success) { double f = m.Groups["u"].Success ? UnitMin(m.Groups["u"].Value) : unitMin; var = D(m) * f * f; t = t.Remove(m.Index, m.Length); }
        m = Regex.Match(t, $@"(?<w>coefficient of variation|\bcv\b|\bscv\b)\D{{0,15}}?(?<n>{Nm})");
        if (m.Success) { double v = D(m); scv = m.Groups["w"].Value == "scv" ? v : v * v; t = t.Remove(m.Index, m.Length); }
        if (t.Contains("erlang"))
        {
            m = Regex.Match(t, @"\bk\s*=\s*(?<n>\d+)");
            if (!m.Success) m = Regex.Match(t, @"(?<n>\d+)\s*(?:phases?|stages?)");
            if (!m.Success) m = Regex.Match(t, @"erlang[\s\-]*(?<n>\d+)");
            if (m.Success) { k = int.Parse(m.Groups["n"].Value); t = t.Remove(m.Index, m.Length); }
        }
        bool Has(string p) => Regex.IsMatch(t, p);
        var spec = new DistSpec();

        var um2 = Regex.Match(t, $@"uniform\w*\D{{0,30}}?(?<a>{Nm})\D{{0,12}}?(?<b>{Nm})\s*(?<u>{U})?");
        if (um2.Success)
        {
            double f = um2.Groups["u"].Success ? UnitMin(um2.Groups["u"].Value) : unitMin;
            spec.Kind = DistKind.Uniform;
            spec.A = double.Parse(um2.Groups["a"].Value, CultureInfo.InvariantCulture) * f;
            spec.B = double.Parse(um2.Groups["b"].Value, CultureInfo.InvariantCulture) * f;
        }
        else
        {
            double? mean = ExtractMean(t, unitMin, who, r);
            if (mean == null) { r.Missing.Add($"Could not find the mean {who} time or rate."); return null; }
            spec.Mean = mean.Value;
            if (Has(@"normal|gaussian"))
            {
                spec.Kind = DistKind.Normal;
                if (sd == null) { sd = 0.25 * spec.Mean; r.Warnings.Add($"Normal {who} time: no standard deviation given, assumed 25% of the mean. Edit it below."); }
                spec.Sd = sd.Value;
            }
            else if (Has(@"gamma"))
            {
                spec.Kind = DistKind.Gamma;
                if (sd == null) { sd = 0.5 * spec.Mean; r.Warnings.Add($"Gamma {who} time: no standard deviation given, assumed 50% of the mean. Edit it below."); }
                spec.Sd = sd.Value;
            }
            else if (Has(@"erlang"))
            {
                spec.Kind = DistKind.Erlang;
                if (k == null) { k = 2; r.Warnings.Add($"Erlang {who} time: number of phases not found, assumed k = 2."); }
                spec.K = k.Value;
            }
            else if (Has(@"determin|constant|fixed|regular|exactly|same amount"))
                spec.Kind = DistKind.Deterministic;
            else if (Has(@"exponential|poisson|markov|memoryless|\bexp\b"))
                spec.Kind = DistKind.Exponential;
            else if (Has(@"general|arbitrary|non-?exponential|any distribution") || sd != null || var != null || scv != null)
            {
                spec.Kind = DistKind.General;
                if (scv != null) spec.Scv = scv.Value;
                else if (var != null) spec.Scv = var.Value / (spec.Mean * spec.Mean);
                else if (sd != null) spec.Scv = sd.Value * sd.Value / (spec.Mean * spec.Mean);
                else { spec.Scv = 1; r.Warnings.Add($"General {who} distribution: no variance/sd given, assumed SCV = 1. Edit it below."); }
            }
            else
            {
                spec.Kind = DistKind.Exponential;
                r.Warnings.Add($"The {who} distribution was not stated; assumed {(who == "arrival" ? "Poisson arrivals (exponential inter-arrival)" : "exponential service")}. Change it below if that is wrong.");
            }
        }
        try { spec.Normalize(); } catch (Exception e) { r.Missing.Add($"{who}: {e.Message}"); return null; }
        return spec;
    }

    /// <summary>Returns the MEAN TIME in minutes (converting rates such as '10 per hour').</summary>
    static double? ExtractMean(string t, double unitMin, string who, ParseResult r)
    {
        var rm = Regex.Match(t, $@"(?<n>{Nm})\s*(?:[a-z\-]+\s+){{0,2}}?(?:per|/|\ban?\b|each)\s*(?<u>{U})\b");
        if (rm.Success) return UnitMin(rm.Groups["u"].Value) / D(rm);
        var tm = Regex.Match(t, $@"(?<n>{Nm})\s*(?<u>{U})\b");
        if (tm.Success) return D(tm) * UnitMin(tm.Groups["u"].Value);
        var nm = Regex.Match(t, $@"(?:mean|average|avg|every|takes?|rate|=)\D{{0,12}}?(?<n>{Nm})");
        if (nm.Success)
        {
            r.Warnings.Add($"No time unit found for the {who} data; assumed minutes.");
            double v = D(nm);
            return Regex.IsMatch(t, @"\brate\b") && !Regex.IsMatch(t, @"mean|average|time") ? 1.0 / v : v;
        }
        return null;
    }
}
