using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using static TiaParser.Driver.TiaBlock;
using static TiaParser.Driver.TiaParserElements;
using static TiaParser.Driver.TiaParserReferenceBlocks;

namespace TiaParser.Driver
{
    public class TiaElementBlockData
    {
        public TiaElementBlockData() { }

        public TiaElementBlockData(
            string name,
            string iD,
            string element,
            TiaBlockType type,
            string blockOffset,
            string dataOffset
        )
        {
            Name = name;
            ID = iD;
            Element = element;
            Type = type;
            BlockOffset = blockOffset;
            DataOffset = dataOffset;
        }

        public string Name { get; set; }
        public string ID { get; set; }
        public string Element { get; set; }
        public TiaBlockType Type { get; set; }
        public string BlockOffset { get; set; }
        public string DataOffset { get; set; }
        public int Address { get; set; }
        public TiaBlock Block { get; set; }
        public string ReferenceBlock { get; set; }
        public TiaXmlBlock XmlBlock { get; set; }
        public List<TiaElementBlockData> ElementBlocks { get; private set; } =
            new List<TiaElementBlockData>();

        private void InsertElementBlock(TiaElementBlockData elementBlockData)
        {
            this.ElementBlocks.Add(elementBlockData);
        }

        private void GroupIdOrderOffset()
        {
            this.ElementBlocks = this
                .ElementBlocks.OrderBy(element => int.Parse(element.DataOffset)) // Order elements by DataOffset before grouping
                .GroupBy(element => element.ID) // Group by ID
                .Select(group => group.OrderBy(e => int.Parse(e.DataOffset)).Last()) // Within each group, order by DataOffset and select the last element
                .ToList();
        }

        /// <summary>
        /// Extracts element data blocks from the specified PLF file and bytes, processing both root and member blocks.
        ///
        /// This method orchestrates the extraction of root and member blocks from the provided PLF file and byte array,
        /// parses relevant data into <see cref="TiaElementBlockData"/> instances, and filters the resulting blocks
        /// based on their type. The method performs the following steps:
        ///
        /// <para>
        /// 1. Calls <see cref="ExtractRootBlocks"/> to find and process root blocks.
        /// 2. Calls <see cref="ExtractMemberBlocks"/> to find and process member blocks.
        /// 3. Orders the extracted element blocks by data offset and groups them by ID, retaining the last occurrence.
        /// 4. Filters the element blocks to include only those of type "DB".
        /// 5. Maps the element blocks to the provided <see cref="TiaBlock"/> instances using <see cref="MapDataBlockElements"/>.
        /// 6. Orders the resulting element blocks by their address, filtering out any with an address of zero.
        /// </para>
        /// </summary>

        /// <param name="tiaBlocks">A list of TiaBlock instances to map against extracted element blocks.</param>
        public void ExtractElementsDataBlocks(TiaParserDriver tiaParser, List<TiaBlock> tiaBlocks)
        {
            ExtractRootBlocks(tiaParser, tiaBlocks);

            ExtractMemberBlocks(tiaParser, tiaBlocks);

            GroupIdOrderOffset();

            MapElementBlocks(tiaBlocks);
        }

        private void ExtractRootBlocks(TiaParserDriver tiaParser, List<TiaBlock> tiaBlocks)
        {
            MatchCollection rootBlockMatches = Regex.Matches(
                tiaParser.PlfFile,
                @"BIVE:(.*?)/",
                RegexOptions.Singleline,
                TimeSpan.FromSeconds(20)
            );

            foreach (Match rootBlockMatch in rootBlockMatches)
            {
                try
                {
                    int size = tiaParser.PlfBytes[rootBlockMatch.Index - 1];

                    if (size == 95)
                    {
                        size = tiaParser.PlfBytes[rootBlockMatch.Index - 2];
                    }

                    string rootBlockData = tiaParser.PlfFile.Substring(rootBlockMatch.Index, size);

                    Match match = TiaElementBlockData.MatchBlock(rootBlockData);

                    if (match.Success)
                    {
                        this.ParseBlock(
                            tiaParser.PlfFile,
                            match,
                            tiaBlocks,
                            rootBlockMatch.Index,
                            "ROOT"
                        );
                    }
                    else if (rootBlockData.All(Char.IsLetterOrDigit))
                    {
                        TiaParserDriver.Logger.Debug(
                            $"INVALID MATCH BRUTE FORCE ROOT BLOCK: {rootBlockMatch.Name} : {rootBlockMatch.Index}"
                        );
                    }
                }
                catch (Exception exception)
                {
                    TiaParserDriver.Logger.Warn(
                        exception,
                        $"ERROR BRUTE FORCE ROOT BLOCK: {rootBlockMatch.Value} : {+rootBlockMatch.Index}"
                    );
                }
            }
        }

