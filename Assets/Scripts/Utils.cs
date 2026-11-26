using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;

namespace Robot
{
    public class Utils
    {
        /// <summary>
        /// Gets the current timestamp in microseconds since Unix epoch (January 1, 1970)
        /// </summary>
        /// <returns>Current timestamp in microseconds</returns>
        public static long GetCurrentTimestamp()
        {
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            long currentTimestamp = (long)((DateTime.UtcNow - epoch).TotalMilliseconds * 1000000);
            return currentTimestamp;
        }

        /// <summary>
        /// Gets a usable local IPv4. When <paramref name="preferredPeerIp"/> is set,
        /// prefers a WLAN address on the same subnet (mask-aware; not hard-coded /24)
        /// so PC Wi‑Fi reverse-connect for video reaches the headset.
        /// </summary>
        public static string GetLocalIPv4(string preferredPeerIp = null)
        {
            IPAddress peer = null;
            if (!string.IsNullOrEmpty(preferredPeerIp) &&
                IPAddress.TryParse(preferredPeerIp, out var parsedPeer) &&
                parsedPeer.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(parsedPeer))
            {
                peer = parsedPeer;
            }

            string sameSubnetWifi = null;
            string sameSubnetAny = null;
            string anyWifi = null;
            string anyUnicast = null;

            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up)
                    {
                        continue;
                    }

                    var type = nic.NetworkInterfaceType;
                    if (type == NetworkInterfaceType.Loopback ||
                        type == NetworkInterfaceType.Tunnel)
                    {
                        continue;
                    }

                    // USB tether / RNDIS often looks like Ethernet; prefer real Wi‑Fi for LAN peers.
                    bool isWifi = type == NetworkInterfaceType.Wireless80211;
                    string nicName = (nic.Name ?? "") + " " + (nic.Description ?? "");
                    string nicLower = nicName.ToLowerInvariant();
                    if (nicLower.Contains("rndis") || nicLower.Contains("usb") ||
                        nicLower.Contains("docker") || nicLower.Contains("veth"))
                    {
                        if (peer != null && !isWifi)
                        {
                            continue;
                        }
                    }

                    foreach (UnicastIPAddressInformation uni in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (uni.Address.AddressFamily != AddressFamily.InterNetwork)
                        {
                            continue;
                        }

                        byte[] bytes = uni.Address.GetAddressBytes();
                        if (bytes[0] == 127)
                        {
                            continue;
                        }

                        // Skip link-local 169.254.x.x
                        if (bytes[0] == 169 && bytes[1] == 254)
                        {
                            continue;
                        }

                        string candidate = uni.Address.ToString();
                        if (isWifi && anyWifi == null)
                        {
                            anyWifi = candidate;
                        }

                        if (anyUnicast == null)
                        {
                            anyUnicast = candidate;
                        }

                        if (peer != null && IsSameIpv4Subnet(uni.Address, peer, uni.IPv4Mask))
                        {
                            if (isWifi)
                            {
                                sameSubnetWifi = candidate;
                            }
                            else if (sameSubnetAny == null)
                            {
                                sameSubnetAny = candidate;
                            }
                        }
                    }

                    if (sameSubnetWifi != null)
                    {
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"GetLocalIPv4 NIC scan failed: {e.Message}");
            }

            if (sameSubnetWifi != null)
            {
                return sameSubnetWifi;
            }

            if (sameSubnetAny != null)
            {
                return sameSubnetAny;
            }

            if (peer != null && anyWifi != null)
            {
                return anyWifi;
            }

            if (anyWifi != null)
            {
                return anyWifi;
            }

            if (anyUnicast != null)
            {
                return anyUnicast;
            }

