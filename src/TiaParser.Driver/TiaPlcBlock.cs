using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using static TiaParser.Driver.TiaBlock;
using static TiaParser.Driver.TiaParserElements;

namespace TiaParser.Driver
{
    public class TiaPlcBlock
    {
        public TiaPlcBlock() { }

        public TiaPlcBlock(string name, int address)
        {
            Name = name;
            Address = address;
        }

        public string Name { get; set; }
        public int Address { get; set; }
        public List<TiaPlcBlock> TiaPlcBlocks { get; private set; } = new List<TiaPlcBlock>();
        public List<PlcBlock> PlcBlocks { get; private set; } = new List<PlcBlock>();
        public List<TiaPlcItem> PlcItems { get; private set; } = new List<TiaPlcItem>();

        private void InsertTiaPlcBlock(TiaPlcBlock tiaPlcBlock)
        {
            this.TiaPlcBlocks.Add(tiaPlcBlock);
        }

        private void InsertPlcBlock(PlcBlock plcBlock)
        {
            this.PlcBlocks.Add(plcBlock);
        }

        /// <summary>
        /// Creates a PlcBlock from the provided TiaElementBlockData based on its XmlBlock.BlockElement type.
        ///
        /// If the BlockElement is a Root, it constructs a PlcBlock using the Root-specific properties.
        /// If the BlockElement is a Member, it constructs a PlcBlock using Member-specific properties.
        ///
        /// The method throws an InvalidOperationException if the BlockElement type is unknown or not handled.
        /// </summary>
        /// <param name="xmlElementBlock">The TiaElementBlockData containing the XmlBlock and BlockElement to process.</param>
        /// <returns>A PlcBlock object populated with data from the given TiaElementBlockData.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the BlockElement type is unknown or unsupported.</exception>
        private static PlcBlock CreatePlcBlockFromElement(TiaElementBlockData xmlElementBlock)
        {
            if (xmlElementBlock.XmlBlock.BlockElement is Root rootBlock)
            {
                return new PlcBlock(
                    xmlElementBlock.ID,
                    xmlElementBlock.Name,
                    xmlElementBlock.Address,
                    xmlElementBlock.Type,
                    xmlElementBlock.ReferenceBlock,
                    rootBlock
                );
            }
            else if (xmlElementBlock.XmlBlock.BlockElement is Member memberBlock)
            {
                return new PlcBlock(
                    xmlElementBlock.ID,
                    xmlElementBlock.Name,
                    xmlElementBlock.Address,
                    xmlElementBlock.Type,
                    xmlElementBlock.ReferenceBlock,
                    memberBlock
                );
            }

            // Handle other cases if necessary
            throw new InvalidOperationException("Unknown BlockElement blockType in XmlBlock.");
        }

        /// <summary>
        /// Builds TIA PLC blocks from the provided TiaElementBlockData and adds them to the current TiaPlcBlocks collection.
        ///
        /// This method iterates through all element blocks within the given TiaElementBlockData. If an element block does not
        /// have an associated XmlBlock, it logs an error. For blocks with a valid XmlBlock, it attempts to find a matching
        /// TiaPlcBlock in the current collection. If found, it inserts the new PlcBlock into the matching TiaPlcBlock;
        /// otherwise, it creates a new TiaPlcBlock and adds it to the collection.
        ///
        /// After processing all blocks, it calls BuildPlcItems to further build the block structure.
        /// </summary>
        /// <param name="xmlElementBlocks">The collection of TiaElementBlockData representing the PLC blocks to process.</param>
        public List<TiaAddress> BuildPlcData(TiaElementBlockData xmlElementBlocks)
        {
            foreach (TiaElementBlockData xmlElementBlock in xmlElementBlocks.ElementBlocks)
            {
                if (xmlElementBlock.XmlBlock == null)
                {
                    TiaParserDriver.Logger.Debug(
                        $"Failed mapping XML block for element '{xmlElementBlock.Name}'"
                    );
                    continue;
                }

                // Try to find a matching TiaPlcBlock
                var tiaPlcBlockMatch = this.TiaPlcBlocks.Find(block =>
                    block.Name == xmlElementBlock.Name
                );

                // Create a new TiaPlcBlock from the xmlElementBlock
                PlcBlock newPlcBlock = CreatePlcBlockFromElement(xmlElementBlock);

                if (tiaPlcBlockMatch != null)
                {
                    tiaPlcBlockMatch.InsertPlcBlock(newPlcBlock); //Insert the PlcBlock inside the correspondent TiaPlcBlock
                }
                else
                {
                    TiaPlcBlock tiaPlcBlock = new TiaPlcBlock( //Create the TiaPlcBlock and Insert the correspondent PlcBlock
                        xmlElementBlock.Name,
                        xmlElementBlock.Address
                    );

                    tiaPlcBlock.InsertPlcBlock(newPlcBlock);

                    this.InsertTiaPlcBlock(tiaPlcBlock);
                }
            }

            BuildTiaPlcItems();

            TiaAddress tiaAddress = new TiaAddress();
            tiaAddress.BuildAddress(this.TiaPlcBlocks);

            return tiaAddress.Addresses;
        }

