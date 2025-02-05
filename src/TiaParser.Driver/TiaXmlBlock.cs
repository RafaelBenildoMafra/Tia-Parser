using System.Text.RegularExpressions;
using System.Xml.Linq;
using static TiaParser.Driver.TiaParserCompressed;
using static TiaParser.Driver.TiaParserElements;

namespace TiaParser.Driver
{
    public class TiaXmlBlock
    {
        public TiaXmlBlock() { }

        public TiaXmlBlock(XDocument xmlData, int offset, int size, bool isCompressed)
        {
            XmlData = xmlData;
            Offset = offset;
            Size = size;
            IsCompressed = isCompressed;
        }

        public TiaXmlBlock(
            XDocument xmlData,
            int offset,
            int size,
            bool isCompressed,
            IBlockElement blockElement
        )
            : this(xmlData, offset, size, isCompressed)
        {
            BlockElement = blockElement;
        }

        public XDocument XmlData { get; set; }
        public int Offset { get; set; }
        public int Size { get; set; }
        public bool IsCompressed { get; set; }
        public IBlockElement BlockElement { get; set; }

        public List<TiaXmlBlock> XmlBlockList { get; private set; } = new List<TiaXmlBlock>();

        private void InsertXmlBlock(TiaXmlBlock tiaXmlBlock)
        {
            this.XmlBlockList.Add(tiaXmlBlock);
        }

        /// <summary>
        /// Extracts and processes XML data blocks from a given PLF file.
        ///
        /// This method performs the following operations:
        /// 1. Parses XML data from the specified PLF file and byte array using <see cref="ParseXmlData"/>.
        /// 2. Decompresses and parses compressed XML data using <see cref="ParseCompressedXmlData"/>.
        /// 3. Retrieves and processes the last <Member> and <Root> XML blocks by grouping them based on their `Name`
        ///    property, ordering by `Offset`, and selecting the last block from each group.
        ///
        /// The method supports working with decompressed XML data from the PLF file and includes error handling for
        /// compressed data blocks.
        /// </summary>
        /// <param name="decompressedList">A list of decompressed elements to process.</param>
        public void ExtractXmlData(TiaParserDriver tiaParser, List<DecompressedElement> decompressedList)
        {
            ParseXmlData(tiaParser);

            ParseCompressedXmlData(tiaParser, decompressedList);

            // Filter for Member blocks, group by ID, order by Offset, and select the last block from each group
            var memberBlocks = this
                .XmlBlockList.Where(block => block.BlockElement is Member)
                .GroupBy(block => ((Member)block.BlockElement).Block.ID)
                .Select(group => group.OrderBy(b => b.Offset).Last()) // Order within the group by Offset and select the last block
                .ToList();

            // Filter for Root blocks, group by ID, order by Offset, and select the last block from each group
            var rootBlocks = this
                .XmlBlockList.Where(block => block.BlockElement is Root)
                .GroupBy(block => ((Root)block.BlockElement).Block.ID)
                .Select(group => group.OrderBy(b => b.Offset).Last()) // Order within the group by Offset and select the last block
                .ToList();

            // Combine the results of both Member and Root blocks
            this.XmlBlockList = memberBlocks.Concat(rootBlocks).ToList();
        }

        /// <summary>
        /// Parses XML data blocks from the given PLF file and processes their element data.
        ///
        /// This method uses regular expressions to identify and extract XML blocks that are
        /// defined by <Root> and <Member> tags within the provided PLF file content.
        /// The extracted XML blocks are parsed into <see cref="XDocument"/> objects for further processing
        /// through the <see cref="ExtractElementData"/> method.
        ///
        /// If XML parsing fails due to malformed data or multiple root elements, the error is caught to prevent
        /// application crashes.
        /// </summary>
        private void ParseXmlData(TiaParserDriver tiaParser)
        {
            List<Match> xmlBlockMatches = ExtractXmlData(tiaParser.PlfFile);

            foreach (Match xmlBlockMatch in xmlBlockMatches)
            {
                try
                {
                    XDocument xmlData = XDocument.Parse(xmlBlockMatch.Value);

                    ExtractElementData(
                        tiaParser,
                        new TiaXmlBlock(
                            xmlData,
                            xmlBlockMatch.Index,
                            xmlBlockMatch.Index + xmlBlockMatch.Length,
                            false
                        )
                    );
                }
                catch (Exception exception)
                {
                    TiaParserDriver.Logger.Warn(exception, $"INVALID XML Block: {xmlBlockMatch.Index}");
                }
            }
        }

