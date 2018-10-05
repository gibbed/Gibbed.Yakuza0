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
using System.IO;
using System.Linq;
using System.Text;
using Gibbed.IO;

namespace Gibbed.Yakuza0.FileFormats
{
    public class ArchiveFile
    {
        public const uint Signature = 0x50415243; // 'PARC'

        private Endian _Endian = Endian.Little;
        private readonly List<FileEntry> _Entries;

        public ArchiveFile()
        {
            this._Entries = new List<FileEntry>();
        }

        public Endian Endian
        {
            get { return this._Endian; }
            set { this._Endian = value; }
        }

        public List<FileEntry> Entries
        {
            get { return this._Entries; }
        }

        private struct DirectoryEstimate
        {
            public int DirectoryCount;
            public int FileCount;
        }

        public static long EstimateHeaderSize(IEnumerable<string> paths)
        {
            var counts = new Dictionary<string, DirectoryEstimate>();

            DirectoryEstimate rootCount;
            rootCount.DirectoryCount = 1; // include itself
            rootCount.FileCount = 0;

            foreach (var path in paths)
            {
                var parts = path.Split('\\');

                var count = parts.Length;
                if (count <= 1)
                {
                    rootCount.FileCount++;
                    continue;
                }

                DirectoryEstimate directoryEstimate;

                string directoryPath = "";
                for (int i = 0; i < count - 2; i++)
                {
                    directoryPath += parts[i].ToLowerInvariant() + '\\';
                    counts.TryGetValue(directoryPath, out directoryEstimate);
                    directoryEstimate.DirectoryCount++;
                    counts[directoryPath] = directoryEstimate;
                }

                directoryPath += parts[count - 2].ToLowerInvariant() + '\\';
                if (counts.TryGetValue(directoryPath, out directoryEstimate) == false)
                {
                    directoryEstimate.DirectoryCount = 1; // include itself
                }
                directoryEstimate.FileCount++;
                counts[directoryPath] = directoryEstimate;
            }

            var totalPathCount = rootCount.DirectoryCount + counts.Sum(kv => kv.Value.DirectoryCount);
            var totalFileCount = rootCount.FileCount + counts.Sum(kv => kv.Value.FileCount);

            return (32) + // header
                   (64 * totalPathCount) + // directory names
                   (64 * totalFileCount) + // file names
                   (32 * totalPathCount) + // directory entries
                   (32 * totalFileCount); // file entries
        }

        private class NewDirectoryEntry
        {
            private string _Name;
            private readonly Dictionary<string, NewDirectoryEntry> _Subdirectories;
            private readonly Dictionary<string, KeyValuePair<string, FileEntry>> _Files;

            public NewDirectoryEntry()
            {
                this._Subdirectories = new Dictionary<string, NewDirectoryEntry>();
                this._Files = new Dictionary<string, KeyValuePair<string, FileEntry>>();
            }

            public string Name
            {
                get { return this._Name; }
                set { this._Name = value; }
            }

            public Dictionary<string, NewDirectoryEntry> Subdirectories
            {
                get { return this._Subdirectories; }
            }

            public Dictionary<string, KeyValuePair<string, FileEntry>> Files
            {
                get { return this._Files; }
            }
        }

