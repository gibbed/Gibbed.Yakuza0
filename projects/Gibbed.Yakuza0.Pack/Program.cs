/* Copyright (c) 2018 Rick (rick 'at' gibbed 'dot' us)
 * 
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 * 
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 * 
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 * 
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 * 
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Gibbed.IO;
using Gibbed.Yakuza0.FileFormats;
using NDesk.Options;

namespace Gibbed.Yakuza0.Pack
{
    internal class Program
    {
        private static string GetExecutableName()
        {
            return Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        }

        private static void GetOptionValue<T>(ref T value, string v, T newValue)
        {
            if (v == null)
            {
                return;
            }
            value = newValue;
        }

        private static void Main(string[] args)
        {
            Endian endian = Endian.Little;
            bool showHelp = false;
            bool verbose = false;

            var options = new OptionSet()
            {
                { "v|verbose", "be verbose", v => verbose = v != null },
                { "l|little-endian", "little-endian mode (default)", v => GetOptionValue(ref endian, v, Endian.Little) },
                { "b|big-endian", "big-endian mode", v => GetOptionValue(ref endian, v, Endian.Big) },
                { "h|help", "show this message and exit", v => showHelp = v != null },
            };

            List<string> extras;

            try
            {
                extras = options.Parse(args);
            }
            catch (OptionException e)
            {
                Console.Write("{0}: ", GetExecutableName());
                Console.WriteLine(e.Message);
                Console.WriteLine("Try `{0} --help' for more information.", GetExecutableName());
                return;
            }

            if (extras.Count < 1 || showHelp == true)
            {
                Console.WriteLine("Usage: {0} [OPTIONS]+ output_par input_directory+", GetExecutableName());
                Console.WriteLine();
                Console.WriteLine("Pack files from input directories into a PARC file.");
                Console.WriteLine();
                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                return;
            }

            var inputPaths = new List<string>();
            string outputPath;

            if (extras.Count == 1)
            {
                inputPaths.Add(extras[0]);
                outputPath = extras[0] + ".par";
            }
            else
            {
                outputPath = extras[0];
                inputPaths.AddRange(extras.Skip(1));
            }

            var pendingEntries = new SortedDictionary<string, string>(ArchiveFile.NameComparer);

            if (verbose == true)
            {
                Console.WriteLine("Finding files...");
            }

            foreach (var relativePath in inputPaths)
            {
                string inputPath = Path.GetFullPath(relativePath);

                if (inputPath.EndsWith(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) == true)
                {
                    inputPath = inputPath.Substring(0, inputPath.Length - 1);
                }

                foreach (string path in Directory.GetFiles(inputPath, "*", SearchOption.AllDirectories))
                {
                    string fullPath = Path.GetFullPath(path);
                    string partPath = fullPath.Substring(inputPath.Length + 1)
                                              .Replace(Path.DirectorySeparatorChar, '\\')
                                              .Replace(Path.AltDirectorySeparatorChar, '\\');

                    var partKey = partPath.ToLowerInvariant();

                    string fullPathPrevious;
                    if (pendingEntries.TryGetValue(partKey, out fullPathPrevious) == true)
                    {
                        if (verbose == true)
                        {
                            Console.WriteLine("Ignoring duplicate of {0}:", partKey);
                            Console.WriteLine("  Previously added from: {0}", fullPathPrevious);
                        }
                        else
                        {
                            Console.WriteLine("Ignoring duplicate of {0}!", partKey);
                        }
                        continue;
                    }

                    pendingEntries[partKey] = fullPath;
                }
            }

            var archive = new ArchiveFile()
            {
                Endian = endian,
            };

            using (var output = File.Create(outputPath))
            {
                var headerSize = ArchiveFile.EstimateHeaderSize(pendingEntries.Keys);

                output.Position = headerSize;

                long current = 0;
                long total = pendingEntries.Count;
                var padding = total.ToString(CultureInfo.InvariantCulture).Length;

                foreach (var kv in pendingEntries)
                {
                    var partPath = kv.Key;
                    var fullPath = kv.Value;

                    current++;

                    if (verbose == true)
                    {
                        Console.WriteLine(
                            "[{0}/{1}] {2}",
                            current.ToString(CultureInfo.InvariantCulture).PadLeft(padding),
                            total,
                            partPath);
                    }

                    using (var input = File.OpenRead(fullPath))
                    {
                        var dataSize = (uint)input.Length;

                        output.Position = output.Position.Align(ArchiveFile.Alignment);

                        if (output.Position > 0xfFFFFFFFFL)
                        {
                            throw new InvalidOperationException("unsupported data offset");
                        }

                        ArchiveFile.FileEntry fileEntry;
                        fileEntry.Path = partPath;
                        fileEntry.IsCompressed = false;
                        fileEntry.DataUncompressedSize = dataSize;
                        fileEntry.DataCompressedSize = dataSize;
                        fileEntry.DataOffset = output.Position;
                        archive.Entries.Add(fileEntry);

                        if (dataSize > 0)
                        {
                            output.WriteFromStream(input, dataSize);
                        }
                    }
                }

                output.SetLength(output.Position.Align(ArchiveFile.Alignment)); // pad file ending

                output.Position = 0;
                archive.Serialize(output);

                if (output.Position != headerSize)
                {
                    throw new InvalidOperationException("header estimation mismatch");
                }
            }
        }
    }
}
