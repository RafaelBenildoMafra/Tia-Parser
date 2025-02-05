using System.Net;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TiaParser.Driver
{
    public class TiaAddressDataBlock
    {
        public TiaAddressDataBlock() { }

        public TiaAddressDataBlock(
            string blockName,
            string dataBlockAddres,
            int referenceAddress,
            string offset
        )
        {
            Name = blockName;
            Address = dataBlockAddres;
            ReferenceAddress = referenceAddress;
            Offset = offset;
        }

        public TiaAddressDataBlock(
            string name,
            string address,
            int referenceAddress,
            string offset,
            TiaCompressedBlock compressedBlock
        )
        {
            Name = name;
            Address = address;
            ReferenceAddress = referenceAddress;
            Offset = offset;
            CompressedBlock = compressedBlock;
        }

        public string Name { get; set; }
        public string Address { get; set; }
        public int ReferenceAddress { get; set; }
        public string Offset { get; set; }
        public TiaCompressedBlock CompressedBlock { get; set; }
        public List<TiaAddressDataBlock> BlockDataList { get; private set; } =
            new List<TiaAddressDataBlock>();

        public void InsertBlockData(TiaAddressDataBlock blockData)
        {
            this.BlockDataList.Add(blockData);
        }

        /// <summary>
        /// Extracts block data from the specified PLF file and byte array,
        /// processes any PLUSBLOCK data, and organizes the results by filtering
        /// out blocks with empty names when there are multiple blocks with the same offset.
        /// </summary>
        /// <returns>A list of <see cref="TiaAddressDataBlock"/> containing the extracted block data.</returns>
        /// <remarks>
        /// This method first extracts any PLUSBLOCK data, then groups the block data by
        /// their offsets. It filters out items with empty names if there are multiple items
        /// in the same group, and finally sorts the blocks by their reference addresses.
        /// </remarks>
        public void ExtractBlocksData(TiaParserDriver tiaParser)
        {
            ExtractPlusBlockData();

            AddressBlockData.ExtractBlockAddressOffset(tiaParser, this);

            // Group by Offset and filter out items with empty Name if there are multiple items in the group
            this.BlockDataList = this
                .BlockDataList.GroupBy(bd => bd.Offset)
                .SelectMany(group =>
                    group.Count() > 1 ? group.Where(bd => !string.IsNullOrEmpty(bd.Name)) : group
                )
                .OrderBy(block => block.ReferenceAddress)
                .ToList();

            void ExtractPlusBlockData()
            {
                MatchCollection plusBlockMatches = Regex.Matches(
                    tiaParser.PlfFile,
                    @"PLUSBLOCK",
                    RegexOptions.Singleline,
                    TimeSpan.FromSeconds(20)
                );

                foreach (Match plusBlockMatch in plusBlockMatches)
                {
                    int dataSize = tiaParser.PlfBytes[plusBlockMatch.Index + plusBlockMatch.Length];

                    string dataBlockData = tiaParser.PlfFile.Substring(
                        plusBlockMatch.Index + plusBlockMatch.Length,
                        dataSize
                    );

                    if (dataBlockData.Contains("%DB"))
                    {
                        PlusBlockData.TreatPlusBlockData(
                            tiaParser,
                            plusBlockMatch,
                            dataBlockData,
                            dataSize,
                            this
                        );
                    }
                }
            }
        }
    }

    public class PlusBlockData : TiaAddressDataBlock
    {
        public PlusBlockData(
            string name,
            string address,
            int referenceAddress,
            string offset,
            TiaCompressedBlock compressedBlock
        )
            : base(name, address, referenceAddress, offset, compressedBlock) { }

        /// <summary>
        /// Extracts and processes PLUSBLOCK data from the specified PLF file.
        /// This method identifies data blocks, retrieves their addresses, and stores relevant block information.
        /// It handles both standard and specific DB block formats, ensuring proper storage of block data.
        /// </summary>
        /// <param name="plusBlockMatch">The regex match object for locating PLUSBLOCK data.</param>
        /// <param name="dataBlockData">The string representation of the block's data.</param>
        /// <param name="dataSize">The size of the block data in bytes.</param>
        public static void TreatPlusBlockData(
            TiaParserDriver tiaParser,
            Match plusBlockMatch,
            string dataBlockData,
            int dataSize,
            TiaAddressDataBlock tiaBlockData
        )
        {
            Match dataBlockMatch = Regex.Match(
                dataBlockData,
                @"%DB",
                RegexOptions.Singleline,
                TimeSpan.FromSeconds(10)
            );

            int dataBlockOffset =
                plusBlockMatch.Index + plusBlockMatch.Length + dataBlockMatch.Index;

            int addressStringSize = dataBlockData[dataBlockMatch.Index - 1];

            string dataBlockAddres = dataBlockData.Substring(
                dataBlockMatch.Index,
                addressStringSize - 1
            );

            int referenceAddress = BitConverter.ToUInt16(
                tiaParser.PlfBytes,
                plusBlockMatch.Index + 53
            );

            int offset1 = tiaParser.PlfBytes[
                plusBlockMatch.Index + plusBlockMatch.Length + dataSize
            ];

            if (tiaParser.PlfBytes[plusBlockMatch.Index + plusBlockMatch.Length + dataSize + 1] > 0)
            {
                TiaCompressedBlock compressedBlock = new TiaCompressedBlock();

                compressedBlock.ParseCompressedBlock(plusBlockMatch, dataSize, tiaParser);

                tiaBlockData.InsertBlockData(
                    new PlusBlockData(
                        "COMPRESSED",
                        dataBlockAddres,
                        referenceAddress,
                        dataBlockOffset.ToString(),
                        compressedBlock
                    )
                );
            }

            int offset2 = tiaParser.PlfBytes[
                plusBlockMatch.Index + plusBlockMatch.Length + dataSize + offset1
            ];

            int blockNameDataSize = tiaParser.PlfBytes[
                plusBlockMatch.Index + plusBlockMatch.Length + dataSize + offset1 + offset2
            ];

            string blockNameData = tiaParser.PlfFile.Substring(
                plusBlockMatch.Index + plusBlockMatch.Length + dataSize + offset1 + offset2,
                blockNameDataSize
            );

            Match dbMatch = Regex.Match(
                blockNameData,
                @"DB",
                RegexOptions.Singleline,
                TimeSpan.FromSeconds(10)
            );

            if (dbMatch.Success)
            {
                try
                {
                    TreatStandardFoundBlock(dbMatch);
                }
                catch (Exception exception)
                {
                    TiaParserDriver.Logger.Warn(
                        exception,
                        $"FAILED TO EXTRACT BLOCK - LAST STEP: {dataBlockOffset}"
                    );
                }
            }

            void TreatStandardFoundBlock(Match dbMatch)
            {
                int blockNameSize = blockNameData[dbMatch.Index + dbMatch.Length];
                string blockName = blockNameData.Substring(
                    dbMatch.Index + dbMatch.Length + 1,
                    blockNameSize - 1
                );

                tiaBlockData.InsertBlockData(
                    new TiaAddressDataBlock(
                        blockName,
                        dataBlockAddres,
                        referenceAddress,
                        dataBlockOffset.ToString()
                    )
                );
            }
        }
    }

    public class AddressBlockData : TiaAddressDataBlock
    {
        public AddressBlockData(
            string name,
            string address,
            int referenceAddress,
            string offset,
            TiaCompressedBlock compressedBlock
        )
            : base(name, address, referenceAddress, offset, compressedBlock) { }

        public AddressBlockData() { }

        /// <summary>
        /// Extracts block address offsets from the specified PLF file based on matched patterns for the "%DB" marker.
        /// It identifies the size of each address, validates it, and retrieves the corresponding address string.
        /// If the address string contains non-alphanumeric characters, they are removed before extracting the block address.
        /// The method also checks if the extracted address matches the expected format (e.g., "DB<number>")
        /// and logs any errors encountered during processing.
        /// </summary>
        public static void ExtractBlockAddressOffset(
            TiaParserDriver tiaParser,
            TiaAddressDataBlock tiaBlockData
        )
        {
            MatchCollection blockAddressMatches = Regex.Matches(
                tiaParser.PlfFile,
                @"%DB",
                RegexOptions.Singleline,
                TimeSpan.FromSeconds(10)
            );

            foreach (Match blockAddressMatch in blockAddressMatches)
            {
                try
                {
                    int addressSize = tiaParser.PlfBytes[blockAddressMatch.Index - 1];

                    if (addressSize == 0)
                    {
                        TiaParserDriver.Logger.Debug(
                            $"INVALID ADDRESS SIZE {blockAddressMatch.Index} = 0"
                        );

                        continue;
                    }

                    string addressString = tiaParser.PlfFile.Substring(
                        blockAddressMatch.Index,
                        addressSize - 1
                    );

                    TiaCompressedBlock compressedBlock = TreatCompressedData(
                        tiaParser,
                        blockAddressMatch,
                        addressString
                    );

                    if (!addressString.All(Char.IsLetterOrDigit))
                    {
                        //Clean Data
                        addressString = Regex.Replace(
                            addressString,
                            @"[^\w\.@-]",
                            "",
                            RegexOptions.None,
                            TimeSpan.FromSeconds(10)
                        );
                    }

                    Regex numberRegex = new Regex(
                        @"\d+",
                        RegexOptions.None,
                        TimeSpan.FromSeconds(10)
                    );

                    MatchCollection matches = numberRegex.Matches(addressString);

                    if (int.TryParse(matches[0].Value, out int blockAddress))
                    {
                        Regex addresFormatRegex = new Regex(
                            @"^DB\d+",
                            RegexOptions.None,
                            TimeSpan.FromSeconds(10)
                        );
                        Match match = addresFormatRegex.Match(addressString);

                        if (match.Success)
                        {
                            tiaBlockData.InsertBlockData(
                                new AddressBlockData(
                                    "",
                                    addressString,
                                    blockAddress,
                                    blockAddressMatch.Index.ToString(),
                                    compressedBlock
                                )
                            );
                        }
                    }
                    else
                    {
                        TiaParserDriver.Logger.Debug(
                            $"FAILED EXTRACTING BLOCK ADDRESS {addressString} {blockAddressMatch.Index}"
                        );
                    }
                }
                catch (Exception exception)
                {
                    TiaParserDriver.Logger.Warn(
                        exception,
                        $"FAILED EXTRACTING BLOCK ADDRESS {blockAddressMatch.Index}"
                    );
                }
            }
        }

        private static TiaCompressedBlock TreatCompressedData(
            TiaParserDriver tiaParser,
            Match blockAddressMatch,
            string addressString
        )
        {
            int startIndex = blockAddressMatch.Index + addressString.Length;

            // Extract compressed data size safely
            if (startIndex + sizeof(ushort) > tiaParser.PlfBytes.Length)
            {
                return new TiaCompressedBlock();
            }

            int compressedDataSize = BitConverter.ToUInt16(tiaParser.PlfBytes, startIndex);

            // If the compressed data size is zero, return an empty TiaCompressedBlock
            if (compressedDataSize != 0)
            {
                // Ensure we have enough data for extraction
                if (startIndex + compressedDataSize > tiaParser.PlfFile.Length)
                {
                    return new TiaCompressedBlock();
                }

                // Extract compressed data substring
                string compressedData = tiaParser.PlfFile.Substring(startIndex, compressedDataSize);

                TiaCompressedBlock tiaCompressedBlock = new TiaCompressedBlock(compressedData);

                tiaCompressedBlock.ParseData(tiaParser, startIndex);

                return tiaCompressedBlock;
            }
            else
            {
                return new TiaCompressedBlock();
            }
        }
    }
}
