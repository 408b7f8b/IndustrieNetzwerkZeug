using System;
using SharpPcap;
using System.Dynamic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using PacketDotNet.DhcpV4;
using System.Collections.Generic;
using System.Linq;
using IndustrieNetzwerkZeug.Allgemein;

namespace IndustrieNetzwerkZeug.Profinet
{
    public class NetzwerksystemProfinet : Netzwerksystem
    {
        public int VendorId;
        public int DeviceId;
        public string NameOfStation;
        public ElementeProfinet.DeviceRoles DeviceRole;
        public string DeviceType;

        public NetzwerksystemProfinet()
        {
            typ = Type.Profinet;
            NameOfStation = "";
            DeviceType = "";
        }

        public void IpZuweisen(string ip, string subnetzmaske, string gateway = "0.0.0.0")
        {
            var verfuegbareInterfaces = CaptureDeviceList.Instance.ToList();
            var zuNutzendesInterface = verfuegbareInterfaces.Find(x => x.Name == Netzwerkadapter);
            if (zuNutzendesInterface == null)
            {
                throw new Exception("Netzwerkadapter nicht verfügbar.");
            }

            // Open the device for capturing
            int readTimeoutMilliseconds = 1000;
            zuNutzendesInterface.Open(read_timeout: readTimeoutMilliseconds);

            var frame = ElementeProfinet.GetIpRequest(mac, zuNutzendesInterface.MacAddress.ToString(), ip, subnetzmaske, gateway);
            zuNutzendesInterface.SendPacket(frame); //ist noch ohne abfrage obs tatsächlich durchgeht
            this.ip = ip;
            this.subnetzmaske = subnetzmaske;

            zuNutzendesInterface.Close();
        }
    }

    public class ProtokollFunktionenProfinet : IProtokollFunktionen
    {
        List<NetzwerksystemProfinet> GefundeneSysteme = new();