        /// <summary>
        /// Builds PLC items for each TiaPlcBlock in the current TiaPlcBlocks collection.
        ///
        /// This method iterates over each TiaPlcBlock, invoking the BuildPlcItems method for each PlcBlock it contains.
        /// After building the PLC items, it proceeds to build any necessary extension blocks. Finally, it aggregates
        /// all the PlcItems from each PlcBlock into their corresponding TiaPlcBlock.
        ///
        /// At the end, it retrieves a sorted list of TiaPlcBlocks that have non-zero addresses, ordered by their address.
        /// </summary>
        private void BuildTiaPlcItems()
        {
            foreach (TiaPlcBlock tiaPlc in TiaPlcBlocks)
            {
                foreach (PlcBlock block in tiaPlc.PlcBlocks)
                {
                    block.BuildPlcItems();
                }
            }

            BuildExtensionBlocks();

            foreach (TiaPlcBlock tiaPlc in TiaPlcBlocks)
            {
                foreach (PlcBlock block in tiaPlc.PlcBlocks)
                {
                    if (block.Address != 0)
                    {
                        tiaPlc.Address = block.Address;
                    }

                    tiaPlc.PlcItems.AddRange(block.PlcItems);
                }
            }

            this.TiaPlcBlocks = this
                .TiaPlcBlocks.Where(block => block.Address != 0)
                .OrderBy(block => block.Address)
                .ToList();
        }

        /// <summary>
        /// Iterates through all PLC blocks and items, and for each item with a non-empty reference,
        /// it attempts to find the corresponding PLC block and builds the reference items.
        /// </summary>
        /// <remarks>
        /// This method looks for each PLC item with a non-empty `Reference` in the list of `PlcBlocks`.
        /// Once a matching block is found (based on the reference), the method calls `BuildReferenceItems`
        /// to recursively insert the reference items into the current PLC item.
        /// </remarks>
        private void BuildExtensionBlocks()
        {
            foreach (TiaPlcBlock tiaPlc in TiaPlcBlocks)
            {
                foreach (PlcBlock plcBlock in tiaPlc.PlcBlocks)
                {
                    SearchInBlock(plcBlock);
                }
            }
        }

        private void SearchInBlock(PlcBlock plcBlock)
        {
            foreach (TiaPlcItem plcItem in plcBlock.PlcItems)
            {
                if (!string.IsNullOrEmpty(plcItem.Reference))
                {
                    foreach (TiaPlcBlock searchTiaPlc in TiaPlcBlocks)
                    {
                        foreach (PlcBlock searchPlcBlock in searchTiaPlc.PlcBlocks)
                        {
                            if (plcItem.Reference == searchPlcBlock.Name) //Found the block that corresponds to the Item reference
                            {
                                plcItem.BuildReferenceItems(searchPlcBlock.PlcItems, PlcBlocks); // Pass PlcBlocks for recursive searching

                                break;
                            }
                        }
                    }
                }
            }
        }
    }

    public class PlcBlock
    {
        public PlcBlock() { }