        public void Serialize(Stream output)
        {
            var rootDirectoryEntry = new NewDirectoryEntry();

            foreach (var fileEntry in this._Entries)
            {
                var directoryEntry = rootDirectoryEntry;

                var parts = fileEntry.Path.Split('\\');
                var partCount = parts.Length;
                if (partCount <= 0)
                {
                    continue;
                }

                if (partCount >= 2)
                {
                    for (int i = 0; i < partCount - 1; i++)
                    {
                        var subdirectoryKey = parts[i].ToLowerInvariant();

                        NewDirectoryEntry subdirectory;
                        if (directoryEntry.Subdirectories.TryGetValue(subdirectoryKey, out subdirectory) == false)
                        {
                            subdirectory = directoryEntry.Subdirectories[subdirectoryKey] = new NewDirectoryEntry()
                            {
                                Name = parts[i],
                            };
                        }
                        directoryEntry = subdirectory;
                    }
                }

                var fileName = parts[partCount - 1];
                var fileKey = fileName.ToLowerInvariant();
                directoryEntry.Files.Add(fileKey, new KeyValuePair<string, FileEntry>(fileName, fileEntry));
            }

            var directoryNames = new List<string>();
            var fileNames = new List<string>();
            var rawDirectoryEntries = new List<RawDirectoryEntry>();
            var rawFileEntries = new List<RawFileEntry>();

            var queue = new Queue<KeyValuePair<string, NewDirectoryEntry>>();
            queue.Enqueue(new KeyValuePair<string, NewDirectoryEntry>(".", rootDirectoryEntry));

            while (queue.Count > 0)
            {
                var pair = queue.Dequeue();
                var directoryName = pair.Key;
                var directoryEntry = pair.Value;

                RawDirectoryEntry rawDirectoryEntry;
                rawDirectoryEntry.DirectoryIndex = rawDirectoryEntries.Count + 1;
                rawDirectoryEntry.DirectoryCount = directoryEntry.Subdirectories.Count;
                rawDirectoryEntry.FileIndex = rawFileEntries.Count;
                rawDirectoryEntry.FileCount = directoryEntry.Files.Count;
                rawDirectoryEntry.Unknown10 = 0;
                rawDirectoryEntry.Unknown14 = 0;
                rawDirectoryEntry.Unknown18 = 0;
                rawDirectoryEntry.Unknown1C = 0;

                rawDirectoryEntries.Add(rawDirectoryEntry);
                directoryNames.Add(directoryName);

                foreach (var kv in directoryEntry.Subdirectories)
                {
                    queue.Enqueue(kv);
                }

                foreach (var kv in directoryEntry.Files.Values)
                {
                    var fileName = kv.Key;
                    var fileEntry = kv.Value;

                    RawFileEntry rawFileEntry;
                    rawFileEntry.CompressionFlags = fileEntry.IsCompressed == true ? 0x80000000u : 0u;
                    rawFileEntry.DataUncompressedSize = fileEntry.DataUncompressedSize;
                    rawFileEntry.DataCompressedSize = fileEntry.DataCompressedSize;
                    rawFileEntry.DataOffsetLo = (uint)fileEntry.DataOffset;
                    rawFileEntry.Unknown10 = 0;
                    rawFileEntry.Unknown14 = 0;
                    rawFileEntry.Unknown18 = 0;
                    rawFileEntry.Unknown1C = 0;
                    rawFileEntry.DataOffsetHi = (byte)((fileEntry.DataOffset >> 32) & 0xFu);

                    rawFileEntries.Add(rawFileEntry);
                    fileNames.Add(fileName);
                }
            }

            var endian = Endian.Big;

            byte[] directoryTableBytes;
            using (var temp = new MemoryStream())
            {
                foreach (var rawDirectoryEntry in rawDirectoryEntries)
                {
                    rawDirectoryEntry.Write(temp, endian);
                }
                temp.Flush();
                directoryTableBytes = temp.ToArray();
            }

            byte[] fileTableBytes;
            using (var temp = new MemoryStream())
            {
                foreach (var rawFileEntry in rawFileEntries)
                {
                    rawFileEntry.Write(temp, endian);
                }
                temp.Flush();
                fileTableBytes = temp.ToArray();
            }

            var endianness = GetEndianness(endian);

            var headerSize = 32;
            headerSize += 64 * rawDirectoryEntries.Count;
            headerSize += 64 * rawFileEntries.Count;

            output.WriteValueU32(Signature, Endian.Big);
            output.WriteValueU8(2);
            output.WriteValueU8(endianness);
            output.WriteValueU8(0);
            output.WriteValueU8(0);
            output.WriteValueU32(0x00020001u, endian);
            output.WriteValueU32(0, endian);
            output.WriteValueS32(rawDirectoryEntries.Count, endian);
            output.WriteValueS32(headerSize, endian);
            output.WriteValueS32(rawFileEntries.Count, endian);
            output.WriteValueS32(headerSize + directoryTableBytes.Length, endian);

            foreach (var directoryName in directoryNames)
            {
                output.WriteString(directoryName, 64, Encoding.ASCII);
            }

            foreach (var fileName in fileNames)
            {
                output.WriteString(fileName, 64, Encoding.ASCII);
            }

            output.WriteBytes(directoryTableBytes);
            output.WriteBytes(fileTableBytes);
        }

        private static byte GetEndianness(Endian endian)
        {
            switch (endian)
            {
                case Endian.Little:
                {
                    return 0;
                }

                case Endian.Big:
                {
                    return 1;
                }
            }

            throw new NotSupportedException();
        }

