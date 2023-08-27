using System;
using System.Linq;

namespace EdsmScanner.Models
{
    internal class SystemDetails
    {
        private SystemBody[]? _filteredBodies;
        [Queryable]
        public long? Id64 => Ref?.Id64;
        [Queryable]
        public int? DiscoveredStars => Bodies?.Count(b => b.Type.Equals("star", StringComparison.OrdinalIgnoreCase));
        [Queryable]
        public int? DiscoveredBodies => Bodies?.Length;
        [Queryable]
        public bool? IsFullyDiscovered => BodyCount > 0 && DiscoveredBodies.HasValue ? BodyCount <= DiscoveredBodies : null;

        /// <summary>
        /// Expected count
        /// </summary>
        public int? BodyCount { get; set; }
        public SystemBody[]? Bodies { get; set; }
        public string Url { get; set; } = string.Empty;
        public SystemRef? Ref { get; set; }

        public override string ToString() => $"{Ref}";
        public decimal PlottedDistance { get; set; }
        public SystemBody[] FilteredBodies => _filteredBodies ?? Bodies ?? Array.Empty<SystemBody>();

        public void ApplyBodyFilter(Func<IQueryable<SystemBody>, IQueryable<SystemBody>> filter)
        {
            _filteredBodies = filter((Bodies ?? Array.Empty<SystemBody>()).AsQueryable()).ToArray();
        }
    }
}