        public PlcBlock(
            string id,
            string name,
            int address,
            TiaBlockType type,
            string referenceBlock,
            IBlockElement elementBlock
        )
        {
            ID = id;
            Name = name;
            Address = address;
            Type = type;
            ReferenceBlock = referenceBlock;
            ElementBlock = elementBlock;
        }

        public string ID { get; set; }
        public string Name { get; set; }
        public int Address { get; set; }
        public TiaBlockType Type { get; set; }
        public string ReferenceBlock { get; set; }
        public IBlockElement ElementBlock { get; set; }
        public List<TiaPlcItem> PlcItems { get; set; } = new List<TiaPlcItem>();

        public void InsertPlcItem(TiaPlcItem plcItem)
        {
            plcItem.TreatItemDataType();

            this.PlcItems.Add(plcItem);
        }

        /// <summary>
        /// Builds PLC items from the current element block. It handles both Root and Member blocks,
        /// constructing TiaPlcItem instances from the items contained within them.
        /// </summary>
        public void BuildPlcItems()
        {
            if (this.ElementBlock is Root rootBlock)
            {
                foreach (MemberItem item in rootBlock.Items)
                {
                    BuildItems(item, rootBlock);
                }

                if (rootBlock.External != null)
                {
                    BuildExternalItem(rootBlock);
                }
            }
            else
            {
                Member memberBlock = (Member)this.ElementBlock;

                foreach (MemberItem item in memberBlock.Items)
                {
                    BuildItems(item, memberBlock);
                }
            }
        }

        /// <summary>
        /// Builds external PLC items for the specified root block. This method constructs
        /// TiaPlcItem instances based on the external types and their usages defined in the root block.
        /// </summary>
        /// <param name="rootBlock">The root block containing external item definitions.</param>
        private void BuildExternalItem(Root rootBlock)
        {
            foreach (ExternalType externalType in rootBlock.External.ExternalTypes)
            {
                int pos = 0;

                TiaBlockType tiaBlockType = StringToBlockType(externalType.BlockClass);

                foreach (Usage usage in externalType.Usages)
                {
                    TiaPlcItem newPlcItem = new TiaPlcItem(
                        pos.ToString(), // Use position as the ID
                        usage.Name,
                        usage.Path,
                        tiaBlockType,
                        "UNDEFINED",
                        externalType.Type
                    );

                    InsertPlcItem(newPlcItem);

                    pos++;
                }
            }
        }

        /// <summary>
        /// Builds and inserts a PLC item from the specified Item instance and the associated Root block.
        /// This method handles internal items nested within the given item.
        /// </summary>
        /// <param name="item">The item to be converted into a PLC item.</param>
        /// <param name="rootBlock">The root block associated with the item.</param>
        private void BuildItems(MemberItem item, Root rootBlock)
        {
            TiaPlcItem newPlcItem = new TiaPlcItem(
                item.ID,
                item.Name,
                item.LID,
                rootBlock.Block.BlockType,
                item.DataType,
                ""
            );

            foreach (MemberItem internalItem in item.Items)
            {
                newPlcItem.InsertInternalItems(internalItem, rootBlock);
            }

            InsertPlcItem(newPlcItem);
        }

        /// <summary>
        /// Builds and inserts a PLC item from the specified Item instance and the associated Member block.
        /// This method handles internal items nested within the given item.
        /// </summary>
        /// <param name="item">The item to be converted into a PLC item.</param>
        /// <param name="memberBlock">The member block associated with the item.</param>
        private void BuildItems(MemberItem item, Member memberBlock)
        {
            TiaPlcItem newPlcItem = new TiaPlcItem(
                item.ID,
                item.Name,
                item.LID,
                memberBlock.Block.BlockType,
                item.DataType,
                ""
            );

            foreach (MemberItem internalItem in item.Items)
            {
                newPlcItem.InsertInternalItems(internalItem, memberBlock);
            }

            InsertPlcItem(newPlcItem);
        }
    }

    public class TiaPlcItem
    {
        public TiaPlcItem() { }

        public TiaPlcItem(
            string iD,
            string name,
            string address,
            TiaBlockType blockType,
            string dataType,
            string reference
        )
        {
            ID = iD;
            Name = name;
            Address = address;
            BlockType = blockType;
            DataType = dataType;
            Reference = reference;
        }