        public void Deserialize(Stream input)
        {
            var basePosition = input.Position;

            var magic = input.ReadValueU32(Endian.Big);
            if (magic != Signature)
            {
                throw new FormatException();
            }

            var version = input.ReadValueU8();
            if (version != 2)
            {
                throw new FormatException();
            }

            var endianness = input.ReadValueU8();
            if (endianness != 0 && endianness != 1)
            {
                throw new FormatException();
            }
            var endian = endianness == 0 ? Endian.Little : Endian.Big;

            var unknown06 = input.ReadValueU8();
            var unknown07 = input.ReadValueU8();
            if (unknown06 != 0 || unknown07 != 0)
            {
                throw new FormatException();
            }

            var unknown08 = input.ReadValueU32(endian);
            if (unknown08 != 0x00020001u)
            {
                throw new FormatException();
            }

            var unknown0C = input.ReadValueU32(endian);
            if (unknown0C != 0)
            {
                throw new FormatException();
            }

            var directoryCount = input.ReadValueU32(endian);
            var directoryTableOffset = input.ReadValueU32(endian);
            var fileCount = input.ReadValueU32(endian);
            var fileTableOffset = input.ReadValueU32(endian);

            var directoryNames = new string[directoryCount];
            for (uint i = 0; i < directoryCount; i++)
            {
                directoryNames[i] = input.ReadString(64, true, Encoding.ASCII);
            }

            var fileNames = new string[fileCount];
            for (uint i = 0; i < fileCount; i++)
            {
                fileNames[i] = input.ReadString(64, true, Encoding.ASCII);
            }

            var rawDirectoryEntries = new RawDirectoryEntry[directoryCount];
            if (directoryCount > 0)
            {
                input.Position = basePosition + directoryTableOffset;
                for (uint i = 0; i < directoryCount; i++)
                {
                    var directoryEntry = rawDirectoryEntries[i] = RawDirectoryEntry.Read(input, endian);
                    if (directoryEntry.Unknown14 != 0 || directoryEntry.Unknown18 != 0 || directoryEntry.Unknown1C != 0)
                    {
                        throw new FormatException();
                    }
                }
            }

            var rawFileEntries = new RawFileEntry[fileCount];
            if (fileCount > 0)
            {
                input.Position = basePosition + fileTableOffset;
                for (uint i = 0; i < fileCount; i++)
                {
                    var fileEntry = rawFileEntries[i] = RawFileEntry.Read(input, endian);
                    if ((fileEntry.CompressionFlags & 0x7FFFFFFFu) != 0)
                    {
                        throw new FormatException();
                    }
                    if (fileEntry.Unknown14 != 0 || fileEntry.Unknown18 != 0)
                    {
                        throw new FormatException();
                    }
                }
            }

            var fileEntries = new List<FileEntry>();

            if (directoryCount > 0)
            {
                var queue = new Queue<KeyValuePair<int, string>>();
                queue.Enqueue(new KeyValuePair<int, string>(0, null));

                while (queue.Count > 0)
                {
                    var directoryInfo = queue.Dequeue();
                    var directoryIndex = directoryInfo.Key;
                    var rawDirectoryEntry = rawDirectoryEntries[directoryIndex];
                    var directoryName = directoryNames[directoryIndex];
                    var isRootDirectory = directoryIndex == 0 && directoryName == ".";
                    var directoryBasePath = directoryInfo.Value;
                    var directoryPath = directoryBasePath == null
                                            ? isRootDirectory == true
                                                  ? null
                                                  : directoryName
                                            : isRootDirectory == true
                                                  ? directoryBasePath
                                                  : Path.Combine(directoryBasePath, directoryName);

                    for (int i = 0, o = rawDirectoryEntry.DirectoryIndex;
                        i < rawDirectoryEntry.DirectoryCount;
                        i++, o++)
                    {
                        queue.Enqueue(new KeyValuePair<int, string>(o, directoryPath));
                    }

                    for (int i = 0, o = rawDirectoryEntry.FileIndex; i < rawDirectoryEntry.FileCount; i++, o++)
                    {
                        var rawFileEntry = rawFileEntries[o];
                        FileEntry fileEntry;
                        fileEntry.Path = directoryPath == null
                                             ? fileNames[o]
                                             : Path.Combine(directoryPath, fileNames[o]);
                        fileEntry.IsCompressed = ((FileFlags)rawFileEntry.CompressionFlags & FileFlags.IsCompressed) !=
                                                 0;
                        fileEntry.DataUncompressedSize = rawFileEntry.DataUncompressedSize;
                        fileEntry.DataCompressedSize = rawFileEntry.DataCompressedSize;
                        fileEntry.DataOffset =
                            (long)(rawFileEntry.DataOffsetLo) << 0 |
                            (long)(rawFileEntry.DataOffsetHi) << 32;
                        fileEntries.Add(fileEntry);
                    }
                }
            }
            else
            {
                // no directories but there's a file?
                if (fileCount > 0)
                {
                    throw new FormatException();
                }
            }

            this._Endian = endian;
            this._Entries.Clear();
            this._Entries.AddRange(fileEntries);
        }

        [Flags]
        public enum FileFlags : uint
        {
            None = 0u,
            IsCompressed = 1u << 31,
        }

