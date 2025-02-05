using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using ComponentAce.Compression.Libs.zlib;

namespace TiaParser.Driver
{
    public class TiaCompressedBlock
    {
        public TiaCompressedBlock() { }

        public TiaCompressedBlock(string compressedData)
        {
            CompressedData = compressedData;
        }

        public TiaCompressedBlock(string data, string offset)
        {
            DecompressedData = data;
            Offset = offset;
        }

        private string DecompressedData { get; set; }
        private string CompressedData { get; set; }
        private string Offset { get; set; }

        public void ParseData(TiaParserDriver tiaParser, int compressedDataOffset)
        {
            // Regex to detect the ZLIB header, adjust if needed for specific ZLIB formats
            Regex zlibHeaderRegex = new Regex(@"x\^", RegexOptions.None, TimeSpan.FromSeconds(10)); // Can adjust to match the header exactly, or '78 5E' in hex
            Match zlibMatch = zlibHeaderRegex.Match(CompressedData);

            if (zlibMatch.Success)
            {
                // ZLIB header found, calculate the start position
                int zlibStartIndex = zlibMatch.Index;
                int extractionStart = compressedDataOffset + zlibStartIndex;

                // Ensure enough data to extract
                if (extractionStart + CompressedData.Length > tiaParser.PlfBytes.Length)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(extractionStart), // Correct parameter name
                        "Compressed data size exceeds available bytes in PlfBytes."
                    );
                }

                // Create a byte array and copy the data including the ZLIB header
                byte[] data = new byte[CompressedData.Length];
                Buffer.BlockCopy(
                    tiaParser.PlfBytes,
                    extractionStart,
                    data,
                    0,
                    CompressedData.Length
                );

                DecompressData(data);
            }
        }

        public void DecompressData(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                throw new InvalidOperationException("No compressed data to decompress.");
            }

            try
            {
                using (var decompressedData = new MemoryStream(data))
                {
                    using (ZInputStream zlibStream = new ZInputStream(decompressedData))
                    {
                        byte[] buffer = new byte[4096];
                        int bytesRead;

                        try
                        {
                            // Read from zlibStream and write to decompressedData
                            while ((bytesRead = zlibStream.read(buffer, 0, buffer.Length)) > 0)
                            {
                                decompressedData.Write(buffer, 0, bytesRead);
                            }
                        }
                        catch
                        {
                            throw new InvalidDataException();
                        }
                    }

                    // Convert the decompressed byte array to a UTF-8 encoded string
                    byte[] decompressedBytes = decompressedData.ToArray();

                    this.DecompressedData = Encoding.UTF8.GetString(decompressedBytes);
                }
            }
            catch
            {
                this.DecompressedData = "UNDEFINED";
            }
        }

        /// <summary>
        /// Parses a compressed block from the specified PLF file using the provided regex match.
        /// It calculates the offset and size of the compressed data, extracts it, and attempts to create a
        /// <see cref="TiaCompressedBlock"/> object. Additionally, it checks for the presence of a specific
        /// block type ("Block_DB%") within the extracted data. If parsing fails, it logs an error message.
        /// </summary>
        /// <param name="plusBlockMatch">The regex match object for locating the PLUSBLOCK data.</param>
        /// <param name="dataSize">The size of the block data to process.</param>
        public void ParseCompressedBlock(Match plusBlockMatch, int dataSize, TiaParserDriver tiaParser)
        {
            try
            {
                // Calculate the compressed data offset
                int compressedDataSize = BitConverter.ToUInt16(
                    tiaParser.PlfBytes,
                    plusBlockMatch.Index + plusBlockMatch.Length + dataSize
                );

                // Extract the substring starting from after plusBlockMatch to the size of compressedDataOffset
                string compressedData = tiaParser.PlfFile.Substring(
                    plusBlockMatch.Index + plusBlockMatch.Length + dataSize,
                    compressedDataSize
                );

                int compressedDataOffset = plusBlockMatch.Index + plusBlockMatch.Length + dataSize;

                this.CompressedData = compressedData;

                ParseData(tiaParser, compressedDataOffset);
            }
            catch (Exception exception)
            {
                TiaParserDriver.Logger.Warn(exception, "FAILED PARSING COMPRESSED BLOCK");
            }
        }
    }
}