        private void ExtractMemberBlocks(TiaParserDriver tiaParser, List<TiaBlock> tiaBlocks)
        {
            MatchCollection memberBlockMatches = Regex.Matches(
                tiaParser.PlfFile,
                @"BI:(.*?)/",
                RegexOptions.Singleline,
                TimeSpan.FromSeconds(20)
            );

            // Process memberBlockMatches
            foreach (Match memberBlockMatch in memberBlockMatches)
            {
                try
                {
                    int size = BitConverter.ToUInt16(
                        tiaParser.PlfBytes,
                        memberBlockMatch.Index - 1
                    );
                    string memberBlockData = tiaParser.PlfFile.Substring(
                        memberBlockMatch.Index,
                        size
                    );

                    Match match = TiaElementBlockData.MatchBlock(memberBlockData);

                    //Discard Members with ID prefix Value
                    if (match.Success && match.Groups[1].Value != "Values")
                    {
                        this.ParseBlock(
                            tiaParser.PlfFile,
                            match,
                            tiaBlocks,
                            memberBlockMatch.Index,
                            "MEMBER"
                        );
                    }
                    else if (
                        memberBlockData.All(Char.IsLetterOrDigit)
                        && match.Groups[1].Value != "Values"
                    )
                    {
                        TiaParserDriver.Logger.Debug(
                            $"INVALID BRUTE FORCE MEMBER BLOCK: {memberBlockMatch.Value} : {memberBlockMatch.Index}"
                        );
                    }
                }
                catch (Exception exception)
                {
                    TiaParserDriver.Logger.Warn(
                        exception,
                        $"ERROR BRUTE FORCE MEMBER BLOCK: {memberBlockMatch.Value} : {memberBlockMatch.Index}"
                    );
                }
            }
        }

        private static Match MatchBlock(string blockData)
        {
            Regex IdRegex = new Regex(
                @"([a-zA-Z0-9]+):.*?/([a-zA-Z0-9\-]{36})",
                RegexOptions.None,
                TimeSpan.FromSeconds(10)
            );
            Match match = IdRegex.Match(blockData);

            return match;
        }

        private void ParseBlock(
            string PlfFile,
            Match blockMatch,
            List<TiaBlock> tiaBlocks,
            int offset,
            string element
        )
        {
            try
            {
                // Extract the block name between the last ':' and the first '/'
                string name = ExtractBlockName(blockMatch);

                if (name == "UNKNOWN")
                {
                    TiaParserDriver.Logger.Debug("NAME NOT FOUND: " + blockMatch.Index);
                    return;
                }

                MatchCollection nameMatches = Regex.Matches(
                    PlfFile,
                    $"{Regex.Escape(name)}",
                    RegexOptions.None,
                    TimeSpan.FromSeconds(10)
                );

                Dictionary<string, TiaBlockType> types = GetBlockTypes(
                    PlfFile,
                    name,
                    nameMatches,
                    tiaBlocks
                );

                string id = $"{blockMatch.Groups[1].Value}:{blockMatch.Groups[2].Value}";

                if (element == "ROOT")
                {
                    id = $"{blockMatch.Groups[2].Value}";
                }

                foreach (var type in types)
                {
                    InsertElementBlock(
                        new TiaElementBlockData(
                            name,
                            id,
                            element,
                            type.Value,
                            type.Key,
                            offset.ToString()
                        )
                    );
                }
            }
            catch (Exception exception)
            {
                TiaParserDriver.Logger.Warn(
                    exception,
                    $"ERROR PARSING BLOCKS: {blockMatch.Value} Offset: {offset}"
                );
                throw;
            }
        }