        private struct RawDirectoryEntry
        {
            public int DirectoryCount;
            public int DirectoryIndex;
            public int FileCount;
            public int FileIndex;
            public uint Unknown10;
            public uint Unknown14;
            public uint Unknown18;
            public uint Unknown1C;

            public static RawDirectoryEntry Read(Stream input, Endian endian)
            {
                RawDirectoryEntry instance;
                instance.DirectoryCount = input.ReadValueS32(endian);
                instance.DirectoryIndex = input.ReadValueS32(endian);
                instance.FileCount = input.ReadValueS32(endian);
                instance.FileIndex = input.ReadValueS32(endian);
                instance.Unknown10 = input.ReadValueU32(endian);
                instance.Unknown14 = input.ReadValueU32(endian);
                instance.Unknown18 = input.ReadValueU32(endian);
                instance.Unknown1C = input.ReadValueU32(endian);
                return instance;
            }

            public static void Write(Stream output, RawDirectoryEntry instance, Endian endian)
            {
                output.WriteValueS32(instance.DirectoryCount, endian);
                output.WriteValueS32(instance.DirectoryIndex, endian);
                output.WriteValueS32(instance.FileCount, endian);
                output.WriteValueS32(instance.FileIndex, endian);
                output.WriteValueU32(instance.Unknown10, endian);
                output.WriteValueU32(instance.Unknown14, endian);
                output.WriteValueU32(instance.Unknown18, endian);
                output.WriteValueU32(instance.Unknown1C, endian);
            }

            public void Write(Stream output, Endian endian)
            {
                Write(output, this, endian);
            }

            public override string ToString()
            {
                return string.Format(
                    "dcount={0}, dindex={1}, fcount={2}, findex={3}, {4:X}, {5:X}, {6:X}, {7:X}",
                    this.DirectoryCount,
                    this.DirectoryIndex,
                    this.FileCount,
                    this.FileIndex,
                    this.Unknown10,
                    this.Unknown14,
                    this.Unknown18,
                    this.Unknown1C);
            }
        }

        private struct RawFileEntry
        {
            public uint CompressionFlags;
            public uint DataUncompressedSize;
            public uint DataCompressedSize;
            public uint DataOffsetLo;
            public uint Unknown10; // probably header size?
            public uint Unknown14;
            public uint Unknown18;
            public uint Unknown1C; // probably data hash?

            public byte DataOffsetHi
            {
                get { return (byte)(this.Unknown14 & 0xFu); }
                set
                {
                    this.Unknown14 &= ~0xFu;
                    this.Unknown14 |= value & 0xFu;
                }
            }

            public static RawFileEntry Read(Stream input, Endian endian)
            {
                RawFileEntry instance;
                instance.CompressionFlags = input.ReadValueU32(endian);
                instance.DataUncompressedSize = input.ReadValueU32(endian);
                instance.DataCompressedSize = input.ReadValueU32(endian);
                instance.DataOffsetLo = input.ReadValueU32(endian);
                instance.Unknown10 = input.ReadValueU32(endian);
                instance.Unknown14 = input.ReadValueU32(endian);
                instance.Unknown18 = input.ReadValueU32(endian);
                instance.Unknown1C = input.ReadValueU32(endian);
                return instance;
            }

            public static void Write(Stream output, RawFileEntry instance, Endian endian)
            {
                output.WriteValueU32(instance.CompressionFlags, endian);
                output.WriteValueU32(instance.DataUncompressedSize, endian);
                output.WriteValueU32(instance.DataCompressedSize, endian);
                output.WriteValueU32(instance.DataOffsetLo, endian);
                output.WriteValueU32(instance.Unknown10, endian);
                output.WriteValueU32(instance.Unknown14, endian);
                output.WriteValueU32(instance.Unknown18, endian);
                output.WriteValueU32(instance.Unknown1C, endian);
            }

            public void Write(Stream output, Endian endian)
            {
                Write(output, this, endian);
            }

            public override string ToString()
            {
                return string.Format(
                    "flags={0:X}, usize={1:X}, csize={2:X}, offset={3:X}, {4:X}, {5:X}, {6:X}, {7:X}",
                    this.CompressionFlags,
                    this.DataUncompressedSize,
                    this.DataCompressedSize,
                    this.DataOffsetLo,
                    this.Unknown10,
                    this.Unknown14,
                    this.Unknown18,
                    this.Unknown1C);
            }
        }

        public struct FileEntry
        {
            public string Path;
            public bool IsCompressed;
            public uint DataUncompressedSize;
            public uint DataCompressedSize;
            public long DataOffset;

            public override string ToString()
            {
                return this.Path ?? base.ToString();
            }
        }
    }
}
