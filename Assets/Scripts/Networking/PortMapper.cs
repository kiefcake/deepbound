using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

namespace Deepbound.Networking
{
    /// <summary>
    /// Opening the host's port on their own router (2026-10-21, the user: "get multiplayer working for
    /// testing" with a friend elsewhere). UPnP: an SSDP search on the LAN finds the router, a SOAP
    /// AddPortMapping forwards the game's UDP port to this PC, and GetExternalIPAddress tells us the
    /// address a friend types in. Nothing leaves the local network but the router's own answers; no
    /// outside service is asked anything. Runs off the main thread; the UI reads <see cref="Line"/>.
    /// The mapping is removed when hosting stops or the game quits.
    /// </summary>
    public static class PortMapper
    {
        public enum State { Idle, Searching, Open, NoRouter, Refused }

        public static volatile State Current = State.Idle;
        /// <summary>The router's public address, once known (may be shared by the ISP: see <see cref="SharedAddress"/>).</summary>
        public static volatile string ExternalIp;
        /// <summary>This PC's address on the home network.</summary>
        public static volatile string LocalIp;
        public static volatile string Detail = "";
        public static ushort Port { get; private set; }

        private static string controlUrl, serviceType;
        private static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };

        /// <summary>The router's "public" address is itself private: the ISP shares one address between homes (CGNAT), so port forwarding cannot work.</summary>
        public static bool SharedAddress => IsPrivate(ExternalIp);

        /// <summary>What to tell the host, in one line.</summary>
        public static string Line
        {
            get
            {
                switch (Current)
                {
                    case State.Searching: return "Opening port " + Port + " on your router...";
                    case State.Open:
                        return SharedAddress
                            ? "Your internet provider shares your address (" + ExternalIp + "): friends outside your home need a LAN tool such as Tailscale or Radmin VPN."
                            : "Friends join at  " + ExternalIp + (Port != 7777 ? ":" + Port : "");
                    case State.NoRouter: return "Your router did not answer (UPnP off?): forward UDP " + Port + " to " + (LocalIp ?? "this PC") + ", or use Tailscale / Radmin VPN.";
                    case State.Refused: return "Your router refused to open port " + Port + " (" + Detail + "): forward UDP " + Port + " to " + (LocalIp ?? "this PC") + " by hand.";
                    default: return "";
                }
            }
        }

        public static void Open(ushort port)
        {
            if (Current == State.Searching || (Current == State.Open && Port == port)) return;
            Port = port;
            Current = State.Searching;
            LocalIp = GuessLocalIp(null);
            Task.Run(async () =>
            {
                try { await OpenAsync(port); }
                catch (Exception e) { Current = State.NoRouter; Detail = e.Message; Debug.Log("[PortMapper] " + e.Message); }
            });
        }

        /// <summary>Remove the mapping (short, blocking: called when hosting stops and on quit).</summary>
        public static void Close()
        {
            if (Current != State.Open || controlUrl == null) { Current = State.Idle; return; }
            Current = State.Idle;
            try
            {
                var t = Soap("DeletePortMapping", "<NewRemoteHost></NewRemoteHost><NewExternalPort>" + Port + "</NewExternalPort><NewProtocol>UDP</NewProtocol>");
                t.Wait(1500);
            }
            catch { }
        }

