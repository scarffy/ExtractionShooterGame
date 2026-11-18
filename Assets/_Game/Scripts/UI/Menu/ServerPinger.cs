using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEngine;

namespace ExtractionShooter.UI.Menu
{
    /// <summary>
    /// Measures round trips to server hosts for the server browser with <see cref="Ping"/> (ICMP echo; IPv4 only).
    /// Host names are resolved first with <see cref="Dns.GetHostAddressesAsync(string)"/>. Nothing blocks: the owner
    /// calls <see cref="Tick"/> every frame and it polls the lookups and pings. Unknown (no IPv4 address, ICMP blocked,
    /// timed out) is -1. At most <see cref="MaxConcurrent"/> probes run at once; the rest wait their turn.
    /// </summary>
    internal sealed class ServerPinger : IDisposable
    {
        private const int MaxConcurrent = 8;
        private const float TimeoutSeconds = 2f;

        private sealed class Probe
        {
            public string Host;
            public Task<IPAddress[]> Lookup;
            public Ping Ping;
            public float StartedAt;
        }

        private readonly Dictionary<string, int> _results = new Dictionary<string, int>();
        private readonly List<Probe> _active = new List<Probe>();
        private readonly List<string> _waiting = new List<string>();

        /// <summary>The last measured ping to <paramref name="host"/> in ms, or -1 when unknown.</summary>
        public int Get(string host) => host != null && _results.TryGetValue(host, out int ms) ? ms : -1;

        /// <summary>Queues a new measurement (the previous result stays until it finishes).</summary>
        public void Request(string host)
        {
            if (string.IsNullOrEmpty(host) || _waiting.Contains(host)) return;
            foreach (Probe probe in _active)
                if (probe.Host == host) return;
            _waiting.Add(host);
        }

        /// <summary>Advances lookups and pings. True when a result changed.</summary>
        public bool Tick(float now)
        {
            bool changed = false;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Probe probe = _active[i];
                int result;
                if (!TryAdvance(probe, now, out result)) continue;
                Finish(probe);
                _active.RemoveAt(i);
                if (!_results.TryGetValue(probe.Host, out int old) || old != result)
                {
                    _results[probe.Host] = result;
                    changed = true;
                }
            }

            while (_active.Count < MaxConcurrent && _waiting.Count > 0)
            {
                string host = _waiting[0];
                _waiting.RemoveAt(0);
                Probe probe = Start(host, now, out int immediate);
                if (probe != null)
                {
                    _active.Add(probe);
                }
                else if (!_results.TryGetValue(host, out int old) || old != immediate)
                {
                    _results[host] = immediate;
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>Stops every lookup and ping in progress (results are kept).</summary>
        public void Cancel()
        {
            foreach (Probe probe in _active) Finish(probe);
            _active.Clear();
            _waiting.Clear();
        }

        public void Dispose() => Cancel();

        // ---------------- Internals ----------------

        /// <summary>A probe for <paramref name="host"/>, or null with the result when it is known at once (-1: cannot ping).</summary>
        private static Probe Start(string host, float now, out int immediate)
        {
            immediate = -1;
            if (IPAddress.TryParse(host, out IPAddress address))
            {
                string ip = ToIPv4(address);
                return ip != null ? StartPing(new Probe { Host = host }, ip, now) : null;
            }

            try
            {
                Task<IPAddress[]> lookup = Dns.GetHostAddressesAsync(host);
                // A lookup given up on (timeout, panel closed) may still fail later: observe it so it is not reported.
                lookup.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                return new Probe { Host = host, Lookup = lookup, StartedAt = now };
            }
            catch (Exception exception) when (exception is ArgumentException || exception is SocketException)
            {
                return null;
            }
        }

        private static Probe StartPing(Probe probe, string ip, float now)
        {
            probe.Ping = new Ping(ip);
            probe.StartedAt = now;
            return probe;
        }

        /// <summary>True when the probe is over, with its result.</summary>
        private static bool TryAdvance(Probe probe, float now, out int result)
        {
            result = -1;
            if (probe.Ping == null)
            {
                Task<IPAddress[]> lookup = probe.Lookup;
                if (lookup == null) return true;
                if (!lookup.IsCompleted) return now - probe.StartedAt > TimeoutSeconds;

                string ip = null;
                if (lookup.Status == TaskStatus.RanToCompletion && lookup.Result != null)
                {
                    foreach (IPAddress address in lookup.Result)
                        if ((ip = ToIPv4(address)) != null) break;
                }
                else
                {
                    _ = lookup.Exception; // observed: a failed lookup is just "unknown"
                }
                probe.Lookup = null;
                if (ip == null) return true;
                StartPing(probe, ip, now);
                return false;
            }

            if (probe.Ping.isDone)
            {
                result = probe.Ping.time >= 0 ? probe.Ping.time : -1;
                return true;
            }
            return now - probe.StartedAt > TimeoutSeconds;
        }

        private static void Finish(Probe probe)
        {
            probe.Ping?.DestroyPing();
            probe.Ping = null;
            probe.Lookup = null;
        }

        /// <summary>The dotted IPv4 form (IPv4-mapped IPv6 included), or null for other IPv6 addresses.</summary>
        private static string ToIPv4(IPAddress address)
        {
            if (address == null) return null;
            if (address.AddressFamily == AddressFamily.InterNetwork) return address.ToString();
            if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv4MappedToIPv6) return address.MapToIPv4().ToString();
            return null;
        }
    }
}
