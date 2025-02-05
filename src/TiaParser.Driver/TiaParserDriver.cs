using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using NLog;
using static TiaParser.Driver.TiaBlock;
using static TiaParser.Driver.TiaParserCompressed;
using static TiaParser.Driver.TiaParserReferenceBlocks;

namespace TiaParser.Driver
{
    public class TiaParserDriver
    {
        public string PlfFile { get; private set; }

        public byte[] PlfBytes { get; private set; }

        public List<TiaAddress> TiaBlockAddresses { get; private set; } = new List<TiaAddress>();

        public static Logger Logger => LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Initializes a new instance of the TiaParser class by converting the provided file path into bytes.
        /// </summary>
        /// <param name="filePath">The file path of the file to be converted to bytes.</param>
        public TiaParserDriver(string filePath)
        {            
            byte[] fileBytes = ConvertFormFileToBytes(filePath);
            PlfBytes = fileBytes;
        }

        /// <summary>
        /// Converts a file from the given file path into a byte array.
        /// </summary>
        /// <param name="filePath">The file path to read and convert.</param>
        /// <returns>A byte array containing the file data.</returns>
        public static byte[] ConvertFormFileToBytes(string filePath)
        {
            using var memoryStream = new MemoryStream();
            File.OpenRead(filePath).CopyTo(memoryStream);
            return memoryStream.ToArray();
        }

        /// <summary>
        /// Project Input Parses the TIA reference addresses from the PlfBytes by processing elements, reference blocks, and addresses.
        /// </summary>
        public List<TiaAddress> ParseTiaReferenceAddresses()
        {
            PlfFile = System.Text.Encoding.ASCII.GetString(PlfBytes);

            TiaElementBlockData tiaElementBlocks = ParseElementsData();

            TiaParserElements.ParseXmlData(tiaElementBlocks.ElementBlocks);

            BuildElementsAddresses(tiaElementBlocks);

            return this.TiaBlockAddresses;
        }

        /// <summary>
        /// Parses and returns the TIA element block data by processing compressed elements, reference blocks, and XML mappings.
        /// </summary>
        /// <returns>The parsed TiaElementBlockData containing all the parsed element blocks and their mappings.</returns>
        private TiaElementBlockData ParseElementsData()
        {            
            TiaParserCompressed compressedElements = ParseCompressedElementsData();
            TiaParserReferenceBlocks referenceBlocks = PraseReferenceElementsData(
                compressedElements.DecompressedElementList
            );
            TiaBlock tiaDataBlocks = ParseDataBlocks();
            TiaElementBlockData tiaElementsDataBlocks = ParseElementsDataBlocks(
                tiaDataBlocks.Blocks,
                referenceBlocks
            );
            TiaXmlBlock xmlBlocks = ParseElementsXmlBlocks(
                compressedElements.DecompressedElementList
            );

            tiaElementsDataBlocks.MapXmlElementBlock(xmlBlocks);
            return tiaElementsDataBlocks;
        }

        /// <summary>
        /// Parses and returns the compressed TIA elements from the PlfBytes.
        /// </summary>
        /// <returns>An instance of TiaParserCompressed containing the decompressed elements.</returns>
        private TiaParserCompressed ParseCompressedElementsData()
        {
            TiaParserCompressed tiaParserCompressed = new TiaParserCompressed();
            tiaParserCompressed.ParseCompressedElements(this);
            return tiaParserCompressed;
        }

        /// <summary>
        /// Parses reference blocks and compressed reference blocks from the decompressed element list.
        /// </summary>
        /// <param name="decompressedElementList">The list of decompressed elements to parse reference data from.</param>
        /// <returns>An instance of TiaParserReferenceBlocks containing parsed reference blocks.</returns>
        private TiaParserReferenceBlocks PraseReferenceElementsData(
            List<DecompressedElement> decompressedElementList
        )
        {
            TiaParserReferenceBlocks tiaParserReferenceBlocks = new TiaParserReferenceBlocks();
            tiaParserReferenceBlocks.ParseReferenceBlocks(this.PlfFile);
            tiaParserReferenceBlocks.ParseCompressedRefBlocks(decompressedElementList);
            return tiaParserReferenceBlocks;
        }

        /// <summary>
        /// Extracts data blocks from the parsed PlfBytes and returns an instance of TiaBlock containing the blocks.
        /// </summary>
        /// <returns>An instance of TiaBlock with extracted blocks.</returns>
        private TiaBlock ParseDataBlocks()
        {
            TiaBlock tiaBlock = new TiaBlock();
            tiaBlock.ExtractBlocks(this);
            return tiaBlock;
        }

        /// <summary>
        /// Parses and maps element data blocks and references from the data blocks and reference blocks.
        /// </summary>
        /// <param name="dataBlocks">List of parsed TIA data blocks.</param>
        /// <param name="referenceBlocks">Parsed reference blocks to map with the data blocks.</param>
        /// <returns>An instance of TiaElementBlockData containing mapped data blocks and references.</returns>
        private TiaElementBlockData ParseElementsDataBlocks(
            List<TiaBlock> dataBlocks,
            TiaParserReferenceBlocks referenceBlocks
        )
        {
            TiaElementBlockData tiaElementBlockData = new TiaElementBlockData();
            tiaElementBlockData.ExtractElementsDataBlocks(this, dataBlocks);
            tiaElementBlockData.MapReferenceElementBlocks(referenceBlocks);
            return tiaElementBlockData;
        }

        /// <summary>
        /// Extracts XML data from the decompressed elements and returns a TiaXmlBlock containing the parsed XML blocks.
        /// </summary>
        /// <param name="decompressedElementList">The list of decompressed elements to extract XML data from.</param>
        /// <returns>A TiaXmlBlock instance with the extracted XML data.</returns>
        private TiaXmlBlock ParseElementsXmlBlocks(
            List<DecompressedElement> decompressedElementList
        )
        {
            TiaXmlBlock xmlBlocks = new TiaXmlBlock();
            xmlBlocks.ExtractXmlData(this, decompressedElementList);

            return xmlBlocks;
        }

        /// <summary>
        /// Builds the PLC block addresses based on the parsed TIA element block data.
        /// </summary>
        /// <param name="xmlElementBlocks">The parsed XML element blocks to build addresses from.</param>
        private void BuildElementsAddresses(TiaElementBlockData xmlElementBlocks)
        {
            TiaPlcBlock tiaPlcBlock = new TiaPlcBlock();

            this.TiaBlockAddresses = tiaPlcBlock.BuildPlcData(xmlElementBlocks);
        }
    }
}
