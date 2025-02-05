using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ComponentAce.Compression.Libs.zlib;
using TiaParser.Driver;

namespace TiaParser.Driver
{
    public class TiaParserCompressed
    {
        public TiaParserCompressed()
        {
            DecompressedElementList = new List<DecompressedElement>();
        }

        public List<DecompressedElement> DecompressedElementList { get; private set; }

        public class DecompressedElement
        {
            public DecompressedElement(XDocument xmlData, int offset, int size)
            {
                XmlData = xmlData;
                Offset = offset;
                Size = size;
            }

            public XDocument XmlData { get; set; }
            public int Offset { get; set; }
            public int Size { get; set; }
        }

        /// <summary>
        /// Converts the decompressed byte array into an XML document. If XML parsing fails, it retries with a larger data segment.
        /// If the retry fails again, an error message is logged. Adds the decompressed element to the list upon success.
        /// </summary>
        /// <param name="elementName">Name of the element being processed.</param>
        /// <param name="zlibHeader">Match object representing the location of the ZLIB header in the data.</param>
        /// <param name="tiaParserCompressed">Instance where the decompressed element will be stored.</param>
        private static void DefineDecompressedElement(
            TiaParserDriver tiaParser,
            Match zlibHeader,
            string elementName,
            TiaParserCompressed tiaParserCompressed
        )
        {
            int blockSize = BitConverter.ToUInt16(tiaParser.PlfBytes, zlibHeader.Index - 2);

            byte[] segment = new byte[blockSize];

            Array.Copy(tiaParser.PlfBytes, zlibHeader.Index, segment, 0, blockSize);

            byte[] decompressedElementData = DecompressSegment(segment);

            BuildDecompressedElement(
                tiaParser,
                decompressedElementData,
                segment,
                elementName,
                zlibHeader,
                tiaParserCompressed,
                false
            );
        }

        /// <summary>
        /// Attempts to convert decompressed data into an XML document and adds it to the list of decompressed elements.
        /// If parsing fails, the method retries with a larger data segment, or logs an error if the retry also fails.
        /// </summary>
        /// <param name="decompressedElementData">Decompressed byte array to be parsed into XML.</param>
        /// <param name="segment">The data segment used for decompression.</param>
        /// <param name="elementName">The name of the element being processed.</param>
        /// <param name="zlibHeader">The location of the ZLIB header in the data.</param>
        /// <param name="tiaParserCompressed">Instance to store the decompressed elements.</param>
        /// <param name="failed">Indicates if this is a retry attempt (true if retrying, false if initial).</param>
        private static void BuildDecompressedElement(
            TiaParserDriver tiaParser,
            byte[] decompressedElementData,
            byte[] segment,
            string elementName,
            Match zlibHeader,
            TiaParserCompressed tiaParserCompressed,
            bool failed
        )
        {
            try
            {
                string xmlString = Encoding.UTF8.GetString(decompressedElementData);

                // Remove BOM if it exists
                var preamble = Encoding.UTF8.GetPreamble();
                string byteOrderMarkUtf8 = Encoding.UTF8.GetString(preamble);
                if (xmlString.StartsWith(byteOrderMarkUtf8))
                {
                    xmlString = xmlString.Remove(0, byteOrderMarkUtf8.Length);
                }

                XDocument xmlData = XDocument.Parse(xmlString);

                DecompressedElement decompressedElement = new DecompressedElement(
                    xmlData,
                    zlibHeader.Index,
                    segment.Length
                );

                tiaParserCompressed.DecompressedElementList.Add(decompressedElement);
                TiaParserDriver.Logger.Debug($"Found element - {elementName} - {zlibHeader.Index}");
            }
            catch (Exception exception) //If it was not possible to form an XML document with the extracted data, try a bigger segment
            {
                byte[] newSegment = tiaParser.PlfBytes.Skip(zlibHeader.Index).ToArray();

                decompressedElementData = DecompressSegment(newSegment);

                // most likely a part it is necessary to build the entire XML and repeat the method
                if (decompressedElementData.Length == 4096)
                {
                    PartialExtraction(
                        tiaParser,
                        decompressedElementData,
                        zlibHeader,
                        elementName,
                        segment,
                        tiaParserCompressed
                    );
                }
                else if (failed)
                {
                    TiaParserDriver.Logger.Warn(
                        exception,
                        $"INVALID XML STRUCTURE {zlibHeader.Index} - {exception.Message}"
                    );
                }
                else
                {
                    BuildDecompressedElement(
                        tiaParser,
                        decompressedElementData,
                        newSegment,
                        elementName,
                        zlibHeader,
                        tiaParserCompressed,
                        true
                    );
                }
            }
        }

