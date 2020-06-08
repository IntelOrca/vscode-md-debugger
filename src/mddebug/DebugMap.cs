using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace IntelOrca.MegaDrive.Debugger
{
    internal class DebugMap
    {
        private Dictionary<(string, int), DebugMapping> _lineMap = new Dictionary<(string, int), DebugMapping>();
        private Dictionary<uint, DebugMapping> _addressMap = new Dictionary<uint, DebugMapping>();
        private Dictionary<string, uint> _symbolMap = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        public DebugMap(string path)
        {
            using (var sr = new StreamReader(path))
            {
                Parse(sr);
            }
        }

        public DebugMapping? GetMapping(uint address)
        {
            return _addressMap.TryGetValue(address, out var result) ? result : default(DebugMapping?);
        }

        public DebugMapping? GetMapping(string path, int line)
        {
            return _lineMap.TryGetValue((path.ToLowerInvariant(), line), out var result) ? result : default(DebugMapping?);
        }

        public uint? GetSymbolAddress(string name)
        {
            return _symbolMap.TryGetValue(name, out var result) ? result : (uint?)null;
        }

        private void Parse(TextReader textReader)
        {
            var fileRegex = new Regex(@"File\s(.+)", RegexOptions.Compiled);
            var mappingRegex = new Regex(@"(\d+):([0-9A-F]+)", RegexOptions.Compiled);
            var symbolRegex = new Regex(@"^(\S+)\s+\S+\s+([0-9A-Fa-f]+)\s+\S+\s+([0-9]+)", RegexOptions.Compiled);

            string currentFile = null;
            var mappings = new List<DebugMapping>();

            // Read address mappings
            string line;
            while ((line = textReader.ReadLine()) != null)
            {
                if (line.StartsWith("Symbols"))
                {
                    break;
                }

                var matches = fileRegex.Matches(line);
                if (matches.Count > 0)
                {
                    currentFile = matches[0].Groups[1].Value;
                    continue;
                }

                matches = mappingRegex.Matches(line);
                if (matches.Count > 0)
                {
                    foreach (Match match in matches)
                    {
                        var lineNumber = int.Parse(match.Groups[1].Value);
                        var addr = Convert.ToUInt32(match.Groups[2].Value, 16);
                        mappings.Add(new DebugMapping(currentFile, lineNumber, addr));
                    }
                    continue;
                }
            }

            foreach (var mapping in mappings)
            {
                if (!_addressMap.ContainsKey(mapping.Address))
                {
                    _lineMap[(mapping.Path.ToLowerInvariant(), mapping.Line)] = mapping;
                    _addressMap[mapping.Address] = mapping;
                }
            }

            // Read symbol mappings
            while ((line = textReader.ReadLine()) != null)
            {
                var match = symbolRegex.Match(line);
                if (match.Success)
                {
                    var name = match.Groups[1].Value;
                    var value = match.Groups[2].Value;
                    var isAddress = int.Parse(match.Groups[3].Value);
                    if (isAddress == 1)
                    {
                        var address = Convert.ToUInt64(value, 16);
                        _symbolMap[name] = (uint)(address & 0xFFFFFFFF);
                    }
                }
            }
        }
    }
}
