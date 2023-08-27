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

        public async Task<SystemRef[]> SearchForSystems(string originSystem, string? destinationSystem, int radius)
        {
            Console.WriteLine($"Searching for systems within a {radius}ly distance from {originSystem}{(destinationSystem != null ? $" to {destinationSystem}" : "")}");
            var systems = await _client.SearchSystems(originSystem, radius);

            if (destinationSystem != null)
            {
                // Get the coordinates of the origin system
                var origin = systems.First(s => s.Name.Equals(originSystem, StringComparison.OrdinalIgnoreCase));

                // Get the coordinates of the destination system
                var lastSystems = await _client.SearchSystems(destinationSystem, radius);
                var destination = lastSystems.First(s => s.Name.Equals(destinationSystem, StringComparison.OrdinalIgnoreCase));

                Console.WriteLine($"{originSystem} and {destinationSystem} are {origin.Coords.Distance(destination.Coords):N0}ly apart.");

                // For each sphere of systems returned, if the volume doesn't include the destination system then find the one closest system to the destination and continue from there.
                var spheres = new List<(SystemRef center, SystemRef[] systems)>();
                spheres.Add((origin, systems));

                while (!SphereContains(spheres.Last().center, radius, destination)) // .Last() is O(1) on List<T>
                {
                    // Find the closest system we have to the destination system
                    var next = spheres.Last().systems.OrderBy(s => s.Coords.Distance(destination.Coords)).First();

                    Console.WriteLine($"Continuing search from {next.Name} ({next.Coords.Distance(destination.Coords):N0}ly from {destinationSystem})");

                    // Make sure we haven't already searched from here for some reason.
                    if (spheres.Any(s => next.Name.Equals(s.center.Name, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException($"Stuck when finding a route between the two systems using a search radius of {radius}ly");

                    // Grab a new set of search results from here
                    var results = await _client.SearchSystems(next.Name, radius);
                    if (results == null || results.Length == 0)
                        throw new InvalidOperationException($"Unable to find a route between the two systems using a search radius of {radius}ly");

                    spheres.Add((next, results));
                }

                // Return the full set of systems along the way, sorted by distance from the route
                spheres.Add((destination, lastSystems));

                // Sort each system by the distance from the line between originSystem and destinationSystem
                systems = spheres.SelectMany(s => s.systems).Distinct().OrderBy(s => DistanceToRoute(origin, destination, s)).ToArray();
            }

            Console.WriteLine($"  Found systems: {systems.Length}");
            return systems;
        }

        private static bool SphereContains(SystemRef center, int radius, SystemRef target)
        {
            var c = center.Coords;
            var p = target.Coords;
            return Math.Pow(p.X - c.X, 2) + Math.Pow(p.Y - c.Y, 2) + Math.Pow(p.Z - c.Z, 2) <= Math.Pow(radius, 2);
        }

        private static float DistanceToRoute(SystemRef origin, SystemRef destination, SystemRef system)
        {
            // Convert CoordF to Vector3 for easier vector operations
            var v1 = new Vector3(origin.Coords.X, origin.Coords.Y, origin.Coords.Z);
            var v2 = new Vector3(destination.Coords.X, destination.Coords.Y, destination.Coords.Z);
            var vp = new Vector3(system.Coords.X, system.Coords.Y, system.Coords.Z);

            // Calculate line vector (v2 - v1)
            var lineVec = v2 - v1;

            // Calculate the vector from one point on the line to the point in question
            var pointVec = vp - v1;

            // Project pointVec onto lineVec
            var projection = Vector3.Dot(pointVec, Vector3.Normalize(lineVec)) * Vector3.Normalize(lineVec);

            // Calculate the vector from the point in question to its projection on the line
            var toProjection = pointVec - projection;

            // The length of toProjection is the perpendicular distance from point p to the line
            return toProjection.Length();
        }
    }
}