        /// <summary>
        /// Extracts XML blocks from the PLF file content using two regular expressions.
        ///
        /// This method identifies <Root> and <Member> elements in the PLF file using two different
        /// regex patterns. One pattern captures <Root> and <Member> tags with attributes, and the other
        /// captures <Root> and <Member> tags with nested content. The resulting matches from both patterns
        /// are combined into a single list of matches.
        /// </summary>
        /// <param name="PlfFile">The content of the PLF file as a string.</param>
        /// <returns>A list of regex matches representing extracted XML blocks.</returns>
        private static List<Match> ExtractXmlData(string PlfFile)
        {
            MatchCollection xmlBlockMatches1 = Regex.Matches(
                PlfFile,
                @"<Root\s.*?</Root>|<Member\s.*?</Member>",
                RegexOptions.Singleline,
                TimeSpan.FromMinutes(5)
            );

            // Second regex for nested elements
            MatchCollection xmlBlockMatches2 = Regex.Matches(
                PlfFile,
                @"<Root>(.*?)</Root>|<Member>(.*?)</Member>",
                RegexOptions.Singleline,
                TimeSpan.FromMinutes(5)
            );

            // Combine the matches from both collections into one list
            List<Match> combinedMatches = new List<Match>();

            combinedMatches.AddRange(xmlBlockMatches1.Cast<Match>());
            combinedMatches.AddRange(xmlBlockMatches2.Cast<Match>());

            return combinedMatches;
        }

        /// <summary>
        /// Parses decompressed XML data blocks from a given PLF file and extracts relevant element data.
        ///
        /// This method iterates over a list of decompressed XML elements, creating a <see cref="TiaXmlBlock"/>
        /// for each one and invoking the <see cref="ExtractElementData"/> method to process the extracted data.
        /// </summary>
        /// <param name="decompressedList">A list of decompressed XML elements containing data to be parsed.</param>
        private void ParseCompressedXmlData(
            TiaParserDriver tiaParser,
            List<DecompressedElement> decompressedList
        )
        {
            foreach (DecompressedElement decompressedElement in decompressedList)
            {
                ExtractElementData(
                    tiaParser,
                    new TiaXmlBlock(
                        decompressedElement.XmlData,
                        decompressedElement.Offset,
                        decompressedElement.Size,
                        true
                    )
                );
            }
        }

        /// <summary>
        /// Extracts and processes XML data blocks from a given PLF file.
        ///
        /// This method creates an <see cref="ElementBlock"/> for the specified XML block, determines the size
        /// of the data, and retrieves the relevant block data. Depending on whether the XML block is compressed,
        /// it adjusts the extraction logic. The method then delegates the processing of the data to either
        /// <see cref="ExtractRootData"/> or <see cref="ExtractMemberData"/> based on the root element type of the XML data.
        /// </summary>
        /// <param name="xmlBlock">The <see cref="TiaXmlBlock"/> containing the XML data to be extracted and processed.</param>
        private void ExtractElementData(TiaParserDriver tiaParser, TiaXmlBlock xmlBlock)
        {
            if (xmlBlock.XmlData.Root != null)
            {
                ElementBlock elementBlock = new ElementBlock();

                elementBlock.Size = BitConverter.ToUInt16(tiaParser.PlfBytes, xmlBlock.Size);

                string blockData = tiaParser.PlfFile.Substring(
                    xmlBlock.Size + 1,
                    elementBlock.Size
                );

                int initialOffset = xmlBlock.Size + elementBlock.Size;

                if (xmlBlock.IsCompressed)
                {
                    elementBlock.Size = xmlBlock.Size;

                    try
                    {
                        blockData = tiaParser.PlfFile.Substring(
                            xmlBlock.Offset,
                            xmlBlock.Size + xmlBlock.Size * 2
                        );
                    }
                    catch
                    {
                        blockData = tiaParser.PlfFile.Substring(xmlBlock.Offset, xmlBlock.Size);
                    }
                }

                if (xmlBlock.XmlData.Root.Name.ToString() == "Root")
                {
                    ExtractRootData(tiaParser, xmlBlock, elementBlock, blockData, initialOffset);
                }
                else if (xmlBlock.XmlData.Root.Name.ToString() == "Member")
                {
                    ExtractMemberData(tiaParser, xmlBlock, elementBlock, blockData);
                }
            }
        }

