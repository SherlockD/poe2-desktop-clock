using System.Net.Http.Headers;

namespace Poe2DeskTracker.PublicStash;

/// <summary>
/// Process-wide scheduler for Trade requests. It begins with the previous
/// conservative spacing and replaces it with the server-advertised windows as
/// soon as the API returns X-Rate-Limit headers.
/// </summary>
internal sealed class TradeRequestRateLimiter
{
    private static readonly IReadOnlyList<RateLimitRule> SearchFallback = [new(1, TimeSpan.FromMilliseconds(10_100))];
    private static readonly IReadOnlyList<RateLimitRule> FetchFallback = [new(1, TimeSpan.FromMilliseconds(400))];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, PolicyState> _policies = new(StringComparer.Ordinal);

    public async Task WaitAsync(string requestedPolicy, CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan delay;
            await _gate.WaitAsync(cancellationToken);
            try
            {
                var state = GetPolicy(requestedPolicy);
                var now = DateTimeOffset.UtcNow;
                if (state.BlockedUntil > now)
                {
                    delay = state.BlockedUntil - now;
                }
                else
                {
                    delay = GetRequiredDelay(state, now);
                    if (delay <= TimeSpan.Zero)
                    {
                        state.Requests.Add(now);
                        return;
                    }
                }
            }
            finally
            {
                _gate.Release();
            }

            await Task.Delay(delay, cancellationToken);
        }
    }

    public void Observe(string requestedPolicy, HttpResponseHeaders headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        _gate.Wait();
        try
        {
            var policyName = headers.TryGetValues("X-Rate-Limit-Policy", out var names)
                ? names.FirstOrDefault() ?? requestedPolicy
                : requestedPolicy;
            var requested = GetPolicy(requestedPolicy);
            var state = string.Equals(policyName, requestedPolicy, StringComparison.Ordinal)
                ? requested
                : MergePolicy(requestedPolicy, policyName, requested);

            var rules = ParseRules(headers);
            if (rules.Count > 0)
            {
                state.Rules = rules;
            }

            if (headers.RetryAfter?.Delta is { } retryAfter)
            {
                state.BlockedUntil = DateTimeOffset.UtcNow + retryAfter;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private PolicyState GetPolicy(string policyName)
    {
        if (_policies.TryGetValue(policyName, out var state))
        {
            return state;
        }

        var fallback = policyName.Contains("search", StringComparison.OrdinalIgnoreCase)
            ? SearchFallback
            : FetchFallback;
        return _policies[policyName] = new PolicyState(fallback);
    }

    private PolicyState MergePolicy(string requestedPolicy, string policyName, PolicyState requested)
    {
        if (_policies.TryGetValue(policyName, out var actual))
        {
            if (ReferenceEquals(actual, requested))
            {
                return actual;
            }

            actual.Requests.AddRange(requested.Requests);
            actual.BlockedUntil = Max(actual.BlockedUntil, requested.BlockedUntil);
            _policies[requestedPolicy] = actual;
            return actual;
        }

        _policies[policyName] = requested;
        _policies[requestedPolicy] = requested;
        return requested;
    }

    private static TimeSpan GetRequiredDelay(PolicyState state, DateTimeOffset now)
    {
        var longestWindow = state.Rules.Max(rule => rule.Window);
        state.Requests.RemoveAll(requestedAt => requestedAt <= now - longestWindow);
        var requiredDelay = TimeSpan.Zero;
        foreach (var rule in state.Rules)
        {
            var requestsInWindow = state.Requests
                .Where(requestedAt => requestedAt > now - rule.Window)
                .ToArray();
            if (requestsInWindow.Length < rule.MaximumHits)
            {
                continue;
            }

            var earliestAllowed = requestsInWindow[^rule.MaximumHits] + rule.Window;
            requiredDelay = Max(requiredDelay, earliestAllowed - now);
        }

        return requiredDelay;
    }

    private static IReadOnlyList<RateLimitRule> ParseRules(HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues("X-Rate-Limit-Rules", out var ruleNames))
        {
            return [];
        }

        var rules = new List<RateLimitRule>();
        foreach (var ruleName in string.Join(',', ruleNames).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!headers.TryGetValues($"X-Rate-Limit-{ruleName}", out var values))
            {
                continue;
            }

            foreach (var value in string.Join(',', values).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = value.Split(':', StringSplitOptions.TrimEntries);
                if (parts.Length != 3 ||
                    !int.TryParse(parts[0], out var maximumHits) ||
                    !int.TryParse(parts[1], out var windowSeconds) ||
                    maximumHits < 1 || windowSeconds < 1)
                {
                    continue;
                }

                rules.Add(new RateLimitRule(maximumHits, TimeSpan.FromSeconds(windowSeconds)));
            }
        }

        return rules;
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left >= right ? left : right;

    private sealed class PolicyState
    {
        public PolicyState(IReadOnlyList<RateLimitRule> rules) => Rules = rules;

        public IReadOnlyList<RateLimitRule> Rules { get; set; }

        public List<DateTimeOffset> Requests { get; } = [];

        public DateTimeOffset BlockedUntil { get; set; }
    }

    private sealed record RateLimitRule(int MaximumHits, TimeSpan Window);
}