        private static async Task OpenAsync(ushort port)
        {
            string location = await Discover();
            if (location == null) { Current = State.NoRouter; return; }
            var baseUri = new Uri(location);
            LocalIp = GuessLocalIp(baseUri.Host);
            string xml = await http.GetStringAsync(location);
            // The WAN connection service: IP for most routers, PPP for some DSL ones.
            var m = Regex.Match(xml, @"<serviceType>(urn:schemas-upnp-org:service:WAN(?:IP|PPP)Connection:\d)</serviceType>.*?<controlURL>([^<]+)</controlURL>", RegexOptions.Singleline);
            if (!m.Success) { Current = State.NoRouter; Detail = "no WAN service"; return; }
            serviceType = m.Groups[1].Value;
            controlUrl = new Uri(baseUri, m.Groups[2].Value.Trim()).ToString();

            string args(int lease) => "<NewRemoteHost></NewRemoteHost><NewExternalPort>" + port + "</NewExternalPort><NewProtocol>UDP</NewProtocol>" +
                                      "<NewInternalPort>" + port + "</NewInternalPort><NewInternalClient>" + LocalIp + "</NewInternalClient><NewEnabled>1</NewEnabled>" +
                                      "<NewPortMappingDescription>Deepbound</NewPortMappingDescription><NewLeaseDuration>" + lease + "</NewLeaseDuration>";
            var (ok, body) = await Soap("AddPortMapping", args(7200));
            // Some routers only take permanent leases (error 725): ask again without one.
            if (!ok && body.Contains("725")) (ok, body) = await Soap("AddPortMapping", args(0));
            var (ipOk, ipBody) = await Soap("GetExternalIPAddress", "");
            if (ipOk)
            {
                var ip = Regex.Match(ipBody, @"<NewExternalIPAddress>([^<]*)</NewExternalIPAddress>");
                if (ip.Success && ip.Groups[1].Value.Length > 0) ExternalIp = ip.Groups[1].Value.Trim();
            }
            if (ok) { Current = State.Open; Debug.Log("[PortMapper] UDP " + port + " -> " + LocalIp + ", external " + ExternalIp); }
            else
            {
                var code = Regex.Match(body ?? "", @"<errorDescription>([^<]*)</errorDescription>");
                Detail = code.Success ? code.Groups[1].Value : "error";
                Current = State.Refused;
                Debug.Log("[PortMapper] refused: " + Detail);
            }
        }

        private static async Task<string> Discover()
        {
            using (var udp = new UdpClient(AddressFamily.InterNetwork))
            {
                udp.Client.ReceiveTimeout = 3000;
                string[] targets = { "urn:schemas-upnp-org:device:InternetGatewayDevice:1", "urn:schemas-upnp-org:service:WANIPConnection:1", "urn:schemas-upnp-org:device:InternetGatewayDevice:2" };
                var group = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
                foreach (var st in targets)
                {
                    var msg = Encoding.ASCII.GetBytes("M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: " + st + "\r\n\r\n");
                    await udp.SendAsync(msg, msg.Length, group);
                }
                var until = DateTime.UtcNow.AddSeconds(3.5);
                while (DateTime.UtcNow < until)
                {
                    var receive = udp.ReceiveAsync();
                    var done = await Task.WhenAny(receive, Task.Delay(until - DateTime.UtcNow));
                    if (done != receive) break;
                    string text = Encoding.ASCII.GetString(receive.Result.Buffer);
                    var loc = Regex.Match(text, @"^LOCATION:\s*(\S+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                    if (loc.Success && (text.IndexOf("InternetGatewayDevice", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("WANIPConnection", StringComparison.OrdinalIgnoreCase) >= 0))
                        return loc.Groups[1].Value.Trim();
                }
            }
            return null;
        }

        private static async Task<(bool ok, string body)> Soap(string action, string args)
        {
            string envelope = "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                              "<s:Body><u:" + action + " xmlns:u=\"" + serviceType + "\">" + args + "</u:" + action + "></s:Body></s:Envelope>";
            var req = new HttpRequestMessage(HttpMethod.Post, controlUrl) { Content = new StringContent(envelope, Encoding.UTF8, "text/xml") };
            req.Headers.TryAddWithoutValidation("SOAPACTION", "\"" + serviceType + "#" + action + "\"");
            var res = await http.SendAsync(req);
            string body = await res.Content.ReadAsStringAsync();
            return (res.IsSuccessStatusCode, body);
        }

        /// <summary>The address this PC uses to reach the router (no packet is sent: a UDP "connect" only picks a route).</summary>
        public static string GuessLocalIp(string toward)
        {
            try
            {
                using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    s.Connect(toward ?? "192.168.1.1", 1900);
                    return ((IPEndPoint)s.LocalEndPoint).Address.ToString();
                }
            }
            catch { return null; }
        }

        public static bool IsPrivate(string ip)
        {
            if (string.IsNullOrEmpty(ip) || !IPAddress.TryParse(ip, out var a) || a.AddressFamily != AddressFamily.InterNetwork) return false;
            var b = a.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] < 32) || (b[0] == 192 && b[1] == 168) || (b[0] == 100 && b[1] >= 64 && b[1] < 128) || b[0] == 127;
        }
    }
}