        /// <summary>
        /// Extracts root data from the specified XML block and processes it accordingly.
        ///
        /// This method looks for a specific pattern in the block data to identify root blocks. If a match
        /// is found, it invokes the <see cref="ExtractRootBlock"/> method to handle the extraction. If no match
        /// is found, it calculates offsets to check for encrypted data. If the conditions are met, it calls
        /// <see cref="ParseEncryptedRoot"/> to process the encrypted data. Finally, it invokes the
        /// <see cref="InsertXmlBlock"/> method to store the processed XML block.
        /// </summary>
        /// <param name="xmlBlock">The <see cref="TiaXmlBlock"/> containing the XML data to be extracted.</param>
        /// <param name="elementBlock">The <see cref="ElementBlock"/> to be populated with extracted data.</param>
        /// <param name="blockData">The raw block data extracted from the PLF file.</param>
        /// <param name="initialOffset">The initial offset to be used for further data extraction.</param>
        private void ExtractRootData(
            TiaParserDriver tiaParser,
            TiaXmlBlock xmlBlock,
            ElementBlock elementBlock,
            string blockData,
            int initialOffset
        )
        {
            Match blockDataMatch = Regex.Match(
                blockData,
                @"BIVE:(.*?)/",
                RegexOptions.None,
                TimeSpan.FromMinutes(5)
            );

            if (blockDataMatch.Success)
            {
                ExtractRootBlock(elementBlock, blockData, blockDataMatch, xmlBlock);
            }
            else
            {
                int offsetData1 = BitConverter.ToUInt16(
                    tiaParser.PlfBytes,
                    xmlBlock.Size + elementBlock.Size
                );

                int offsetData2 = tiaParser.PlfBytes[initialOffset + offsetData1];

                if (tiaParser.PlfBytes[initialOffset + offsetData1 + offsetData2] == 255)
                {
                    ParseEncryptedRoot(
                        tiaParser,
                        xmlBlock,
                        elementBlock,
                        initialOffset + offsetData1 + offsetData2
                    );
                }
            }

            InsertXmlBlock(xmlBlock);
        }

        /// <summary>
        /// Extracts the root block information from the given block data and updates the specified element block.
        ///
        /// This method uses a regular expression to find a GUID within the block data. If a valid GUID is found,
        /// it assigns the GUID and name to the <see cref="ElementBlock"/>. It also creates a new instance of
        /// <see cref="Root"/> containing the updated block and assigns it to the <see cref="TiaXmlBlock"/>.
        /// If no GUID is found, it logs a warning indicating the extraction failure.
        /// </summary>
        /// <param name="elementBlock">The <see cref="ElementBlock"/> to be updated with extracted data.</param>
        /// <param name="blockData">The raw block data containing potential root information.</param>
        /// <param name="blockDataMatch">The regex match containing the extracted block data information.</param>
        /// <param name="xmlBlock">The <see cref="TiaXmlBlock"/> that will hold the extracted root block.</param>
        private static void ExtractRootBlock(
            ElementBlock elementBlock,
            string blockData,
            Match blockDataMatch,
            TiaXmlBlock xmlBlock
        )
        {
            Regex guidRegex = new Regex(
                @"/([a-zA-Z0-9\-]{36})",
                RegexOptions.None,
                TimeSpan.FromMinutes(5)
            );

            Match match = guidRegex.Match(blockData);

            if (match.Success)
            {
                elementBlock.ID = match.Groups[1].Value;

                elementBlock.Name = blockDataMatch.Groups[1].Value;

                Root root = new Root { Block = elementBlock };

                xmlBlock.BlockElement = root;
            }
            else
            {
                TiaParserDriver.Logger.Warn($"FAILED TO EXTRACT ROOT BLOCK: {xmlBlock.Offset}");
            }
        }