        /// <summary>
        /// Retrieves the types of blocks identified in the specified PLF file based on a collection of regex matches.
        ///
        /// This method iterates through the provided matches to determine the type of each block by analyzing
        /// the prefix preceding each match in the PLF file. It categorizes blocks as User-Defined Types (UDT),
        /// Function Blocks (FB), Data Blocks (DB), Organization Blocks (OB), or Function Calls (FC),
        /// and stores them in a dictionary where the key is the block's offset in the PLF file.
        ///
        /// <para>
        /// The method uses the following logic:
        /// - For each match, it extracts the prefix located two characters before the match's index.
        /// - Depending on the prefix, it adds the corresponding block type to the dictionary.
        /// </para>
        /// </summary>
        /// <param name="PlfFile">The content of the PLF file being processed.</param>
        /// <param name="blockMatches">A collection of regex matches representing potential block definitions.</param>
        /// <returns>
        /// A dictionary mapping the offsets of blocks (as strings) to their corresponding types (as strings).
        /// </returns>
        private static Dictionary<string, TiaBlockType> GetBlockTypes(
            string PlfFile,
            string name,
            MatchCollection blockMatches,
            List<TiaBlock> tiaBlocks
        )
        {
            Dictionary<string, TiaBlockType> types = new Dictionary<string, TiaBlockType>();

            foreach (int index in blockMatches.Cast<Match>().Select(match => match.Index))
            {
                string prefix = PlfFile.Substring(index - 3, 2);

                string offset = index.ToString();

                switch (prefix)
                {
                    case "DT":
                        types[offset] = TiaBlockType.UDT;
                        break;
                    case "FB":
                        types[offset] = TiaBlockType.FB;
                        break;
                    case "DB":
                        types[offset] = TiaBlockType.DB;
                        break;
                    case "OB":
                        types[offset] = TiaBlockType.OB;
                        break;
                    case "FC":
                        types[offset] = TiaBlockType.FC;
                        break;
                }
            }

            //Try to find the block data type by searching for it in the TiaBlocks
            if (types.Count == 0)
            {
                SearchDataTypes(types, tiaBlocks, name);
            }

            return types;
        }

        private static void SearchDataTypes(
            Dictionary<string, TiaBlockType> types,
            List<TiaBlock> tiaBlocks,
            string name
        )
        {
            List<TiaBlock> dataTypes = tiaBlocks.Where(data => data.Data.Contains(name)).ToList();

            if (dataTypes.Count != 0)
            {
                foreach (TiaBlock data in dataTypes)
                {
                    Match blockNameMatch = Regex.Match(
                        data.Data,
                        $"{Regex.Escape(name)}",
                        RegexOptions.None,
                        TimeSpan.FromSeconds(10)
                    );

                    if (
                        blockNameMatch.Success
                        && blockNameMatch.Index > 0
                        && blockNameMatch.Index - 1 < data.Data.Length
                        && data.Data[blockNameMatch.Index - 1] == name.Length + 1
                    )
                    {
                        types[data.Offset] = data.BlockType;
                    }
                }
            }
            else
            {
                types["0"] = TiaBlockType.UNDEFINED;
            }
        }

        /// <summary>
        /// Extracts the block name from a regex match that contains block data.
        ///
        /// This method analyzes the matched string to identify and extract the block name, which is defined as
        /// the substring located between the last colon (':') and the first forward slash ('/') following it.
        /// If the expected delimiters are not found, it returns "UNKNOWN".
        ///
        /// <para>
        /// The extraction process involves:
        /// - Locating the position of the last colon in the matched value.
        /// - Finding the position of the first forward slash after the last colon.
        /// - Returning the substring that represents the block name if both delimiters are found.
        /// </para>
        /// </summary>
        /// <param name="blockMatch">The regex match containing the block data from which to extract the block name.</param>
        /// <returns>
        /// The extracted block name as a string, or "UNKNOWN" if the name cannot be determined.
        /// </returns>
        private static string ExtractBlockName(Match blockMatch)
        {
            string value = blockMatch.Groups[0].Value;

            int lastColonIndex = value.LastIndexOf(':');
            int slashIndex = value.IndexOf('/', lastColonIndex);

            if (lastColonIndex != -1 && slashIndex != -1)
            {
                return value.Substring(lastColonIndex + 1, slashIndex - lastColonIndex - 1);
            }

            return "UNKNOWN";
        }

