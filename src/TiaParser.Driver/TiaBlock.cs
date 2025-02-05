using System;
using System.IO;
using System.Text.RegularExpressions;

namespace TiaParser.Driver
{
    public class TiaBlock
    {
        public TiaBlock() { }

        public TiaBlock(string data, TiaBlockType blockType, string offset)
        {
            Data = data;
            BlockType = blockType;
            Offset = offset;
        }

        public enum TiaBlockType
        {
            UNDEFINED,
            DB, // Data Block
            FB, // Function
            FC, // Function Block
            OB, // Organization Block
            UDT // Unique Data Type
        }

        public TiaBlockType BlockType { get; set; }
        public string Data { get; set; }
        public string Offset { get; set; }
        public TiaAddressDataBlock BlockData { get; set; }
        public List<TiaBlock> Blocks { get; private set; } = new List<TiaBlock>();

        /// <summary>
        /// Extracts blocks of various types (UDT, FB, DB, OB, FC, or PLUSBLOCK) from the given PLF file and byte array.
        /// </summary>
        /// <returns>A list of TiaBlock objects representing the extracted DB blocks.</returns>
        /// <remarks>
        /// This method searches the PLF file for matches to specific block types using regular expressions,
        /// extracts the corresponding data based on offsets and sizes, and categorizes the blocks accordingly.
        /// After extraction, it filters the blocks to return only those of type "DB".
        /// </remarks>
        public void ExtractBlocks(TiaParserDriver tiaParser)
        {
            MatchCollection typeMatches = Regex.Matches(
                tiaParser.PlfFile,
                @"(UDT|FB|DB|OB|FC)!|PLUSBLOCK",
                RegexOptions.Singleline,
                TimeSpan.FromSeconds(30)
            );

            foreach (Match typeMatch in typeMatches)
            {
                int offset = tiaParser.PlfBytes[typeMatch.Index + typeMatch.Length];
                int size = tiaParser.PlfBytes[typeMatch.Index + typeMatch.Length + offset];

                string dataType = "UNKNOWN";
                string blockData = tiaParser.PlfFile.Substring(
                    typeMatch.Index + typeMatch.Length + offset,
                    size
                );

                switch (true)
                {
                    case var _ when blockData.Contains("UDT") || typeMatch.Value == "UDT!":
                        dataType = "UDT";
                        break;
                    case var _ when blockData.Contains("FB") || typeMatch.Value == "FB!":
                        dataType = "FB";
                        break;
                    case var _ when blockData.Contains("DB") || typeMatch.Value == "DB!":
                        dataType = "DB";
                        break;
                    case var _ when blockData.Contains("OB") || typeMatch.Value == "OB!":
                        dataType = "OB";
                        break;
                    case var _ when blockData.Contains("FC") || typeMatch.Value == "FC!":
                        dataType = "FC";
                        break;
                }

                if (
                    dataType != "UNKNOWN"
                    && blockData.All(Char.IsLetterOrDigit)
                    && !string.IsNullOrWhiteSpace(blockData)
                )
                {
                    this.Blocks.Add(
                        new TiaBlock(
                            blockData,
                            StringToBlockType(dataType),
                            typeMatch.Index.ToString()
                        )
                    );
                }
            }

            ParseBlockNames(tiaParser);

            ParseDataBlockAddress(tiaParser);
        }

        // <summary>
        /// Parses the data block address from the provided PLF file and its byte array, extracts block data,
        /// and maps the data blocks to their respective offsets.
        /// </summary>
        /// <param name="PlfFile">The name of the PLF file being processed.</param>
        /// <param name="PlfBytes">The byte array containing the data from the PLF file.</param>
        /// <remarks>
        /// This method performs two key operations:
        /// 1. Extracts data blocks using the <see cref= "TiaAddressDataBlock"/> method, which reads
        ///    and processes block data from the PLF file and its byte array.
        /// 2. Maps the extracted data blocks to the corresponding TIA blocks based on their offsets using the
        ///    <see cref= "MapDataBlock"/> method. The closest data block with a positive offset distance is assigned
        ///    to each TIA block.
        /// </remarks>
        private void ParseDataBlockAddress(TiaParserDriver tiaParser)
        {
            TiaAddressDataBlock tiaAddressDataBlock = new TiaAddressDataBlock();

            tiaAddressDataBlock.ExtractBlocksData(tiaParser);

            MapDataBlock(tiaAddressDataBlock.BlockDataList);
        }