        /// <summary>
        /// Extracts member data from the specified XML block and processes it accordingly.
        ///
        /// This method searches for member block patterns in the provided block data. If a match is found,
        /// it invokes the <see cref="ExtractMemberBlock"/> method to extract the relevant member information.
        /// If no match is found, it checks for an encrypted member using the offset from the byte array and
        /// invokes <see cref="ParseEncryptedMember"/> if necessary. Finally, it calls <see cref="InsertXmlBlock"/>
        /// to store the processed XML block.
        /// </summary>
        /// <param name="PlfFile">The content of the PLF file as a string.</param>
        /// <param name="PlfBytes">The byte array representation of the PLF file.</param>
        /// <param name="xmlBlock">The <see cref="TiaXmlBlock"/> containing the XML data to be extracted.</param>
        /// <param name="elementBlock">The <see cref="ElementBlock"/> to be populated with extracted data.</param>
        /// <param name="blockData">The raw block data extracted from the PLF file.</param>
        /// <param name="InitialOffset">The initial offset to be used for further data extraction.</param>
        private void ExtractMemberData(
            TiaParserDriver tiaParser,
            TiaXmlBlock xmlBlock,
            ElementBlock elementBlock,
            string blockData
        )
        {
            Match blockDataMatch = Regex.Match(
                blockData,
                @"BI:(.*?)/",
                RegexOptions.None,
                TimeSpan.FromMinutes(5)
            );

            if (blockDataMatch.Success)
            {
                ExtractMemberBlock(elementBlock, blockData, blockDataMatch, xmlBlock);
            }
            else
            {
                int offsetData = BitConverter.ToUInt16(
                    tiaParser.PlfBytes,
                    xmlBlock.Size + elementBlock.Size
                );

                if (tiaParser.PlfBytes[xmlBlock.Size + elementBlock.Size + offsetData] == 255)
                {
                    ParseEncryptedMember(
                        tiaParser,
                        xmlBlock,
                        elementBlock,
                        xmlBlock.Size + elementBlock.Size + offsetData
                    );
                }
            }

            InsertXmlBlock(xmlBlock);
        }

        /// <summary>
        /// Extracts member block data from the provided block data and populates the specified element block.
        ///
        /// This method uses a regex pattern to find the ID within the block data. If a match is found, it sets
        /// the ID and Name properties of the <see cref="ElementBlock"/>. It then creates a <see cref="Member"/>
        /// instance containing the populated block and assigns it to the <see cref="TiaXmlBlock"/>.
        /// </summary>
        /// <param name="elementBlock">The <see cref="ElementBlock"/> to be populated with extracted member data.</param>
        /// <param name="blockData">The raw block data extracted from the PLF file.</param>
        /// <param name="blockDataMatch">The regex match result for the block data.</param>
        /// <param name="xmlBlock">The <see cref="TiaXmlBlock"/> to which the member block will be assigned.</param>
        private static void ExtractMemberBlock(
            ElementBlock elementBlock,
            string blockData,
            Match blockDataMatch,
            TiaXmlBlock xmlBlock
        )
        {
            // Match the pattern starting after the first '/' and containing at least 36 chars
            Regex IdRegex = new Regex(
                @"(?<=:)([a-zA-Z0-9]+):.*?/([a-zA-Z0-9\-]{36})",
                RegexOptions.None,
                TimeSpan.FromMinutes(5)
            );

            Match match = IdRegex.Match(blockData);

            if (match.Success)
            {
                elementBlock.ID = $"{match.Groups[1].Value}:{match.Groups[2].Value}";

                elementBlock.Name = blockDataMatch.Groups[1].Value.Split(':')[1];

                Member member = new Member { Block = elementBlock };

                xmlBlock.BlockElement = member;
            }
        }

