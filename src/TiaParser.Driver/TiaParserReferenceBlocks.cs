using System.Data;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using static TiaParser.Driver.TiaParserCompressed;

namespace TiaParser.Driver
{
    public class TiaParserReferenceBlocks
    {
        public class ReferenceBlock
        {
            public ReferenceBlock(string id, string block, string type, InstanceBlock instanceBlock)
            {
                TRKG = id;
                Block = block;
                Type = type;
                InstanceBlocks = new List<InstanceBlock> { instanceBlock };
            }

            public string TRKG { get; set; }
            public string Block { get; set; }
            public string Type { get; set; }
            public List<InstanceBlock> InstanceBlocks { get; set; }
        }

        public class InstanceBlock
        {
            public InstanceBlock() { }

            public InstanceBlock(
                BlockProperties blockProperties,
                string block,
                string type,
                int offset,
                int address,
                string id
            )
            {
                Properties = blockProperties;
                Block = block;
                Type = type;
                Offset = offset;
                Address = address;
                TRKG = id;
            }

            public BlockProperties Properties { get; set; }
            public string Block { get; set; }
            public string Type { get; set; }
            public int Offset { get; set; }
            public int Address { get; set; }
            public string TRKG { get; set; }
        }

        public class BlockProperties
        {
            // ID block attributes
            public string IDName { get; set; }
            public string IDScope { get; set; }
            public string RID { get; set; }
            public string IS { get; set; }

            // CS block attributes
            public string NID { get; set; }
            public string UID { get; set; }
            public string AK { get; set; }

            // OD block attributes
            public string DTR { get; set; }
            public string ODScope { get; set; }
            public string Block { get; set; }

            // TOD block attributes
            public string TODN { get; set; }
            public string SM { get; set; }
            public string BT { get; set; }
            public string CID { get; set; }
            public string TRKG { get; set; }

            // DBBD block attributes
            public string IM { get; set; }
            public string NR { get; set; }
        }

        public TiaParserReferenceBlocks()
        {
            RefBlockList = new List<ReferenceBlock>();
        }

        private void InsertReferenceBlock(ReferenceBlock referenceBlock)
        {
            this.RefBlockList.Add(referenceBlock);
        }

        public List<ReferenceBlock> RefBlockList { get; private set; }

        /// <summary>
        /// Parses reference blocks from the provided PLF file and extracts the relevant <IdentXmlPart> elements.
        /// </summary>
        /// <param name="PlfFile">The path to the PLF file being parsed.</param>
        /// <remarks>
        /// The method searches for <IdentXmlPart> elements in the PLF file's content. If an element contains "DBBlock",
        /// it is parsed as XML and the corresponding DBBlock objects are stored.
        /// </remarks>
        public void ParseReferenceBlocks(string PlfFile)
        {
            MatchCollection identXmlMatches = Regex.Matches(
                PlfFile,
                @"<IdentXmlPart\b[^>]*>[\s\S]*?<\/IdentXmlPart>",
                RegexOptions.Singleline,
                TimeSpan.FromSeconds(20)
            );

            identXmlMatches
                .Cast<Match>()
                .Where(identXmlMatch => identXmlMatch.Value.Contains("DBBlock"))
                .ToList()
                .ForEach(identXmlMatch =>
                {
                    string identXmlString = identXmlMatch.Value;

                    XDocument xdoc = XDocument.Parse(identXmlString);
                    XNamespace ns =
                        "http://schemas.siemens.com/Simatic/ES/14/IdentManager/IdentXmlPart.xsd";

                    StoreCompressedBlock(xdoc, ns, this, identXmlMatch.Index);
                });
        }

        /// <summary>
        /// Parses a list of decompressed elements and extracts compressed reference blocks from <IdentXmlPart> elements.
        /// </summary>
        /// <param name="decompressedList">A list of decompressed elements containing XML data.</param>
        /// <remarks>
        /// This method iterates through each decompressed element, checks if its XML data's root element is <IdentXmlPart>,
        /// and stores the corresponding compressed block if found.
        /// </remarks>
        public void ParseCompressedRefBlocks(List<DecompressedElement> decompressedList)
        {
            foreach (DecompressedElement decompressedElement in decompressedList)
            {
                // Ensure XmlData and its Root are not null
                if (
                    decompressedElement.XmlData != null
                    && decompressedElement.XmlData.Root != null
                    && decompressedElement.XmlData.Root.Name.LocalName == "IdentXmlPart"
                )
                {
                    StoreCompressedBlock(
                        decompressedElement.XmlData,
                        "http://schemas.siemens.com/Simatic/ES/14/IdentManager/IdentXmlPart.xsd",
                        this,
                        decompressedElement.Offset
                    );
                }
            }

            this.FilterData();
        }

