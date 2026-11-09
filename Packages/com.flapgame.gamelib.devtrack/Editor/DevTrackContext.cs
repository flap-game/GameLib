using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEditor;
using UnityEngine;

namespace FlapGame.GameLib.DevTrack
{
    /// <summary>
    /// Everything about this Editor that does not change while it runs: the project, the machine and the git checkout.
    /// Collected once per Editor session and kept in <see cref="DevTrackState"/>, so a heartbeat costs nothing.
    /// The field names are the wire names (<see cref="DevTrackPayload"/>), and <c>JsonUtility</c> persists the class
    /// as it stands.
    /// </summary>
    [Serializable]
    public sealed class DevTrackContext
    {
        public string projectName;
        public string projectPath;
        public string bundleId;
        public string unityVersion;
        public string buildTarget;
        public string gitUrl;
        public string gitBranch;
        public string gitCommit;
        public string gitUserName;
        public string gitUserEmail;
        public string pcUser;
        public string pcName;
        public string localIp;

        /// <summary>
        /// The part that needs the Editor and the main thread. <see cref="FillMachineAndGit"/> adds the rest and may run
        /// on any thread.
        /// </summary>
        public static DevTrackContext FromEditor() => new DevTrackContext
        {
            projectName = Application.productName,
            projectPath = ProjectPath(),
            bundleId = Application.identifier,
            unityVersion = Application.unityVersion,
            buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
        };

        /// <summary>
        /// The machine name / user / IP and the git checkout. No Unity API, so it runs on a background thread
        /// (<see cref="DevTrackContextProbe"/>): <c>git</c> and the network stack are both slow enough to be felt on the
        /// main thread. Never throws.
        /// </summary>
        public static void FillMachineAndGit(DevTrackContext context, string projectPath)
        {
            if (context == null) return;

            try { context.pcUser = Environment.UserName; }
            catch (Exception) { }
            try { context.pcName = Environment.MachineName; }
            catch (Exception) { }
            context.localIp = PrimaryLocalIPv4();

            DevTrackGitInfo git = DevTrackGit.Read(projectPath);
            context.gitUrl = git.Url;
            context.gitBranch = git.Branch;
            context.gitCommit = git.Commit;
            context.gitUserName = git.UserName;
            context.gitUserEmail = git.UserEmail;
        }

        /// <summary>The project folder: the parent of <c>Application.dataPath</c>, the folder that holds Assets and Packages.</summary>
        public static string ProjectPath()
        {
            try
            {
                DirectoryInfo parent = Directory.GetParent(Application.dataPath);
                return parent != null ? parent.FullName : Application.dataPath;
            }
            catch (Exception)
            {
                return Application.dataPath;
            }
        }

        /// <summary>
        /// The machine's primary IPv4, the same idea as aframework's <c>GetLocalIPAddress</c>: an address on an interface
        /// that is up and has a gateway (so a VPN or a virtual adapter does not win over the home router), else any
        /// non-loopback IPv4, else the host name lookup. Empty when the machine has none. Never throws.
        /// </summary>
        public static string PrimaryLocalIPv4()
        {
            var withGateway = new List<string>();
            var others = new List<string>();

            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    IPInterfaceProperties properties = nic.GetIPProperties();
                    bool hasGateway = false;
                    foreach (GatewayIPAddressInformation gateway in properties.GatewayAddresses)
                    {
                        if (gateway.Address != null && gateway.Address.AddressFamily == AddressFamily.InterNetwork && !gateway.Address.Equals(IPAddress.Any))
                        {
                            hasGateway = true;
                            break;
                        }
                    }

                    foreach (UnicastIPAddressInformation info in properties.UnicastAddresses)
                    {
                        if (info.Address == null || info.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(info.Address)) continue;
                        (hasGateway ? withGateway : others).Add(info.Address.ToString());
                    }
                }
            }
            catch (Exception) { }

            if (withGateway.Count > 0) return withGateway[0];
            if (others.Count > 0) return others[0];

            try
            {
                IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (IPAddress ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        return ip.ToString();
                }
            }
            catch (Exception) { }

            return string.Empty;
        }
    }
}
