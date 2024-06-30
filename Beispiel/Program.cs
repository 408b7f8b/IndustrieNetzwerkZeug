namespace IndustrieNetzwerkZeugBeispiel;
class Program
{
    static void Main(string[] args)
    {
        var netzwerk = new IndustrieNetzwerkZeug.Profinet.ProtokollFunktionenProfinet();

        netzwerk.NetzwerkAbfragen("eth0", 30000);

        System.Console.WriteLine("");
    }
}