using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;

namespace FlapGame.GameLib.Analytics.Local
{
    /// <summary>
    /// Decides whether this device is one of ours: on device, one of its IPv4 addresses starts with a configured prefix
    /// (the office LAN); in the Editor, the module's own toggle. A player's phone never matches, so the module is inert
    /// in a shipped build even though it is compiled in.
    /// </summary>
    public static class LocalTrackingGate
    {
        /// <param name="trackInEditor">The Editor answer (<c>LocalAnalyticsModule.TrackInEditor</c>).</param>
        /// <param name="lanAddressPrefixes">IPv4 prefixes of the networks local tracking runs on, e.g. <c>192.25.20</c>.</param>
        /// <param name="reason">One line for the log: what matched, or why nothing did.</param>
        public static bool IsLocalDevice(bool trackInEditor, IReadOnlyList<string> lanAddressPrefixes, out string reason)
        {
            if (Application.isEditor)
            {
                reason = trackInEditor ? "Editor, Track In Editor is on" : "Editor and Track In Editor is off";
                return trackInEditor;
            }

            IReadOnlyList<string> addresses = LocalIPv4Addresses();
            string match = FindMatch(addresses, lanAddressPrefixes);
            if (match != null)
            {
                reason = $"local address {match}";
                return true;
            }

            reason = addresses.Count == 0
                ? "no local IPv4 address found"
                : $"no local address on {Describe(lanAddressPrefixes)} (this device: {string.Join(", ", addresses)})";
            return false;
        }

        /// <summary>The first address in <paramref name="addresses"/> that starts with one of <paramref name="prefixes"/>, else null.</summary>
        public static string FindMatch(IEnumerable<string> addresses, IReadOnlyList<string> prefixes)
        {
            if (addresses == null || prefixes == null) return null;

            foreach (string address in addresses)
            {
                if (string.IsNullOrEmpty(address)) continue;
                for (int i = 0; i < prefixes.Count; i++)
                {
                    string prefix = prefixes[i]?.Trim();
                    if (string.IsNullOrEmpty(prefix)) continue;
                    if (address.StartsWith(prefix, StringComparison.Ordinal)) return address;
                }
            }

            return null;
        }

        /// <summary>
        /// Every IPv4 address of an interface that is up, from the network stack. Falls back to a DNS lookup of the host
        /// name (what aframework used) when the interface list comes back empty, as it can on some Android devices.
        /// </summary>
        public static IReadOnlyList<string> LocalIPv4Addresses()
        {
            var addresses = new List<string>();
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    foreach (UnicastIPAddressInformation info in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (info.Address != null && info.Address.AddressFamily == AddressFamily.InterNetwork)
                            addresses.Add(info.Address.ToString());
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LocalAnalyticsModule.LogTag} Could not read the network interfaces ({e.GetType().Name}: {e.Message}); falling back to the host name.");
            }

            if (addresses.Count > 0) return addresses;

            try
            {
                IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (IPAddress ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                        addresses.Add(ip.ToString());
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LocalAnalyticsModule.LogTag} Could not resolve the host name ({e.GetType().Name}: {e.Message}).");
            }

            return addresses;
        }

        private static string Describe(IReadOnlyList<string> prefixes)
        {
            if (prefixes == null || prefixes.Count == 0) return "(no prefix configured)";
            return string.Join(", ", prefixes);
        }
    }
}