        private void OnPacketArrival(object sender, PacketCapture e)
        {
            //var time = e.Header.Timeval.Date;
            //var len = e.Data.Length;
            var rawPacket = e.GetPacket();

            var packet = PacketDotNet.Packet.ParsePacket(rawPacket.LinkLayerType, rawPacket.Data);

            if (packet is PacketDotNet.EthernetPacket eth)
            {
                byte[] data;
                var offset = 0;
                if (eth.Type == PacketDotNet.EthernetType.VLanTaggedFrame)
                {
                    data = rawPacket.Data;
                    offset = 18;
                }
                else if (eth.Type == PacketDotNet.EthernetType.Profinet)
                {
                    data = eth.PayloadData;
                    offset = 0;
                }
                else
                {
                    return;
                }

                isci.Logger.Information(Convert.ToString(eth.Type));
                isci.Logger.Information("Zieladresse: " + eth.DestinationHardwareAddress);
                isci.Logger.Information("Absender:" + eth.SourceHardwareAddress);
                isci.Logger.Information("FrameID: " + data[0 + offset].ToString("X2") + data[1 + offset].ToString("X2"));

                // get the Enum name of the ServiceID, the ServiceID is given by PayloadData[2] and PayloadData[3]

                ushort serviceId = (ushort)((data[2 + offset] << 8) | data[3 + offset]);
                string serviceName = Enum.GetName(typeof(ElementeProfinet.ServiceIds), serviceId);
                isci.Logger.Information("ServiceName: " + serviceName);

                // get the xID, the xID is given by PayloadData[4] - PayloadData[7]
                uint xID = (uint)((data[4 + offset] << 24) | (data[5 + offset] << 16) | (data[6 + offset] << 8) | data[7 + offset]);

                if (serviceId == (ushort)ElementeProfinet.ServiceIds.Identify_Response)
                {
                    isci.Logger.Information("Ein neues Gerät mit der xID " + xID.ToString() + " hat sich angemeldet!");

                    // PayloadData[8] and PayloadData[9] are reserved

                    // the dataLength is given by PayloadData[10] and PayloadData[11]
                    ushort dataLength = (ushort)((data[10 + offset] << 8) | data[11 + offset]);

                    isci.Logger.Information("\t DataLength: " + dataLength.ToString());
                    byte[] deviceInfo = new byte[dataLength];
                    Array.Copy(sourceArray: data, 12 + offset, deviceInfo, 0, dataLength);

                    var system = GefundeneSysteme.Find(x => x.mac == eth.SourceHardwareAddress.ToString());
                    if (system == null)
                    {
                        system = new NetzwerksystemProfinet()
                        {
                            mac = eth.SourceHardwareAddress.ToString()
                        };
                        GefundeneSysteme.Add(system);

                        int i = 0;

                        // die Selbstauskunft des Gerätes parsen
                        // der Aufbau kann mit Hilfe von Wireshark nachvollzogen werden
                        // viel Byteschubserei
                        // die gewonnen Daten sollten anhand der Log-Befehle ersichtlich sein
                        while (i < deviceInfo.Length)
                        {
                            // get the next ServiceID, the ServiceID is given by PayloadData[i] and PayloadData[i+1]
                            ushort nextServiceId = (ushort)((deviceInfo[i] << 8) | deviceInfo[i + 1]);
                            string BlockName = Enum.GetName(typeof(ElementeProfinet.BlockOptions), nextServiceId);
                            isci.Logger.Information("\t BlockName: " + BlockName);

                            //get the BlockLength, the BlockLength is given by deviceInfo[i+2] and deviceInfo[i+3]
                            ushort blockLength = (ushort)((deviceInfo[i + 2] << 8) | deviceInfo[i + 3]);
                            isci.Logger.Information("\t BlockLength: " + blockLength.ToString());

                            //get the BlockInfo, the BlockInfo is given by deviceInfo[i+4] and deviceInfo[i+5]
                            ushort blockInfoValue = (ushort)((deviceInfo[i + 4] << 8) | deviceInfo[i + 5]);
                            isci.Logger.Information("\t BlockInfoValue: " + blockInfoValue.ToString());
                            string blockInfo = Enum.GetName(typeof(ElementeProfinet.BlockInfo), blockInfoValue);

                            if (nextServiceId == (ushort)ElementeProfinet.BlockOptions.DeviceProperties_NameOfStation)
                            {
                                // Convert deviceInfo[i + 4 + 2] - deviceInfo[i + 4 + blockLength] to ASCII
                                byte[] asciiBytes = new byte[blockLength - 2];
                                Array.Copy(deviceInfo, i + 6, asciiBytes, 0, blockLength - 2);
                                string asciiString = System.Text.Encoding.ASCII.GetString(asciiBytes);
                                isci.Logger.Information("Parsed NameOfStation: " + asciiString);
                                system.NameOfStation = asciiString;
                            }
                            else if (nextServiceId == (ushort)ElementeProfinet.BlockOptions.IP_IPParameter)
                            {
                                system.ip = (new IPAddress(new byte[] { deviceInfo[i + 6], deviceInfo[i + 7], deviceInfo[i + 8], deviceInfo[i + 9] })).ToString();
                                isci.Logger.Information("Parsed IP: " + system.ip);
                                system.subnetzmaske = (new IPAddress(new byte[] { deviceInfo[i + 10], deviceInfo[i + 11], deviceInfo[i + 12], deviceInfo[i + 13] })).ToString();
                                isci.Logger.Information("Parsed Subnet: " + system.subnetzmaske);
                                system.gateway = (new IPAddress(new byte[] { deviceInfo[i + 14], deviceInfo[i + 15], deviceInfo[i + 16], deviceInfo[i + 17] })).ToString();
                                isci.Logger.Information("Parsed Gateway: " + system.gateway);
                            }
                            else if (nextServiceId == (ushort)ElementeProfinet.BlockOptions.DeviceProperties_DeviceID)
                            {
                                system.VendorId = deviceInfo[i + 6] << 8 + deviceInfo[i + 7];
                                isci.Logger.Information("Parsed VendorID: " + system.VendorId);
                                system.DeviceId = deviceInfo[i + 8] << 8 + deviceInfo[i + 9];
                                isci.Logger.Information("Parsed DeviceID: " + system.DeviceId);

                                //VendorID = 0x0136 für IFM
                                //VendorID = 0x0101 für SICK
                            }
                            else if (nextServiceId == (ushort)ElementeProfinet.BlockOptions.DeviceProperties_DeviceOptions)
                            {
                                //TBD
                            }
                            else if (nextServiceId == (ushort)ElementeProfinet.BlockOptions.DeviceProperties_DeviceRole)
                            {
                                system.DeviceRole = (ElementeProfinet.DeviceRoles)deviceInfo[i + 6];
                                isci.Logger.Information("Parsed DeviceRole: " + system.DeviceRole.ToString());
                            }
                            else if (nextServiceId == (ushort)ElementeProfinet.BlockOptions.DeviceInitiative_DeviceInitiative)
                            {
                                //TBD
                            }
                            else if (nextServiceId == (ushort)ElementeProfinet.BlockOptions.DeviceProperties_DeviceVendor)
                            {
                                // Convert deviceInfo[i + 4 + 2] - deviceInfo[i + 4 + blockLength] to ASCII
                                byte[] asciiBytes = new byte[blockLength - 2];
                                Array.Copy(deviceInfo, i + 4 + 2, asciiBytes, 0, blockLength - 2);
                                string asciiString = System.Text.Encoding.ASCII.GetString(asciiBytes);
                                system.DeviceType = asciiString;
                                isci.Logger.Information("Parsed DeviceType: " + system.DeviceType);
                                //vendor ist hier identifizierbar, darüber kann dann API bestimmt werden
                            }
                            else
                            {
                                isci.Logger.Information("\t BlockInfo: " + blockInfo);
                                var logOutput = "\t ";
                                for (int j = 2; j < blockLength; j++)
                                {
                                    logOutput += deviceInfo[i + 4 + j].ToString("X2") + "|";
                                }
                                isci.Logger.Information(logOutput);
                            }

                            if (blockLength % 2 == 1)
                            {
                                isci.Logger.Information("\t Padding");
                                i++; // wenn die BlockLength ungerade ist, muss ein Byte übersprungen werden
                            }
                            i = i + blockLength + 4;
                        }
                    }
                    system.ZeitpunktLetzterKontakt = DateTime.Now;
                }
            }
        }

