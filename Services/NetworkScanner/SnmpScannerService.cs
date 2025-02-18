using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NetworkDeviceScannerWPF.Models;
using SnmpSharpNet;

namespace NetworkDeviceScannerWPF.Services.NetworkScanner
{
    public class SnmpScannerService
    {
        private static readonly string[] CommunityStrings = { "public", "private" };
        private static readonly OctetString[] SnmpVersions = { new OctetString("v1"), new OctetString("v2c") };

        public async Task<NetworkDevice> ScanDeviceAsync(string ipAddress)
        {
            foreach (var community in CommunityStrings)
            {
                try
                {
                    var result = await Task.Run(() => GetSnmpInfo(ipAddress, community));
                    if (result != null)
                    {
                        return result;
                    }
                }
                catch
                {
                    // 继续尝试下一个community string
                    continue;
                }
            }
            return null;
        }

        private NetworkDevice GetSnmpInfo(string ipAddress, string community)
        {
            var agent = new IpAddress(ipAddress);
            var param = new AgentParameters(SnmpVersion.Ver2, new OctetString(community));
            var target = new UdpTarget((IPAddress)agent, 161, 2000, 1);

            try
            {
                // 系统描述 OID
                var sysDescr = new Oid("1.3.6.1.2.1.1.1.0");
                // 系统名称 OID
                var sysName = new Oid("1.3.6.1.2.1.1.5.0");
                // 系统位置 OID
                var sysLocation = new Oid("1.3.6.1.2.1.1.6.0");

                var pdu = new Pdu(PduType.Get);
                pdu.VbList.Add(sysDescr);
                pdu.VbList.Add(sysName);
                pdu.VbList.Add(sysLocation);

                var result = target.Request(pdu, param);

                if (result != null && result.Pdu.ErrorStatus == 0)
                {
                    var device = new NetworkDevice
                    {
                        IP = ipAddress,
                        IsOnline = true,
                        LastSeen = DateTime.Now,
                        DiscoveryMethod = "SNMP",
                        Name = result.Pdu.VbList[1].Value.ToString(),
                        Location = result.Pdu.VbList[2].Value.ToString(),
                        CustomName = $"SNMP Device ({result.Pdu.VbList[1].Value})"
                    };

                    return device;
                }
            }
            finally
            {
                target.Close();
            }

            return null;
        }
    }
} 