using System;
using System.IO;
using System.Net;
using System.Xml;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Web.Script.Serialization;
using System.Text.RegularExpressions;

namespace Updater_2
{
    internal class SearchFactor
    {
        public static List<string> computersList = new List<string>();

        public static bool Check(string ip)
        {
            Regex regex = new Regex("^((25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\\.){3}(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$");
            return regex.IsMatch(ip);
        }

        static uint IpToUInt32(string ipAddress)
        {
            return BitConverter.ToUInt32(IPAddress.Parse(ipAddress).GetAddressBytes().Reverse().ToArray(), 0);
        }

        static string UInt32ToIp(uint ipAddress)
        {
            return new IPAddress(BitConverter.GetBytes(ipAddress).Reverse().ToArray()).ToString();
        }

        static async Task NameComplex(string ip, string webPort)
        {
            UI.NameVersion nameVersion = new UI.NameVersion();
            string host = "IP is unavailable";
            string version = "-.-.-";
            PingReply pr = await new Ping().SendPingAsync(ip, 5000);
            if (pr.Status == IPStatus.Success)
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)HttpWebRequest.Create($"http://{ip}:{webPort}/unitinfo/api/unitinfo");
                    HttpWebResponse resp = (HttpWebResponse)req.GetResponse();
                    using (StreamReader stream = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        string factorJson = stream.ReadToEnd();
                        var datajson = new JavaScriptSerializer().Deserialize<dynamic>(factorJson);
                        string factoryNumber = datajson["unit"]["factoryNumber"];
                        string serialNumber = "";

                        if (datajson["certificate"] != null &&
                            datajson["certificate"].ContainsKey("serialNumber") &&
                            datajson["certificate"]["serialNumber"] != null)
                        {
                            serialNumber = datajson["certificate"]["serialNumber"] + " - ";
                        }
                        host = serialNumber + factoryNumber;
                    }
                    HttpWebRequest reqv = (HttpWebRequest)HttpWebRequest.Create($"http://{ip}:{webPort}/updater/installed-factor-version");
                    HttpWebResponse respv = (HttpWebResponse)reqv.GetResponse();
                    using (StreamReader stream = new StreamReader(respv.GetResponseStream(), Encoding.UTF8))
                    {
                        string factorJson = stream.ReadToEnd();
                        var datajson = new JavaScriptSerializer().Deserialize<dynamic>(factorJson);
                        version = datajson["version"];
                    }
                }
                catch
                {
                    host = "Not a Factor";
                }
            }
            nameVersion.name = host;
            nameVersion.version = version;

            UI.FactorsAdd(ip, nameVersion);
        }

        static void SearchFactors(string webPort)
        {
            Task[] tasks = new Task[computersList.Count];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = NameComplex(computersList.ElementAt<string>(i), webPort);
            }
            Task.WaitAll(tasks);
            UI.UiUnLock();
            UI.FullProgressBar();
            UI.AddDataGridView();

        }

        public static void IpSearch(string Start_IP, string Stop_IP, string webPort)
        {
            computersList.Clear();
            uint StartIPv4_UInt32 = IpToUInt32(Start_IP);
            uint EndIPv4_UInt32 = IpToUInt32(Stop_IP);

            if (StartIPv4_UInt32 > EndIPv4_UInt32)
            {
                uint xxx = StartIPv4_UInt32;
                StartIPv4_UInt32 = EndIPv4_UInt32;
                EndIPv4_UInt32 = xxx;
            }

            for (uint i = StartIPv4_UInt32; i <= EndIPv4_UInt32; i++)
            {
                computersList.Add(UInt32ToIp(i));
            }

            UI.SetMaxProgressBar(computersList.Count);

            new Thread(() => {
                SearchFactors(webPort);
            }).Start();

        }

        public static void Drop(string file, string webPort)
        {
            computersList.Clear();
            XmlDocument xDoc = new XmlDocument();
            xDoc.Load(file);
            XmlElement xRoot = xDoc.DocumentElement;
            if (xRoot != null)
            {
                foreach (XmlElement xnode in xRoot)
                {
                    if (xnode.Name == "ip")
                    {
                        if (SearchFactor.Check(xnode.InnerText))
                        {
                            computersList.Add(xnode.InnerText);
                        }
                    }
                }
            }

            UI.SetMaxProgressBar(computersList.Count);

            new Thread(() => {
                SearchFactors(webPort);
            }).Start();
        }
    }
}
