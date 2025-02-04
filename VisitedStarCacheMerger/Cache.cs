﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VisitedStarCacheMerger
{
    class Cache
    {
        private readonly Header _header;
        private readonly Dictionary<long, Record> _records;
        private readonly byte[] _footer;

        private Cache(Header header, Record[] records, byte[] footer)
        {
            _header = header;
            _records = records.ToDictionary(r => r.Id);
            _footer = footer;
        }

        public int Count => _records.Count;

        public static Cache Read(BinaryReader input)
        {
            var header = Header.Read(input);
            var records = Enumerable.Range(0, header.Rows).Select(_ => Record.Read(input)).ToArray();
            var footer = input.ReadBytes((int)(input.BaseStream.Length - input.BaseStream.Position));
            return new Cache(header, records, footer);
        }

        public void MergeSystemIds(IList<long> systemIds)
        {
            // Make sure the first records always take precedence if the cache file gets truncated by Elite
            var maxDate = _records.Max(r => r.Value.VisitedDate) + systemIds.Count;

            foreach (var systemId in systemIds)
            {
                if (_records.TryGetValue(systemId, out var r))
                    r.VisitedDate = maxDate--;
                else
                    _records[systemId] = new Record { Id = systemId, VisitedDate = maxDate--, Visits = 1 };
            }

            _header.UpdateRows(_records.Count);
        }

        public void MergeCaches(Cache cache)
        {
            foreach (var record in cache._records.Values)
            {
                if (_records.TryGetValue(record.Id, out var r))
                    r.VisitedDate = Math.Max(r.VisitedDate, record.VisitedDate);
                else
                    _records[record.Id] = record;
            }

            _header.UpdateRows(_records.Count);
        }

        public void Write(BinaryWriter output)
        {
            _header.Write(output);
            foreach (var record in _records.Values.OrderByDescending(v => v.VisitedDate)) //latest to win
                record.Write(output);
            output.Write(_footer);
        }
    }
}