        public void NetzwerkAbfragen(string Netzwerkadapter, int AntwortDauerMs)
        {
            var verfuegbareInterfaces = CaptureDeviceList.Instance.ToList();
            var zuNutzendesInterface = verfuegbareInterfaces.Find(x => x.Name == Netzwerkadapter);
            if (zuNutzendesInterface == null)
            {
                throw new Exception("Netzwerkadapter nicht verfügbar.");
            }

            @zuNutzendesInterface.OnPacketArrival += new PacketArrivalEventHandler(OnPacketArrival);

            // Open the device for capturing
            int readTimeoutMilliseconds = 1000;
            zuNutzendesInterface.Open(read_timeout: readTimeoutMilliseconds);
            //@zuNutzendesInterface.Open(mode: DeviceModes.Promiscuous | DeviceModes.DataTransferUdp | DeviceModes.NoCaptureLocal, read_timeout: readTimeoutMilliseconds);

            // Start the capturing process
            zuNutzendesInterface.StartCapture();
            zuNutzendesInterface.SendPacket(ElementeProfinet.GetRequestIdentity(zuNutzendesInterface.MacAddress.GetAddressBytes()));

            System.Threading.Thread.Sleep(AntwortDauerMs);

            zuNutzendesInterface.StopCapture();
            zuNutzendesInterface.Close();
        }
    }

    public static class ElementeProfinet
    {
        public static byte[] GetRequestIdentity(byte[] senderMacAddress)
        {
            byte[] destination = MulticastBytes();

            byte[] source = senderMacAddress;

            byte[] type = [0x88, 0x92];

            byte[] frameID = [0xfe, 0xfe];

            byte[] serviceID = [0x05, 0x00];

            byte[] xID = [0x01, 0x02, 0x03, 0x04];

            byte[] responseDelay = [0x00, 0xff];

            byte[] dataLength = [0x00, 0x04];

            byte[] dataBlock = [0xff, 0xff, 0x00, 0x00]; //all, all, 0, 0

            byte[] identifyRequest = new byte[30];

            Array.Copy(destination, 0, identifyRequest, 0, 6);
            Array.Copy(source, 0, identifyRequest, 6, 6);
            Array.Copy(type, 0, identifyRequest, 12, 2);
            Array.Copy(frameID, 0, identifyRequest, 14, 2);
            Array.Copy(serviceID, 0, identifyRequest, 16, 2);
            Array.Copy(xID, 0, identifyRequest, 18, 4);
            Array.Copy(responseDelay, 0, identifyRequest, 22, 2);
            Array.Copy(dataLength, 0, identifyRequest, 24, 2);
            Array.Copy(dataBlock, 0, identifyRequest, 26, 4);

            return identifyRequest;
        }