        /// <summary>
        /// Maps the provided TiaBlocks to corresponding TiaElementBlockData instances based on block names.
        ///
        /// This method iterates through a list of TiaBlock objects, attempting to find matches in the
        /// TiaElementBlocks collection. If a match is found, it updates the address and assigns the block
        /// to the corresponding element block. The method performs both strict and lenient matching
        /// to accommodate variations in block naming conventions.
        ///
        /// <para>
        /// The mapping process involves:
        /// - Checking for exact matches between block data and element block names.
        /// - If no exact match is found, it attempts to match by checking if the block data contains
        ///   the name of any element block.
        /// - If a match is found, it logs the successful mapping. If no matches are found after both attempts,
        ///   it logs a warning with the unmapable block data.
        /// </para>
        /// </summary>
        /// <param name="tiaBlocks">The list of TiaBlock objects to be mapped to TiaElementBlockData.</param>
        public void MapElementBlocks(List<TiaBlock> tiaBlocks)
        {
            foreach (TiaBlock block in tiaBlocks)
            {
                if (block.BlockType == TiaBlockType.DB)
                {
                    bool anyMatchFound = false; // Track if any match is found

                    foreach (TiaElementBlockData elementBlock in this.ElementBlocks)
                    {
                        if (block.Data == elementBlock.Name)
                        {
                            MapElementBlock(elementBlock, block);
                            anyMatchFound = true;
                            break;
                        }
                    }

                    // Log if no match was found
                    if (!anyMatchFound && block.Data.All(Char.IsLetterOrDigit))
                    {
                        TiaParserDriver.Logger.Warn(
                            $"NO NAME MATCH FOUND FOR '{block.Data}' Offset: {block.Offset} - COULD NOT MAP ADDRESS"
                        );
                    }
                }
            }
        }

        private static void MapElementBlock(TiaElementBlockData elementBlock, TiaBlock block)
        {
            try
            {
                elementBlock.Address = block.BlockData.ReferenceAddress;
                elementBlock.Block = block;
                TiaParserDriver.Logger.Debug($"BLOCK FORMED {elementBlock.Name}:{elementBlock.Address}");
            }
            catch (Exception exception)
            {
                TiaParserDriver.Logger.Warn(
                    exception,
                    $"FORM BLOCK FAIL {elementBlock.Name}:{elementBlock.Address}"
                );
            }
        }

        /// <summary>
        /// Maps reference element blocks to the corresponding instance blocks based on their data and addresses.
        ///
        /// This method iterates through the collection of <see cref="TiaElementBlockData"/> and attempts to find
        /// matching <see cref="ReferenceBlock"/> instances from the provided <see cref="TiaParserReferenceBlocks"/>.
        /// If a match is found based on both the element block's data and address, the corresponding reference block
        /// is assigned to the element block. If no match is found, the element block's name is assigned as its
        /// reference block. The method also logs any mismatches in addresses or names encountered during the mapping process.
        ///
        /// After processing, the method groups the element blocks by their assigned reference block and orders
        /// the elements by their address for further processing or analysis.
        /// </summary>
        /// <param name="referenceBlocks">An instance of <see cref="TiaParserReferenceBlocks"/> containing reference blocks
        /// and their associated instance blocks.</param>
        public void MapReferenceElementBlocks(TiaParserReferenceBlocks referenceBlocks)
        {
            // Iterate through element blocks and reference blocks to match and assign ReferenceBlock
            foreach (TiaElementBlockData elementBlock in this.ElementBlocks)
            {
                bool foundReferenceBlock = false; // Track if a match is found for the current elementBlock

                foreach (ReferenceBlock referenceBlock in referenceBlocks.RefBlockList)
                {
                    foundReferenceBlock = VerifyInstances(
                        referenceBlock,
                        elementBlock,
                        foundReferenceBlock
                    );

                    if (foundReferenceBlock)
                        break; // Exit outer loop early if a match was found
                }

                // If no match was found, assign the element's own name as the reference block
                if (!foundReferenceBlock)
                {
                    elementBlock.ReferenceBlock = elementBlock.Name;
                }
            }
        }

