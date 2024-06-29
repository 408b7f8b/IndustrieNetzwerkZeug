namespace IndustrieNetzwerkZeug;
class Program
{
    static void Main(string[] args)
    {
        var netzwerk = new Profinet.ProtokollFunktionenProfinet();

        netzwerk.NetzwerkAbfragen("enx606d3cee1b70", 30000);

        System.Console.WriteLine("");
    }
}