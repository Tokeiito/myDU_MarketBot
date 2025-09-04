using System;
using NQutils.Config;

public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine($"Command-line args: {string.Join(", ", args)}");

        try
        {
            // Load configuration from YAML
            Config.ReadYamlFileFromArgs("mod", args);
        }
        catch (Exception e)
        {
            Console.WriteLine($"{e}\n{e.StackTrace}");
            return;
        }

        Mod.Setup(args[1]).Wait();
        var marketBot = new ModMarketBot();
        marketBot.Start().Wait();
    }
}