        public string ID { get; set; }
        public string Name { get; set; }
        public TiaBlockType BlockType { get; set; }
        public string DataType { get; set; }
        public string Address { get; set; }
        public string Reference { get; set; }
        public List<TiaPlcItem> Items { get; set; } = new List<TiaPlcItem>();

        public void InsertItem(TiaPlcItem plcItem)
        {
            plcItem.TreatItemDataType();

            this.Items.Add(plcItem);
        }

        public void InsertInternalItems(MemberItem item, Root rootBlock)
        {
            TiaPlcItem newPlcItem = new TiaPlcItem(
                item.ID,
                item.Name,
                item.LID,
                rootBlock.Block.BlockType,
                item.DataType,
                ""
            );

            this.Items.Add(newPlcItem);

            foreach (MemberItem Internalitem in item.Items)
            {
                newPlcItem.InsertInternalItems(Internalitem, rootBlock);
            }
        }

        public void InsertInternalItems(MemberItem item, Member memberBlock)
        {
            TiaPlcItem newPlcItem = new TiaPlcItem(
                item.ID,
                item.Name,
                item.LID,
                memberBlock.Block.BlockType,
                item.DataType,
                ""
            );

            this.Items.Add(newPlcItem);

            foreach (MemberItem Internalitem in item.Items)
            {
                newPlcItem.InsertInternalItems(Internalitem, memberBlock);
            }
        }