        /// <summary>
        /// Parses the names of blocks (DB, OB, FC, FB) from the provided PLF file and byte array,
        /// extracting and storing the names in the Blocks list.
        /// </summary>
        /// <remarks>
        /// This method uses a regular expression to find block name patterns, determines the name size
        /// and offset, and handles different cases based on the name size to extract the block names.
        /// The extracted names are then added to the Blocks collection along with their type.
        /// </remarks>
        private void ParseBlockNames(TiaParserDriver tiaParser)
        {
            // Create a regex to match the pattern for DB, OB, FC, FB
            string pattern = @"\x01\x03(DB|OB|FC|FB)";

            MatchCollection blockNameMatches = Regex.Matches(
                tiaParser.PlfFile,
                pattern,
                RegexOptions.Singleline,
                TimeSpan.FromSeconds(20)
            );

            foreach (Match blockNameMatch in blockNameMatches)
            {
                string matchType = blockNameMatch.Groups[1].Value; // DB, OB, FC, FB

                int nameSize = tiaParser.PlfBytes[blockNameMatch.Index + blockNameMatch.Length];
                int blockNameOffset = blockNameMatch.Index + blockNameMatch.Length;

                if (nameSize == 33)
                {
                    if (tiaParser.PlfBytes[blockNameOffset + nameSize] == 33)
                    {
                        AddBlockMatch(
                            tiaParser,
                            blockNameOffset + 1,
                            blockNameOffset + nameSize,
                            matchType
                        );
                    }
                    else
                    {
                        int offset = tiaParser.PlfBytes[blockNameOffset + 1];
                        nameSize = tiaParser.PlfBytes[blockNameOffset + 1 + offset];

                        AddBlockMatch(
                            tiaParser,
                            blockNameOffset + 2 + offset,
                            blockNameOffset + nameSize,
                            matchType
                        );
                    }
                }
                else
                {
                    AddBlockMatch(tiaParser, blockNameOffset + 1, nameSize, matchType);
                }
            }
        }

        private void AddBlockMatch(TiaParserDriver tiaParser, int startIndex, int size, string matchType)
        {
            try
            {
                string blockName = tiaParser.PlfFile.Substring(startIndex, size - 1);

                this.Blocks.Add(
                    new TiaBlock(blockName, StringToBlockType(matchType), startIndex.ToString())
                );
            }
            catch (Exception exception)
            {
                TiaParserDriver.Logger.Warn(
                    exception,
                    $"Invalid Block Match {matchType} Exception - {exception.Message}"
                );
            }
        }

        /// <summary>
        /// Maps data blocks to their corresponding TIA blocks based on their offsets.
        ///
        /// This method iterates through each <see cref="TiaBlock"/> in the provided list and finds the closest
        /// <see cref="TiaAddressDataBlock"/> based on the offset values. The closest data block with a positive
        /// distance (greater than zero) is assigned to the respective TIA block. This process ensures that
        /// each TIA block has an associated data block that is nearest to its offset.
        /// </summary>
        /// <param name="dataBlocks">A list of <see cref="TiaAddressDataBlock"/> instances representing the data
        /// blocks to be mapped.</param>
        /// be mapped.</param>
        private void MapDataBlock(List<TiaAddressDataBlock> dataBlocks)
        {
            foreach (
                TiaBlock block in this.Blocks.Where(block => block.BlockType == TiaBlockType.DB)
            )
            {
                int distance = int.MaxValue;

                foreach (TiaAddressDataBlock blockData in dataBlocks)
                {
                    int closerDistance = int.Parse(blockData.Offset) - int.Parse(block.Offset);

                    if (closerDistance < distance && closerDistance > 0)
                    {
                        distance = closerDistance;
                        block.BlockData = blockData;
                    }
                }
            }
        }

        /// <summary>
        /// Converts a string to the corresponding TiaBlockType enum.
        /// </summary>
        /// <param name="blockTypeString">The string representing the block type (e.g., "DB", "FC").</param>
        /// <returns>The corresponding TiaBlockType enum value.</returns>
        public static TiaBlockType StringToBlockType(string blockTypeString)
        {
            // Try to parse the string to an enum, ignoring case sensitivity.
            if (Enum.TryParse(blockTypeString, true, out TiaBlockType blockType))
            {
                return blockType;
            }
            else
            {
                return TiaBlockType.UNDEFINED;
            }
        }
    }
}
