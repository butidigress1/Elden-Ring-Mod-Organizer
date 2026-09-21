using EldenRingOrganizer.SmithboxIntegration;
using EldenRingOrganizer.Storage;

namespace EldenRingOrganizer.Semantic;

public static class RegulationIndexCommand
{
    public const string CommandName = "--index-regulation";

    public static bool IsCommand(string[] args)
    {
        return args.Length > 0 &&
               string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);
    }

    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 5)
            {
                Console.Error.WriteLine("Invalid semantic indexer arguments.");
                return 2;
            }

            var gameFolder = args[1];
            var modRoot = args[2];
            var output = args[3];
            var sourceName = args[4];

            var index = SmithboxRegulationIndexBuilder
                .BuildAsync(gameFolder, modRoot, sourceName)
                .GetAwaiter()
                .GetResult();

            var cache = new RegulationCacheStore(Path.GetDirectoryName(Path.GetDirectoryName(output)!)!);
            cache.WriteAtomic(output, index);

            Console.Out.WriteLine(
                $"Indexed {index.Delta.Params.Count} changed PARAMs at regulation version {index.Document.RegulationVersionDisplay}.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }
}
