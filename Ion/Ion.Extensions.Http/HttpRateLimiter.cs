using System.Diagnostics;
using System.Net;

namespace Ion.Extensions.Http;

/// <summary>
/// Token-bucket rate limits per client address: each address may make <see cref="RequestsPerSecond"/> requests per
/// second on average and <see cref="Burst"/> at once. A connection takes its address's bucket once
/// (<see cref="GetBucket"/>); taking a token afterwards (<see cref="Bucket.TryTake"/>) does not allocate.
/// </summary>
public sealed class HttpRateLimiter
{
	private const int MaxAddresses = 4096;
	private readonly Dictionary<IPAddress, Bucket> _buckets = [];

	/// <summary>Creates a limiter; a rate of 0 or less disables it.</summary>
	public HttpRateLimiter(double requestsPerSecond, double burst)
	{
		RequestsPerSecond = requestsPerSecond;
		Burst = Math.Max(1, burst);
	}

	/// <summary>The sustained rate per address.</summary>
	public double RequestsPerSecond { get; }

	/// <summary>The bucket size per address.</summary>
	public double Burst { get; }

	/// <summary>Whether the limiter limits anything.</summary>
	public bool IsEnabled => RequestsPerSecond > 0;

	/// <summary>The bucket of <paramref name="address"/> (shared by every connection from it).</summary>
	public Bucket GetBucket(IPAddress address)
	{
		ArgumentNullException.ThrowIfNull(address);
		lock (_buckets)
		{
			if (_buckets.TryGetValue(address, out var bucket)) return bucket;
			if (_buckets.Count >= MaxAddresses) _buckets.Clear();
			bucket = new Bucket(this);
			_buckets[address] = bucket;
			return bucket;
		}
	}

	/// <summary>One address's tokens.</summary>
	public sealed class Bucket
	{
		private readonly HttpRateLimiter _limiter;
		private readonly Lock _lock = new();
		private double _tokens;
		private long _last;

		internal Bucket(HttpRateLimiter limiter)
		{
			_limiter = limiter;
			_tokens = limiter.Burst;
			_last = Stopwatch.GetTimestamp();
		}

		/// <summary>Takes one token; false when the address is over its rate.</summary>
		public bool TryTake()
		{
			if (!_limiter.IsEnabled) return true;
			lock (_lock)
			{
				var now = Stopwatch.GetTimestamp();
				var elapsed = (now - _last) / (double)Stopwatch.Frequency;
				_last = now;
				_tokens = Math.Min(_limiter.Burst, _tokens + elapsed * _limiter.RequestsPerSecond);
				if (_tokens < 1) return false;
				_tokens -= 1;
				return true;
			}
		}
	}
}