        /// <summary>
        /// Handles partial decompression of data when the initial decompressed segment is incomplete(4096 bytes).
        /// Iterates through subsequent ZLIB headers to extract and decompress additional segments,
        /// then combines them to form the complete decompressed element.
        /// </summary>
        /// <param name="decompressedElementData">Initial partially decompressed byte array.</param>
        /// <param name="zlibHeader">The location of the current ZLIB header in the data.</param>
        /// <param name="elementName">The name of the element being processed.</param>
        /// <param name="originalSegment">The original data segment used for decompression.</param>
        /// <param name="tiaParserCompressed">Instance to store the decompressed elements.</param>
        private static void PartialExtraction(
            TiaParserDriver tiaParser,
            byte[] decompressedElementData,
            Match zlibHeader,
            string elementName,
            byte[] originalSegment,
            TiaParserCompressed tiaParserCompressed
        )
        {
            List<byte[]> decompressedPartialBytesList = new List<byte[]>();

            decompressedPartialBytesList.Add(decompressedElementData);

            TiaParserDriver.Logger.Debug($"FOUND PARTIAL ELEMENT");
            // Find the extraction space starting right after the current ZLIB header
            string extractionSpace = tiaParser.PlfFile.Substring(zlibHeader.Index + 1);

            // Find every subsequent ZLIB header in the extraction space
            MatchCollection newZlibHeaders = Regex.Matches(
                extractionSpace,
                @"x\^",
                RegexOptions.None,
                TimeSpan.FromSeconds(20)
            );

            // Iterate over each new ZLIB header to extract and decompress the data
            foreach (Match newZlibHeader in newZlibHeaders)
            {
                // Define the new segment as the same size as the original segment but with a new offset
                byte[] segment = new byte[originalSegment.Length];
                Array.Copy(
                    tiaParser.PlfBytes,
                    zlibHeader.Index + newZlibHeader.Index + 1,
                    segment,
                    0,
                    originalSegment.Length
                );

                // Keep extracting segments until the decompressed element size is no longer 4096 bytes
                if (decompressedElementData.Length == 4096)
                {
                    decompressedElementData = DecompressSegment(segment);
                    decompressedPartialBytesList.Add(decompressedElementData);
                }
                else
                {
                    // Combine all partial decompressed segments into a single byte array
                    decompressedElementData = decompressedPartialBytesList
                        .SelectMany(b => b)
                        .ToArray();

                    // Build the final decompressed element
                    BuildDecompressedElement(
                        tiaParser,
                        decompressedElementData,
                        segment,
                        elementName,
                        zlibHeader,
                        tiaParserCompressed,
                        false
                    );

                    return; // Exit after processing the element
                }
            }
        }

        /// <summary>
        /// Parses compressed elements from the provided file data, identifying ZLIB headers and attempting to
        /// decompress the associated segments. Extracts and processes specific element types: "Member", "Root",
        /// and "IdentXmlPart".
        /// </summary>
        public void ParseCompressedElements(TiaParserDriver tiaParser)
        {
            foreach (
                Match zlibHeader in Regex.Matches(
                    tiaParser.PlfFile,
                    @"x\^",
                    RegexOptions.None,
                    TimeSpan.FromSeconds(30)
                )
            )
            {
                byte[]? decompressedHeader = null;

                try
                {
                    decompressedHeader = DecompressSegment(
                        tiaParser.PlfBytes.Skip(zlibHeader.Index).Take(250).ToArray()
                    );
                }
                catch (InvalidDataException)
                {
                    continue; // try the next
                }

                if (decompressedHeader != null && decompressedHeader.Length != 0)
                {
                    string elementName = GetElementHeader(decompressedHeader);
                    //Increase efficiency of IdentXmlPart extraction
                    if (
                        elementName == "Member"
                        || elementName == "Root"
                        || elementName == "IdentXmlPart"
                    )
                    {
                        try
                        {
                            DefineDecompressedElement(tiaParser, zlibHeader, elementName, this);
                        }
                        catch (InvalidDataException exception)
                        {
                            TiaParserDriver.Logger.Warn(
                                exception,
                                $"Failed to Extract Compressed Element {elementName} - {zlibHeader.Index}"
                            );
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Decompresses a given segment of bytes using ZLIB compression and removes any trailing zero bytes.
        /// </summary>
        /// <param name="segment">The compressed byte segment to be decompressed.</param>
        /// <returns>A byte array containing the decompressed data with trailing zeros removed.</returns>
        private static byte[] DecompressSegment(byte[] segment)
        {
            using (var compressedData = new MemoryStream(segment))
            {
                using (var decompressedData = new MemoryStream())
                {
                    using (ZInputStream zlibStream = new ZInputStream(compressedData))
                    {
                        byte[] buffer = new byte[segment.Length];
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

                    return decompressedData.ToArray().Where(b => b != 0).ToArray();
                }
            }
        }

        /// <summary>
        /// Extracts the element header from a byte array that represents compressed data, assuming it is XML format.
        /// </summary>
        /// <param name="data">The byte array containing the compressed data.</param>
        /// <returns>
        /// The name of the element extracted from the XML data, or an empty string if the header is invalid.
        /// If the header is "IdentXmlPart" and contains "DBBlock", it returns "IdentXmlPart".
        /// If the header is "IdentXmlPart" but does not contain "DBBlock", it returns "elementName".
        /// </returns>
        static string GetElementHeader(byte[] data)
        {
            // If this is an XML, put it in the folder that is defined at the top level XML
            if (data.Length > 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            {
                string xmlSegmentData = Encoding.UTF8.GetString(data);

                // Regular expression to match a "<" followed by a tag name (letters and digits)
                Regex headerRegex = new Regex(@"<\w+", RegexOptions.None, TimeSpan.FromSeconds(10));
                Match match = headerRegex.Match(xmlSegmentData);

                if (match.Success)
                {
                    string elementName = match.Value.Replace("<", "");

                    if (elementName == "IdentXmlPart" && xmlSegmentData.Contains("DBBlock"))
                    {
                        return elementName;
                    }
                    else if (elementName == "IdentXmlPart")
                    {
                        return "elementName";
                    }

                    return elementName;
                }
                else
                {
                    return match.Value;
                }
            }

            return "";
        }
    }
}