        /// <summary>
        /// Parses encrypted root data from the given PLF file, extracting relevant block information.
        ///
        /// This method retrieves the size of the encrypted string from the PLF bytes, extracts the corresponding
        /// block data, and attempts to match it against a regex pattern. If a match is found, it calls the
        /// <see cref="ExtractRootBlock"/> method to process the block. If no match is found, it logs a warning
        /// message indicating failure to find the encrypted root block.
        /// </summary>
        /// <param name="xmlBlock">The <see cref="TiaXmlBlock"/> that contains information about the XML structure.</param>
        /// <param name="elementBlock">The <see cref="ElementBlock"/> to be populated with extracted data.</param>
        /// <param name="offset">The offset within the PLF file for processing the encrypted block.</param>
        private static void ParseEncryptedRoot(
            TiaParserDriver tiaParser,
            TiaXmlBlock xmlBlock,
            ElementBlock elementBlock,
            int offset
        )
        {
            int stringSizeOffset = tiaParser.PlfBytes[offset + 127];

            int blockStringSize = tiaParser.PlfBytes[offset + 127 + stringSizeOffset];

            string blockData = tiaParser.PlfFile.Substring(
                offset + 127 + stringSizeOffset,
                blockStringSize
            );

            Match blockDataMatch = Regex.Match(
                blockData,
                @"BIVE:(.*?)/",
                RegexOptions.None,
                TimeSpan.FromMinutes(5)
            );

            if (blockDataMatch.Success)
            {
                ExtractRootBlock(elementBlock, blockData, blockDataMatch, xmlBlock);
            }
            else
            {
                stringSizeOffset = tiaParser.PlfBytes[offset + 214];

                blockStringSize = tiaParser.PlfBytes[offset + 214 + stringSizeOffset];

                blockData = tiaParser.PlfFile.Substring(
                    offset + 215 + stringSizeOffset,
                    blockStringSize
                );

                blockDataMatch = Regex.Match(
                    blockData,
                    @"BIVE:(.*?)/",
                    RegexOptions.None,
                    TimeSpan.FromMinutes(5)
                );

                if (blockDataMatch.Success)
                {
                    ExtractRootBlock(elementBlock, blockData, blockDataMatch, xmlBlock);
                }
                else
                {
                    TiaParserDriver.Logger.Warn(
                        $"FAILED TO FIND ENCRYPTED ROOT BLOCK: {xmlBlock.Offset}:{blockData}"
                    );
                }
            }
        }

        /// <summary>
        /// Parses encrypted member data from the given PLF file and extracts relevant member block information.
        ///
        /// This method retrieves the size of the encrypted string from the PLF bytes, extracts the corresponding
        /// block data, and attempts to match it against a regex pattern. If a match is found, it invokes the
        /// <see cref="ExtractMemberBlock"/> method to process the extracted member data. If no match is found,
        /// it logs a warning message indicating failure to find the encrypted member block.
        /// </summary>
        /// <param name="xmlBlock">The <see cref="TiaXmlBlock"/> that contains information about the XML structure.</param>
        /// <param name="elementBlock">The <see cref="ElementBlock"/> to be populated with extracted data.</param>
        /// <param name="offset">The offset within the PLF file for processing the encrypted block.</param>
        private static void ParseEncryptedMember(
            TiaParserDriver tiaParser,
            TiaXmlBlock xmlBlock,
            ElementBlock elementBlock,
            int offset
        )
        {
            int sizeStringOffset = tiaParser.PlfBytes[offset + 119];

            int blockStringSize = tiaParser.PlfBytes[offset + 119 + sizeStringOffset];

            string blockData = tiaParser.PlfFile.Substring(
                offset + 120 + sizeStringOffset,
                blockStringSize
            );

            Match blockDataMatch = Regex.Match(
                blockData,
                @"BI:(.*?)/",
                RegexOptions.None,
                TimeSpan.FromMinutes(5)
            );

            if (blockDataMatch.Success)
            {
                ExtractMemberBlock(elementBlock, blockData, blockDataMatch, xmlBlock);
            }
            else
            {
                TiaParserDriver.Logger.Warn($"FAILED TO FIND MEMBER BLOCK: {xmlBlock.Offset}");
            }
        }
    }
}