        private static bool VerifyInstances(
            ReferenceBlock referenceBlock,
            TiaElementBlockData elementBlock,
            bool foundReferenceBlock
        )
        {
            foreach (InstanceBlock instanceBlock in referenceBlock.InstanceBlocks)
            {
                string blockName = "";

                if (elementBlock.Block == null)
                {
                    blockName = elementBlock.Name;
                }
                else
                {
                    blockName = elementBlock.Block.Data;
                }

                // Check if the block data matches the instance's IDName
                if (blockName == instanceBlock.Properties.IDName)
                {
                    if (instanceBlock.Address == elementBlock.Address)
                    {
                        // Assign the reference block if both the block data and address match
                        elementBlock.ReferenceBlock = referenceBlock.Block;
                        foundReferenceBlock = true;
                        break; // Exit loop early since a match was found
                    }
                    else
                    {
                        TiaParserDriver.Logger.Debug(
                            $"REFERENCE NAME BLOCK MATCHED WITHOUT PROPER ADDRESS: ELEMENT {elementBlock.Address}, INSTANCE {instanceBlock.Block} - {instanceBlock.Address}"
                        );

                        elementBlock.Address = instanceBlock.Address;
                        elementBlock.ReferenceBlock = referenceBlock.Block;
                        foundReferenceBlock = true;
                        break; // Exit loop early since a match was found
                    }
                }
                else if (instanceBlock.Address == elementBlock.Address)
                {
                    TiaParserDriver.Logger.Debug(
                        $"REFERENCE NAME BLOCK MATCHED FAIL: ELEMENT {elementBlock.Address}, INSTANCE {instanceBlock.Block} - {instanceBlock.Address}"
                    );
                }
            }

            return foundReferenceBlock;
        }

        /// <summary>
        /// Maps XML blocks to element blocks based on matching IDs and reference names.
        ///
        /// This method iterates through a list of element blocks and maps them to
        /// corresponding XML blocks, creating deep copies of the Member or Root blocks
        /// as necessary. It checks for matches based on element IDs and reference
        /// names, and if a match is not found, it logs a message indicating the
        /// failure to find the corresponding XML block. Finally, it processes the
        /// mapped XML data before returning the dictionary of mapped XML blocks.
        /// </summary>
        /// <param name="tiaXmlBlocks">A list of XML blocks containing the XML data to be mapped.</param>
        /// <returns>A dictionary mapping element block names to their corresponding XML blocks.</returns>
        public void MapXmlElementBlock(TiaXmlBlock tiaXmlBlocks)
        {
            foreach (TiaElementBlockData elementBlock in this.ElementBlocks)
            {
                bool foundXmlBlock = false;

                foundXmlBlock = VerifyXmlElement(tiaXmlBlocks, elementBlock, foundXmlBlock);

                if (!foundXmlBlock)
                {
                    TiaParserDriver.Logger.Debug(
                        $"XML BLOCK NOT FOUND FOR ELEMENT: {elementBlock.Name} - {elementBlock.DataOffset}"
                    );
                }
            }
        }

        private static bool VerifyXmlElement(
            TiaXmlBlock tiaXmlBlocks,
            TiaElementBlockData elementBlock,
            bool foundXmlBlock
        )
        {
            foreach (TiaXmlBlock tiaXmlBlock in tiaXmlBlocks.XmlBlockList)
            {
                if (
                    (
                        tiaXmlBlock.BlockElement is Member memberBlock
                        && elementBlock.ID == memberBlock.Block.ID
                    )
                    || (
                        tiaXmlBlock.BlockElement is Root rootBlock
                        && elementBlock.ID == rootBlock.Block.ID
                    )
                )
                {
                    elementBlock.XmlBlock = tiaXmlBlock;
                    foundXmlBlock = true;
                    break;
                }
            }

            return foundXmlBlock;
        }
    }
}
