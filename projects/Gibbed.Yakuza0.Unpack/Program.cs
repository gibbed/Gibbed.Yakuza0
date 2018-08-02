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
using Gibbed.IO;
using Gibbed.Yakuza0.FileFormats;
using NDesk.Options;

namespace Gibbed.Yakuza0.Unpack
{
    internal class Program
    {
        private static string GetExecutableName()
        {
            return Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        }

        public static void Main(string[] args)
        {
            bool showHelp = false;
            bool overwriteFiles = false;
            bool verbose = false;

            var options = new OptionSet()
            {
                { "o|overwrite", "overwrite existing files", v => overwriteFiles = v != null },
                { "v|verbose", "be verbose", v => verbose = v != null },
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

            if (extras.Count < 1 || extras.Count > 2 || showHelp == true)
            {
                Console.WriteLine("Usage: {0} [OPTIONS]+ input_par [output_dir]", GetExecutableName());
                Console.WriteLine();
                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                return;
            }

            var inputPath = Path.GetFullPath(extras[0]);
            var outputPath = extras.Count > 1 ? extras[1] : Path.ChangeExtension(inputPath, null) + "_unpack";

            using (var input = File.OpenRead(inputPath))
            {
                var archive = new ArchiveFile();
                archive.Deserialize(input);

                long current = 0;
                long total = archive.Entries.Count;
                var padding = total.ToString(CultureInfo.InvariantCulture).Length;

                foreach (var entry in archive.Entries)
                {
                    current++;

                    var entryPath = Path.Combine(outputPath, entry.Path);
                    if (overwriteFiles == false && File.Exists(entryPath) == true)
                    {
                        continue;
                    }

                    if (verbose == true)
                    {
                        Console.WriteLine(
                            "[{0}/{1}] {2}",
                            current.ToString(CultureInfo.InvariantCulture).PadLeft(padding),
                            total,
                            entry.Path);
                    }

                    var entryDirectory = Path.GetDirectoryName(entryPath);
                    if (entryDirectory != null)
                    {
                        Directory.CreateDirectory(entryDirectory);
                    }

                    using (var output = File.Create(entryPath))
                    {
                        input.Seek(entry.DataOffset, SeekOrigin.Begin);

                        if (entry.IsCompressed == false)
                        {
                            output.WriteFromStream(input, entry.DataCompressedSize);
                        }
                        else
                        {
                            Decompress(input, entry, output);
                        }
                    }
                }
            }
        }

        private static void Decompress(Stream input, ArchiveFile.FileEntry entry, Stream output)
        {
            const uint signature = 0x534C4C5A; // 'SLLZ'

            var magic = input.ReadValueU32(Endian.Big);
            if (magic != signature)
            {
                throw new FormatException();
            }

            var endianness = input.ReadValueU8();
            if (endianness != 0 && endianness != 1)
            {
                throw new FormatException();
            }
            var endian = endianness == 0 ? Endian.Little : Endian.Big;

            var version = input.ReadValueU8();
            if (version != 1)
            {
                throw new FormatException();
            }

            var headerSize = input.ReadValueU16(endian);
            if (headerSize != 16)
            {
                throw new FormatException();
            }

            var uncompressedSize = input.ReadValueU32(endian);
            var compressedSize = input.ReadValueU32(endian);

            if (entry.DataUncompressedSize != uncompressedSize || entry.DataCompressedSize != compressedSize)
            {
                throw new FormatException();
            }

            compressedSize -= 16; // compressed size includes SLLZ header

            var block = new byte[18];
            long compressedCount = 0;
            long uncompressedCount = 0;

            byte opFlags = input.ReadValueU8();
            compressedCount++;
            int opBits = 8;

            int literalCount = 0;
            while (compressedCount < compressedSize)
            {
                var isCopy = (opFlags & 0x80) != 0;
                opFlags <<= 1;
                opBits--;

                if (opBits == 0)
                {
                    if (literalCount > 0)
                    {
                        input.Read(block, 0, literalCount);
                        output.Write(block, 0, literalCount);
                        uncompressedCount += literalCount;
                        literalCount = 0;
                    }

                    opFlags = input.ReadValueU8();
                    compressedCount++;
                    opBits = 8;
                }

                if (isCopy == false)
                {
                    literalCount++;
                    compressedCount++;
                    continue;
                }

                if (literalCount > 0)
                {
                    input.Read(block, 0, literalCount);
                    output.Write(block, 0, literalCount);
                    uncompressedCount += literalCount;
                    literalCount = 0;
                }

                var copyFlags = input.ReadValueU16(Endian.Little);
                compressedCount += 2;

                var copyDistance = 1 + (copyFlags >> 4);
                var copyCount = 3 + (copyFlags & 0xF);

                var originalPosition = output.Position;
                output.Position = output.Length - copyDistance;
                output.Read(block, 0, copyCount);
                output.Position = originalPosition;
                output.Write(block, 0, copyCount);
                uncompressedCount += copyCount;
            }

            if (literalCount > 0)
            {
                input.Read(block, 0, literalCount);
                output.Write(block, 0, literalCount);
                uncompressedCount += literalCount;
            }

            if (uncompressedCount != uncompressedSize)
            {
                throw new InvalidOperationException();
            }
        }
    }
}
