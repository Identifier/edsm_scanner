﻿using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using EdsmScanner.Clients;
using EdsmScanner.Models;
using EdsmScanner.Plotting;
using EdsmScanner.Search;
using EdsmScanner.Writers;

namespace EdsmScanner
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

            var cmd = new RootCommand
            {
                new Option<int>(new []{"--scan-radius","-r"},100,"Scan radius in ly (default: 100)"),
                new Option<bool>(new []{"--plot-journey","-p"},false,"Plot journey (default: false)"),
                new Option<bool>(new []{"--include-bodies","-b"},false,"Include bodies in systems.txt (default: false)"),
                new Option<TimeSpan>(new []{"--cache-duration"},TimeSpan.FromMinutes(30),"Duration on how long system details are cached (default: 00:30:00)"),
                new Option<string[]>(new []{"--filter-body","-fb"},Array.Empty<string>,$"Body filter(s) written in form on LINQ expression like: \"{nameof(SystemBody.IsScoopable)}==true\". When applied, only the systems with at least one matching body will be returned."),
                new Option<string[]>(new []{"--filter-system","-fs"},Array.Empty<string>,$"System filter(s) written in form on LINQ expression like: \"{nameof(SystemDetails.DiscoveredStars)} > 1\"."),
                new Option<int>(new []{"--max-systems","-max"},int.MaxValue,"Maximum number of systems to output"),
                new Option<bool>(new []{"--auto-merge","-merge"},false,"Merge filtered-out systems into the game's VisitedStarsCache.dat"),
            };
            cmd.Description = "Edsm Scanner";
            cmd.AddArgument(new Argument<string>("origin-system") { Description = "Origin system name" });
            cmd.AddArgument(new Argument<string?>("destination-system", () => null) { Description = "(Optional) Destination system name" });
            cmd.Handler = CommandHandler.Create(Scan);


            var helpCmd = new Command("help", "Displays help");
            helpCmd.AddCommand(new Command("usage", "Displays usage") { Handler = CommandHandler.Create(HelpUsage) });
            helpCmd.AddCommand(new Command("filters", "Displays filters usage") { Handler = CommandHandler.Create(FiltersUsage) });

            cmd.AddCommand(helpCmd);
            return await cmd.InvokeAsync(args);
        }

        private static void FiltersUsage()
        {
            Console.WriteLine("--filter-system option allows to filter systems that match the criteria, where the following attributes can be used in the filter:");
            Console.WriteLine();
            ListQueryableProperties(typeof(SystemDetails));
            Console.WriteLine();

            Console.WriteLine("--filter-body option allows to filter systems that have at least one body matching the criteria, where the following attributes can be used in the filter:");
            Console.WriteLine();
            ListQueryableProperties(typeof(SystemBody));
            Console.WriteLine();
        }

        private static void ListQueryableProperties(Type type)
        {
            foreach (var property in type.GetProperties()
                .Where(p => p.GetCustomAttributes<QueryableAttribute>().Any()).OrderBy(x => x.Name))
            {
                var propType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                Console.WriteLine($"  {property.Name} - {propType.Name}");
            }
        }

        private static void HelpUsage()
        {
            Console.WriteLine("EdsmScanner allows to scan the nearby systems in order to find the interesting features.");
            Console.WriteLine();
            Console.WriteLine("Sample usages:");
            Console.WriteLine();
            Console.WriteLine("EdsmScanner Sol\n  Scans the systems around Sol and generates systems_Sol.txt with list of them (ordered by distance)");
            Console.WriteLine();
            Console.WriteLine("EdsmScanner Sol -p\n  Plots the journey through the systems around Sol and generates systems_Sol.txt with list of them (ordered by journey steps)");
            Console.WriteLine();
            Console.WriteLine("EdsmScanner Sol -p -b -r 100\n  Scans the systems around Sol in range of 100ly, plots the journey around and includes all discovered bodies on the systems_Sol.txt");
            Console.WriteLine();
            Console.WriteLine($"EdsmScanner \"V970 Scorpii\" -p -fs \"{nameof(SystemDetails.IsFullyDiscovered)}==false\"\n  Scans the systems around V970 Scorpii and plots the journey around ones which are not fully discovered yet. The fully discovered systems are added to visited_V970 Scorpii.txt");
            Console.WriteLine();
            Console.WriteLine($"EdsmScanner \"V970 Scorpii\" -p -b -fs \"{nameof(SystemDetails.DiscoveredStars)}>1\" -fb \"{nameof(SystemBody.SurfacePressure)}>0\" \"{nameof(SystemBody.SurfacePressure)}<0.1\"\n  Scans the systems around V970 Scorpii and plots the journey around ones with multiple stars and having planets with thin atmosphere. All not matching systems are added to visited_V970 Scorpii.txt");
            Console.WriteLine();
            Console.WriteLine("EdsmScanner \"V970 Scorpii\" -fs \"false\"\n  Scans the systems around V970 Scorpii and includes all of them in visited_V970 Scorpii.txt");
            Console.WriteLine();
            Console.WriteLine("EdsmScanner Sol -b -fb \"RingTypes.Contains(\\\"Icy\\\")\" \"ReserveLevel==\\\"Pristine\\\"\" \"DistanceToArrival<1000\"\n  Scans the systems around Sol for bodies having pristine, icy rings, located in less than 1000ls from the main star");
            Console.WriteLine();
            Console.WriteLine("EdsmScanner Sol Colonia\n  Scans the systems between Sol and Colonia and generates systems_Sol_to_Colonia.txt with list of them (ordered by distance from the straight-line route");
            Console.WriteLine();
            Console.WriteLine("EdsmScanner \"V970 Scorpii\" -fs \"false\" --auto-merge\n  Scans the systems around V970 Scorpii and includes all of them in visited_V970 Scorpii.txt and then merges them into VisitedStarsCache.dat");
            Console.WriteLine();
            Console.WriteLine("EdsmScanner help filters\n  Prints help for using filters");
            Console.WriteLine();
        }

        static async Task Scan(string originSystem, string? destinationSystem, int scanRadius, bool plotJourney, bool includeBodies, TimeSpan cacheDuration, string[] filterSystem, string[] filterBody, int max, bool merge)
        {
            using var client = new EdsmClient(new SystemCache(cacheDuration));
            var foundSystems = await new SystemResolver(client).SearchForSystems(originSystem, destinationSystem, scanRadius, max);

            // Only resolve bodies via EDSM if includeBodies is true or a non-false filter is specified
            SystemDetails[] resolvedSystems;
            SystemDetails[] filteredSystems;
            if (filterSystem.Contains("false") || filterBody.Contains("false"))
            {
                resolvedSystems = foundSystems.Select(s => new SystemDetails { Ref = s }).ToArray();
                filteredSystems = new SystemDetails[0];
            }
            else if (includeBodies || filterSystem.Any() || filterBody.Any())
            {
                resolvedSystems = await new SystemResolver(client).GetSystemsDetails(foundSystems);
                filteredSystems = new SystemFilter(filterSystem, filterBody).Filter(resolvedSystems);
            }
            else
            {
                resolvedSystems = foundSystems.Select(s => new SystemDetails { Ref = s }).ToArray();
                filteredSystems = resolvedSystems;
            }

            var remainingSystems = resolvedSystems.Except(filteredSystems).Take(max).ToArray();
            var visitedFile = await new VisitedSystemIdsWriter().WriteVisitedSystems(originSystem, destinationSystem, remainingSystems);

            var orderedPartialSystems = new SystemOrderer(filteredSystems, plotJourney).Order().Take(max).ToArray();
            await new SystemListWriter().WriteSystemList(originSystem, destinationSystem, orderedPartialSystems, includeBodies, plotJourney);

            if (merge)
            {
                // Get the most likely Elite Dangerous directory
                var localEliteDangerousData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Frontier Developments", "Elite Dangerous");
                // Look for any VisitedStarsCache.dat files
                var cacheFiles = Directory.EnumerateFiles(localEliteDangerousData, "VisitedStarsCache.dat", SearchOption.AllDirectories).ToList();
                if (cacheFiles.Count == 0)
                {
                    Console.WriteLine($"No VisitedStarsCache.dat files found in {localEliteDangerousData}");
                }
                else if (cacheFiles.Count == 1)
                {
                    await VisitedStarCacheMerger.Program.Main(new[] { cacheFiles[0], visitedFile });
                }
                else foreach (var cacheFile in cacheFiles)
                {
                    Console.Write($"Merge {visitedFile} into {cacheFile}? [y/N]: ");
                    var response = Console.ReadLine();
                    if (response?.ToLowerInvariant() == "y")
                    {
                        await VisitedStarCacheMerger.Program.Main(new[] { cacheFile, visitedFile });
                    }
                }
            }
        }
    }
}
