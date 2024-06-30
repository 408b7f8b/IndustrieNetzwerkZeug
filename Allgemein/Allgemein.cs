using System;
using SharpPcap;
using System.Dynamic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using PacketDotNet.DhcpV4;
using System.Collections.Generic;
using System.Linq;

namespace IndustrieNetzwerkZeug.Allgemein
{
    public class Netzwerksystem
    {
        public string Netzwerkadapter = "";
        public string mac = "";
        public string ip = "";
        public string subnetzmaske = "";
        public string gateway = "";
        public DateTime ZeitpunktLetzterKontakt;

        public enum Type
        {
            None = 0,
            Profinet = 1
        }

        public Type typ;
    }

    public class Netzwerk
    {
        public string netzwerkSchnittstelle = "";
        public string basisIp = "";
        public string subnetzmaske = "";
    }

    public interface IProtokollFunktionen
    {
        public void NetzwerkAbfragen(string Netzwerkadapter, int AntwortDauerMs) { }
    }
}