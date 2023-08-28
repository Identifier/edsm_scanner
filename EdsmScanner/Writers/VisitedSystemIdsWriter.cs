﻿using System;
using System.IO;
using System.Threading.Tasks;
using EdsmScanner.Models;

namespace EdsmScanner.Writers
{
    internal class VisitedSystemIdsWriter
    {
        public async Task<string> WriteVisitedSystems(string originSystem, string? destinationSystem, SystemDetails[] systems)
        {
            var path = PathSanitizer.SanitizePath($"visited_{originSystem}{(destinationSystem != null ? $" to {destinationSystem}" : "")}.txt");
            await using var writer = new StreamWriter(path);
            int lines = 0;
            foreach (var sys in systems)
            {
                if (sys.Id64.HasValue)
                {
                    await writer.WriteLineAsync(sys.Id64.ToString());
                    ++lines;
                }
            }
            Console.WriteLine($"Generated {path} with {lines} systems.");
            return path;
        }
    }
}