            // Legacy fallback
            foreach (IPAddress ip in Dns.GetHostAddresses(Dns.GetHostName()))
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                {
                    return ip.ToString();
                }
            }

            return "Not found";
        }

        private static bool IsSameIpv4Subnet(IPAddress local, IPAddress peer, IPAddress mask)
        {
            if (local == null || peer == null)
            {
                return false;
            }

            byte[] a = local.GetAddressBytes();
            byte[] b = peer.GetAddressBytes();
            if (a.Length != 4 || b.Length != 4)
            {
                return false;
            }

            // Fall back to /23-ish match if mask missing (Quest sometimes omits IPv4Mask).
            byte[] m = mask != null ? mask.GetAddressBytes() : new byte[] { 255, 255, 254, 0 };
            if (m.Length != 4)
            {
                m = new byte[] { 255, 255, 254, 0 };
            }

            for (int i = 0; i < 4; i++)
            {
                if ((a[i] & m[i]) != (b[i] & m[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Check if the local port is occupied
        /// </summary>
        /// <param name="port"></param>
        /// <returns></returns>
        public static bool IsPortAvailable(int port)
        {
            try
            {
                TcpListener listener = new TcpListener(IPAddress.Any, port);

                listener.Start();

                listener.Stop();

                return true;
            }
            catch (SocketException e)
            {
                return false;
            }
        }

        /// <summary>
        /// Gets an automatically assigned available port from the system
        /// </summary>
        /// <returns>An available port number</returns>
        public static int GetAvailablePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0); // 传入 0 让系统自动分配可用端口
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        /// <summary>
        /// Gets camera intrinsics as a formatted string for external devices
        /// </summary>
        /// <param name="width">Camera width</param>
        /// <param name="height">Camera height</param>
        /// <returns>Camera intrinsics string in scientific notation format</returns>
        public static string GetCameraIntrinsicsStrE(int width, int height)
        {
            string cameraIntrinsics = "";
            // double[] intrinsics = PXR_Enterprise.GetCameraIntrinsicsfor4U(width, height, 76.35f, 61.05f);
            // for (int i = 0; i < intrinsics.Length; i++)
            // {
            //     if (i > 0)
            //     {
            //         cameraIntrinsics += ",";
            //     }
            //
            //     cameraIntrinsics += intrinsics[i].ToString("E16");
            // }

            return cameraIntrinsics;
        }

        /// <summary>
        /// Gets camera extrinsics (left and right camera parameters) as a formatted string
        /// </summary>
        /// <returns>Camera extrinsics string with left and right parameters separated by '|'</returns>
        public static string GetCameraExtrinsicsStrE()
        {
            // PXR_EnterprisePlugin.GetCameraExtrinsics(out var leftExtrinsics, out var rightExtrinsics);
            string cameraExtrinsics = "";
            //
            // //left
            // for (int i = 0; i < leftExtrinsics.Length; i++)
            // {
            //     if (i > 0)
            //     {
            //         cameraExtrinsics += ",";
            //     }
            //
            //     cameraExtrinsics += leftExtrinsics[i].ToString("E16");
            // }
            //
            //
            // cameraExtrinsics += "|";
            // //right
            // for (int i = 0; i < rightExtrinsics.Length; i++)
            // {
            //     if (i > 0)
            //     {
            //         cameraExtrinsics += ",";
            //     }
            //
            //     cameraExtrinsics += rightExtrinsics[i].ToString("E16");
            // }
            //
            return cameraExtrinsics;
        }

        /// <summary>
        /// Writes a formatted log message with tag and message content
        /// Uses colored output in Unity Editor and standard logcat format in builds
        /// </summary>
        /// <param name="tag">The tag to identify the log source</param>
        /// <param name="msg">The message content to log</param>
        public static void WriteLog(string tag, string msg)
        {
#if UNITY_EDITOR
            Debug.Log($"[XRoboToolkit]<color=red>{tag}</color>\t{msg}");
#else
            // Output to logcat
            Debug.Log($"[XRoboToolkit]\t{tag}\t{msg}");
#endif
        }
    }
}