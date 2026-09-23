using System;
using System.Collections.Generic;
using System.Linq;
using PrincessStudio.Core;

namespace RaiseArc.Analysis
{
    [Serializable] public sealed class BalanceLedgerRow
    {
        public string key, kind, sourceId, ownerId, nameKey, target, outcome, policyId;
        public string nameEn, nameKo;
        public int count, firstDay, lastDay, firstRun, firstRecord, firstBefore, firstAfter;
        public long requested, applied, moneyIn, moneyOut, actualTotal;
        public string attribution = "Committed runtime observation; no recomputed rewards";
    }

    public sealed partial class BalanceStatistics
    {
        public static Distribution Distribution(IEnumerable<int> values)
        {
            var a = values.OrderBy(x => x).ToArray();
            if (a.Length == 0) return new Distribution();
            return new Distribution { n = a.Length, minimum = a[0], maximum = a[a.Length - 1], mean = a.Average(),
                median = a.Length % 2 == 1 ? a[a.Length / 2] : ((double)a[a.Length / 2 - 1] + a[a.Length / 2]) / 2,
                p10 = a[(int)Math.Ceiling(.1 * a.Length) - 1], p90 = a[(int)Math.Ceiling(.9 * a.Length) - 1] };
        }
        // Two-sided 95% Wilson score interval. Fixed independent Bernoulli sample, not optional-stopping inference.
        public static RateEstimate Wilson(int observed, int n)
        {
            if (n < 0 || observed < 0 || observed > n) throw new ArgumentException("Invalid binomial counts.");
            var r = new RateEstimate { observed = observed, n = n };
            if (n == 0) return r;
            const double z = 1.959963984540054;
            var p = (double)observed / n; var denominator = 1 + z * z / n;
            var center = (p + z * z / (2 * n)) / denominator;
            var radius = z * Math.Sqrt(p * (1 - p) / n + z * z / (4.0 * n * n)) / denominator;
            r.rate = p; r.lower = Math.Max(0, center - radius); r.upper = Math.Min(1, center + radius); return r;
        }
    }
    [Serializable] public sealed class Distribution
    {
        public int n, minimum, maximum, p10, p90;
        public double mean, median;
        public string method = "Exact empirical median; nearest-rank p10/p90; observed extrema, not theoretical bounds";
    }
    [Serializable] public sealed class RateEstimate
    {
        public int observed, n, unknown;
        public double rate, lower, upper = 1, confidence = .95;
        public string method = "Two-sided Wilson 95%, fixed sample; assumptions: independent runs under this authored policy";
        public string denominator = "All started runs";
        public bool fixedSampleComplete;
    }
}