        public static byte[] GetIpRequest(string destinationMac, string sourceMac, string ipAddress, string subNetzMaske, string gateway)
        {
            byte[] destination = PhysicalAddress.Parse(destinationMac).GetAddressBytes();

            byte[] source = PhysicalAddress.Parse(address: sourceMac).GetAddressBytes();

            byte[] type = [0x88, 0x92];

            byte[] frameID = [0xfe, 0xfd];

            byte[] serviceID = [0x04, 0x00]; //erstes byte ist typ, 4 ist set bei SICK

            byte[] xID = [0x0, 0x0, 0x0, 0x71];

            byte[] reserved = [0x00, 0x0];

            byte[] dataLength = [0x00, 0x12];

            byte[] dataBlock = new byte[18];
            dataBlock[0] = 0x01; //option
            dataBlock[1] = 0x02; //suboption
            dataBlock[2] = 0x0; //data block length
            dataBlock[3] = 0x0e;

            dataBlock[4] = 0x00; //blockqualifier permanent speichern
            dataBlock[5] = 0x01;
            IPAddress.Parse(ipAddress).GetAddressBytes().CopyTo(dataBlock, 6);
            IPAddress.Parse(subNetzMaske).GetAddressBytes().CopyTo(dataBlock, 10);
            IPAddress.Parse(gateway).GetAddressBytes().CopyTo(dataBlock, 14);

            byte[] ipRequest = new byte[destination.Length + source.Length + type.Length + frameID.Length + serviceID.Length + xID.Length + reserved.Length + dataLength.Length + dataBlock.Length];

            Array.Copy(destination, 0, ipRequest, 0, 6);
            Array.Copy(source, 0, ipRequest, 6, 6);
            Array.Copy(type, 0, ipRequest, 12, 2);
            Array.Copy(frameID, 0, ipRequest, 14, 2);
            Array.Copy(serviceID, 0, ipRequest, 16, 2);
            Array.Copy(xID, 0, ipRequest, 18, 4);
            Array.Copy(reserved, 0, ipRequest, 22, 2);
            Array.Copy(dataLength, 0, ipRequest, 24, 2);
            Array.Copy(dataBlock, 0, ipRequest, 26, 18);

            return ipRequest;
        }

        private static readonly string MulticastMACAdd_Identify_Address = "01-0E-CF-00-00-00";

        public static string MulticastAddress()
        {
            return MulticastMACAdd_Identify_Address;
        }

        public static byte[] MulticastBytes()
        {
            byte[] MACAddr = new byte[6];
            string[] destinationString = MulticastMACAdd_Identify_Address.Split('-');
            for (int j = 0; j < 6; j++)
            {
                MACAddr[j] = Convert.ToByte(destinationString[j], 16);
            }
            return MACAddr;
        }

        public enum ServiceIds : ushort
        {
            Get_Request = 0x0300,
            Get_Response = 0x0301,
            Set_Request = 0x0400,
            Set_Response = 0x0401,
            Identify_Request = 0x0500,
            Identify_Response = 0x0501,
            Hello_Request = 0x0600,
            ServiceIDNotSupported = 0x0004,
        }

        public enum BlockOptions : ushort
        {
            //IP
            IP_MACAddress = 0x0101,
            IP_IPParameter = 0x0102,
            IP_FullIPSuite = 0x0103,

            //DeviceProperties
            DeviceProperties_DeviceVendor = 0x0201,
            DeviceProperties_NameOfStation = 0x0202,
            DeviceProperties_DeviceID = 0x0203,
            DeviceProperties_DeviceRole = 0x0204,
            DeviceProperties_DeviceOptions = 0x0205,
            DeviceProperties_AliasName = 0x0206,
            DeviceProperties_DeviceInstance = 0x0207,
            DeviceProperties_OEMDeviceID = 0x0208,

            //DHCP
            DHCP_HostName = 0x030C,
            DHCP_VendorSpecificInformation = 0x032B,
            DHCP_ServerIdentifier = 0x0336,
            DHCP_ParameterRequestList = 0x0337,
            DHCP_ClassIdentifier = 0x033C,
            DHCP_DHCPClientIdentifier = 0x033D,
            DHCP_FullyQualifiedDomainName = 0x0351,
            DHCP_UUIDClientIdentifier = 0x0361,
            DHCP_DHCP = 0x03FF,

            //Control
            Control_Start = 0x0501,
            Control_Stop = 0x0502,
            Control_Signal = 0x0503,
            Control_Response = 0x0504,
            Control_FactoryReset = 0x0505,
            Control_ResetToFactory = 0x0506,

            //DeviceInitiative
            DeviceInitiative_DeviceInitiative = 0x0601,

            //AllSelector
            AllSelector_AllSelector = 0xFFFF,
        }

        public enum BlockQualifiers : ushort
        {
            Temporary = 0,
            Permanent = 1,

            ResetApplicationData = 2,
            ResetCommunicationParameter = 4,
            ResetEngineeringParameter = 6,
            ResetAllStoredData = 8,
            ResetDevice = 16,
            ResetAndRestoreData = 18,
        }

        [Flags]
        public enum BlockInfo : ushort
        {
            IpSet = 1,
            IpSetViaDhcp = 2,
            IpConflict = 0x80,
        }

        [Flags]
        public enum DeviceRoles : byte
        {
            Device = 1,
            Controller = 2,
            Multidevice = 4,
            Supervisor = 8,
        }

        public enum BlockErrors : byte
        {
            NoError = 0,
            OptionNotSupported = 1,
            SuboptionNotSupported = 2,
            SuboptionNotSet = 3,
            ResourceError = 4,
            SetNotPossible = 5,
            Busy = 6,
        }
    }
}