using System.Collections.Concurrent;
using API.Schema.MangaContext;

namespace API.MangaDownloadClients;

/// <summary>
/// Tracks per-chapter-source and per-connector download failures so blocked sites
/// (403 / Cloudflare / empty image lists) are cooled down instead of re-queued every minute.
/// </summary>
public static class DownloadFailureTracker
{
    public static readonly string[] DefaultPreferenceOrder =
    [
        "WeebCentral",
        "NeloManga",
        "MangaTown",
        "FanFox",
        "AsuraComic"
    ];

    private static string[] PreferenceOrder = DefaultPreferenceOrder;

    /// <summary>Consecutive failures on a connector before the whole connector is cooled down.</summary>
    public const int ConnectorFailureThreshold = 3;

    public static TimeSpan BaseCooldown { get; set; } = TimeSpan.FromMinutes(30);
    public static TimeSpan MaxCooldown { get; set; } = TimeSpan.FromHours(6);
    public static Func<DateTime> UtcNow { get; set; } = static () => DateTime.UtcNow;

    private static readonly ConcurrentDictionary<string, FailureState> ChapterFailures = new();
    private static readonly ConcurrentDictionary<string, FailureState> ConnectorFailures = new();

    public static void SetPreferenceOrder(IEnumerable<string>? names)
    {
        string[] next = (names ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n) && !n.Equals("Global", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        PreferenceOrder = next.Length > 0 ? next : DefaultPreferenceOrder;
    }

    public static IReadOnlyList<string> GetPreferenceOrder() => PreferenceOrder;

    public static int Rank(string connectorName)
    {
        int index = Array.FindIndex(PreferenceOrder, n => n.Equals(connectorName, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : PreferenceOrder.Length;
    }

    public static void RecordFailure(string chapterConnectorId, string connectorName, string? reason = null)
    {
        DateTime now = UtcNow();
        RecordOn(ChapterFailures, ChapterKey(chapterConnectorId, connectorName), now);

        FailureState connectorState = ConnectorFailures.AddOrUpdate(
            ConnectorKey(connectorName),
            _ => new FailureState { Count = 1, CoolUntil = DateTime.MinValue },
            (_, existing) =>
            {
                existing.Count++;
                return existing;
            });

        // Only 403/Cloudflare/IP-ban cool the whole site. Empty image lists are often
        // one bad chapter or a 200 challenge page — those must not freeze 9000 other jobs.
        if (IsConnectorWideFailure(reason))
            connectorState.CoolUntil = CooldownUntil(connectorState.Count, now);
    }

    public static void RecordSuccess(string chapterConnectorId, string connectorName)
    {
        ChapterFailures.TryRemove(ChapterKey(chapterConnectorId, connectorName), out _);
        ConnectorFailures.TryRemove(ConnectorKey(connectorName), out _);
    }

    public static bool IsCoolingDown(string chapterConnectorId, string connectorName) =>
        IsActive(ChapterFailures, ChapterKey(chapterConnectorId, connectorName));

    public static bool IsConnectorCoolingDown(string connectorName) =>
        IsActive(ConnectorFailures, ConnectorKey(connectorName));

    public static string DescribeSkipReasons(IEnumerable<MangaConnectorId<Chapter>> missing)
    {
        var groups = missing
            .GroupBy(ch => ch.MangaConnectorName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g =>
            {
                string flag = IsConnectorCoolingDown(g.Key) ? " cooling" : "";
                return $"{g.Key} {g.Count()}{flag}";
            });
        return string.Join(", ", groups);
    }

    /// <summary>
    /// One job per logical chapter: drop in-flight and cooled-down sources, pick the
    /// highest-preference remaining connector, then share slots across series.
    /// A new 1000-chapter title must not starve imported series that only need a few missing chapters.
    /// </summary>
    public static List<MangaConnectorId<Chapter>> SelectDownloadSources(
        IEnumerable<MangaConnectorId<Chapter>> missingChapters,
        IReadOnlyCollection<string> inFlightConnectorIds,
        IReadOnlyCollection<string> inFlightChapterKeys,
        int take,
        IReadOnlyDictionary<string, int>? inFlightBySeries = null)
    {
        if (take <= 0)
            return [];

        HashSet<string> inFlightIds = inFlightConnectorIds as HashSet<string> ?? [..inFlightConnectorIds];
        HashSet<string> inFlightKeys = inFlightChapterKeys as HashSet<string> ?? [..inFlightChapterKeys];

        List<MangaConnectorId<Chapter>> onePerChapter = missingChapters
            .Where(ch =>
                !inFlightIds.Contains(ch.Key) &&
                !inFlightKeys.Contains(ch.ObjId) &&
                !IsCoolingDown(ch.Key, ch.MangaConnectorName) &&
                !IsConnectorCoolingDown(ch.MangaConnectorName))
            .GroupBy(ch => ch.ObjId)
            .Select(group => group.OrderBy(ch => Rank(ch.MangaConnectorName)).ThenBy(ch => ch.Key, StringComparer.Ordinal).First())
            .ToList();

        Dictionary<string, Queue<MangaConnectorId<Chapter>>> queues = onePerChapter
            .GroupBy(SeriesKey)
            .ToDictionary(
                g => g.Key,
                g => new Queue<MangaConnectorId<Chapter>>(
                    g.OrderBy(ch => ch.Obj, new Chapter.ChapterComparer())
                        .ThenBy(ch => ch.ObjId, StringComparer.Ordinal)),
                StringComparer.Ordinal);

        Dictionary<string, string> names = queues.ToDictionary(
            kv => kv.Key,
            kv => SeriesName(kv.Value.Peek()),
            StringComparer.Ordinal);

        Dictionary<string, int> load = new(StringComparer.Ordinal);
        if (inFlightBySeries is not null)
        {
            foreach ((string series, int count) in inFlightBySeries)
                load[series] = count;
        }

        // Wave 1: one chapter from each series A–Z. Wave 2 only after every series has had a turn.
        List<MangaConnectorId<Chapter>> selected = [];
        int wave = 0;
        while (selected.Count < take && queues.Count > 0)
        {
            wave++;
            List<string> eligible = queues.Keys
                .Where(k => load.GetValueOrDefault(k) < wave)
                .OrderBy(k => RotationRank(names.GetValueOrDefault(k, k)), StringComparer.OrdinalIgnoreCase)
                .ThenBy(k => names.GetValueOrDefault(k, k), StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (eligible.Count == 0)
            {
                if (wave > 32)
                    break;
                continue;
            }

            foreach (string series in eligible)
            {
                if (selected.Count >= take)
                    break;
                if (!queues.TryGetValue(series, out Queue<MangaConnectorId<Chapter>>? queue) || queue.Count == 0)
                    continue;
                selected.Add(queue.Dequeue());
                load[series] = load.GetValueOrDefault(series) + 1;
                RotationCursor = names.GetValueOrDefault(series, series);
                if (queue.Count == 0)
                    queues.Remove(series);
            }
        }

        return selected;
    }

    private static string? RotationCursor;

    internal static string SeriesName(MangaConnectorId<Chapter> ch) =>
        string.IsNullOrWhiteSpace(ch.Obj.ParentManga?.Name) ? SeriesKey(ch) : ch.Obj.ParentManga.Name;

    /// <summary>Names after the last queued series sort first so we walk A–Z and wrap.</summary>
    private static string RotationRank(string name)
    {
        if (string.IsNullOrEmpty(RotationCursor))
            return name;
        return string.Compare(name, RotationCursor, StringComparison.OrdinalIgnoreCase) > 0
            ? "0:" + name
            : "1:" + name;
    }

    internal static string SeriesKey(MangaConnectorId<Chapter> ch)
    {
        if (!string.IsNullOrEmpty(ch.Obj.ParentMangaId))
            return ch.Obj.ParentMangaId;
        if (!string.IsNullOrEmpty(ch.Obj.ParentManga?.Key))
            return ch.Obj.ParentManga.Key;
        return ch.ObjId;
    }

    public static void Reset()
    {
        ChapterFailures.Clear();
        ConnectorFailures.Clear();
        RotationCursor = null;
        PreferenceOrder = DefaultPreferenceOrder;
        BaseCooldown = TimeSpan.FromMinutes(30);
        MaxCooldown = TimeSpan.FromHours(6);
        UtcNow = static () => DateTime.UtcNow;
    }

    internal static bool IsConnectorWideFailure(string? reason)
    {
        if (string.IsNullOrEmpty(reason))
            return false;

        string lower = reason.ToLowerInvariant();
        return lower.Contains("403") ||
               lower.Contains("forbidden") ||
               lower.Contains("cloudflare") ||
               lower.Contains("flaresolverr") ||
               lower.Contains("ip is banned") ||
               lower.Contains("ip banned") ||
               lower.Contains("probably your ip");
    }

    private static FailureState RecordOn(ConcurrentDictionary<string, FailureState> store, string key, DateTime now)
    {
        return store.AddOrUpdate(
            key,
            _ => new FailureState { Count = 1, CoolUntil = CooldownUntil(1, now) },
            (_, existing) =>
            {
                existing.Count++;
                existing.CoolUntil = CooldownUntil(existing.Count, now);
                return existing;
            });
    }

    private static bool IsActive(ConcurrentDictionary<string, FailureState> store, string key)
    {
        if (!store.TryGetValue(key, out FailureState? state))
            return false;
        if (state.CoolUntil > UtcNow())
            return true;
        return false;
    }

    private static DateTime CooldownUntil(int failureCount, DateTime now)
    {
        double minutes = Math.Min(
            BaseCooldown.TotalMinutes * Math.Pow(2, Math.Max(failureCount, 1) - 1),
            MaxCooldown.TotalMinutes);
        return now.Add(TimeSpan.FromMinutes(minutes));
    }

    private static string ChapterKey(string chapterConnectorId, string connectorName) =>
        $"{connectorName}:{chapterConnectorId}";

    private static string ConnectorKey(string connectorName) =>
        connectorName.ToLowerInvariant();

    private sealed class FailureState
    {
        public int Count;
        public DateTime CoolUntil;
    }
}