        /// <summary>
        /// Filters and sorts data within reference blocks.
        /// This method performs two steps:
        /// 1. Within each ReferenceBlock, it removes duplicate InstanceBlocks based on their Address
        ///    (keeping only the last occurrence) and then sorts the remaining InstanceBlocks by Address.
        /// 2. It sorts the entire list of ReferenceBlocks based on the Address of the first InstanceBlock in each block.
        /// </summary>
        public void FilterData()
        {
            // Step 1: Sort each container's items by Address, ensuring the last element for duplicate addresses is retained
            foreach (ReferenceBlock referenceBlock in this.RefBlockList)
            {
                referenceBlock.InstanceBlocks = referenceBlock
                    .InstanceBlocks.GroupBy(item => item.Address)
                    .Select(group => group.Last()) // Take the last item for duplicate addresses
                    .OrderBy(item => item.Address) // Sort the remaining items by Address
                    .ToList();
            }

            // Step 2: Sort the main container list based on the sorted items inside each container
            this.RefBlockList = this
                .RefBlockList.OrderBy(container =>
                    container.InstanceBlocks.FirstOrDefault()?.Address ?? int.MaxValue
                )
                .ToList();
        }

        /// <summary>
        /// Extracts information from the XML document and stores it in a list of AufDBBlock objects.
        /// Parses each <AufDBBlock> element, retrieves relevant properties (ID, CS, OD, TOD, DBBD),
        /// and stores the result in the provided list.
        /// </summary>
        /// <param name="xdoc">The parsed XML document containing the <AufDBBlock> elements.</param>
        /// <param name="ns">The XML namespace used within the document.</param>
        /// <param name="offset">The match representing the current <IdentXmlPart> being processed.</param>
        private static void StoreCompressedBlock(
            XDocument xdoc,
            XNamespace ns,
            TiaParserReferenceBlocks parseIdentXmlPart,
            int offset
        )
        {
            foreach (XElement aufDBBlockElement in xdoc.Descendants(ns + "AufDBBlock"))
            {
                ParseProperties(parseIdentXmlPart, aufDBBlockElement, ns, offset);
            }

            foreach (XElement aufDBBlockElement in xdoc.Descendants(ns + "DepDBBlock"))
            {
                ParseProperties(parseIdentXmlPart, aufDBBlockElement, ns, offset);
            }
        }

