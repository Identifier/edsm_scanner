using System;

namespace EdsmScanner.Models
{
    internal class SystemRef
    {
        public long? Id64 { get; set; }
        public decimal Distance { get; set; }
        public int? BodyCount { get; set; }
        public string Name { get; set; } = string.Empty;
        public CoordF Coords { get; set; }
        public override string ToString() => $"{Name} [{Distance}ly] ({BodyCount?.ToString() ?? "?"} bodies)";

        public override bool Equals(object? obj)
        {
            return obj is SystemRef other && Object.Equals(Id64, other.Id64);
        }

        public override int GetHashCode()
        {
            return Id64?.GetHashCode() ?? 0;
        }
    }
}