        /// <summary>
        /// Recursively inserts items from a list of reference PLC items into the current PLC item,
        /// handling nested items with non-empty references.
        /// </summary>
        /// <param name="plcItems">The list of PLC items to be inserted into the current PLC item.</param>
        /// <remarks>
        /// For each item in the provided `plcItems` list, this method inserts the item into the current PLC item
        /// and recursively searches for internal items with non-empty references. If a match is found in the
        /// `blockLookup`, the method calls itself recursively to continue inserting reference items.
        /// </remarks>
        public void BuildReferenceItems(List<TiaPlcItem> plcItems, List<PlcBlock> PlcBlocks)
        {
            foreach (TiaPlcItem plcItem in plcItems)
            {
                InsertItem(plcItem);

                // Recursively search inside the plcItem's internal items
                foreach (TiaPlcItem internalPlcItem in plcItem.Items)
                {
                    if (!string.IsNullOrEmpty(internalPlcItem.Reference))
                    {
                        // Find the block matching the internal reference
                        foreach (PlcBlock searchPlcBlock in PlcBlocks)
                        {
                            if (internalPlcItem.Reference == searchPlcBlock.Name)
                            {
                                internalPlcItem.BuildReferenceItems(
                                    searchPlcBlock.PlcItems,
                                    PlcBlocks
                                ); // Recurse

                                break; // Break out after finding a match
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Processes an <see cref="MemberItem"/> object to create a corresponding <see cref="TiaPlcItem"/> object.
        ///
        /// If the item type indicates an array, the method extracts the range and generates multiple PLC items for each index in the range.
        ///
        /// Returns a <see cref="TiaPlcItem"/> representing the processed item, potentially with nested items if the original item was an array.
        /// </summary>
        /// <param name="item">The <see cref="MemberItem"/> to process.</param>
        /// <returns>A <see cref="TiaPlcItem"/> representing the processed item.</returns>
        public void TreatItemDataType()
        {
            if (this.DataType != null && this.DataType != "UNDEFINED")
            {
                Match arrayMatch = Regex.Match(
                    this.DataType,
                    @"Array\[(\d+\.\.\d+(?:,\s*\d+\.\.\d+)*)\] of (\w+)",
                    RegexOptions.None,
                    TimeSpan.FromSeconds(10)
                );

                if (arrayMatch.Success)
                {
                    List<(int StartIndex, int EndIndex)> ranges =
                        new List<(int StartIndex, int EndIndex)>();

                    TiaPlcItem plcItem = BuildPlcItem(this, arrayMatch, ranges);

                    // Loop through each index in the range and create PlcItem instances
                    foreach (var range in ranges)
                    {
                        for (int i = range.StartIndex; i <= range.EndIndex; i++)
                        {
                            // Create a new PlcItem for each index in the range
                            plcItem.Items.Add(
                                new TiaPlcItem(
                                    this.ID,
                                    $"{this.Name}[{i}]",
                                    i.ToString(),
                                    this.BlockType,
                                    this.DataType,
                                    ""
                                )
                            );
                        }
                    }
                }
            }
        }

        private TiaPlcItem BuildPlcItem(
            TiaPlcItem tiaPlcItem,
            Match arrayMatch,
            List<(int StartIndex, int EndIndex)> ranges
        )
        {
            // Split the range part to handle multiple ranges (if applicable)
            string[] rangeParts = arrayMatch.Groups[1].Value.Split(',');
            string[] separator = { ".." };

            foreach (string rangePart in rangeParts)
            {
                string[] bounds = rangePart.Trim().Split(separator, StringSplitOptions.None);

                if (bounds.Length == 2)
                {
                    // Parse and add the start and end indexes to the list of ranges
                    ranges.Add((int.Parse(bounds[0]), int.Parse(bounds[1])));
                }
            }

            return new TiaPlcItem(
                this.ID,
                this.Name,
                this.Address,
                tiaPlcItem.BlockType,
                tiaPlcItem.DataType,
                ""
            );
        }

        /// <summary>
        /// Inserts items from a <see cref="Member"/> block into the corresponding <see cref="PlcBlock"/>
        /// based on matching IDs. If the member block has nested items, they are inserted recursively.
        /// </summary>
        /// <param name="memberBlocks">The <see cref="Member"/> block containing items to be inserted.</param>
        /// <param name="plcBlock">The target <see cref="PlcBlock"/> where items will be inserted.</param>
        public static void InsertItems(Member memberBlocks, PlcBlock plcBlock)
        {
            if (memberBlocks.ParentId is not null)
            {
                string[] addresses = { memberBlocks.ParentId };
                string[] separator = { ":" };

                if (memberBlocks.ParentId != null && memberBlocks.ParentId.Contains(':'))
                {
                    addresses = memberBlocks.ParentId.Split(separator, StringSplitOptions.None);
                }

                foreach (
                    TiaPlcItem plcItem in plcBlock.PlcItems.Where(item => item.ID == addresses[0])
                )
                {
                    if (addresses.Length > 1)
                    {
                        foreach (MemberItem memberItem in memberBlocks.Items)
                        {
                            InsertInternalItem(plcItem, memberItem, addresses, 1);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Recursively inserts a member item into the corresponding internal PLC item based on the address path.
        /// If the current address is the last in the path, the member item is added to the PLC item’s items list.
        /// If no match is found for the address path, a message is logged indicating the failure.
        /// </summary>
        /// <param name="plcItem">The current <see cref="TiaPlcItem"/> being processed.</param>
        /// <param name="memberItem">The <see cref="MemberItem"/> to be inserted into the PLC item.</param>
        /// <param name="addresses">The array of address segments used to locate the insertion point.</param>
        /// <param name="currentAddress">The current index in the address array being processed.</param>
        public static void InsertInternalItem(
            TiaPlcItem plcItem,
            MemberItem memberItem,
            string[] addresses,
            int currentAddress
        )
        {
            // Check if currentIndex is the last in the array
            bool isLastAddress = currentAddress == addresses.Length - 1;

            foreach (TiaPlcItem plcInternalItem in plcItem.Items)
            {
                // Check if current internal item ID matches the current address
                if (plcInternalItem.ID == addresses[currentAddress])
                {
                    // If it's the last address, return the item
                    if (isLastAddress)
                    {
                        plcInternalItem.InsertItem(plcInternalItem);

                        return;
                    }
                    // Recursively call for the next internal address in the list
                    else
                    {
                        InsertInternalItem(
                            plcInternalItem,
                            memberItem,
                            addresses,
                            currentAddress + 1
                        );
                    }
                }
            }

            TiaParserDriver.Logger.Debug($"NO MATCH FOUND FOR ITEM {plcItem.Address};{plcItem.Name}");
        }
    }
}