        /// <summary>
        /// Parses the properties of a block from an XML element, extracting various attributes
        /// such as ID, CS, OD, TOD, and DBBD, and builds a reference block list based on these properties.
        /// </summary>
        /// <param name="referenceBlocks">The reference blocks to populate.</param>
        /// <param name="refElement">The XML element representing the block.</param>
        /// <param name="ns">The XML namespace used for parsing.</param>
        /// <param name="offset">The offset value associated with the block data.</param>
        private static void ParseProperties(
            TiaParserReferenceBlocks referenceBlocks,
            XElement refElement,
            XNamespace ns,
            int offset
        )
        {
            BlockProperties properties = new BlockProperties
            {
                // ID block
                IDName = refElement.Element(ns + "ID")?.Attribute("N")?.Value ?? string.Empty,
                IDScope = refElement.Element(ns + "ID")?.Attribute("S")?.Value ?? string.Empty,
                RID = refElement.Element(ns + "ID")?.Attribute("RID")?.Value ?? string.Empty,
                IS = refElement.Element(ns + "ID")?.Attribute("IS")?.Value ?? string.Empty,

                // CS block
                NID =
                    refElement
                        .Element(ns + "ID")
                        ?.Element(ns + "CS")
                        ?.Element(ns + "C")
                        ?.Attribute("NID")
                        ?.Value ?? string.Empty,
                UID =
                    refElement
                        .Element(ns + "ID")
                        ?.Element(ns + "CS")
                        ?.Element(ns + "C")
                        ?.Attribute("UID")
                        ?.Value ?? string.Empty,
                AK =
                    refElement
                        .Element(ns + "ID")
                        ?.Element(ns + "CS")
                        ?.Element(ns + "C")
                        ?.Attribute("AK")
                        ?.Value ?? string.Empty,

                // OD block
                DTR = refElement.Element(ns + "OD")?.Attribute("DTR")?.Value ?? string.Empty,
                ODScope = refElement.Element(ns + "OD")?.Attribute("S")?.Value ?? string.Empty,
                Block =
                    refElement.Element(ns + "OD")?.Element(ns + "TD")?.Attribute("T")?.Value
                    ?? string.Empty,

                // TOD block
                TODN = refElement.Element(ns + "TOD")?.Attribute("N")?.Value ?? string.Empty,
                SM = refElement.Element(ns + "TOD")?.Attribute("SM")?.Value ?? string.Empty,
                BT = refElement.Element(ns + "TOD")?.Attribute("BT")?.Value ?? string.Empty,
                CID = refElement.Element(ns + "TOD")?.Attribute("CID")?.Value ?? string.Empty,
                TRKG = refElement.Element(ns + "TOD")?.Attribute("TRKG")?.Value ?? string.Empty,

                // DBBD block
                IM = refElement.Element(ns + "DBBD")?.Attribute("IM")?.Value ?? string.Empty,
                NR = refElement.Element(ns + "DBBD")?.Attribute("NR")?.Value ?? string.Empty
            };

            BuildReferenceBlockList(properties, offset, referenceBlocks);
        }

        /// <summary>
        /// Builds a reference block list by validating the block properties and creating or updating
        /// ReferenceBlock and InstanceBlock objects. Ensures block data is correctly formatted,
        /// splits the block into components, and adds the block to the appropriate reference list.
        /// </summary>
        /// <param name="blockProperties">The properties of the block to process.</param>
        /// <param name="offset">The offset value of the block data.</param>
        /// <param name="referenceBlocks">The reference block list to populate or update.</param>
        /// <exception cref="ArgumentNullException">Thrown if block properties or block name is null or empty.</exception>
        /// <exception cref="FormatException">Thrown if the block data does not follow the expected format.</exception>
        private static void BuildReferenceBlockList(
            BlockProperties blockProperties,
            int offset,
            TiaParserReferenceBlocks referenceBlocks
        )
        {
            // Validate BlockProperties using a guard clause
            if (blockProperties == null || string.IsNullOrEmpty(blockProperties.Block))
            {
                throw new ArgumentNullException(
                    nameof(blockProperties),
                    "Block properties or Block cannot be null or empty."
                );
            }

            // Split block into components and validate format
            string[] components = blockProperties.Block.Split(':');
            if (components.Length != 3)
            {
                throw new FormatException(
                    "Invalid input format. Expected format: 'BlockType:BlockID:Name'."
                );
            }

            // Proceed with building the reference block
            BuildReferenceBlock(components, blockProperties, offset, referenceBlocks);
        }

        private static void BuildReferenceBlock(
            string[] components,
            BlockProperties blockProperties,
            int offset,
            TiaParserReferenceBlocks referenceBlocks
        )
        {
            string blockType = components[0]; // e.g., "Block_FB"
            string name = components[2]; // e.g., "FESTO_Axis"

            // Create InstanceBlock
            InstanceBlock instanceBlock = new InstanceBlock(
                blockProperties,
                name,
                blockType,
                offset,
                int.Parse(blockProperties.TODN),
                blockProperties.TRKG
            );

#nullable disable
            // Check for existing ReferenceBlock by TRKG
            ReferenceBlock refBlock = referenceBlocks.RefBlockList.Find(rb =>
                rb.TRKG == blockProperties.TRKG
            );

            if (refBlock != null)
            {
                // Add to existing ReferenceBlock
                refBlock.InstanceBlocks.Add(instanceBlock);
            }
            else
            {
                // Create new ReferenceBlock and add it to the list
                ReferenceBlock newRefBlock = new ReferenceBlock(
                    blockProperties.TRKG,
                    name,
                    blockType,
                    instanceBlock
                );

                referenceBlocks.InsertReferenceBlock(newRefBlock);
            }
        }
    }
}
