using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using EdsmScanner.Clients;
using EdsmScanner.Models;

namespace EdsmScanner.Search
{
    internal class SystemResolver
    {
        private readonly SemaphoreSlim _throttler = new SemaphoreSlim(5);
        private readonly EdsmClient _client;

        public SystemResolver(EdsmClient client)
        {
            _client = client;
        }

        public async Task<SystemDetails[]> GetSystemsDetails(SystemRef[] systems)
        {
            Console.WriteLine("Getting systems details...");
            var notifier = new ProgressNotifier($"  Scanned systems: {{0}}/{systems.Length}");
            try
            {
                return await Task.WhenAll(systems.Select(r => GetSystemDetails(r, notifier)));
            }
            finally
            {
                notifier.Finish();
            }
        }

        private async Task<SystemDetails> GetSystemDetails(SystemRef sys, ProgressNotifier notifier)
        {
            await _throttler.WaitAsync();
            try
            {
                return await _client.GetDetails(sys);
            }
            finally
            {
                notifier.NotifyIncrease();
                _throttler.Release();
            }
        }

        public async Task<SystemRef[]> SearchForSystems(string originSystem, string? destinationSystem, int radius, int max)
        {
            Console.Write($"Searching for systems within a {radius}ly distance from {originSystem}{(destinationSystem != null ? $" to {destinationSystem}" : "")}: ");
            var systems = new HashSet<SystemRef>(await _client.SearchSystems(originSystem, radius));
            Console.WriteLine($"{systems.Count:N0} systems found.");

            // Get the coordinates of the origin system
            var origin = systems.FirstOrDefault(s => s.Name.Equals(originSystem, StringComparison.OrdinalIgnoreCase));
            if (origin == null)
                throw new Exception($"Unable to find system {originSystem} in search results.");

            if (destinationSystem != null)
            {
                // Get the coordinates of the destination system
                var destinationSystems = await _client.SearchSystems(destinationSystem, radius);
                var destination = destinationSystems.First(s => s.Name.Equals(destinationSystem, StringComparison.OrdinalIgnoreCase));
                if (destination == null)
                    throw new Exception($"Unable to find system {destinationSystem} in search results.");

                // For each sphere of systems returned, if the volume doesn't include the destination system then find the one closest system to the destination and continue from there.
                var spheres = new List<(SystemRef center, SystemRef[] systems)> { (origin, systems.ToArray()) };

                // Sometimes we need to increase our search radius from the user-specified one in order to find a path between two systems.
                var searchRadius = radius;

                while (true)
                {
                    // Stop if we've already reached our limit.
                    if (systems.Count >= max)
                    {
                        Console.WriteLine($"Reached maximum of {max} systems.");
                        break;
                    }

                    // Stop if we've reached our destination.
                    if (SphereContains(spheres.Last().center, radius, destination)) // .Last() is O(1) on List<T>
                    {
                        Console.WriteLine("We've reached our destination's space.");
                        systems.UnionWith(destinationSystems);
                        break;
                    }

                    // Find the last sphere's closest system to the destination system.
                    var next = spheres.Last().systems.OrderBy(s => s.Coords.Distance(destination.Coords)).First();

                    // Make sure we haven't already searched from here, meaning our last search didn't return any system closer to our destination.
                    if (spheres.Any(s => s.center.Name.Equals(next.Name, StringComparison.OrdinalIgnoreCase)))
                        if (searchRadius < 100)
                            searchRadius = 100; // Search for the maximum of 100ly instead of whatever the user-specified radius is.
                        else
                            throw new InvalidOperationException($"Stuck when finding a route between the two systems using a search radius of {radius}ly");
                    else
                        searchRadius = radius; // We have a good new starting point now, so reset the search radius to the user-specified one.

                    Console.Write($"{originSystem} " + $"-{DistanceBetween(origin, next):N0}ly-->".PadLeft(12, '-') + $" {next.Name} ({DistanceToRoute(origin, destination, next):N0}ly dev) ".PadRight(35) + $"-{DistanceBetween(next, destination),5:N0}ly-->".PadLeft(12, '-') + $" {destinationSystem}: ");

                    // Grab a new set of search results from here
                    var results = await _client.SearchSystems(next.Name, searchRadius);
                    if (results == null || results.Length == 0)
                        throw new InvalidOperationException($"Unable to find a route from {next.Name} to {destinationSystem} using a search radius of {searchRadius}ly");

                    spheres.Add((next, results));

                    var oldCount = systems.Count;
                    systems.UnionWith(results);

                    Console.WriteLine($"{systems.Count - oldCount:N0} new systems found.");
                }

                Console.WriteLine($"Total systems found: {systems.Count:N0}.");
#if DEBUG
                var path = Writers.PathSanitizer.SanitizePath($"visited_{originSystem}{(destinationSystem != null ? $" to {destinationSystem}" : "")}.debug.txt");
                await using var writer = new System.IO.StreamWriter(path);
                foreach (var sys in systems.OrderBy(s => DistanceToRoute(origin, destination, s)).ToArray())
                {
                    await writer.WriteLineAsync($"{sys.Id64,-25}\t{sys.Name,-35}\t{DistanceBetween(origin, sys),6:N2}ly from origin\t{sys.Distance,6:N2}ly from search\t{SystemResolver.DistanceToRoute(origin, destination, sys),6:N2}ly from route");
                }
#endif
                // Make the systems' .Distance property reflect the distance from the origin, not the distance from their EDSM search.
                systems.Select(s => s.Distance = (decimal)DistanceBetween(origin, s));

                // Return the systems sorted by distance from the route line so that one can simply truncate the list to get the closest systems along the route.
                return systems.OrderBy(s => DistanceToRoute(origin, destination, s)).ToArray();
            }

            // Return the systems sorted by distance from the origin.
            return systems.OrderBy(s => DistanceBetween(origin, s)).ToArray();
        }

        public static bool SphereContains(SystemRef center, int radius, SystemRef target)
        {
            var c = center.Coords;
            var p = target.Coords;
            return Math.Pow(p.X - c.X, 2) + Math.Pow(p.Y - c.Y, 2) + Math.Pow(p.Z - c.Z, 2) <= Math.Pow(radius, 2);
        }

        public static double DistanceBetween(SystemRef origin, SystemRef destination)
        {
            return destination.Coords.Distance(origin.Coords);
        }

        public static double DistanceToRoute(SystemRef origin, SystemRef destination, SystemRef system)
        {
            if (system == origin || system == destination)
                return 0;

            if (origin == destination)
                return DistanceBetween(origin, system);

            // Convert CoordF to Vector3 for easier vector operations
            Vector3 vOrigin = new Vector3(origin.Coords.X, origin.Coords.Y, origin.Coords.Z);
            Vector3 vDestination = new Vector3(destination.Coords.X, destination.Coords.Y, destination.Coords.Z);
            Vector3 vSystem = new Vector3(system.Coords.X, system.Coords.Y, system.Coords.Z);

            // Calculate the route vector
            Vector3 vRoute = vDestination - vOrigin;

            // Calculate vectors from point to endpoints of line segment
            Vector3 vSystemToOrigin = vSystem - vOrigin;
            Vector3 vSystemToDestination = vSystem - vDestination;

            // Calculate the cross product of the vectors (vSystem - vOrigin) and vSystem - vDestination)
            Vector3 crossProduct = Vector3.Cross(vSystemToOrigin, vSystemToDestination);

            // Calculate the magnitude of the cross product and divide it by the magnitude of the route vector
            double distance = crossProduct.Length() / vRoute.Length();

            return distance;
        }
    }
}