﻿namespace EdsmScanner
{
    internal class SystemDetails
    {
        public long? Id64 { get; set; }
        public int? BodyCount { get; set; }
        public SystemBody[] Bodies { get; set; }
        public string Url { get; set; }

        public bool IsNotFullyDiscovered => BodyCount == null || BodyCount > (Bodies?.Length ?? 0);
        public SystemRef Ref { get; set; }
        public override string ToString() => $"{Ref}";
    }
}